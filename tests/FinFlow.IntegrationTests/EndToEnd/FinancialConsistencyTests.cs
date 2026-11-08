using System.Net;
using System.Text;
using System.Text.Json;
using FinFlow.Application.Messaging;
using FinFlow.Application.Transactions;
using FinFlow.Domain.Entities;
using FinFlow.Domain.Enums;
using FinFlow.Domain.Services;
using FinFlow.Infrastructure.Messaging.Outbox;
using FinFlow.Infrastructure.Messaging.RabbitMq;
using FinFlow.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RabbitMQ.Client;
using Xunit.Abstractions;

namespace FinFlow.IntegrationTests.EndToEnd;

/// <summary>
/// Invariantes financeiras sob carga, com o pipeline real (API → Outbox → RabbitMQ → Worker → SQL):
/// - dinheiro não é criado nem destruído (soma dos saldos é constante);
/// - nenhum saldo fica negativo;
/// - cada saldo é exatamente o inicial + créditos − débitos das transações Completed;
/// - nenhuma transação fica presa em Pending/Processing e nada vai para a DLQ.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class FinancialConsistencyTests(IntegrationTestEnvironment env, ITestOutputHelper output) : IAsyncLifetime
{
    private FinFlowApiFactory _api = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _api = new FinFlowApiFactory(env, enableOutboxPublisher: true);
        _client = await _api.LoginAsync(IntegrationTestEnvironment.AdminEmail, IntegrationTestEnvironment.AdminPassword);
    }

    public async Task DisposeAsync() => await _api.DisposeAsync();

    [Fact]
    public async Task TresWorkers_TransferenciasCruzadasConcorrentes_ConservamOTotal_ENadaFicaPreso()
    {
        const decimal saldoInicial = 200m;
        const int quantidade = 60;

        var contas = new List<Account>();
        for (var i = 0; i < 3; i++)
            contas.Add(await env.CreateAccountAsync(saldoInicial));
        var totalInicial = saldoInicial * contas.Count;

        var dlqAntes = await ContarMensagensAsync(RabbitMqTopology.TransactionRequestedDeadLetterQueue);

        // Três instâncias do Worker competindo pela mesma fila (escala horizontal).
        await using var worker1 = await WorkerHost.StartAsync(env);
        await using var worker2 = await WorkerHost.StartAsync(env);
        await using var worker3 = await WorkerHost.StartAsync(env);

        // Pares aleatórios (inclui A→B e B→A simultâneos) com valores que esgotam saldos.
        var random = new Random(20261106);
        var pedidos = Enumerable.Range(0, quantidade).Select(_ =>
        {
            var origem = random.Next(contas.Count);
            var destino = (origem + 1 + random.Next(contas.Count - 1)) % contas.Count;
            return (Origem: contas[origem], Destino: contas[destino], Valor: (decimal)random.Next(5, 61));
        }).ToList();

        var criadas = await Task.WhenAll(pedidos.Select(p =>
            _client.CreateTransactionAsync(p.Origem.AccountNumber, p.Destino.AccountNumber, p.Valor)));
        TransactionStatusResponse[] finais;
        try
        {
            finais = await Task.WhenAll(criadas.Select(c =>
                _client.WaitForFinalStatusAsync(c.Id, TimeSpan.FromSeconds(30))));
        }
        catch (TimeoutException)
        {
            await using var diag = env.CreateDbContext();
            var presos = await diag.Transactions.AsNoTracking()
                .Where(t => criadas.Select(c => c.Id).Contains(t.Id) && t.Status != TransactionStatus.Completed && t.Status != TransactionStatus.Failed)
                .ToListAsync();
            var outbox = await diag.OutboxMessages.AsNoTracking().Where(m => m.ProcessedAt == null).CountAsync();
            output.WriteLine($"DIAG presos: {string.Join("; ", presos.Select(t => $"{t.Id} {t.Status}"))}");
            output.WriteLine($"DIAG outbox não publicado: {outbox}, fila: {await ContarMensagensAsync(RabbitMqTopology.TransactionRequestedQueue)}, DLQ: {await ContarMensagensAsync(RabbitMqTopology.TransactionRequestedDeadLetterQueue)} (antes: {dlqAntes})");
            throw;
        }

        // Só dois desfechos são aceitáveis: concluída ou recusada por saldo.
        Assert.All(finais, f => Assert.True(
            f.Status == "Completed" ||
            (f.Status == "Failed" && f.FailureReason == TransferFailureReasons.InsufficientFunds),
            $"{f.Id}: {f.Status} ({f.FailureReason})"));

        await using var db = env.CreateDbContext();
        var ids = criadas.Select(c => c.Id).ToList();
        var transacoes = await db.Transactions.AsNoTracking().Where(t => ids.Contains(t.Id)).ToListAsync();
        var numeros = contas.Select(c => c.AccountNumber).ToList();
        var saldos = await db.Accounts.AsNoTracking()
            .Where(a => numeros.Contains(a.AccountNumber))
            .ToDictionaryAsync(a => a.AccountNumber, a => a.Balance);

        Assert.Equal(totalInicial, saldos.Values.Sum());
        Assert.All(saldos.Values, saldo => Assert.True(saldo >= 0, $"Saldo negativo: {saldo}"));

        var concluidas = transacoes.Where(t => t.Status == TransactionStatus.Completed).ToList();
        foreach (var conta in contas)
        {
            var esperado = saldoInicial
                + concluidas.Where(t => t.DestinationAccount == conta.AccountNumber).Sum(t => t.Amount)
                - concluidas.Where(t => t.SourceAccount == conta.AccountNumber).Sum(t => t.Amount);
            Assert.Equal(esperado, saldos[conta.AccountNumber]);
        }

        output.WriteLine($"Completed: {concluidas.Count}, Failed por saldo: {transacoes.Count - concluidas.Count}, saldos: {string.Join(", ", saldos.Values)}");
        Assert.NotEmpty(concluidas);
        Assert.DoesNotContain(transacoes, t => t.Status is TransactionStatus.Pending or TransactionStatus.Processing);
        Assert.Equal(dlqAntes, await ContarMensagensAsync(RabbitMqTopology.TransactionRequestedDeadLetterQueue));
        // Nada ficou girando na fila (nenhum requeue em loop).
        Assert.Equal(0u, await ContarMensagensAsync(RabbitMqTopology.TransactionRequestedQueue));
    }

    // ---------- Recuperação de transações presas em Processing ----------

    private static readonly Dictionary<string, string?> RecuperacaoRapida = new()
    {
        ["Worker:Recovery:Interval"] = "00:00:00.250",
        ["Worker:Recovery:StaleAfter"] = "00:00:01",
    };

    [Fact]
    public async Task Recuperacao_TransacaoPresaEmProcessingSemMensagem_EConcluidaUmaVez()
    {
        var origem = await env.CreateAccountAsync(100m);
        var destino = await env.CreateAccountAsync(0m);

        // Cenário real do defeito: etapa 1 comitada, mensagem perdida (foi para a DLQ).
        var presa = await CriarPresaEmProcessingAsync(origem, destino, 25m);

        await using var worker = await WorkerHost.StartAsync(env, RecuperacaoRapida);
        var final = await _client.WaitForFinalStatusAsync(presa, TimeSpan.FromSeconds(15));

        Assert.Equal("Completed", final.Status);
        Assert.Equal(75m, await env.GetBalanceAsync(origem.AccountNumber));
        Assert.Equal(25m, await env.GetBalanceAsync(destino.AccountNumber));
    }

    [Fact]
    public async Task Recuperacao_DisputandoComMensagensDuplicadasEDoisWorkers_NaoDuplicaMovimentacao()
    {
        var origem = await env.CreateAccountAsync(100m);
        var destino = await env.CreateAccountAsync(0m);
        var presa = await CriarPresaEmProcessingAsync(origem, destino, 25m);

        // Recuperação dos dois Workers + 3 cópias da mensagem chegando ao mesmo tempo.
        await using var worker1 = await WorkerHost.StartAsync(env, RecuperacaoRapida);
        await using var worker2 = await WorkerHost.StartAsync(env, RecuperacaoRapida);
        await PublicarDiretoNoRabbitAsync(presa, origem, destino, 25m, vezes: 3);

        Assert.Equal("Completed", (await _client.WaitForFinalStatusAsync(presa, TimeSpan.FromSeconds(15))).Status);

        // Dá tempo para todas as cópias e varreduras terminarem antes de conferir.
        await Task.Delay(TimeSpan.FromSeconds(2));
        Assert.Equal(75m, await env.GetBalanceAsync(origem.AccountNumber));
        Assert.Equal(25m, await env.GetBalanceAsync(destino.AccountNumber));
        Assert.Equal(0u, await ContarMensagensAsync(RabbitMqTopology.TransactionRequestedQueue));
    }

    // ---------- Recuperação de transações presas em Pending ----------

    [Fact]
    public async Task RecuperacaoDePending_DisputandoComConsumerEDoisWorkers_NaoDuplicaMovimentacao()
    {
        var origem = await env.CreateAccountAsync(100m);
        var destino = await env.CreateAccountAsync(0m);

        // Pending antigo cuja mensagem "reaparece" (3 cópias) enquanto dois Workers também o recuperam.
        var transacao = new Transaction(origem.AccountNumber, destino.AccountNumber, 25m);
        await using (var db = env.CreateDbContext())
        {
            db.Transactions.Add(transacao);
            await db.SaveChangesAsync();
        }

        var recuperacaoDePending = new Dictionary<string, string?>(RecuperacaoRapida)
        {
            ["Worker:Recovery:PendingStaleAfter"] = "00:00:00.500",
        };
        await Task.Delay(TimeSpan.FromSeconds(1)); // fica mais antiga que o limite

        await using var worker1 = await WorkerHost.StartAsync(env, recuperacaoDePending);
        await using var worker2 = await WorkerHost.StartAsync(env, recuperacaoDePending);
        await PublicarDiretoNoRabbitAsync(transacao.Id, origem, destino, 25m, vezes: 3);

        Assert.Equal("Completed", (await _client.WaitForFinalStatusAsync(transacao.Id, TimeSpan.FromSeconds(15))).Status);

        // Dá tempo para todas as cópias e varreduras terminarem antes de conferir.
        await Task.Delay(TimeSpan.FromSeconds(2));
        Assert.Equal(75m, await env.GetBalanceAsync(origem.AccountNumber));
        Assert.Equal(25m, await env.GetBalanceAsync(destino.AccountNumber));
        Assert.Equal(0u, await ContarMensagensAsync(RabbitMqTopology.TransactionRequestedQueue));
    }

    private async Task<Guid> CriarPresaEmProcessingAsync(Account origem, Account destino, decimal valor)
    {
        var transacao = new Transaction(origem.AccountNumber, destino.AccountNumber, valor);
        transacao.StartProcessing();
        await using var db = env.CreateDbContext();
        db.Transactions.Add(transacao);
        await db.SaveChangesAsync();

        // Envelhece o registro (como se estivesse parado há 10 minutos).
        await db.Database.ExecuteSqlAsync(
            $"UPDATE [Transactions] SET [UpdatedAt] = DATEADD(MINUTE, -10, SYSDATETIMEOFFSET()) WHERE [Id] = {transacao.Id}");
        return transacao.Id;
    }

    private async Task PublicarDiretoNoRabbitAsync(Guid transactionId, Account origem, Account destino, decimal valor, int vezes)
    {
        await using var connection = await CriarConexaoAsync();
        await using var channel = await connection.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true));

        // O consumer declara a topologia em background após o StartAsync do Worker; sem declarar aqui,
        // num container novo a fila ainda pode não existir e o broker devolve NO_ROUTE.
        await RabbitMqTopology.DeclareAsync(channel);

        var message = new TransactionRequested(
            transactionId, origem.AccountNumber, destino.AccountNumber, valor, "BRL", DateTimeOffset.UtcNow);
        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message, MessagingJson.Options));

        for (var i = 0; i < vezes; i++)
        {
            await channel.BasicPublishAsync(
                RabbitMqTopology.Exchange, TransactionRequested.MessageType, mandatory: true,
                basicProperties: new BasicProperties { MessageId = $"recuperacao-{i}", DeliveryMode = DeliveryModes.Persistent },
                body: body);
        }
    }

    [Fact]
    public async Task CancelamentoDisputandoComOWorker_SempreTerminaConsistente()
    {
        var origem = await env.CreateAccountAsync(1000m);
        var destino = await env.CreateAccountAsync(0m);
        await using var worker = await WorkerHost.StartAsync(env);

        var transferidoEsperado = 0m;
        for (var i = 0; i < 15; i++)
        {
            var criada = await _client.CreateTransactionAsync(origem.AccountNumber, destino.AccountNumber, 10m);

            // Atraso variável (0–2,1 s) para cair antes, durante e depois do processamento.
            await Task.Delay(i * 150);
            var cancel = await _client.PatchAsync($"/api/transactions/{criada.Id}/cancel", null);
            var final = await _client.WaitForFinalStatusAsync(criada.Id);

            if (cancel.StatusCode == HttpStatusCode.OK)
            {
                Assert.Equal("Cancelled", final.Status);
            }
            else
            {
                Assert.Equal(HttpStatusCode.Conflict, cancel.StatusCode);
                Assert.Equal("Completed", final.Status);
                transferidoEsperado += 10m;
            }
        }

        output.WriteLine($"Canceladas: {15 - transferidoEsperado / 10m}, concluídas: {transferidoEsperado / 10m}");
        Assert.Equal(1000m - transferidoEsperado, await env.GetBalanceAsync(origem.AccountNumber));
        Assert.Equal(transferidoEsperado, await env.GetBalanceAsync(destino.AccountNumber));
    }

    [Fact]
    public async Task Reentrega_DeTransacaoPresaEmProcessing_ConcluiUmaUnicaVez()
    {
        var origem = await env.CreateAccountAsync(100m);
        var destino = await env.CreateAccountAsync(0m);

        // Simula queda do Worker depois da etapa 1 (Pending → Processing já comitado), antes do débito.
        var transacao = new Transaction(origem.AccountNumber, destino.AccountNumber, 40m);
        transacao.StartProcessing();
        await using (var db = env.CreateDbContext())
        {
            db.Transactions.Add(transacao);
            await db.SaveChangesAsync();
        }

        await using var services = env.CreateServices();
        var resultados = new List<ProcessTransactionOutcome>();
        for (var i = 0; i < 2; i++)
        {
            await using var scope = services.CreateAsyncScope();
            resultados.Add(await scope.ServiceProvider.GetRequiredService<ProcessTransactionHandler>()
                .HandleAsync(transacao.Id));
        }

        Assert.Equal([ProcessTransactionOutcome.Completed, ProcessTransactionOutcome.Skipped], resultados);
        Assert.Equal(60m, await env.GetBalanceAsync(origem.AccountNumber));
        Assert.Equal(40m, await env.GetBalanceAsync(destino.AccountNumber));
    }

    private async Task<uint> ContarMensagensAsync(string fila)
    {
        await using var connection = await CriarConexaoAsync();
        await using var channel = await connection.CreateChannelAsync();
        await RabbitMqTopology.DeclareAsync(channel);
        return await channel.MessageCountAsync(fila);
    }

    private Task<IConnection> CriarConexaoAsync() => new ConnectionFactory
    {
        HostName = env.Settings["RabbitMq:HostName"]!,
        Port = int.Parse(env.Settings["RabbitMq:Port"]!),
        UserName = env.Settings["RabbitMq:UserName"]!,
        Password = env.Settings["RabbitMq:Password"]!
    }.CreateConnectionAsync();
}
