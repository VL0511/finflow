using FinFlow.Domain.Entities;

namespace FinFlow.Application.Transactions;

/// <summary>Dados para solicitar uma transferência.</summary>
public sealed record CreateTransactionRequest
{
    /// <summary>Número da conta de origem.</summary>
    /// <example>12345</example>
    public string SourceAccount { get; init; } = string.Empty;

    /// <summary>Número da conta de destino. Deve ser diferente da origem.</summary>
    /// <example>67890</example>
    public string DestinationAccount { get; init; } = string.Empty;

    /// <summary>Valor da transferência. Maior que zero, com até 2 casas decimais.</summary>
    /// <example>500.00</example>
    public decimal Amount { get; init; }
}

/// <summary>Representação de uma transação.</summary>
public sealed record TransactionResponse
{
    /// <summary>Identificador gerado pela API.</summary>
    /// <example>3fa85f64-5717-4562-b3fc-2c963f66afa6</example>
    public Guid Id { get; init; }

    /// <example>12345</example>
    public string SourceAccount { get; init; } = string.Empty;

    /// <example>67890</example>
    public string DestinationAccount { get; init; } = string.Empty;

    /// <summary>Valor debitado da origem, na moeda da origem.</summary>
    /// <example>500.00</example>
    public decimal Amount { get; init; }

    /// <summary>Moeda da origem (do valor debitado).</summary>
    /// <example>BRL</example>
    public string Currency { get; init; } = string.Empty;

    /// <summary>Moeda do destino (do valor creditado). Igual a Currency quando não há câmbio.</summary>
    /// <example>BRL</example>
    public string DestinationCurrency { get; init; } = string.Empty;

    /// <summary>Valor creditado no destino, convertido e arredondado na criação (snapshot imutável).</summary>
    /// <example>500.00</example>
    public decimal DestinationAmount { get; init; }

    /// <summary>Taxa aplicada no momento da criação (1 na mesma moeda).</summary>
    /// <example>1</example>
    public decimal ExchangeRate { get; init; }

    /// <summary>Versão da taxa usada (nula na mesma moeda).</summary>
    public Guid? ExchangeRateId { get; init; }

    /// <summary>Usuário que solicitou (nulo em transações anteriores à autenticação).</summary>
    public string? RequestedByUserId { get; init; }

    /// <summary>Pending, Processing, Completed, Failed ou Cancelled.</summary>
    /// <example>Pending</example>
    public string Status { get; init; } = string.Empty;

    /// <summary>Motivo da falha, quando Status = Failed.</summary>
    /// <example>null</example>
    public string? FailureReason { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? UpdatedAt { get; init; }

    public static TransactionResponse FromEntity(Transaction transaction) => new()
    {
        Id = transaction.Id,
        SourceAccount = transaction.SourceAccount,
        DestinationAccount = transaction.DestinationAccount,
        Amount = transaction.Amount,
        Currency = transaction.Currency,
        DestinationCurrency = transaction.DestinationCurrency,
        DestinationAmount = transaction.DestinationAmount,
        ExchangeRate = transaction.ExchangeRate,
        ExchangeRateId = transaction.ExchangeRateId,
        RequestedByUserId = transaction.RequestedByUserId,
        Status = transaction.Status.ToString(),
        FailureReason = transaction.FailureReason,
        CreatedAt = transaction.CreatedAt,
        UpdatedAt = transaction.UpdatedAt
    };
}

/// <summary>
/// Prévia de uma transferência (POST /api/transactions/preview). Calculada pelo mesmo código da
/// criação; a criação recalcula tudo e nunca usa valores enviados pelo cliente.
/// </summary>
public sealed record TransferPreviewResponse
{
    /// <example>USD</example>
    public string SourceCurrency { get; init; } = string.Empty;

    /// <example>BRL</example>
    public string DestinationCurrency { get; init; } = string.Empty;

    /// <example>100.00</example>
    public decimal Amount { get; init; }

    /// <summary>Valor estimado a receber no destino.</summary>
    /// <example>520.00</example>
    public decimal DestinationAmount { get; init; }

    /// <example>5.2</example>
    public decimal ExchangeRate { get; init; }

    public Guid? ExchangeRateId { get; init; }

    public bool IsConversion { get; init; }

    /// <summary>Titular do destino, mascarado para confirmação (ex.: "Ana S.").</summary>
    /// <example>Bruno L.</example>
    public string DestinationHolder { get; init; } = string.Empty;
}
