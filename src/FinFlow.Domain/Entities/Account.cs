using FinFlow.Domain.Exceptions;

namespace FinFlow.Domain.Entities;

public class Account
{
    public Guid Id { get; private set; }

    public string AccountNumber { get; private set; } = string.Empty;

    public string HolderName { get; private set; } = string.Empty;

    public decimal Balance { get; private set; }

    public string Currency { get; private set; } = "BRL";

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>
    /// Usuário dono da conta (Customer). Nulo em contas institucionais e nas contas criadas antes
    /// da autenticação existir: essas só são visíveis para Administrator/Treasurer.
    /// </summary>
    public string? OwnerUserId { get; private set; }

    private Account() { }

    public Account(
        string accountNumber,
        string holderName,
        decimal initialBalance,
        string currency,
        bool isActive = true,
        string? ownerUserId = null)
    {
        if (string.IsNullOrWhiteSpace(accountNumber))
            throw new ArgumentException("Número da conta obrigatório.");

        if (string.IsNullOrWhiteSpace(holderName))
            throw new ArgumentException("Titular obrigatório.");

        if (initialBalance < 0)
            throw new ArgumentException("O saldo inicial não pode ser negativo.");

        if (string.IsNullOrWhiteSpace(currency) || currency.Trim().Length != 3)
            throw new ArgumentException("Moeda deve ter 3 letras (ex.: BRL).");

        Id = Guid.NewGuid();
        AccountNumber = accountNumber.Trim();
        HolderName = holderName.Trim();
        Balance = initialBalance;
        Currency = currency.Trim().ToUpperInvariant();
        IsActive = isActive;
        CreatedAt = DateTimeOffset.UtcNow;
        OwnerUserId = string.IsNullOrWhiteSpace(ownerUserId) ? null : ownerUserId;
    }

    public bool HasSufficientFunds(decimal amount) => Balance >= amount;

    public void Debit(decimal amount)
    {
        EnsurePositive(amount);

        if (!IsActive)
            throw new DomainException("Conta inativa não pode ser debitada.");

        if (!HasSufficientFunds(amount))
            throw new DomainException("Saldo insuficiente.");

        Balance -= amount;
    }

    public void Credit(decimal amount)
    {
        EnsurePositive(amount);

        if (!IsActive)
            throw new DomainException("Conta inativa não pode ser creditada.");

        Balance += amount;
    }

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;

    private static void EnsurePositive(decimal amount)
    {
        if (amount <= 0)
            throw new ArgumentException("O valor deve ser maior que zero.");
    }
}
