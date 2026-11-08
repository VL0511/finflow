using FinFlow.Domain.Enums;

namespace FinFlow.Application.Transactions;

/// <summary>Filtros de GET /api/transactions (combinados com AND; aplicados no banco antes da paginação).</summary>
public sealed record TransactionListQuery
{
    /// <summary>
    /// Status aceitos (Pending, Processing, Completed, Failed, Cancelled), sem diferenciar maiúsculas.
    /// Aceita parâmetro repetido (<c>status=Pending&amp;status=Processing</c>) ou separado por vírgula
    /// (<c>status=Pending,Processing</c>). Vazio = todos.
    /// </summary>
    public IReadOnlyList<string>? Status { get; init; }

    /// <summary>Número de conta presente como origem OU destino.</summary>
    public string? AccountNumber { get; init; }
}

/// <summary>
/// Filtro já validado, repassado ao repositório. <c>VisibleAccounts</c> é o escopo do usuário:
/// só transações em que alguma destas contas é origem ou destino; nulo = sem restrição
/// (Administrator, Treasurer, sistema).
/// </summary>
public sealed record TransactionListFilter(
    IReadOnlyCollection<TransactionStatus> Statuses,
    string? AccountNumber,
    IReadOnlyCollection<string>? VisibleAccounts = null)
{
    public static TransactionListFilter None { get; } = new([], null);
}

/// <summary>Período de GET /api/accounts/{id}/cash-flow.</summary>
public sealed record AccountCashFlowQuery
{
    /// <summary>Primeiro dia (inclusivo, no fuso de <see cref="UtcOffsetMinutes"/>). Padrão: <c>To</c> − 29 dias.</summary>
    public DateOnly? From { get; init; }

    /// <summary>Último dia (inclusivo). Padrão: hoje no fuso informado.</summary>
    public DateOnly? To { get; init; }

    /// <summary>
    /// Deslocamento do fuso do cliente em relação ao UTC, em minutos (ex.: -180 para Brasília).
    /// Define em que dia cada liquidação cai. Padrão: 0 (UTC).
    /// </summary>
    public int? UtcOffsetMinutes { get; init; }
}

/// <summary>Entradas e saídas liquidadas de uma conta em um dia (agregado no banco).</summary>
public sealed record DailyCashFlow
{
    /// <example>2026-11-06</example>
    public DateOnly Date { get; init; }

    /// <summary>Soma das transferências concluídas em que a conta é destino.</summary>
    /// <example>500.00</example>
    public decimal Inflow { get; init; }

    /// <summary>Soma das transferências concluídas em que a conta é origem.</summary>
    /// <example>120.00</example>
    public decimal Outflow { get; init; }
}

/// <summary>Série diária de movimentações liquidadas de uma conta (GET /api/accounts/{id}/cash-flow).</summary>
public sealed record AccountCashFlowResponse
{
    public Guid AccountId { get; init; }

    /// <example>12345</example>
    public string AccountNumber { get; init; } = string.Empty;

    /// <example>BRL</example>
    public string Currency { get; init; } = string.Empty;

    public DateOnly From { get; init; }

    public DateOnly To { get; init; }

    /// <example>-180</example>
    public int UtcOffsetMinutes { get; init; }

    /// <summary>Somente os dias com movimento, em ordem crescente (dias ausentes = sem movimento).</summary>
    public IReadOnlyList<DailyCashFlow> Days { get; init; } = [];
}

/// <summary>Contagem global de transações por status (GET /api/transactions/summary).</summary>
public sealed record TransactionSummaryResponse
{
    /// <example>11</example>
    public int TotalTransactions { get; init; }

    /// <example>3</example>
    public int Pending { get; init; }

    /// <example>0</example>
    public int Processing { get; init; }

    /// <example>3</example>
    public int Completed { get; init; }

    /// <example>4</example>
    public int Failed { get; init; }

    /// <example>1</example>
    public int Cancelled { get; init; }
}
