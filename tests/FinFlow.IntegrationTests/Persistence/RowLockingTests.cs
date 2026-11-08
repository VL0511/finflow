using FinFlow.Application.Accounts;
using FinFlow.Application.Common;
using FinFlow.Application.Transactions;
using FinFlow.Domain.Entities;
using FinFlow.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace FinFlow.IntegrationTests.Persistence;

/// <summary>Bloqueio pessimista (UPDLOCK) dos repositórios contra o SQL Server real.</summary>
[Collection(IntegrationCollection.Name)]
public class RowLockingTests(IntegrationTestEnvironment env)
{
    [Fact]
    public async Task LeituraComBloqueio_ForaDeTransacao_FalhaAlto()
    {
        var conta = await env.CreateAccountAsync(10m);
        await using var services = env.CreateServices();
        await using var scope = services.CreateAsyncScope();

        var accounts = scope.ServiceProvider.GetRequiredService<IAccountRepository>();
        var transactions = scope.ServiceProvider.GetRequiredService<ITransactionRepository>();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            accounts.GetPairForUpdateAsync(conta.AccountNumber, IntegrationTestEnvironment.NewAccountNumber()));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            transactions.GetByIdForUpdateAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task UpdLock_SegundoProcessoEsperaOCommitDoPrimeiro_ELeOSaldoJaAtualizado()
    {
        var a = await env.CreateAccountAsync(100m);
        var b = await env.CreateAccountAsync(0m);
        await using var services = env.CreateServices();

        var primeiroBloqueou = new TaskCompletionSource();
        var liberarPrimeiro = new TaskCompletionSource();

        // Processo 1: bloqueia A→B, debita 30 e segura a transação aberta.
        var primeiro = Task.Run(async () =>
        {
            await using var scope = services.CreateAsyncScope();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var accounts = scope.ServiceProvider.GetRequiredService<IAccountRepository>();
            await uow.ExecuteInTransactionAsync(async ct =>
            {
                var (origem, destino) = await accounts.GetPairForUpdateAsync(a.AccountNumber, b.AccountNumber, ct);
                origem!.Debit(30m);
                destino!.Credit(30m);
                primeiroBloqueou.TrySetResult();
                await liberarPrimeiro.Task;
            });
        });
        await primeiroBloqueou.Task;

        // Processo 2: transferência no sentido inverso (B→A) — bloqueia na mesma ordem e precisa esperar.
        decimal? saldoVistoPeloSegundo = null;
        var segundo = Task.Run(async () =>
        {
            await using var scope = services.CreateAsyncScope();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var accounts = scope.ServiceProvider.GetRequiredService<IAccountRepository>();
            await uow.ExecuteInTransactionAsync(async ct =>
            {
                var (origem, destino) = await accounts.GetPairForUpdateAsync(b.AccountNumber, a.AccountNumber, ct);
                saldoVistoPeloSegundo = origem!.Balance;
                origem.Debit(10m);
                destino!.Credit(10m);
            });
        });

        await Task.Delay(500);
        Assert.False(segundo.IsCompleted, "O segundo processo deveria estar bloqueado esperando o primeiro.");

        liberarPrimeiro.SetResult();
        await Task.WhenAll(primeiro, segundo);

        // O segundo enxergou o crédito comitado pelo primeiro (30), não o valor antigo (0).
        Assert.Equal(30m, saldoVistoPeloSegundo);
        Assert.Equal(80m, await env.GetBalanceAsync(a.AccountNumber));
        Assert.Equal(20m, await env.GetBalanceAsync(b.AccountNumber));
    }

    [Fact]
    public async Task UpdLock_MesmaTransacaoEmDoisProcessos_OSegundoEsperaEVeOStatusFinal()
    {
        var origem = await env.CreateAccountAsync(100m);
        var destino = await env.CreateAccountAsync(0m);
        var transacao = new Transaction(origem.AccountNumber, destino.AccountNumber, 10m);
        transacao.StartProcessing();
        await using (var db = env.CreateDbContext())
        {
            db.Transactions.Add(transacao);
            await db.SaveChangesAsync();
        }

        await using var services = env.CreateServices();
        var handlers = Enumerable.Range(0, 4).Select(_ => Task.Run(async () =>
        {
            await using var scope = services.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<ProcessTransactionHandler>().HandleAsync(transacao.Id);
        }));

        // Sem retry nenhum: com o bloqueio, não há conflito — um conclui e os demais veem o status final.
        var resultados = await Task.WhenAll(handlers);

        Assert.Single(resultados, r => r == ProcessTransactionOutcome.Completed);
        Assert.All(resultados.Where(r => r != ProcessTransactionOutcome.Completed),
            r => Assert.Equal(ProcessTransactionOutcome.Skipped, r));
        Assert.Equal(90m, await env.GetBalanceAsync(origem.AccountNumber));
        Assert.Equal(10m, await env.GetBalanceAsync(destino.AccountNumber));
    }
}
