using System.Text.RegularExpressions;

namespace FinFlow.Domain.Entities;

/// <summary>
/// Moeda suportada (ISO 4217). Cadastrada e ativada/desativada pelo Administrator.
/// Moeda inativa não é aceita em novas contas nem em novas transferências; contas e
/// transações existentes continuam íntegras.
/// </summary>
public partial class Currency
{
    /// <summary>
    /// Casas decimais aceitas. Valores monetários são gravados como decimal(18,2), então uma
    /// moeda com 3 casas (ex.: BHD) exigiria antes ampliar a precisão das colunas de valor.
    /// </summary>
    public const int MaxDecimalPlaces = 2;

    /// <summary>Código ISO 4217 (3 letras maiúsculas). Chave da moeda.</summary>
    public string Code { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    /// <summary>Casas decimais da moeda (BRL/USD/EUR = 2; JPY = 0).</summary>
    public int DecimalPlaces { get; private set; }

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    private Currency() { }

    public Currency(string code, string name, int decimalPlaces, bool isActive = true)
    {
        code = (code ?? string.Empty).Trim().ToUpperInvariant();
        if (!IsoCode().IsMatch(code))
            throw new ArgumentException("O código da moeda deve ter 3 letras (ISO 4217, ex.: BRL).");

        if (decimalPlaces < 0 || decimalPlaces > MaxDecimalPlaces)
            throw new ArgumentException($"As casas decimais devem estar entre 0 e {MaxDecimalPlaces}.");

        Code = code;
        Rename(name);
        DecimalPlaces = decimalPlaces;
        IsActive = isActive;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("O nome da moeda é obrigatório.");
        Name = name.Trim();
    }

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;

    /// <summary>Arredonda para as casas da moeda (meio para longe do zero, como em extratos bancários).</summary>
    public decimal Round(decimal amount) => Math.Round(amount, DecimalPlaces, MidpointRounding.AwayFromZero);

    /// <summary>O valor não tem mais casas decimais que a moeda permite.</summary>
    public bool HasValidScale(decimal amount) => Round(amount) == amount;

    [GeneratedRegex("^[A-Z]{3}$")]
    private static partial Regex IsoCode();
}
