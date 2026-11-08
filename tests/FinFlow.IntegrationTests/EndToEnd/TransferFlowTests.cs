using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FinFlow.Application.Accounts;
using FinFlow.Application.Messaging;
using FinFlow.IntegrationTests.Infrastructure;
using FinFlow.Infrastructure.Messaging.Outbox;
using FinFlow.Infrastructure.Messaging.RabbitMq;
using Microsoft.EntityFrameworkCore;
using RabbitMQ.Client;

namespace FinFlow.IntegrationTests.EndToEnd;

/// <summary>
/// Fluxo completo: API → Outbox → RabbitMQ → Worker → SQL Server.
/// Nada é mockado: a API publica de verdade e o Worker consome de verdade.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class TransferFlowTests(IntegrationTestEnvironment env) : IAsyncLifetime
{
    private FinFlowApiFactory _api = null!;
    private WorkerHost _worker = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _api = new FinFlowApiFactory(env, enableOutboxPublisher: true);
        _client = await _api.LoginAsync(IntegrationTestEnvironment.AdminEmail, IntegrationTestEnvironment.AdminPassword);
        _worker = await WorkerHost.StartAsync(env);
    }

    public async Task DisposeAsync()
    {
        await _worker.DisposeAsync();
        await _api.DisposeAsync();
    }

    [Fact]
    public async Task Transferencia500_DeAnaParaBruno_TerminaCompleted_ESaldosAtualizados()
    {
        var ana = await env.CreateAccountAsync(1000m, holder: "Ana");
        var bruno = await env.CreateAccountAsync(250m, holder: "Bruno");

        var criada = await _client.CreateTransactionAsync(ana.AccountNumber, bruno.AccountNumber, 500m);
        Assert.Equal("Pending", criada.Status);

        var final = await _client.WaitForFinalStatusAsync(criada.Id);

        Assert.Equal("Completed", final.Status);
        Assert.Equal(500m, (await SaldoViaApiAsync(ana.Id)).Balance);
        Assert.Equal(750m, (await SaldoViaApiAsync(bruno.Id)).Balance);

        // O outbox registrou a confirmação do broker.
        await using var db = env.CreateDbContext();
        var mensagem = await db.OutboxMessages.SingleAsync(m => m.Payload.Contains(criada.Id.ToString()));
        Assert.NotNull(mensagem.ProcessedAt);
    }

    [Fact]
    public async Task SaldoInsuficiente_TerminaFailed_ComMotivo_ESaldosIntactos()
    {
        var origem = await env.CreateAccountAsync(100m);
        var destino = await env.CreateAccountAsync(0m);

        var criada = await _client.CreateTransactionAsync(origem.AccountNumber, destino.AccountNumber, 500m);
        var final = await _client.WaitForFinalStatusAsync(criada.Id);

        Assert.Equal("Failed", final.Status);
        Assert.Equal("Saldo insuficiente na conta de origem.", final.FailureReason);
        Assert.Equal(100m, (await SaldoViaApiAsync(origem.Id)).Balance);
        Assert.Equal(0m, (await SaldoViaApiAsync(destino.Id)).Balance);
    }

    [Fact]
    public async Task MensagemDuplicadaNoRabbitMq_NaoTransfereDuasVezes()
    {
        var ana = await env.CreateAccountAsync(1000m);
        var bruno = await env.CreateAccountAsync(250m);

        var criada = await _client.CreateTransactionAsync(ana.AccountNumber, bruno.AccountNumber, 500m);
        Assert.Equal("Completed", (await _client.WaitForFinalStatusAsync(criada.Id)).Status);

        // Reenvia a MESMA mensagem duas vezes direto no RabbitMQ (simula reentrega/duplicidade).
        var duplicada = new TransactionRequested(
            criada.Id, ana.AccountNumber, bruno.AccountNumber, 500m, "BRL", criada.CreatedAt);
        await PublicarDiretoNoRabbitAsync(duplicada, vezes: 2);

        // O Worker consome em ordem: quando esta "sentinela" terminar, as duplicadas já foram processadas.
        var sentinela = await _client.CreateTransactionAsync(ana.AccountNumber, bruno.AccountNumber, 1m);
        Assert.Equal("Completed", (await _client.WaitForFinalStatusAsync(sentinela.Id)).Status);

        // Só a transferência original (500) e a sentinela (1) foram aplicadas.
        Assert.Equal(499m, (await SaldoViaApiAsync(ana.Id)).Balance);
        Assert.Equal(751m, (await SaldoViaApiAsync(bruno.Id)).Balance);
    }

    [Fact]
    public async Task VariasTransferenciasSimultaneas_SaldosFinaisConsistentes()
    {
        var origem = await env.CreateAccountAsync(1000m);
        var destino = await env.CreateAccountAsync(0m);

        var criadas = await Task.WhenAll(Enumerable.Range(0, 10)
            .Select(_ => _client.CreateTransactionAsync(origem.AccountNumber, destino.AccountNumber, 50m)));
        var finais = await Task.WhenAll(criadas.Select(c => _client.WaitForFinalStatusAsync(c.Id)));

        Assert.All(finais, f => Assert.Equal("Completed", f.Status));
        Assert.Equal(500m, (await SaldoViaApiAsync(origem.Id)).Balance);
        Assert.Equal(500m, (await SaldoViaApiAsync(destino.Id)).Balance);
    }

    private async Task<AccountBalanceResponse> SaldoViaApiAsync(Guid accountId) =>
        (await _client.GetFromJsonAsync<AccountBalanceResponse>($"/api/accounts/{accountId}/balance"))!;

    private async Task PublicarDiretoNoRabbitAsync(TransactionRequested message, int vezes)
    {
        var factory = new ConnectionFactory
        {
            HostName = env.Settings["RabbitMq:HostName"]!,
            Port = int.Parse(env.Settings["RabbitMq:Port"]!),
            UserName = env.Settings["RabbitMq:UserName"]!,
            Password = env.Settings["RabbitMq:Password"]!
        };

        await using var connection = await factory.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true));

        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message, MessagingJson.Options));

        for (var i = 0; i < vezes; i++)
        {
            await channel.BasicPublishAsync(
                RabbitMqTopology.Exchange,
                TransactionRequested.MessageType,
                mandatory: true,
                basicProperties: new BasicProperties { MessageId = $"duplicada-{i}", DeliveryMode = DeliveryModes.Persistent },
                body: body);
        }
    }
}
