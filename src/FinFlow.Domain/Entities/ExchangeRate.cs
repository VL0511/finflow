namespace FinFlow.Domain.Entities;

/// <summary>
/// Taxa de câmbio em UMA direção: 1 unidade de <see cref="FromCurrency"/> = <see cref="Rate"/>
/// unidades de <see cref="ToCurrency"/>. A inversa NÃO é deduzida (arredondamentos diferem).
///
/// Registros são imutáveis: atualizar uma taxa = encerrar a vigente (<see cref="Supersede"/>) e
/// criar outra. Assim o histórico completo fica preservado e cada transferência guarda a
/// referência exata da versão usada.
/// </summary>
public class ExchangeRate
{
    /// <summary>Casas decimais aceitas na taxa (coluna decimal(18,8)).</summary>
    public const int MaxRateDecimals = 8;

    public Guid Id { get; private set; }

    public string FromCurrency { get; private set; } = string.Empty;

    public string ToCurrency { get; private set; } = string.Empty;

    public decimal Rate { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Usuário (Administrator) que configurou a taxa.</summary>
    public string CreatedByUserId { get; private set; } = string.Empty;

    /// <summary>Vigente: é a taxa usada em novas conversões desta direção.</summary>
    public bool IsActive { get; private set; }

    /// <summary>Quando deixou de ser vigente (substituída ou desativada).</summary>
    public DateTimeOffset? SupersededAt { get; private set; }

    public string? SupersededByUserId { get; private set; }

    private ExchangeRate() { }

    public ExchangeRate(string fromCurrency, string toCurrency, decimal rate, string createdByUserId)
    {
        fromCurrency = (fromCurrency ?? string.Empty).Trim().ToUpperInvariant();
        toCurrency = (toCurrency ?? string.Empty).Trim().ToUpperInvariant();

        if (fromCurrency.Length != 3 || toCurrency.Length != 3)
            throw new ArgumentException("As moedas devem ter 3 letras.");
        if (fromCurrency == toCurrency)
            throw new ArgumentException("As moedas de origem e destino devem ser diferentes.");
        if (rate <= 0)
            throw new ArgumentException("A taxa deve ser maior que zero.");
        if (Math.Round(rate, MaxRateDecimals) != rate)
            throw new ArgumentException($"A taxa aceita no máximo {MaxRateDecimals} casas decimais.");
        if (string.IsNullOrWhiteSpace(createdByUserId))
            throw new ArgumentException("O responsável pela taxa é obrigatório.");

        Id = Guid.NewGuid();
        FromCurrency = fromCurrency;
        ToCurrency = toCurrency;
        Rate = rate;
        CreatedAt = DateTimeOffset.UtcNow;
        CreatedByUserId = createdByUserId;
        IsActive = true;
    }

    /// <summary>Encerra a vigência (por substituição ou desativação). Não altera a taxa.</summary>
    public void Supersede(string userId)
    {
        if (!IsActive)
            throw new InvalidOperationException("A taxa já não está vigente.");
        IsActive = false;
        SupersededAt = DateTimeOffset.UtcNow;
        SupersededByUserId = userId;
    }
}
