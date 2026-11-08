using FinFlow.Application.Common;
using FinFlow.Application.Transactions;
using FinFlow.Domain.Entities;
using FinFlow.Domain.Enums;
using FinFlow.Domain.Services;
using FinFlow.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace FinFlow.IntegrationTests.Worker;

/// <summary>
/// Caso de uso do Worker com SQL Server real (sem RabbitMQ): valida que débito,
/// crédito e status são persistidos juntos e que o processamento é idempotente,
/// inclusive com várias instâncias processando ao mesmo tempo.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class ProcessTransactionHandlerTests(IntegrationTestEnvironment env)
{
    [Fact]
    public async Task Processar_Transferencia500_PersisteSaldosEStatusCompleted()
    {
        var ana = await env.CreateAccountAsync(1000m);
        var bruno = await env.CreateAccountAsync(250m);
        var transacao = await CriarTransacaoAsync(ana, bruno, 500m);

        var outcome = await ProcessarAsync(transacao.Id);

        Assert.Equal(ProcessTransactionOutcome.Completed, outcome);
        Assert.Equal(500m, await env.GetBalanceAsync(ana.AccountNumber));
        Assert.Equal(750m, await env.GetBalanceAsync(bruno.AccountNumber));
        var salva = await env.GetTransactionAsync(transacao.Id);
        Assert.Equal(TransactionStatus.Completed, salva!.Status);
        Assert.Null(salva.FailureReason);
    }

    public static TheoryData<string, decimal, string, bool, decimal, string, bool> CenariosDeFalha => new()
    {
        // cenário, saldoOrigem, moedaOrigem, origemAtiva, saldoDestino, moedaDestino, destinoAtivo
        { "saldo insuficiente", 100m, "BRL", true, 0m, "BRL", true },
        { "origem inativa", 1000m, "BRL", false, 0m, "BRL", true },
        { "destino inativo", 1000m, "BRL", true, 0m, "BRL", false },
        { "moedas incompatíveis", 1000m, "BRL", true, 0m, "USD", true },
    };

    [Theory]
    [MemberData(nameof(CenariosDeFalha))]
    public async Task Processar_RegraViolada_MarcaFailedComMotivo_ESaldosIntactos(
        string cenario, decimal saldoOrigem, string moedaOrigem, bool origemAtiva,
        decimal saldoDestino, string moedaDestino, bool destinoAtivo)
    {
        var origem = await env.CreateAccountAsync(saldoOrigem, moedaOrigem, origemAtiva);
        var destino = await env.CreateAccountAsync(saldoDestino, moedaDestino, destinoAtivo);
        var transacao = await CriarTransacaoAsync(origem, destino, 500m);

        var outcome = await ProcessarAsync(transacao.Id);

        Assert.Equal(ProcessTransactionOutcome.Failed, outcome);
        var salva = await env.GetTransactionAsync(transacao.Id);
        Assert.Equal(TransactionStatus.Failed, salva!.Status);
        Assert.False(string.IsNullOrEmpty(salva.FailureReason), cenario);
        Assert.Equal(saldoOrigem, await env.GetBalanceAsync(origem.AccountNumber));
        Assert.Equal(saldoDestino, await env.GetBalanceAsync(destino.AccountNumber));
    }

    [Fact]
    public async Task Processar_ContaDeOrigemRemovidaDoBanco_MarcaFailed()
    {
        var destino = await env.CreateAccountAsync(0m);
        var transacao = await CriarTransacaoAsync(
            IntegrationTestEnvironment.NewAccountNumber(), destino.AccountNumber, 10m);

        var outcome = await ProcessarAsync(transacao.Id);

        Assert.Equal(ProcessTransactionOutcome.Failed, outcome);
        Assert.Equal(TransferFailureReasons.SourceNotFound, (await env.GetTransactionAsync(transacao.Id))!.FailureReason);
    }

    [Fact]
    public async Task Idempotencia_MesmaMensagemTresVezes_TransfereUmaUnicaVez()
    {
        var ana = await env.CreateAccountAsync(1000m);
        var bruno = await env.CreateAccountAsync(250m);
        var transacao = await CriarTransacaoAsync(ana, bruno, 500m);

        var resultados = new List<ProcessTransactionOutcome>();
        for (var i = 0; i < 3; i++)
            resultados.Add(await ProcessarAsync(transacao.Id));

        Assert.Equal(
            [ProcessTransactionOutcome.Completed, ProcessTransactionOutcome.Skipped, ProcessTransactionOutcome.Skipped],
            resultados);
        Assert.Equal(500m, await env.GetBalanceAsync(ana.AccountNumber));
        Assert.Equal(750m, await env.GetBalanceAsync(bruno.AccountNumber));
    }

    [Fact]
    public async Task Idempotencia_CincoWorkersProcessandoAMesmaMensagemEmParalelo_TransfereUmaUnicaVez()
    {
        var ana = await env.CreateAccountAsync(1000m);
        var bruno = await env.CreateAccountAsync(250m);
        var transacao = await CriarTransacaoAsync(ana, bruno, 500m);

        await using var services = env.CreateServices();
        var resultados = await Task.WhenAll(
            Enumerable.Range(0, 5).Select(_ => ProcessarComRetryAsync(services, transacao.Id)));

        Assert.Single(resultados, r => r == ProcessTransactionOutcome.Completed);
        Assert.All(resultados.Where(r => r != ProcessTransactionOutcome.Completed),
            r => Assert.Equal(ProcessTransactionOutcome.Skipped, r));
        Assert.Equal(500m, await env.GetBalanceAsync(ana.AccountNumber));
        Assert.Equal(750m, await env.GetBalanceAsync(bruno.AccountNumber));
    }

    [Fact]
    public async Task Concorrencia_CincoTransferenciasDeUmaContaComSaldoParaTres_NuncaFicaNegativa()
    {
        var origem = await env.CreateAccountAsync(100m);
        var destinos = new List<Account>();
        for (var i = 0; i < 5; i++)
            destinos.Add(await env.CreateAccountAsync(0m));

        var transacoes = new List<Transaction>();
        foreach (var destino in destinos)
            transacoes.Add(await CriarTransacaoAsync(origem, destino, 30m));

        await using var services = env.CreateServices();
        var resultados = await Task.WhenAll(transacoes.Select(t => ProcessarComRetryAsync(services, t.Id)));

        // 100 / 30 = 3 transferências cabem no saldo; as outras 2 falham por saldo insuficiente.
        Assert.Equal(3, resultados.Count(r => r == ProcessTransactionOutcome.Completed));
        Assert.Equal(2, resultados.Count(r => r == ProcessTransactionOutcome.Failed));
        Assert.Equal(10m, await env.GetBalanceAsync(origem.AccountNumber));

        var totalCreditado = 0m;
        foreach (var destino in destinos)
            totalCreditado += await env.GetBalanceAsync(destino.AccountNumber);
        Assert.Equal(90m, totalCreditado);
    }

    [Fact]
    public async Task TransacaoCancelada_NaoEProcessada()
    {
        var origem = await env.CreateAccountAsync(100m);
        var destino = await env.CreateAccountAsync(0m);
        var transacao = new Transaction(origem.AccountNumber, destino.AccountNumber, 10m);
        transacao.Cancel();
        await using (var db = env.CreateDbContext())
        {
            db.Transactions.Add(transacao);
            await db.SaveChangesAsync();
        }

        var outcome = await ProcessarAsync(transacao.Id);

        Assert.Equal(ProcessTransactionOutcome.Skipped, outcome);
        Assert.Equal(100m, await env.GetBalanceAsync(origem.AccountNumber));
    }

    // ---------- Helpers ----------

    private Task<Transaction> CriarTransacaoAsync(Account origem, Account destino, decimal valor) =>
        CriarTransacaoAsync(origem.AccountNumber, destino.AccountNumber, valor, origem.Currency);

    private async Task<Transaction> CriarTransacaoAsync(string origem, string destino, decimal valor, string moeda = "BRL")
    {
        var transacao = new Transaction(origem, destino, valor, moeda);
        await using var db = env.CreateDbContext();
        db.Transactions.Add(transacao);
        await db.SaveChangesAsync();
        return transacao;
    }

    private async Task<ProcessTransactionOutcome> ProcessarAsync(Guid id)
    {
        await using var services = env.CreateServices();
        await using var scope = services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ProcessTransactionHandler>().HandleAsync(id);
    }

    /// <summary>Mesma política do TransactionRequestedConsumer: escopo novo e retry em conflito.</summary>
    private static async Task<ProcessTransactionOutcome> ProcessarComRetryAsync(IServiceProvider services, Guid id)
    {
        const int maxAttempts = 10;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await using var scope = services.CreateAsyncScope();
                return await scope.ServiceProvider.GetRequiredService<ProcessTransactionHandler>().HandleAsync(id);
            }
            catch (ConcurrencyConflictException) when (attempt < maxAttempts)
            {
                await Task.Delay(Random.Shared.Next(10, 50));
            }
        }
    }
}
