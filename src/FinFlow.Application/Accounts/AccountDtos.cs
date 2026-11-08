using FinFlow.Domain.Entities;

namespace FinFlow.Application.Accounts;

/// <summary>
/// Dados para abertura de conta. O NÚMERO É GERADO PELO BACKEND (10 dígitos com verificador)
/// e devolvido na resposta; não é aceito no pedido.
/// </summary>
public sealed record CreateAccountRequest
{
    /// <summary>Nome do titular.</summary>
    /// <example>Fernanda Lima</example>
    public string HolderName { get; init; } = string.Empty;

    /// <summary>Saldo inicial (não pode ser negativo; casas decimais conforme a moeda).</summary>
    /// <example>1500.00</example>
    public decimal InitialBalance { get; init; }

    /// <summary>Moeda da conta (deve estar cadastrada e ativa). O saldo é sempre mantido nela.</summary>
    /// <example>BRL</example>
    public string Currency { get; init; } = "BRL";

    /// <summary>Se a conta nasce ativa. Use false para testar a regra de conta inativa.</summary>
    /// <example>true</example>
    public bool IsActive { get; init; } = true;

    /// <summary>
    /// Dono da conta. Só o Administrator escolhe (ou omite, para conta institucional);
    /// para o Customer é sempre ele mesmo — o valor enviado é ignorado.
    /// </summary>
    public string? OwnerUserId { get; init; }
}

public sealed record AccountResponse
{
    /// <example>a1b2c3d4-0000-0000-0000-000000000001</example>
    public Guid Id { get; init; }

    /// <example>12345</example>
    public string AccountNumber { get; init; } = string.Empty;

    /// <example>Ana Souza</example>
    public string HolderName { get; init; } = string.Empty;

    /// <example>1000.00</example>
    public decimal Balance { get; init; }

    /// <example>BRL</example>
    public string Currency { get; init; } = string.Empty;

    /// <example>true</example>
    public bool IsActive { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>Dono da conta (nulo em contas institucionais/legadas).</summary>
    public string? OwnerUserId { get; init; }

    public static AccountResponse FromEntity(Account account) => new()
    {
        Id = account.Id,
        AccountNumber = account.AccountNumber,
        HolderName = account.HolderName,
        Balance = account.Balance,
        Currency = account.Currency,
        IsActive = account.IsActive,
        CreatedAt = account.CreatedAt,
        OwnerUserId = account.OwnerUserId
    };
}

public sealed record AccountBalanceResponse
{
    /// <example>a1b2c3d4-0000-0000-0000-000000000001</example>
    public Guid AccountId { get; init; }

    /// <example>12345</example>
    public string AccountNumber { get; init; } = string.Empty;

    /// <example>1000.00</example>
    public decimal Balance { get; init; }

    /// <example>BRL</example>
    public string Currency { get; init; } = string.Empty;

    public static AccountBalanceResponse FromEntity(Account account) => new()
    {
        AccountId = account.Id,
        AccountNumber = account.AccountNumber,
        Balance = account.Balance,
        Currency = account.Currency
    };
}

/// <summary>Totais de uma moeda (GET /api/accounts/summary). Sem conversão cambial: moedas nunca são somadas entre si.</summary>
public sealed record AccountCurrencySummary
{
    /// <example>BRL</example>
    public string Currency { get; init; } = string.Empty;

    /// <summary>Quantidade de contas nesta moeda.</summary>
    /// <example>3</example>
    public int Accounts { get; init; }

    /// <summary>Contas ativas nesta moeda.</summary>
    /// <example>2</example>
    public int ActiveAccounts { get; init; }

    /// <summary>Soma dos saldos de todas as contas nesta moeda (ativas e inativas).</summary>
    /// <example>1550.00</example>
    public decimal TotalBalance { get; init; }
}

/// <summary>Indicadores globais de contas, calculados no banco (independem da paginação).</summary>
public sealed record AccountSummaryResponse
{
    /// <example>4</example>
    public int TotalAccounts { get; init; }

    /// <example>3</example>
    public int ActiveAccounts { get; init; }

    /// <example>1</example>
    public int InactiveAccounts { get; init; }

    /// <summary>Totais por moeda, em ordem alfabética de moeda.</summary>
    public IReadOnlyList<AccountCurrencySummary> Balances { get; init; } = [];
}
