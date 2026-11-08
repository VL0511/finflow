using FinFlow.Application.Accounts;
using FinFlow.Application.Common;
using FinFlow.Domain.Entities;
using FinFlow.Infrastructure.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace FinFlow.Infrastructure.Persistence.Repositories;

public sealed class AccountRepository(FinFlowDbContext context) : IAccountRepository
{
    public async Task AddAsync(Account account, CancellationToken cancellationToken = default) =>
        await context.Accounts.AddAsync(account, cancellationToken);

    public Task<Account?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        context.Accounts.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

    public Task<Account?> GetByNumberAsync(string accountNumber, CancellationToken cancellationToken = default) =>
        context.Accounts.FirstOrDefaultAsync(a => a.AccountNumber == accountNumber, cancellationToken);

    public async Task<(Account? Source, Account? Destination)> GetPairForUpdateAsync(
        string sourceAccountNumber, string destinationAccountNumber, CancellationToken cancellationToken = default)
    {
        RowLocking.EnsureActiveTransaction(context, nameof(GetPairForUpdateAsync));

        // Ordem global e determinística: quem transfere A→B e quem transfere B→A
        // bloqueiam primeiro a mesma conta, então um espera o outro em vez de travar em ciclo.
        var lockOrder = new[] { sourceAccountNumber, destinationAccountNumber }
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal);

        var locked = new Dictionary<string, Account?>(StringComparer.Ordinal);
        foreach (var accountNumber in lockOrder)
            locked[accountNumber] = await LockByNumberAsync(accountNumber, cancellationToken);

        return (locked[sourceAccountNumber], locked[destinationAccountNumber]);
    }

    public Task<bool> ExistsByNumberAsync(string accountNumber, CancellationToken cancellationToken = default) =>
        context.Accounts.AnyAsync(a => a.AccountNumber == accountNumber, cancellationToken);

    public Task<PagedResult<Account>> ListAsync(PageRequest page, AccessScope scope, CancellationToken cancellationToken = default) =>
        InScope(scope)
            // AccountNumber é único; Id garante a ordem total mesmo se essa regra mudar.
            .OrderBy(a => a.AccountNumber)
            .ThenBy(a => a.Id)
            .ToPagedResultAsync(page, cancellationToken);

    public async Task<IReadOnlyList<AccountCurrencySummary>> SummarizeByCurrencyAsync(AccessScope scope, CancellationToken cancellationToken = default) =>
        await InScope(scope)
            .GroupBy(a => a.Currency)
            .Select(g => new AccountCurrencySummary
            {
                Currency = g.Key,
                Accounts = g.Count(),
                ActiveAccounts = g.Count(a => a.IsActive),
                TotalBalance = g.Sum(a => a.Balance)
            })
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<string>> ListNumbersByOwnerAsync(string ownerUserId, CancellationToken cancellationToken = default) =>
        await context.Accounts
            .AsNoTracking()
            .Where(a => a.OwnerUserId == ownerUserId)
            .Select(a => a.AccountNumber)
            .ToListAsync(cancellationToken);

    /// <summary>Filtro de escopo aplicado NO BANCO (contagem e página usam a mesma consulta).</summary>
    private IQueryable<Account> InScope(AccessScope scope)
    {
        var query = context.Accounts.AsNoTracking();
        return scope.IsUnrestricted ? query : query.Where(a => a.OwnerUserId == scope.OwnerUserId);
    }

    // UPDLOCK: lê a versão comitada mais recente e impede que outro processo bloqueie/altere a
    // linha até o commit; ROWLOCK: mantém o bloqueio na granularidade de linha.
    private Task<Account?> LockByNumberAsync(string accountNumber, CancellationToken cancellationToken) =>
        context.Accounts
            .FromSql($"SELECT * FROM [Accounts] WITH (UPDLOCK, ROWLOCK) WHERE [AccountNumber] = {accountNumber}")
            .AsTracking()
            .SingleOrDefaultAsync(cancellationToken);
}
