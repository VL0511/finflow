using System.Diagnostics;
using System.Text;
using System.Text.Json;
using FinFlow.Application.Common;
using FinFlow.Application.Messaging;
using FinFlow.Domain.Entities;
using FinFlow.Domain.Enums;
using FinFlow.Infrastructure.Messaging.Outbox;
using FinFlow.Infrastructure.Messaging.RabbitMq;
using FinFlow.IntegrationTests.Infrastructure;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;
using Xunit.Abstractions;

namespace FinFlow.IntegrationTests.Worker;

/// <summary>
/// Consumidor do Worker contra RabbitMQ e SQL Server reais (Testcontainers): ACK/NACK/DLQ,
/// falhas transitórias e persistentes, limite de concorrência, desligamento controlado e
/// reinício do consumo. As transações são gravadas direto no banco de teste e a mensagem é
/// publicada direto na fila, como o OutboxPublisherService faria.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class WorkerConsumerTests(IntegrationTestEnvironment env, ITestOutputHelper output)
{
    private static readonly Dictionary<string, string?> RetryRapido = new()
    {
        ["Worker:Processing:BaseDelay"] = "00:00:00.010",
        ["Worker:Processing:MaxDelay"] = "00:00:00.050",
        ["Worker:Consumer:ReconnectDelay"] = "00:00:00.200",
    };

    // ---------- Inicialização e conexão ----------

    [Fact]
    public async Task Inicializacao_ConectaAoRabbitMq_RegistraConsumidor_EDesligamentoRemoveConsumidor()
    {
        await using var worker = await WorkerHost.StartAsync(env, RetryRapido);

        await AguardarAsync(async () => await ConsumidoresAsync() >= 1, "consumidor registrado na fila");

        await worker.StopAsync();
        await AguardarAsync(async () => await ConsumidoresAsync() == 0, "consumidor removido no desligamento");
    }

    [Theory]
    [InlineData("Worker:Consumer:MaxConcurrency", "0")]
    [InlineData("Worker:Consumer:PrefetchCount", "0")]
    [InlineData("Worker:Recovery:BatchSize", "0")]
    [InlineData("Worker:Processing:MaxTransientAttempts", "0")]
    public async Task Inicializacao_ConfiguracaoInvalida_FalhaNaPartidaComMensagemClara(string chave, string valor)
    {
        var ex = await Assert.ThrowsAsync<OptionsValidationException>(() =>
            WorkerHost.StartAsync(env, new Dictionary<string, string?> { [chave] = valor }));
        output.WriteLine(ex.Message);
    }

    // ---------- Mensagem válida: processamento e ACK ----------

    [Fact]
    public async Task MensagemValida_ProcessaTransferencia_EConfirmaComAck()
    {
        var (origem, destino, id) = await CriarPendingAsync(100m, 0m, 40m);
        var dlqAntes = await ContarAsync(RabbitMqTopology.TransactionRequestedDeadLetterQueue);

        await using (var worker = await WorkerHost.StartAsync(env, RetryRapido))
        {
            await PublicarAsync(Mensagem(id, origem, destino, 40m));
            await AguardarStatusFinalAsync(id);
        } // ao fechar o canal, mensagens sem ACK voltariam para a fila

        Assert.Equal(TransactionStatus.Completed, (await env.GetTransactionAsync(id))!.Status);
        Assert.Equal(60m, await env.GetBalanceAsync(origem.AccountNumber));
        Assert.Equal(40m, await env.GetBalanceAsync(destino.AccountNumber));
        Assert.Equal(0u, await ContarAsync(RabbitMqTopology.TransactionRequestedQueue)); // ACK: não voltou
        Assert.Equal(dlqAntes, await ContarAsync(RabbitMqTopology.TransactionRequestedDeadLetterQueue));
    }

    // ---------- Mensagem inválida: DLQ, sem derrubar o consumidor ----------

    [Fact]
    public async Task MensagensInvalidas_VaoParaADlq_EOConsumidorContinuaProcessando()
    {
        var (origem, destino, id) = await CriarPendingAsync(100m, 0m, 10m);
        var dlqAntes = await ContarAsync(RabbitMqTopology.TransactionRequestedDeadLetterQueue);

        await using var worker = await WorkerHost.StartAsync(env, RetryRapido);
        await PublicarAsync(Encoding.UTF8.GetBytes("{ isto não é json"), "invalida-json");
        await PublicarAsync(Encoding.UTF8.GetBytes("""{"transactionId":"00000000-0000-0000-0000-000000000000"}"""), "invalida-sem-id");
        await PublicarAsync(Mensagem(id, origem, destino, 10m));

        await AguardarStatusFinalAsync(id);
        await AguardarAsync(async () => await ContarAsync(RabbitMqTopology.TransactionRequestedDeadLetterQueue) == dlqAntes + 2,
            "duas mensagens inválidas na DLQ");

        Assert.Equal(TransactionStatus.Completed, (await env.GetTransactionAsync(id))!.Status);
        Assert.Equal(90m, await env.GetBalanceAsync(origem.AccountNumber));
        Assert.Equal(10m, await env.GetBalanceAsync(destino.AccountNumber));
    }

    // ---------- Falha transitória: retry e NACK com requeue (nunca DLQ) ----------

    [Fact]
    public async Task FalhaTransitoria_QueEsgotaAsTentativas_DevolveAFila_EAReentregaConcluiUmaVez()
    {
        var (origem, destino, id) = await CriarPendingAsync(100m, 0m, 30m);
        var dlqAntes = await ContarAsync(RabbitMqTopology.TransactionRequestedDeadLetterQueue);

        // 2 tentativas por entrega e 3 falhas: a 1ª entrega esgota (→ NACK requeue), a 2ª conclui.
        var falhas = new FalhasSimuladas(origem.AccountNumber, restantes: 3, () => new ConcurrencyConflictException("simulado"));
        var config = new Dictionary<string, string?>(RetryRapido) { ["Worker:Processing:MaxTransientAttempts"] = "2" };

        await using var worker = await WorkerHost.StartAsync(env, config,
            s => s.InterceptarBloqueioDeContas(falhas.TalvezFalharAsync));
        await PublicarAsync(Mensagem(id, origem, destino, 30m));
        await AguardarStatusFinalAsync(id);

        Assert.Equal(0, falhas.Restantes);
        Assert.Equal(TransactionStatus.Completed, (await env.GetTransactionAsync(id))!.Status);
        Assert.Equal(70m, await env.GetBalanceAsync(origem.AccountNumber));
        Assert.Equal(30m, await env.GetBalanceAsync(destino.AccountNumber));
        Assert.Equal(dlqAntes, await ContarAsync(RabbitMqTopology.TransactionRequestedDeadLetterQueue));
    }

    // ---------- Falha persistente: DLQ sem loop; recuperação P1 conclui depois ----------

    [Fact]
    public async Task FalhaPersistente_VaiParaADlqSemLoop_SemMovimentar_EARecuperacaoConcluiUmaVez()
    {
        var (origem, destino, id) = await CriarPendingAsync(100m, 0m, 25m);
        var dlqAntes = await ContarAsync(RabbitMqTopology.TransactionRequestedDeadLetterQueue);

        var falhas = new FalhasSimuladas(origem.AccountNumber, restantes: int.MaxValue, () => new InvalidOperationException("simulado"));
        var config = new Dictionary<string, string?>(RetryRapido)
        {
            ["Worker:Processing:MaxPersistentAttempts"] = "2",
            ["Worker:Recovery:Enabled"] = "false",
        };

        await using (var worker = await WorkerHost.StartAsync(env, config,
                         s => s.InterceptarBloqueioDeContas(falhas.TalvezFalharAsync)))
        {
            await PublicarAsync(Mensagem(id, origem, destino, 25m));
            await AguardarAsync(async () => await ContarAsync(RabbitMqTopology.TransactionRequestedDeadLetterQueue) == dlqAntes + 1,
                "mensagem na DLQ");

            // Sem loop: depois de ir para a DLQ, nenhuma nova tentativa acontece.
            var tentativas = int.MaxValue - falhas.Restantes;
            await Task.Delay(TimeSpan.FromSeconds(1));
            Assert.Equal(2, tentativas);
            Assert.Equal(tentativas, int.MaxValue - falhas.Restantes);
        }

        // Etapa 1 comitada, etapa 2 sempre revertida: Processing e nenhum valor movimentado.
        Assert.Equal(TransactionStatus.Processing, (await env.GetTransactionAsync(id))!.Status);
        Assert.Equal(100m, await env.GetBalanceAsync(origem.AccountNumber));
        Assert.Equal(0m, await env.GetBalanceAsync(destino.AccountNumber));
        Assert.Equal(0u, await ContarAsync(RabbitMqTopology.TransactionRequestedQueue));

        // Falha resolvida: a recuperação P1 (sem mensagem alguma) conclui a transação uma única vez.
        var recuperacao = new Dictionary<string, string?>(RetryRapido)
        {
            ["Worker:Recovery:Interval"] = "00:00:00.250",
            ["Worker:Recovery:StaleAfter"] = "00:00:00",
        };
        await using (var worker = await WorkerHost.StartAsync(env, recuperacao))
            await AguardarStatusFinalAsync(id);

        Assert.Equal(TransactionStatus.Completed, (await env.GetTransactionAsync(id))!.Status);
        Assert.Equal(75m, await env.GetBalanceAsync(origem.AccountNumber));
        Assert.Equal(25m, await env.GetBalanceAsync(destino.AccountNumber));
    }

    // ---------- Concorrência ----------

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public async Task Concorrencia_RespeitaOLimiteDeMensagensSimultaneas(int limite)
    {
        const int quantidade = 6;
        var transferencias = new List<(Account Origem, Account Destino, Guid Id)>();
        for (var i = 0; i < quantidade; i++)
            transferencias.Add(await CriarPendingAsync(100m, 0m, 10m));
        var origens = transferencias.Select(t => t.Origem.AccountNumber).ToHashSet();

        int atual = 0, maximo = 0;
        async Task Observar(string conta)
        {
            if (!origens.Contains(conta)) return;
            var n = Interlocked.Increment(ref atual);
            int anterior;
            while (n > (anterior = Volatile.Read(ref maximo)) && Interlocked.CompareExchange(ref maximo, n, anterior) != anterior) { }
            try { await Task.Delay(300); }
            finally { Interlocked.Decrement(ref atual); }
        }

        var config = new Dictionary<string, string?>(RetryRapido) { ["Worker:Consumer:MaxConcurrency"] = limite.ToString() };
        await using var worker = await WorkerHost.StartAsync(env, config, s => s.InterceptarBloqueioDeContas(Observar));

        foreach (var (origem, destino, id) in transferencias)
            await PublicarAsync(Mensagem(id, origem, destino, 10m));
        foreach (var (_, _, id) in transferencias)
            await AguardarStatusFinalAsync(id);

        output.WriteLine($"limite {limite}: máximo observado de {maximo} transferências simultâneas");
        Assert.InRange(maximo, 1, limite);
        if (limite > 1)
            Assert.True(maximo >= 2, "com limite > 1, as mensagens deveriam ser processadas em paralelo");

        foreach (var (origem, destino, id) in transferencias)
        {
            Assert.Equal(TransactionStatus.Completed, (await env.GetTransactionAsync(id))!.Status);
            Assert.Equal(90m, await env.GetBalanceAsync(origem.AccountNumber));
            Assert.Equal(10m, await env.GetBalanceAsync(destino.AccountNumber));
        }
    }

    [Fact]
    public async Task Concorrencia_MesmaTransferenciaEntreguePorVariasCopiasEmParalelo_MovimentaUmaUnicaVez()
    {
        var (origem, destino, id) = await CriarPendingAsync(100m, 0m, 50m);
        var config = new Dictionary<string, string?>(RetryRapido) { ["Worker:Consumer:MaxConcurrency"] = "8" };

        await using (var worker = await WorkerHost.StartAsync(env, config))
        {
            for (var i = 0; i < 8; i++)
                await PublicarAsync(Mensagem(id, origem, destino, 50m), $"copia-{i}");
            await AguardarStatusFinalAsync(id);
            await AguardarAsync(async () => await ContarAsync(RabbitMqTopology.TransactionRequestedQueue) == 0 && worker.Consumer.InFlight == 0,
                "todas as cópias confirmadas");
        }

        Assert.Equal(TransactionStatus.Completed, (await env.GetTransactionAsync(id))!.Status);
        Assert.Equal(50m, await env.GetBalanceAsync(origem.AccountNumber));
        Assert.Equal(50m, await env.GetBalanceAsync(destino.AccountNumber));
    }

    // ---------- Desligamento controlado ----------

    [Fact]
    public async Task Desligamento_ComMensagemEmProcessamento_EsperaConcluir_ConfirmaENaoReentrega()
    {
        var (origem, destino, id) = await CriarPendingAsync(100m, 0m, 20m);
        var dlqAntes = await ContarAsync(RabbitMqTopology.TransactionRequestedDeadLetterQueue);

        var iniciou = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task Lento(string conta)
        {
            if (conta != origem.AccountNumber) return;
            iniciou.TrySetResult();
            await Task.Delay(TimeSpan.FromSeconds(1.5));
        }

        await using var worker = await WorkerHost.StartAsync(env, RetryRapido, s => s.InterceptarBloqueioDeContas(Lento));
        await PublicarAsync(Mensagem(id, origem, destino, 20m));
        await iniciou.Task.WaitAsync(TimeSpan.FromSeconds(15));

        var relogio = Stopwatch.StartNew();
        await worker.StopAsync(); // SIGTERM no meio da transferência
        output.WriteLine($"desligamento levou {relogio.ElapsedMilliseconds} ms");

        // O desligamento esperou a transferência terminar e confirmou a mensagem antes de fechar o canal.
        Assert.Equal(0, worker.Consumer.InFlight);
        Assert.Equal(TransactionStatus.Completed, (await env.GetTransactionAsync(id))!.Status);
        Assert.Equal(80m, await env.GetBalanceAsync(origem.AccountNumber));
        Assert.Equal(20m, await env.GetBalanceAsync(destino.AccountNumber));
        Assert.Equal(0u, await ContarAsync(RabbitMqTopology.TransactionRequestedQueue)); // não ficou para reentrega
        Assert.Equal(dlqAntes, await ContarAsync(RabbitMqTopology.TransactionRequestedDeadLetterQueue));
    }

    // ---------- Reinício do consumo ----------

    [Fact]
    public async Task ConsumidorCanceladoPeloBroker_FilaRemovida_OWorkerReiniciaOConsumo()
    {
        await using var worker = await WorkerHost.StartAsync(env, RetryRapido);
        await AguardarAsync(async () => await ConsumidoresAsync() >= 1, "consumidor registrado");

        // Remover a fila faz o broker cancelar o consumidor (o canal continua aberto).
        await using (var connection = await ConectarAsync())
        await using (var channel = await connection.CreateChannelAsync())
            await channel.QueueDeleteAsync(RabbitMqTopology.TransactionRequestedQueue, ifUnused: false, ifEmpty: false);

        // O Worker percebe, recria a topologia e volta a consumir.
        await AguardarAsync(async () => await ConsumidoresAsync() >= 1, "consumidor registrado novamente");

        var (origem, destino, id) = await CriarPendingAsync(100m, 0m, 15m);
        await PublicarAsync(Mensagem(id, origem, destino, 15m));
        await AguardarStatusFinalAsync(id);

        Assert.Equal(TransactionStatus.Completed, (await env.GetTransactionAsync(id))!.Status);
        Assert.Equal(85m, await env.GetBalanceAsync(origem.AccountNumber));
    }

    // ---------- Helpers ----------

    private async Task<(Account Origem, Account Destino, Guid Id)> CriarPendingAsync(decimal saldoOrigem, decimal saldoDestino, decimal valor)
    {
        var origem = await env.CreateAccountAsync(saldoOrigem);
        var destino = await env.CreateAccountAsync(saldoDestino);
        var transacao = new Transaction(origem.AccountNumber, destino.AccountNumber, valor);
        await using var db = env.CreateDbContext();
        db.Transactions.Add(transacao);
        await db.SaveChangesAsync();
        return (origem, destino, transacao.Id);
    }

    private static byte[] Mensagem(Guid id, Account origem, Account destino, decimal valor) =>
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
            new TransactionRequested(id, origem.AccountNumber, destino.AccountNumber, valor, "BRL", DateTimeOffset.UtcNow),
            MessagingJson.Options));

    private async Task PublicarAsync(byte[] corpo, string? messageId = null)
    {
        await using var connection = await ConectarAsync();
        await using var channel = await connection.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true));
        await RabbitMqTopology.DeclareAsync(channel);
        await channel.BasicPublishAsync(
            RabbitMqTopology.Exchange, TransactionRequested.MessageType, mandatory: true,
            basicProperties: new BasicProperties { MessageId = messageId ?? Guid.NewGuid().ToString(), DeliveryMode = DeliveryModes.Persistent },
            body: corpo);
    }

    private async Task<uint> ContarAsync(string fila)
    {
        await using var connection = await ConectarAsync();
        await using var channel = await connection.CreateChannelAsync();
        await RabbitMqTopology.DeclareAsync(channel);
        return await channel.MessageCountAsync(fila);
    }

    /// <summary>Consumidores na fila principal; 0 se a fila não existir no momento.</summary>
    private async Task<uint> ConsumidoresAsync()
    {
        await using var connection = await ConectarAsync();
        await using var channel = await connection.CreateChannelAsync();
        try
        {
            return (await channel.QueueDeclarePassiveAsync(RabbitMqTopology.TransactionRequestedQueue)).ConsumerCount;
        }
        catch (OperationInterruptedException)
        {
            return 0; // 404: a fila ainda não foi recriada
        }
    }

    private Task<IConnection> ConectarAsync() => new ConnectionFactory
    {
        HostName = env.Settings["RabbitMq:HostName"]!,
        Port = int.Parse(env.Settings["RabbitMq:Port"]!),
        UserName = env.Settings["RabbitMq:UserName"]!,
        Password = env.Settings["RabbitMq:Password"]!
    }.CreateConnectionAsync();

    private async Task AguardarStatusFinalAsync(Guid id) =>
        await AguardarAsync(async () => (await env.GetTransactionAsync(id))?.Status
            is TransactionStatus.Completed or TransactionStatus.Failed or TransactionStatus.Cancelled, $"status final de {id}");

    private static async Task AguardarAsync(Func<Task<bool>> condicao, string descricao, int segundos = 20)
    {
        var limite = DateTime.UtcNow.AddSeconds(segundos);
        while (DateTime.UtcNow < limite)
        {
            if (await condicao()) return;
            await Task.Delay(100);
        }
        throw new TimeoutException($"Tempo esgotado aguardando: {descricao}.");
    }
}
