using FinFlow.Application.Transactions;
using FinFlow.Domain.Enums;
using Microsoft.Extensions.Options;

namespace FinFlow.Worker.Processing;

/// <summary>
/// Retoma transações presas:
/// - em Processing: a etapa 1 foi comitada, mas a mensagem se perdeu (foi para a DLQ por falha
///   persistente, ou o Worker caiu e a mensagem foi descartada);
/// - em Pending há mais de PendingStaleAfter: a mensagem se perdeu ANTES da etapa 1 (DLQ, fila
///   perdida/esvaziada). São processadas diretamente, sem republicar no outbox.
///
/// É seguro rodar em paralelo com o consumer e em várias instâncias: o handler bloqueia a
/// transação (UPDLOCK) e só movimenta saldo se ela ainda estiver em Processing; caso contrário
/// retorna Skipped. Nenhum valor é movimentado duas vezes.
/// </summary>
public sealed class StuckTransactionRecoveryService(
    IServiceScopeFactory scopeFactory,
    TransactionProcessor processor,
    IOptions<RecoveryOptions> options,
    TimeProvider timeProvider,
    ILogger<StuckTransactionRecoveryService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.Enabled)
        {
            logger.LogInformation("Recuperação de transações em Processing desabilitada.");
            return;
        }

        logger.LogInformation(
            "Recuperação de transações presas ativa (a cada {Interval}; Processing há mais de {StaleAfter}, Pending há mais de {PendingStaleAfter}).",
            settings.Interval, settings.StaleAfter, settings.PendingStaleAfter);

        using var timer = new PeriodicTimer(settings.Interval, timeProvider);
        do
        {
            try
            {
                await RecoverOnceAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Falha na varredura de transações presas; nova tentativa na próxima execução.");
            }
        }
        while (await WaitNextAsync(timer, stoppingToken));
    }

    /// <summary>
    /// Uma varredura: primeiro as presas em Processing (já passaram da etapa 1), depois as presas em
    /// Pending, cada grupo com até BatchSize itens. Retorna quantas foram concluídas por esta
    /// varredura (Completed/Failed); as já resolvidas por outro processo (Skipped) não contam.
    /// </summary>
    public async Task<int> RecoverOnceAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;

        var recovered = await RecoverAsync(TransactionStatus.Processing, settings.StaleAfter,
            (repository, olderThan) => repository.ListStaleProcessingAsync(olderThan, settings.BatchSize, stoppingToken),
            stoppingToken);

        if (stoppingToken.IsCancellationRequested)
            return recovered;

        // Consultado só depois de tratar as de Processing, para usar o estado mais recente.
        recovered += await RecoverAsync(TransactionStatus.Pending, settings.PendingStaleAfter,
            (repository, olderThan) => repository.ListStalePendingAsync(olderThan, settings.BatchSize, stoppingToken),
            stoppingToken);

        return recovered;
    }

    /// <summary>
    /// Retoma as candidatas pelo MESMO caminho do consumer (TransactionProcessor → ProcessTransactionHandler).
    /// A consulta não é confiável como decisão — o status pode mudar até o processamento —; quem decide é o
    /// handler: rowversion na saída de Pending (disputa com cancelamento/consumer/outro Worker) e UPDLOCK +
    /// conferência de Processing no commit que movimenta saldo. Por isso nada é movimentado duas vezes.
    /// </summary>
    private async Task<int> RecoverAsync(
        TransactionStatus stuckIn,
        TimeSpan staleAfter,
        Func<ITransactionRepository, DateTimeOffset, Task<IReadOnlyList<Guid>>> listCandidates,
        CancellationToken stoppingToken)
    {
        var olderThan = timeProvider.GetUtcNow() - staleAfter;

        IReadOnlyList<Guid> stuck;
        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            stuck = await listCandidates(scope.ServiceProvider.GetRequiredService<ITransactionRepository>(), olderThan);
        }

        if (stuck.Count == 0)
            return 0;

        logger.LogWarning(
            "{Count} transação(ões) presa(s) em {Status} há mais de {StaleAfter} encontrada(s); retomando.",
            stuck.Count, stuckIn, staleAfter);

        var recovered = 0;
        foreach (var transactionId in stuck)
        {
            if (stoppingToken.IsCancellationRequested)
                break;

            // Falhas de uma transação não interrompem as demais: ProcessAsync devolve o resultado, não lança.
            var result = await processor.ProcessAsync(transactionId, $"recuperação de {stuckIn}", stoppingToken);

            if (result.Kind != RetryResultKind.Succeeded)
            {
                logger.LogError(result.Error,
                    "Transação {TransactionId} (presa em {Status}) não foi recuperada ({Kind} após {Attempts} tentativas); nova tentativa na próxima varredura.",
                    transactionId, stuckIn, result.Kind, result.Attempts);
            }
            else if (result.Value is ProcessTransactionOutcome.Completed or ProcessTransactionOutcome.Failed)
            {
                logger.LogWarning("Transação {TransactionId} (presa em {Status}) recuperada: {Outcome}.",
                    transactionId, stuckIn, result.Value);
                recovered++;
            }
            else
            {
                // Skipped: cancelada, ou finalizada pelo consumer/outro Worker entre a consulta e o processamento.
                logger.LogInformation(
                    "Transação {TransactionId} (presa em {Status}) já havia sido resolvida por outro processo: {Outcome}.",
                    transactionId, stuckIn, result.Value);
            }
        }

        return recovered;
    }

    private static async Task<bool> WaitNextAsync(PeriodicTimer timer, CancellationToken stoppingToken)
    {
        try
        {
            return await timer.WaitForNextTickAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
