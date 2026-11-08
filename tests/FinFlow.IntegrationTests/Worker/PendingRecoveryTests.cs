using System.Net;
using FinFlow.Application;
using FinFlow.Application.Common;
using FinFlow.Domain.Entities;
using FinFlow.Domain.Enums;
using FinFlow.Domain.Services;
using FinFlow.Infrastructure;
using FinFlow.IntegrationTests.Infrastructure;
using FinFlow.Worker;
using FinFlow.Worker.Processing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit.Abstractions;

namespace FinFlow.IntegrationTests.Worker;

/// <summary>
/// Recuperação de transações presas em Pending (mensagem perdida antes da etapa 1), contra o
/// SQL Server do container de teste. A varredura é chamada diretamente (RecoverOnceAsync) para
/// os cenários serem determinísticos; a composição é a mesma do Worker real (AddFinFlowWorker).
///
/// O banco é compartilhado pela suíte: a varredura também processa transações antigas deixadas
/// por outros testes. Por isso as asserções olham só para as contas e transações de cada teste.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class PendingRecoveryTests(IntegrationTestEnvironment env, ITestOutputHelper output)
{
    private static readonly TimeSpan PendingStaleAfter = TimeSpan.FromMinutes(10);

    // ---------- Cenário 1: Pending antigo ----------

    [Fact]
    public async Task PendingAntigo_EConcluido_ComSaldosExatos()
    {
        var origem = await env.CreateAccountAsync(100m);
        var destino = await env.CreateAccountAsync(0m);
        var id = await InserirPendingAsync(origem, destino, 25m, criadaHaMinutos: 30);

        await using var worker = CriarRecuperacao();
        await worker.Recovery.RecoverOnceAsync(CancellationToken.None);

        var transacao = await env.GetTransactionAsync(id);
        Assert.Equal(TransactionStatus.Completed, transacao!.Status);
        Assert.Equal(75m, await env.GetBalanceAsync(origem.AccountNumber));
        Assert.Equal(25m, await env.GetBalanceAsync(destino.AccountNumber));
    }

    [Fact]
    public async Task PendingAntigo_ComSaldoInsuficiente_TerminaFailed_ESaldosIntactos()
    {
        var origem = await env.CreateAccountAsync(10m);
        var destino = await env.CreateAccountAsync(0m);
        var id = await InserirPendingAsync(origem, destino, 25m, criadaHaMinutos: 30);

        await using var worker = CriarRecuperacao();
        await worker.Recovery.RecoverOnceAsync(CancellationToken.None);

        var transacao = await env.GetTransactionAsync(id);
        Assert.Equal(TransactionStatus.Failed, transacao!.Status);
        Assert.Equal(TransferFailureReasons.InsufficientFunds, transacao.FailureReason);
        Assert.Equal(10m, await env.GetBalanceAsync(origem.AccountNumber));
        Assert.Equal(0m, await env.GetBalanceAsync(destino.AccountNumber));
    }

    // ---------- Cenário 2: Pending recente ----------

    [Fact]
    public async Task PendingRecenteOuAbaixoDoLimite_NaoETocado()
    {
        var origem = await env.CreateAccountAsync(100m);
        var destino = await env.CreateAccountAsync(0m);
        var recente = await InserirPendingAsync(origem, destino, 10m, criadaHaMinutos: 0);
        var quaseNoLimite = await InserirPendingAsync(origem, destino, 20m, criadaHaMinutos: 8); // limite: 10 min
        var versoes = new[] { await RowVersionAsync(recente), await RowVersionAsync(quaseNoLimite) };

        await using var worker = CriarRecuperacao();
        await worker.Recovery.RecoverOnceAsync(CancellationToken.None);

        foreach (var (id, versao) in new[] { recente, quaseNoLimite }.Zip(versoes))
        {
            Assert.Equal(TransactionStatus.Pending, (await env.GetTransactionAsync(id))!.Status);
            Assert.Equal(versao, await RowVersionAsync(id)); // nem a etapa 1 foi gravada
        }
        Assert.Equal(100m, await env.GetBalanceAsync(origem.AccountNumber));
        Assert.Equal(0m, await env.GetBalanceAsync(destino.AccountNumber));
    }

    // ---------- Cenário 3: dois Workers recuperando ao mesmo tempo ----------

    [Fact]
    public async Task DoisWorkersRecuperandoSimultaneamente_CadaTransferenciaMovimentaUmaUnicaVez()
    {
        const int quantidade = 10;
        await using var worker1 = CriarRecuperacao();
        await using var worker2 = CriarRecuperacao();

        // Limpa o que outros testes deixaram antigo, para a contagem abaixo ser só destas transações.
        await worker1.Recovery.RecoverOnceAsync(CancellationToken.None);

        var pares = new List<(Account Origem, Account Destino, Guid Id)>();
        for (var i = 0; i < quantidade; i++)
        {
            var origem = await env.CreateAccountAsync(100m);
            var destino = await env.CreateAccountAsync(0m);
            pares.Add((origem, destino, await InserirPendingAsync(origem, destino, 25m, criadaHaMinutos: 30)));
        }

        // As duas varreduras recebem a mesma lista, na mesma ordem: colisão em todas as transações.
        var concluidas = await Task.WhenAll(
            worker1.Recovery.RecoverOnceAsync(CancellationToken.None),
            worker2.Recovery.RecoverOnceAsync(CancellationToken.None));

        output.WriteLine($"Concluídas pelo worker 1: {concluidas[0]}, pelo worker 2: {concluidas[1]}");
        // Cada transação foi concluída por exatamente um dos dois; o outro recebeu Skipped.
        Assert.Equal(quantidade, concluidas.Sum());

        foreach (var (origem, destino, id) in pares)
        {
            Assert.Equal(TransactionStatus.Completed, (await env.GetTransactionAsync(id))!.Status);
            Assert.Equal(75m, await env.GetBalanceAsync(origem.AccountNumber));
            Assert.Equal(25m, await env.GetBalanceAsync(destino.AccountNumber));
        }
    }

    // ---------- Cenário 5: cancelamento concorrente ----------

    [Fact]
    public async Task CancelamentoDisputandoComARecuperacao_SempreTerminaConsistente()
    {
        var client = env.AdminClient;
        await using var worker = CriarRecuperacao();
        await worker.Recovery.RecoverOnceAsync(CancellationToken.None); // limpa sobras de outros testes

        int canceladas = 0, concluidas = 0;
        for (var i = 0; i < 12; i++)
        {
            var origem = await env.CreateAccountAsync(100m);
            var destino = await env.CreateAccountAsync(0m);
            var id = await InserirPendingAsync(origem, destino, 10m, criadaHaMinutos: 30);

            // Atraso variável no cancelamento para cair antes, durante e depois da etapa 1.
            var cancelamento = Task.Run(async () =>
            {
                await Task.Delay(i % 4 * 15);
                return await client.PatchAsync($"/api/transactions/{id}/cancel", null);
            });
            var recuperacao = worker.Recovery.RecoverOnceAsync(CancellationToken.None);
            await Task.WhenAll(cancelamento, recuperacao);

            var resposta = await cancelamento;
            var final = (await env.GetTransactionAsync(id))!;

            if (resposta.StatusCode == HttpStatusCode.OK)
            {
                // Cancelamento venceu: nada pode ter sido movimentado depois.
                Assert.Equal(TransactionStatus.Cancelled, final.Status);
                Assert.Equal(100m, await env.GetBalanceAsync(origem.AccountNumber));
                Assert.Equal(0m, await env.GetBalanceAsync(destino.AccountNumber));
                canceladas++;
            }
            else
            {
                // Recuperação venceu: o cancelamento é recusado e o valor sai uma única vez.
                Assert.Equal(HttpStatusCode.Conflict, resposta.StatusCode);
                Assert.Equal(TransactionStatus.Completed, final.Status);
                Assert.Equal(90m, await env.GetBalanceAsync(origem.AccountNumber));
                Assert.Equal(10m, await env.GetBalanceAsync(destino.AccountNumber));
                concluidas++;
            }
        }

        output.WriteLine($"Canceladas: {canceladas}, concluídas pela recuperação: {concluidas}");
    }

    // ---------- Cenário 6: falha durante a recuperação ----------

    [Fact]
    public async Task FalhaTransitoriaNaRecuperacao_ENovaTentativa_ConcluiUmaUnicaVez()
    {
        var origem = await env.CreateAccountAsync(100m);
        var destino = await env.CreateAccountAsync(0m);
        var id = await InserirPendingAsync(origem, destino, 25m, criadaHaMinutos: 30);

        // Conflito de concorrência na etapa 2 (depois de Pending → Processing já comitado).
        var falhas = new FalhasSimuladas(origem.AccountNumber, restantes: 1, () => new ConcurrencyConflictException("simulado"));
        await using var worker = CriarRecuperacao(falhas: falhas);
        await worker.Recovery.RecoverOnceAsync(CancellationToken.None);

        Assert.Equal(0, falhas.Restantes); // a falha de fato ocorreu
        Assert.Equal(TransactionStatus.Completed, (await env.GetTransactionAsync(id))!.Status);
        Assert.Equal(75m, await env.GetBalanceAsync(origem.AccountNumber));
        Assert.Equal(25m, await env.GetBalanceAsync(destino.AccountNumber));
    }

    [Fact]
    public async Task FalhaPersistenteNaRecuperacao_FicaEmProcessingSemMovimentar_EVarreduraSeguinteConcluiUmaVez()
    {
        var origem = await env.CreateAccountAsync(100m);
        var destino = await env.CreateAccountAsync(0m);
        var id = await InserirPendingAsync(origem, destino, 25m, criadaHaMinutos: 30);

        // Falha não transitória em todas as 3 tentativas da primeira varredura.
        var falhas = new FalhasSimuladas(origem.AccountNumber, restantes: 3, () => new InvalidOperationException("simulado"));
        await using var worker = CriarRecuperacao(falhas: falhas, processingStaleAfter: TimeSpan.Zero);

        await worker.Recovery.RecoverOnceAsync(CancellationToken.None);

        Assert.Equal(0, falhas.Restantes);
        // Etapa 1 comitada, etapa 2 sempre revertida: Processing, nenhum valor movimentado.
        Assert.Equal(TransactionStatus.Processing, (await env.GetTransactionAsync(id))!.Status);
        Assert.Equal(100m, await env.GetBalanceAsync(origem.AccountNumber));
        Assert.Equal(0m, await env.GetBalanceAsync(destino.AccountNumber));

        // A varredura seguinte a encontra em Processing (recuperação já existente) e conclui.
        await worker.Recovery.RecoverOnceAsync(CancellationToken.None);
        await worker.Recovery.RecoverOnceAsync(CancellationToken.None); // a terceira não pode repetir

        Assert.Equal(TransactionStatus.Completed, (await env.GetTransactionAsync(id))!.Status);
        Assert.Equal(75m, await env.GetBalanceAsync(origem.AccountNumber));
        Assert.Equal(25m, await env.GetBalanceAsync(destino.AccountNumber));
    }

    // ---------- Cenário 7: estados terminais ----------

    [Theory]
    [InlineData(TransactionStatus.Completed)]
    [InlineData(TransactionStatus.Failed)]
    [InlineData(TransactionStatus.Cancelled)]
    public async Task EstadosTerminaisAntigos_NaoSaoProcessadosDeNovo(TransactionStatus status)
    {
        var origem = await env.CreateAccountAsync(100m);
        var destino = await env.CreateAccountAsync(0m);

        var transacao = new Transaction(origem.AccountNumber, destino.AccountNumber, 25m);
        switch (status)
        {
            case TransactionStatus.Completed: transacao.StartProcessing(); transacao.Complete(); break;
            case TransactionStatus.Failed: transacao.StartProcessing(); transacao.Fail("Motivo de teste."); break;
            case TransactionStatus.Cancelled: transacao.Cancel(); break;
        }
        Envelhecer(transacao, criadaHaMinutos: 60, atualizadaHaMinutos: 50);
        await InserirAsync(transacao);
        var versao = await RowVersionAsync(transacao.Id);

        // Registro mais antigo que os dois limites: só o status pode excluí-lo da recuperação.
        var limite = TimeSpan.FromMinutes(30);
        await using var worker = CriarRecuperacao(processingStaleAfter: limite, pendingStaleAfter: limite);
        await worker.Recovery.RecoverOnceAsync(CancellationToken.None);

        var depois = (await env.GetTransactionAsync(transacao.Id))!;
        Assert.Equal(status, depois.Status);
        Assert.Equal(transacao.UpdatedAt, depois.UpdatedAt);
        Assert.Equal(versao, await RowVersionAsync(transacao.Id));
        // O registro terminal foi inserido sem movimentar saldo: qualquer movimento seria indevido.
        Assert.Equal(100m, await env.GetBalanceAsync(origem.AccountNumber));
        Assert.Equal(0m, await env.GetBalanceAsync(destino.AccountNumber));
    }

    // ---------- Helpers ----------

    /// <summary>
    /// Um "Worker" sem os serviços em background: mesma composição do Program.cs (AddFinFlowWorker),
    /// mas a varredura é disparada pelo teste. Cada instância tem seus próprios singletons e DbContexts.
    /// </summary>
    private RecoveryWorker CriarRecuperacao(
        FalhasSimuladas? falhas = null, TimeSpan? processingStaleAfter = null, TimeSpan? pendingStaleAfter = null)
    {
        var settings = new Dictionary<string, string?>(env.Settings)
        {
            ["Worker:Recovery:PendingStaleAfter"] = (pendingStaleAfter ?? PendingStaleAfter).ToString(),
            ["Worker:Recovery:StaleAfter"] = (processingStaleAfter ?? TimeSpan.FromMinutes(2)).ToString(),
            ["Worker:Processing:BaseDelay"] = "00:00:00.020",
            ["Worker:Processing:MaxDelay"] = "00:00:00.200",
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplication();
        services.AddInfrastructure(configuration);
        services.AddFinFlowWorker(configuration);

        if (falhas is not null)
        {
            // Envolve o repositório real: só a conta escolhida falha, e só nas N primeiras chamadas.
            services.InterceptarBloqueioDeContas(falhas.TalvezFalharAsync);
        }

        var provider = services.BuildServiceProvider();
        var recovery = provider.GetServices<IHostedService>().OfType<StuckTransactionRecoveryService>().Single();
        return new RecoveryWorker(provider, recovery);
    }

    private sealed record RecoveryWorker(ServiceProvider Provider, StuckTransactionRecoveryService Recovery) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Provider.DisposeAsync();
    }

    /// <summary>Transação Pending com CreatedAt no passado (relógio da aplicação, o mesmo do Worker).</summary>
    private async Task<Guid> InserirPendingAsync(Account origem, Account destino, decimal valor, int criadaHaMinutos)
    {
        var transacao = new Transaction(origem.AccountNumber, destino.AccountNumber, valor);
        Envelhecer(transacao, criadaHaMinutos, atualizadaHaMinutos: null);
        await InserirAsync(transacao);
        return transacao.Id;
    }

    private static void Envelhecer(Transaction transacao, int criadaHaMinutos, int? atualizadaHaMinutos)
    {
        var agora = DateTimeOffset.UtcNow;
        typeof(Transaction).GetProperty(nameof(Transaction.CreatedAt))!.SetValue(transacao, agora.AddMinutes(-criadaHaMinutos));
        if (atualizadaHaMinutos is { } minutos)
            typeof(Transaction).GetProperty(nameof(Transaction.UpdatedAt))!.SetValue(transacao, agora.AddMinutes(-minutos));
    }

    private async Task InserirAsync(Transaction transacao)
    {
        await using var db = env.CreateDbContext();
        db.Transactions.Add(transacao);
        await db.SaveChangesAsync();
    }

    private async Task<byte[]> RowVersionAsync(Guid id)
    {
        await using var db = env.CreateDbContext();
        return await db.Transactions.AsNoTracking()
            .Where(t => t.Id == id)
            .Select(t => EF.Property<byte[]>(t, "RowVersion"))
            .SingleAsync();
    }
}
