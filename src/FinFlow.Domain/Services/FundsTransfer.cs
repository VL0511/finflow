using FinFlow.Domain.Entities;
using FinFlow.Domain.Enums;

namespace FinFlow.Domain.Services;

public sealed record TransferResult(bool Succeeded, string? FailureReason)
{
    public static TransferResult Success() => new(true, null);
    public static TransferResult Failure(string reason) => new(false, reason);
}

public static class TransferFailureReasons
{
    public const string InvalidAmount = "O valor da transferência deve ser positivo.";
    public const string SourceNotFound = "Conta de origem não encontrada.";
    public const string DestinationNotFound = "Conta de destino não encontrada.";
    public const string SameAccount = "Não é possível transferir para a própria conta.";
    public const string SourceInactive = "Conta de origem inativa.";
    public const string DestinationInactive = "Conta de destino inativa.";
    public const string InsufficientFunds = "Saldo insuficiente na conta de origem.";

    public static string IncompatibleCurrencies(string source, string destination, string transaction) =>
        $"Moedas incompatíveis (origem: {source}, destino: {destination}, transação: {transaction}).";
}

/// <summary>
/// Regras financeiras de uma transferência. Valida tudo antes de mover
/// qualquer valor: ou a transferência é aplicada por inteiro (débito, crédito
/// e Completed), ou nada é movimentado e a transação é marcada como Failed.
///
/// Câmbio: debita <see cref="Transaction.Amount"/> na moeda da origem e credita
/// <see cref="Transaction.DestinationAmount"/> na moeda do destino — ambos fixados na criação
/// (snapshot). Nada é recalculado aqui; se a moeda de alguma conta não bater com o snapshot,
/// a transferência falha sem movimentar.
/// </summary>
public static class FundsTransfer
{
    public static TransferResult Execute(Transaction transaction, Account? source, Account? destination)
    {
        if (transaction.Status != TransactionStatus.Processing)
            throw new InvalidOperationException("A transação precisa estar em processamento.");

        var failure = Validate(transaction, source, destination);
        if (failure is not null)
        {
            transaction.Fail(failure);
            return TransferResult.Failure(failure);
        }

        source!.Debit(transaction.Amount);
        destination!.Credit(transaction.DestinationAmount);
        transaction.Complete();

        return TransferResult.Success();
    }

    public static string? Validate(Transaction transaction, Account? source, Account? destination)
    {
        if (transaction.Amount <= 0)
            return TransferFailureReasons.InvalidAmount;

        if (source is null)
            return TransferFailureReasons.SourceNotFound;

        if (destination is null)
            return TransferFailureReasons.DestinationNotFound;

        if (source.Id == destination.Id)
            return TransferFailureReasons.SameAccount;

        if (!source.IsActive)
            return TransferFailureReasons.SourceInactive;

        if (!destination.IsActive)
            return TransferFailureReasons.DestinationInactive;

        if (source.Currency != transaction.Currency || destination.Currency != transaction.DestinationCurrency)
            return TransferFailureReasons.IncompatibleCurrencies(
                source.Currency, destination.Currency,
                transaction.IsCrossCurrency ? $"{transaction.Currency}→{transaction.DestinationCurrency}" : transaction.Currency);

        if (transaction.DestinationAmount <= 0)
            return TransferFailureReasons.InvalidAmount;

        if (!source.HasSufficientFunds(transaction.Amount))
            return TransferFailureReasons.InsufficientFunds;

        return null;
    }
}
