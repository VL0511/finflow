using FinFlow.Domain.Entities;

namespace FinFlow.Application.Transactions;

/// <summary>Situação atual de uma transação (GET /api/transactions/{id}/status).</summary>
public sealed record TransactionStatusResponse
{
    /// <example>3fa85f64-5717-4562-b3fc-2c963f66afa6</example>
    public Guid Id { get; init; }

    /// <summary>Pending, Processing, Completed, Failed ou Cancelled.</summary>
    /// <example>Completed</example>
    public string Status { get; init; } = string.Empty;

    /// <summary>Motivo da falha, quando Status = Failed.</summary>
    /// <example>null</example>
    public string? FailureReason { get; init; }

    /// <summary>Data da última mudança de status.</summary>
    public DateTimeOffset? UpdatedAt { get; init; }

    public static TransactionStatusResponse FromEntity(Transaction transaction) => new()
    {
        Id = transaction.Id,
        Status = transaction.Status.ToString(),
        FailureReason = transaction.FailureReason,
        UpdatedAt = transaction.UpdatedAt ?? transaction.CreatedAt
    };
}
