using FinFlow.Application.Common;
using FinFlow.Application.Currencies;
using FinFlow.Domain.Entities;
using FinFlow.Infrastructure.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace FinFlow.Infrastructure.Persistence.Repositories;

public sealed class CurrencyRepository(FinFlowDbContext context) : ICurrencyRepository
{
    public Task<Currency?> GetAsync(string code, CancellationToken cancellationToken = default) =>
        context.Currencies.FirstOrDefaultAsync(c => c.Code == code, cancellationToken);

    public async Task<IReadOnlyList<Currency>> ListAsync(bool onlyActive, CancellationToken cancellationToken = default) =>
        await context.Currencies
            .AsNoTracking()
            .Where(c => !onlyActive || c.IsActive)
            .OrderBy(c => c.Code)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(Currency currency, CancellationToken cancellationToken = default) =>
        await context.Currencies.AddAsync(currency, cancellationToken);
}

public sealed class ExchangeRateRepository(FinFlowDbContext context) : IExchangeRateRepository
{
    public Task<ExchangeRate?> GetActiveAsync(string fromCurrency, string toCurrency, CancellationToken cancellationToken = default) =>
        context.ExchangeRates.FirstOrDefaultAsync(
            r => r.FromCurrency == fromCurrency && r.ToCurrency == toCurrency && r.IsActive, cancellationToken);

    public async Task<IReadOnlyList<ExchangeRate>> ListActiveAsync(CancellationToken cancellationToken = default) =>
        await context.ExchangeRates
            .AsNoTracking()
            .Where(r => r.IsActive)
            .OrderBy(r => r.FromCurrency).ThenBy(r => r.ToCurrency)
            .ToListAsync(cancellationToken);

    public Task<PagedResult<ExchangeRate>> ListHistoryAsync(
        string? fromCurrency, string? toCurrency, PageRequest page, CancellationToken cancellationToken = default)
    {
        var query = context.ExchangeRates.AsNoTracking();
        if (fromCurrency is not null) query = query.Where(r => r.FromCurrency == fromCurrency);
        if (toCurrency is not null) query = query.Where(r => r.ToCurrency == toCurrency);

        return query
            .OrderByDescending(r => r.CreatedAt)
            .ThenByDescending(r => r.Id)
            .ToPagedResultAsync(page, cancellationToken);
    }

    public async Task AddAsync(ExchangeRate rate, CancellationToken cancellationToken = default) =>
        await context.ExchangeRates.AddAsync(rate, cancellationToken);
}
