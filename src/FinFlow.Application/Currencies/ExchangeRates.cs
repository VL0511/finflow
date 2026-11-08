using FinFlow.Application.Common;
using FinFlow.Application.Users;
using FinFlow.Domain.Entities;
using FinFlow.Domain.Exceptions;
using FluentValidation;

namespace FinFlow.Application.Currencies;

// ----------------------------------------------------------------------------------------------
// Contratos
// ----------------------------------------------------------------------------------------------

public sealed record ExchangeRateResponse
{
    public Guid Id { get; init; }

    /// <example>USD</example>
    public string FromCurrency { get; init; } = string.Empty;

    /// <example>BRL</example>
    public string ToCurrency { get; init; } = string.Empty;

    /// <summary>1 unidade de FromCurrency = Rate unidades de ToCurrency.</summary>
    /// <example>5.2</example>
    public decimal Rate { get; init; }

    public bool IsActive { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public string CreatedByUserId { get; init; } = string.Empty;

    public string? CreatedByEmail { get; init; }

    public DateTimeOffset? SupersededAt { get; init; }

    public static ExchangeRateResponse FromEntity(ExchangeRate r, string? createdByEmail = null) => new()
    {
        Id = r.Id,
        FromCurrency = r.FromCurrency,
        ToCurrency = r.ToCurrency,
        Rate = r.Rate,
        IsActive = r.IsActive,
        CreatedAt = r.CreatedAt,
        CreatedByUserId = r.CreatedByUserId,
        CreatedByEmail = createdByEmail,
        SupersededAt = r.SupersededAt
    };
}

public sealed record SetExchangeRateRequest
{
    /// <example>USD</example>
    public string FromCurrency { get; init; } = string.Empty;

    /// <example>BRL</example>
    public string ToCurrency { get; init; } = string.Empty;

    /// <example>5.20</example>
    public decimal Rate { get; init; }
}

/// <summary>Resultado de uma conversão calculada no backend (prévia ou transferência).</summary>
public sealed record ExchangeQuote
{
    public string FromCurrency { get; init; } = string.Empty;
    public string ToCurrency { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public decimal ConvertedAmount { get; init; }

    /// <summary>1 na mesma moeda.</summary>
    public decimal Rate { get; init; }

    /// <summary>Versão da taxa usada; nula na mesma moeda.</summary>
    public Guid? RateId { get; init; }

    public bool IsConversion => RateId is not null;
}

public interface IExchangeRateRepository
{
    Task<ExchangeRate?> GetActiveAsync(string fromCurrency, string toCurrency, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ExchangeRate>> ListActiveAsync(CancellationToken cancellationToken = default);

    /// <summary>Todas as versões (vigentes e encerradas), mais recentes primeiro.</summary>
    Task<PagedResult<ExchangeRate>> ListHistoryAsync(string? fromCurrency, string? toCurrency, PageRequest page, CancellationToken cancellationToken = default);

    Task AddAsync(ExchangeRate rate, CancellationToken cancellationToken = default);
}

public sealed class SetExchangeRateRequestValidator : AbstractValidator<SetExchangeRateRequest>
{
    public SetExchangeRateRequestValidator()
    {
        RuleFor(x => x.FromCurrency).Matches("^[A-Za-z]{3}$").WithMessage("Moeda de origem inválida.");
        RuleFor(x => x.ToCurrency).Matches("^[A-Za-z]{3}$").WithMessage("Moeda de destino inválida.")
            .Must((r, to) => !string.Equals(r.FromCurrency?.Trim(), to?.Trim(), StringComparison.OrdinalIgnoreCase))
            .WithMessage("As moedas de origem e destino devem ser diferentes.");
        RuleFor(x => x.Rate)
            .GreaterThan(0).WithMessage("A taxa deve ser maior que zero.")
            .PrecisionScale(18, ExchangeRate.MaxRateDecimals, ignoreTrailingZeros: true)
            .WithMessage($"A taxa aceita no máximo {ExchangeRate.MaxRateDecimals} casas decimais.");
    }
}

// ----------------------------------------------------------------------------------------------
// Serviço
// ----------------------------------------------------------------------------------------------

public interface IExchangeRateService
{
    Task<IReadOnlyList<ExchangeRateResponse>> ListActiveAsync(CancellationToken cancellationToken = default);
    Task<PagedResult<ExchangeRateResponse>> ListHistoryAsync(string? from, string? to, PageRequest page, CancellationToken cancellationToken = default);
    Task<ExchangeRateResponse> SetAsync(SetExchangeRateRequest request, CancellationToken cancellationToken = default);
    Task DeactivateAsync(string from, string to, CancellationToken cancellationToken = default);

