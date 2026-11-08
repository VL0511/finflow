using FinFlow.Application.Common;
using FinFlow.Application.Transactions;
using FinFlow.Domain.Entities;
using FinFlow.Domain.Enums;
using FinFlow.Infrastructure.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace FinFlow.Infrastructure.Persistence.Repositories;

public sealed class TransactionRepository(FinFlowDbContext context) : ITransactionRepository
{
    public async Task AddAsync(Transaction transaction, CancellationToken cancellationToken = default) =>
        await context.Transactions.AddAsync(transaction, cancellationToken);

    public Task<Transaction?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        context.Transactions.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

    public Task<Transaction?> GetByIdForUpdateAsync(Guid id, CancellationToken cancellationToken = default)
    {
        RowLocking.EnsureActiveTransaction(context, nameof(GetByIdForUpdateAsync));

        return context.Transactions
            .FromSql($"SELECT * FROM [Transactions] WITH (UPDLOCK, ROWLOCK) WHERE [Id] = {id}")
            .AsTracking()
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Guid>> ListStaleProcessingAsync(
        DateTimeOffset olderThan, int maxCount, CancellationToken cancellationToken = default) =>
        await context.Transactions
            .AsNoTracking()
            .Where(t => t.Status == TransactionStatus.Processing && (t.UpdatedAt ?? t.CreatedAt) < olderThan)
            .OrderBy(t => t.UpdatedAt ?? t.CreatedAt)
            .Select(t => t.Id)
            .Take(maxCount)
            .ToListAsync(cancellationToken);

    // Em Pending, UpdatedAt é sempre nulo (nenhuma transição leva a Pending): a idade é a de CreatedAt.
    public async Task<IReadOnlyList<Guid>> ListStalePendingAsync(
        DateTimeOffset olderThan, int maxCount, CancellationToken cancellationToken = default) =>
        await context.Transactions
            .AsNoTracking()
            .Where(t => t.Status == TransactionStatus.Pending && t.CreatedAt < olderThan)
            .OrderBy(t => t.CreatedAt)
            .Select(t => t.Id)
            .Take(maxCount)
            .ToListAsync(cancellationToken);

    public Task<PagedResult<Transaction>> ListAsync(
        TransactionListFilter filter, PageRequest page, CancellationToken cancellationToken = default)
    {
        var query = context.Transactions.AsNoTracking();

        if (filter.Statuses.Count > 0)
        {
            var statuses = filter.Statuses.ToList();
            query = query.Where(t => statuses.Contains(t.Status));
        }

        if (filter.AccountNumber is { } accountNumber)
            query = query.Where(t => t.SourceAccount == accountNumber || t.DestinationAccount == accountNumber);

        query = InScope(query, filter.VisibleAccounts);

        // Mais recentes primeiro; Id desempata transações criadas no mesmo instante.
        return query
            .OrderByDescending(t => t.CreatedAt)
            .ThenByDescending(t => t.Id)
            .ToPagedResultAsync(page, cancellationToken);
    }

    public async Task<IReadOnlyDictionary<TransactionStatus, int>> CountByStatusAsync(
        IReadOnlyCollection<string>? visibleAccounts = null, CancellationToken cancellationToken = default) =>
        await InScope(context.Transactions.AsNoTracking(), visibleAccounts)
            .GroupBy(t => t.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Status, x => x.Count, cancellationToken);

    /// <summary>Escopo do usuário: transações em que alguma conta visível é origem ou destino (nulo = todas).</summary>
    private static IQueryable<Transaction> InScope(IQueryable<Transaction> query, IReadOnlyCollection<string>? visibleAccounts)
    {
        if (visibleAccounts is null)
            return query;
        if (visibleAccounts.Count == 0)
            return query.Where(_ => false);

        var numbers = visibleAccounts.ToList();
        return query.Where(t => numbers.Contains(t.SourceAccount) || numbers.Contains(t.DestinationAccount));
    }

    public async Task<IReadOnlyList<DailyCashFlow>> GetDailyCashFlowAsync(
        string accountNumber,
        DateTimeOffset from,
        DateTimeOffset toExclusive,
        int utcOffsetMinutes,
        CancellationToken cancellationToken = default)
    {
        // Tudo no SQL Server: filtro, deslocamento de fuso (DATEADD), dia (CONVERT date) e somas.
        // Só as linhas agregadas (no máx. uma por dia) trafegam para a aplicação.
        var rows = await context.Transactions
            .AsNoTracking()
            .Where(t => t.Status == TransactionStatus.Completed
                && t.UpdatedAt != null
                && t.UpdatedAt >= from
                && t.UpdatedAt < toExclusive
                && (t.SourceAccount == accountNumber || t.DestinationAccount == accountNumber))
            .GroupBy(t => t.UpdatedAt!.Value.AddMinutes(utcOffsetMinutes).Date)
            .Select(g => new
            {
                Day = g.Key,
                // Entrada = valor CREDITADO (já na moeda da conta); saída = valor debitado.
                Inflow = g.Sum(t => t.DestinationAccount == accountNumber ? t.DestinationAmount : 0m),
                Outflow = g.Sum(t => t.SourceAccount == accountNumber ? t.Amount : 0m)
            })
            .ToListAsync(cancellationToken);

        return rows
            .Select(r => new DailyCashFlow { Date = DateOnly.FromDateTime(r.Day), Inflow = r.Inflow, Outflow = r.Outflow })
            .ToList();
    }
}
