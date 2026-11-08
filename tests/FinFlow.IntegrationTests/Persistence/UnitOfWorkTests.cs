using FinFlow.Application.Common;
using FinFlow.Application.Transactions;
using FinFlow.Domain.Entities;
using FinFlow.Domain.Enums;
using FinFlow.IntegrationTests.Infrastructure;
using FinFlow.Infrastructure.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FinFlow.IntegrationTests.Persistence;

/// <summary>Atomicidade (rollback) e concorrência otimista (rowversion) no SQL Server real.</summary>
[Collection(IntegrationCollection.Name)]
public class UnitOfWorkTests(IntegrationTestEnvironment env)
{
    [Fact]
    public async Task ExecuteInTransaction_ExcecaoDepoisDoDebito_FazRollbackDeTudo()
    {
        var origem = await env.CreateAccountAsync(1000m);
        var destino = await env.CreateAccountAsync(0m);
        await using var services = env.CreateServices();
        await using var scope = services.CreateAsyncScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var db = scope.ServiceProvider.GetRequiredService<FinFlowDbContext>();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            unitOfWork.ExecuteInTransactionAsync(async ct =>
            {
                var source = await db.Accounts.SingleAsync(a => a.Id == origem.Id, ct);
                var destination = await db.Accounts.SingleAsync(a => a.Id == destino.Id, ct);

                source.Debit(300m);
                destination.Credit(300m);
                await db.SaveChangesAsync(ct); // já enviado ao banco, mas não comitado

                throw new InvalidOperationException("falha simulada antes do commit");
            }));

        Assert.Equal(1000m, await env.GetBalanceAsync(origem.AccountNumber));
        Assert.Equal(0m, await env.GetBalanceAsync(destino.AccountNumber));
    }

    [Fact]
    public async Task ExecuteInTransaction_Sucesso_ComitaTodasAsAlteracoesJuntas()
    {
        var origem = await env.CreateAccountAsync(1000m);
        var destino = await env.CreateAccountAsync(0m);
        await using var services = env.CreateServices();
        await using var scope = services.CreateAsyncScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var db = scope.ServiceProvider.GetRequiredService<FinFlowDbContext>();

        await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            (await db.Accounts.SingleAsync(a => a.Id == origem.Id, ct)).Debit(300m);
            (await db.Accounts.SingleAsync(a => a.Id == destino.Id, ct)).Credit(300m);
        });

        Assert.Equal(700m, await env.GetBalanceAsync(origem.AccountNumber));
        Assert.Equal(300m, await env.GetBalanceAsync(destino.AccountNumber));
    }

    [Fact]
    public async Task RowVersion_DuasAlteracoesNaMesmaConta_SegundaRecebeConcurrencyConflict()
    {
        var conta = await env.CreateAccountAsync(100m);
        await using var services = env.CreateServices();
        await using var scopeA = services.CreateAsyncScope();
        await using var scopeB = services.CreateAsyncScope();

        // Os dois processos leem o mesmo saldo (100)...
        var contaA = await scopeA.ServiceProvider.GetRequiredService<FinFlowDbContext>()
            .Accounts.SingleAsync(a => a.Id == conta.Id);
        var contaB = await scopeB.ServiceProvider.GetRequiredService<FinFlowDbContext>()
            .Accounts.SingleAsync(a => a.Id == conta.Id);

        // ...e cada um debita 80. Sem controle de concorrência o saldo ficaria 20 e
        // 160 teriam saído de uma conta com 100.
        contaA.Debit(80m);
        contaB.Debit(80m);

        await scopeA.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() =>
            scopeB.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync());

        Assert.Equal(20m, await env.GetBalanceAsync(conta.AccountNumber));
    }

    [Fact]
    public async Task RowVersion_CancelamentoEProcessamentoSimultaneos_SoUmVence()
    {
        var origem = await env.CreateAccountAsync(100m);
        var destino = await env.CreateAccountAsync(0m);
        var transacao = new Transaction(origem.AccountNumber, destino.AccountNumber, 10m);
        await using (var db = env.CreateDbContext())
        {
            db.Transactions.Add(transacao);
            await db.SaveChangesAsync();
        }

        await using var services = env.CreateServices();
        await using var usuario = services.CreateAsyncScope();
        await using var worker = services.CreateAsyncScope();

        var vistaPeloUsuario = await usuario.ServiceProvider.GetRequiredService<ITransactionRepository>()
            .GetByIdAsync(transacao.Id);
        var vistaPeloWorker = await worker.ServiceProvider.GetRequiredService<ITransactionRepository>()
            .GetByIdAsync(transacao.Id);

        vistaPeloUsuario!.Cancel();
        vistaPeloWorker!.StartProcessing();

        await usuario.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() =>
            worker.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync());

        Assert.Equal(TransactionStatus.Cancelled, (await env.GetTransactionAsync(transacao.Id))!.Status);
    }
}