    /// <summary>
    /// Converte <paramref name="amount"/> (na moeda <paramref name="from"/>) usando a taxa vigente.
    /// Única implementação de conversão do sistema: usada na prévia e na criação da transferência.
    /// </summary>
    Task<ExchangeQuote> QuoteAsync(string from, string to, decimal amount, CancellationToken cancellationToken = default);
}

public sealed class ExchangeRateService(
    IExchangeRateRepository rates,
    ICurrencyService currencies,
    IUserDirectory users,
    IUnitOfWork unitOfWork,
    IAuditTrail audit,
    ICurrentUser currentUser,
    IValidator<SetExchangeRateRequest> validator) : IExchangeRateService
{
    public async Task<IReadOnlyList<ExchangeRateResponse>> ListActiveAsync(CancellationToken cancellationToken = default) =>
        await WithEmailsAsync(await rates.ListActiveAsync(cancellationToken), cancellationToken);

    public async Task<PagedResult<ExchangeRateResponse>> ListHistoryAsync(
        string? from, string? to, PageRequest page, CancellationToken cancellationToken = default)
    {
        var result = await rates.ListHistoryAsync(Normalize(from), Normalize(to), page, cancellationToken);
        var items = await WithEmailsAsync(result.Items, cancellationToken);
        return PagedResult<ExchangeRateResponse>.Create(items, page, result.TotalItems);
    }

    public async Task<ExchangeRateResponse> SetAsync(SetExchangeRateRequest request, CancellationToken cancellationToken = default)
    {
        await validator.ValidateAndThrowAsync(request, cancellationToken);
        var userId = currentUser.UserId ?? throw new ForbiddenException("Operação exige um usuário autenticado.");

        var from = await currencies.GetActiveOrThrowAsync(Normalize(request.FromCurrency)!, cancellationToken);
        var to = await currencies.GetActiveOrThrowAsync(Normalize(request.ToCurrency)!, cancellationToken);

        // Nova versão; a anterior é encerrada (nunca editada). Ambas no mesmo commit.
        var previous = await rates.GetActiveAsync(from.Code, to.Code, cancellationToken);
        previous?.Supersede(userId);

        var rate = new ExchangeRate(from.Code, to.Code, request.Rate, userId);
        await rates.AddAsync(rate, cancellationToken);

        audit.Record(AuditActions.ExchangeRateSet, nameof(ExchangeRate), rate.Id.ToString(), new
        {
            rate.FromCurrency,
            rate.ToCurrency,
            rate.Rate,
            PreviousRateId = previous?.Id,
            PreviousRate = previous?.Rate
        });

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is UniqueConstraintViolationException or ConcurrencyConflictException)
        {
            // Índice único filtrado (uma vigente por direção) + rowversion: outra alteração simultânea venceu.
            throw new ConflictException($"A taxa {from.Code}→{to.Code} foi alterada por outro usuário. Consulte e tente novamente.");
        }

        return ExchangeRateResponse.FromEntity(rate, currentUser.Email);
    }

    public async Task DeactivateAsync(string from, string to, CancellationToken cancellationToken = default)
    {
        var userId = currentUser.UserId ?? throw new ForbiddenException("Operação exige um usuário autenticado.");
        var rate = await rates.GetActiveAsync(Normalize(from)!, Normalize(to)!, cancellationToken)
            ?? throw new NotFoundException($"Não há taxa vigente de {from} para {to}.");

        rate.Supersede(userId);
        audit.Record(AuditActions.ExchangeRateDeactivated, nameof(ExchangeRate), rate.Id.ToString(),
            new { rate.FromCurrency, rate.ToCurrency, rate.Rate });

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (ConcurrencyConflictException)
        {
            throw new ConflictException($"A taxa {rate.FromCurrency}→{rate.ToCurrency} foi alterada por outro usuário.");
        }
    }

    public async Task<ExchangeQuote> QuoteAsync(string from, string to, decimal amount, CancellationToken cancellationToken = default)
    {
        if (amount <= 0)
            throw new DomainException("O valor deve ser maior que zero.");

        var source = await currencies.GetActiveOrThrowAsync(Normalize(from) ?? string.Empty, cancellationToken);
        var destination = await currencies.GetActiveOrThrowAsync(Normalize(to) ?? string.Empty, cancellationToken);

        if (!source.HasValidScale(amount))
            throw new DomainException($"O valor em {source.Code} aceita no máximo {source.DecimalPlaces} casa(s) decimal(is).");

        if (source.Code == destination.Code)
            return new ExchangeQuote { FromCurrency = source.Code, ToCurrency = destination.Code, Amount = amount, ConvertedAmount = amount, Rate = 1m };

        var rate = await rates.GetActiveAsync(source.Code, destination.Code, cancellationToken)
            ?? throw new DomainException($"Não há taxa de câmbio vigente de {source.Code} para {destination.Code}.");

        // decimal em todo o cálculo; arredondado nas casas da moeda de destino.
        var converted = destination.Round(amount * rate.Rate);
        if (converted <= 0)
            throw new DomainException($"O valor convertido para {destination.Code} seria zero. Informe um valor maior.");

        return new ExchangeQuote
        {
            FromCurrency = source.Code,
            ToCurrency = destination.Code,
            Amount = amount,
            ConvertedAmount = converted,
            Rate = rate.Rate,
            RateId = rate.Id
        };
    }

    private async Task<List<ExchangeRateResponse>> WithEmailsAsync(IReadOnlyList<ExchangeRate> list, CancellationToken cancellationToken)
    {
        var emails = await users.GetEmailsAsync(list.Select(r => r.CreatedByUserId).Distinct().ToList(), cancellationToken);
        return list.Select(r => ExchangeRateResponse.FromEntity(r, emails.GetValueOrDefault(r.CreatedByUserId))).ToList();
    }

    private static string? Normalize(string? code) =>
        string.IsNullOrWhiteSpace(code) ? null : code.Trim().ToUpperInvariant();
}
