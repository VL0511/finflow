using FinFlow.Application.Transactions;
using Microsoft.Extensions.Options;

namespace FinFlow.Worker.Processing;

/// <summary>
/// Executa o ProcessTransactionHandler com a política de retry. Compartilhado pelo consumer
/// do RabbitMQ e pela recuperação de transações presas, para que os dois caminhos tenham
/// exatamente as mesmas garantias.
/// </summary>
public sealed class TransactionProcessor(
    IServiceScopeFactory scopeFactory,
    IOptions<ProcessingOptions> options,
    ILogger<TransactionProcessor> logger)
{
    public Task<RetryResult<ProcessTransactionOutcome>> ProcessAsync(
        Guid transactionId, string origin, CancellationToken stoppingToken) =>
        RetryExecutor.ExecuteAsync(
            async _ =>
            {
                // Escopo novo a cada tentativa = DbContext limpo, estado relido do banco.
                await using var scope = scopeFactory.CreateAsyncScope();
                var handler = scope.ServiceProvider.GetRequiredService<ProcessTransactionHandler>();

                // CancellationToken.None: uma vez iniciado, o processamento não é abortado no meio
                // pelo desligamento do Worker (o commit é atômico de qualquer forma).
                return await handler.HandleAsync(transactionId, CancellationToken.None);
            },
            options.Value,
            onRetry: (attempt, ex, transient, wait) => logger.LogWarning(
                "Transação {TransactionId} ({Origin}): falha {Kind} na tentativa {Attempt} ({Error}); nova tentativa em {Wait} ms.",
                transactionId, origin, transient ? "transitória" : "persistente", attempt,
                ex.GetType().Name, (int)wait.TotalMilliseconds),
            cancellationToken: stoppingToken);
}
