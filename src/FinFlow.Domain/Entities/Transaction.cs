using FinFlow.Domain.Enums;

namespace FinFlow.Domain.Entities;

/// <summary>
/// Condições de câmbio fixadas na criação de uma transferência entre moedas diferentes.
/// Calculadas no backend a partir da taxa vigente; nunca recalculadas depois.
/// </summary>
public sealed record ExchangeSnapshot(string DestinationCurrency, decimal DestinationAmount, decimal Rate, Guid RateId);

/// <summary>
/// Transferência entre contas. Ciclo de vida:
/// Pending → Processing → Completed | Failed, ou Pending → Cancelled.
///
/// Valores: <see cref="Amount"/> é debitado da origem na moeda <see cref="Currency"/>;
/// <see cref="DestinationAmount"/> é creditado no destino na moeda <see cref="DestinationCurrency"/>.
/// Na mesma moeda, os dois são iguais e a taxa é 1. Esses campos são um SNAPSHOT imutável:
/// alterar a taxa de câmbio depois não muda transações já criadas.
/// </summary>
public class Transaction
{
    public Guid Id { get; private set; }

    public string SourceAccount { get; private set; } = string.Empty;

    public string DestinationAccount { get; private set; } = string.Empty;

    /// <summary>Valor debitado da origem, na moeda da origem.</summary>
    public decimal Amount { get; private set; }

    /// <summary>Moeda da conta de origem (do valor debitado).</summary>
    public string Currency { get; private set; } = "BRL";

    /// <summary>Moeda da conta de destino (do valor creditado).</summary>
    public string DestinationCurrency { get; private set; } = "BRL";

    /// <summary>Valor creditado no destino, já convertido e arredondado na criação.</summary>
    public decimal DestinationAmount { get; private set; }

    /// <summary>Taxa aplicada (1 unidade da origem = taxa unidades do destino). 1 na mesma moeda.</summary>
    public decimal ExchangeRate { get; private set; } = 1m;

    /// <summary>Versão exata da taxa usada (nula na mesma moeda).</summary>
    public Guid? ExchangeRateId { get; private set; }

    /// <summary>Usuário que solicitou a transferência (nulo em registros anteriores à autenticação).</summary>
    public string? RequestedByUserId { get; private set; }

    public TransactionStatus Status { get; private set; }

    public string? FailureReason { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? UpdatedAt { get; private set; }

    /// <summary>Completed, Failed ou Cancelled: nenhuma transição é mais possível.</summary>
    public bool IsFinal => Status is TransactionStatus.Completed
        or TransactionStatus.Failed
        or TransactionStatus.Cancelled;

    public bool IsCrossCurrency => Currency != DestinationCurrency;

    private Transaction() { }

    /// <param name="exchange">Snapshot do câmbio quando a moeda de destino difere; nulo na mesma moeda.</param>
    public Transaction(
        string sourceAccount,
        string destinationAccount,
        decimal amount,
        string currency = "BRL",
        ExchangeSnapshot? exchange = null,
        string? requestedByUserId = null)
    {
        if (string.IsNullOrWhiteSpace(sourceAccount))
            throw new ArgumentException("Conta de origem obrigatória.");

        if (string.IsNullOrWhiteSpace(destinationAccount))
            throw new ArgumentException("Conta de destino obrigatória.");

        if (sourceAccount == destinationAccount)
            throw new ArgumentException("As contas devem ser diferentes.");

        if (amount <= 0)
            throw new ArgumentException("O valor deve ser maior que zero.");

        if (string.IsNullOrWhiteSpace(currency) || currency.Trim().Length != 3)
            throw new ArgumentException("Moeda deve ter 3 letras (ex.: BRL).");

        Id = Guid.NewGuid();
        SourceAccount = sourceAccount;
        DestinationAccount = destinationAccount;
        Amount = amount;
        Currency = currency.Trim().ToUpperInvariant();
        Status = TransactionStatus.Pending;
        CreatedAt = DateTimeOffset.UtcNow;
        RequestedByUserId = string.IsNullOrWhiteSpace(requestedByUserId) ? null : requestedByUserId;

        if (exchange is null)
        {
            DestinationCurrency = Currency;
            DestinationAmount = amount;
            ExchangeRate = 1m;
            return;
        }

        var destinationCurrency = (exchange.DestinationCurrency ?? string.Empty).Trim().ToUpperInvariant();
        if (destinationCurrency.Length != 3)
            throw new ArgumentException("Moeda de destino deve ter 3 letras.");
        if (destinationCurrency == Currency)
            throw new ArgumentException("Câmbio só se aplica a moedas diferentes.");
        if (exchange.Rate <= 0)
            throw new ArgumentException("A taxa de câmbio deve ser maior que zero.");
        if (exchange.DestinationAmount <= 0)
            throw new ArgumentException("O valor convertido deve ser maior que zero.");
        if (exchange.RateId == Guid.Empty)
            throw new ArgumentException("A referência da taxa de câmbio é obrigatória.");

        DestinationCurrency = destinationCurrency;
        DestinationAmount = exchange.DestinationAmount;
        ExchangeRate = exchange.Rate;
        ExchangeRateId = exchange.RateId;
    }

    public void StartProcessing() =>
        TransitionTo(TransactionStatus.Processing, from: TransactionStatus.Pending,
            "Somente transações pendentes podem iniciar processamento.");

    public void Complete() =>
        TransitionTo(TransactionStatus.Completed, from: TransactionStatus.Processing,
            "Somente transações em processamento podem ser concluídas.");

    public void Fail(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("O motivo da falha é obrigatório.");

        TransitionTo(TransactionStatus.Failed, from: TransactionStatus.Processing,
            "Somente transações em processamento podem falhar.");

        FailureReason = reason;
    }

    public void Cancel() =>
        TransitionTo(TransactionStatus.Cancelled, from: TransactionStatus.Pending,
            "Somente transações pendentes podem ser canceladas.");

    private void TransitionTo(TransactionStatus to, TransactionStatus from, string error)
    {
        if (Status != from)
            throw new InvalidOperationException(error);

        Status = to;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
