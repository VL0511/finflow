using FinFlow.Application.Accounts;
using FinFlow.Application.Common;
using FinFlow.Domain.Enums;
using FinFlow.Domain.Services;
using Microsoft.Extensions.Logging;

namespace FinFlow.Application.Transactions;

public enum ProcessTransactionOutcome
{
    Completed,
    Failed,
    /// <summary>Mensagem duplicada ou transação já finalizada/cancelada: nada foi feito.</summary>
    Skipped,
    NotFound
}

/// <summary>
/// Caso de uso executado pelo Worker ao consumir TransactionRequested.
///
/// Idempotência: o status da transação é a fonte da verdade. A movimentação de
/// saldo só acontece no mesmo commit que leva a transação de Processing para
/// Completed. Se a mensagem chegar de novo, a transação já estará finalizada
/// e é ignorada.
///
/// Concorrência: a etapa 2 bloqueia (UPDLOCK) a transação e depois as duas contas
/// em ordem determinística, então instâncias concorrentes se serializam em vez de
/// conflitar, sem risco de deadlock. O rowversion segue como proteção adicional:
/// se algo escapar do bloqueio, o commit falha com rollback e o Worker reprocessa.
/// </summary>
public sealed class ProcessTransactionHandler(
    ITransactionRepository transactions,
    IAccountRepository accounts,
    IUnitOfWork unitOfWork,
    IAuditTrail audit,
    ILogger<ProcessTransactionHandler> logger)
{
    public async Task<ProcessTransactionOutcome> HandleAsync(Guid transactionId, CancellationToken cancellationToken = default)
    {
        // Etapa 1: Pending → Processing (fica visível no GET /status).
        var transaction = await transactions.GetByIdAsync(transactionId, cancellationToken);

        if (transaction is null)
        {
            logger.LogWarning("Transação {TransactionId} não encontrada.", transactionId);
            return ProcessTransactionOutcome.NotFound;
        }

        if (transaction.IsFinal)
        {
            logger.LogInformation(
                "Transação {TransactionId} já está {Status}; mensagem ignorada (idempotência).",
                transactionId, transaction.Status);
            return ProcessTransactionOutcome.Skipped;
        }

        if (transaction.Status == TransactionStatus.Pending)
        {
            transaction.StartProcessing();
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        // Se já estava Processing, é uma reentrega após falha antes do commit:
        // nada foi movimentado ainda, então é seguro continuar.

        // Etapa 2: validação + débito + crédito + status final, tudo num único
        // commit. Qualquer exceção provoca rollback de tudo.
        var outcome = ProcessTransactionOutcome.Skipped;

        await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            // Bloqueios pessimistas, sempre na mesma ordem: 1) a transação, 2) as contas por número.
            // Quem chega depois espera o commit de quem chegou antes e então relê o estado já
            // comitado: duplicatas veem o status final e saem; transferências que disputam a
            // mesma conta enxergam o saldo atualizado. O RowVersion continua como segunda barreira.
            var current = await transactions.GetByIdForUpdateAsync(transactionId, ct);
            if (current is null || current.Status != TransactionStatus.Processing)
            {
                outcome = ProcessTransactionOutcome.Skipped;
                return;
            }

            var (source, destination) = await accounts.GetPairForUpdateAsync(
                current.SourceAccount, current.DestinationAccount, ct);

            var result = FundsTransfer.Execute(current, source, destination);

            outcome = result.Succeeded
                ? ProcessTransactionOutcome.Completed
                : ProcessTransactionOutcome.Failed;

            // Auditoria no MESMO commit do débito/crédito/status: se o commit for desfeito, o
            // registro também é; se a operação for reexecutada, o registro é recriado uma única vez.
            audit.Record(
                result.Succeeded ? AuditActions.TransferCompleted : AuditActions.TransferFailed,
                nameof(Domain.Entities.Transaction), current.Id.ToString(),
                TransactionService.TransferDetails(current),
                result.Succeeded ? AuditResults.Succeeded : AuditResults.Failed);

            if (!result.Succeeded)
                logger.LogInformation(
                    "Transação {TransactionId} falhou: {Reason}", transactionId, result.FailureReason);
        }, cancellationToken);

        return outcome;
    }
}
