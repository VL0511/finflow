using FinFlow.Application.Common;
using FinFlow.Domain.Entities;
using FinFlow.Domain.Exceptions;
using FluentValidation;
using FluentValidation.Results;

namespace FinFlow.Application.Currencies;

// ----------------------------------------------------------------------------------------------
// Contratos
// ----------------------------------------------------------------------------------------------

public sealed record CurrencyResponse
{
    /// <example>BRL</example>
    public string Code { get; init; } = string.Empty;

    /// <example>Real brasileiro</example>
    public string Name { get; init; } = string.Empty;

    /// <example>2</example>
    public int DecimalPlaces { get; init; }

    /// <example>true</example>
    public bool IsActive { get; init; }

    public static CurrencyResponse FromEntity(Currency c) => new()
    {
        Code = c.Code,
        Name = c.Name,
        DecimalPlaces = c.DecimalPlaces,
        IsActive = c.IsActive
    };
}

public sealed record CreateCurrencyRequest
{
    /// <summary>Código ISO 4217.</summary>
    /// <example>GBP</example>
    public string Code { get; init; } = string.Empty;

    /// <example>Libra esterlina</example>
    public string Name { get; init; } = string.Empty;

    /// <summary>Casas decimais (0 a 2).</summary>
    /// <example>2</example>
    public int DecimalPlaces { get; init; } = 2;
}

public sealed record UpdateCurrencyRequest
{
    public string? Name { get; init; }

    /// <summary>true ativa, false desativa (novas contas/transferências deixam de aceitá-la).</summary>
    public bool? IsActive { get; init; }
}

public interface ICurrencyRepository
{
    Task<Currency?> GetAsync(string code, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Currency>> ListAsync(bool onlyActive, CancellationToken cancellationToken = default);
    Task AddAsync(Currency currency, CancellationToken cancellationToken = default);
}

public sealed class CreateCurrencyRequestValidator : AbstractValidator<CreateCurrencyRequest>
{
    public CreateCurrencyRequestValidator()
    {
        RuleFor(x => x.Code).Matches("^[A-Za-z]{3}$").WithMessage("Use o código ISO 4217 de 3 letras (ex.: GBP).");
        RuleFor(x => x.Name).NotEmpty().WithMessage("Nome obrigatório.").MaximumLength(60);
        RuleFor(x => x.DecimalPlaces).InclusiveBetween(0, Currency.MaxDecimalPlaces)
            .WithMessage($"As casas decimais devem estar entre 0 e {Currency.MaxDecimalPlaces}.");
    }
}

// ----------------------------------------------------------------------------------------------
// Serviço
// ----------------------------------------------------------------------------------------------

public interface ICurrencyService
{
    Task<IReadOnlyList<CurrencyResponse>> ListAsync(bool includeInactive, CancellationToken cancellationToken = default);
    Task<CurrencyResponse> CreateAsync(CreateCurrencyRequest request, CancellationToken cancellationToken = default);
    Task<CurrencyResponse> UpdateAsync(string code, UpdateCurrencyRequest request, CancellationToken cancellationToken = default);

    /// <summary>Moeda existente e ativa, ou DomainException (422) com mensagem clara.</summary>
    Task<Currency> GetActiveOrThrowAsync(string code, CancellationToken cancellationToken = default);
}

public sealed class CurrencyService(
    ICurrencyRepository currencies,
    IUnitOfWork unitOfWork,
    IAuditTrail audit,
    IValidator<CreateCurrencyRequest> validator) : ICurrencyService
{
    public async Task<IReadOnlyList<CurrencyResponse>> ListAsync(bool includeInactive, CancellationToken cancellationToken = default) =>
        (await currencies.ListAsync(onlyActive: !includeInactive, cancellationToken)).Select(CurrencyResponse.FromEntity).ToList();

    public async Task<CurrencyResponse> CreateAsync(CreateCurrencyRequest request, CancellationToken cancellationToken = default)
    {
        await validator.ValidateAndThrowAsync(request, cancellationToken);

        var currency = new Currency(request.Code, request.Name, request.DecimalPlaces);
        if (await currencies.GetAsync(currency.Code, cancellationToken) is not null)
            throw new ConflictException($"A moeda '{currency.Code}' já está cadastrada.");

        await currencies.AddAsync(currency, cancellationToken);
        audit.Record(AuditActions.CurrencyCreated, nameof(Currency), currency.Code,
            new { currency.Code, currency.Name, currency.DecimalPlaces });

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (UniqueConstraintViolationException)
        {
            throw new ConflictException($"A moeda '{currency.Code}' já está cadastrada.");
        }

        return CurrencyResponse.FromEntity(currency);
    }

    public async Task<CurrencyResponse> UpdateAsync(string code, UpdateCurrencyRequest request, CancellationToken cancellationToken = default)
    {
        var currency = await currencies.GetAsync(Normalize(code), cancellationToken)
            ?? throw new NotFoundException($"Moeda '{code}' não encontrada.");

        if (request.Name is not null && request.Name.Trim() != currency.Name)
        {
            if (request.Name.Trim().Length > 60)
                throw new ValidationException([new ValidationFailure(nameof(request.Name), "Máximo de 60 caracteres.")]);
            var before = currency.Name;
            currency.Rename(request.Name);
            audit.Record(AuditActions.CurrencyUpdated, nameof(Currency), currency.Code, new { Name = new { before, after = currency.Name } });
        }

        if (request.IsActive is { } active && active != currency.IsActive)
        {
            if (active) currency.Activate(); else currency.Deactivate();
            audit.Record(active ? AuditActions.CurrencyActivated : AuditActions.CurrencyDeactivated, nameof(Currency), currency.Code);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return CurrencyResponse.FromEntity(currency);
    }

    public async Task<Currency> GetActiveOrThrowAsync(string code, CancellationToken cancellationToken = default)
    {
        var currency = await currencies.GetAsync(Normalize(code), cancellationToken)
            ?? throw new DomainException($"Moeda '{code}' não cadastrada.");
        if (!currency.IsActive)
            throw new DomainException($"A moeda '{currency.Code}' está desativada.");
        return currency;
    }

    private static string Normalize(string code) => (code ?? string.Empty).Trim().ToUpperInvariant();
}
