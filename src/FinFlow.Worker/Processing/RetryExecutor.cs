using System.Data.Common;
using FinFlow.Application.Common;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace FinFlow.Worker.Processing;

public enum RetryResultKind
{
    Succeeded,
    /// <summary>Só falhas transitórias, tentativas esgotadas: seguro tentar de novo mais tarde.</summary>
    TransientFailure,
    /// <summary>Falha não transitória, tentativas esgotadas.</summary>
    PersistentFailure
}

public sealed record RetryResult<T>(RetryResultKind Kind, T? Value, Exception? Error, int Attempts);

/// <summary>Classifica exceções como transitórias (vale tentar de novo) ou não.</summary>
public static class TransientFailures
{
    // 1205 deadlock, 1222 lock timeout, -2 timeout de comando, 4060/40613/40501/49918-49920
    // banco indisponível/limitado, 233/10053/10054/10060 conexão interrompida.
    private static readonly HashSet<int> TransientSqlErrors =
        [1205, 1222, -2, 4060, 40613, 40501, 49918, 49919, 49920, 233, 10053, 10054, 10060];

    public static bool IsTransient(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            switch (current)
            {
                case ConcurrencyConflictException:
                case DbUpdateConcurrencyException:
                case RetryLimitExceededException: // o EF já esgotou os retries de falha transitória do SQL
                case TimeoutException:
                    return true;
                case SqlException sql when sql.Errors.Cast<SqlError>().Any(e => TransientSqlErrors.Contains(e.Number)):
                    return true;
                case DbException { IsTransient: true }:
                    return true;
            }
        }

        return false;
    }
}

/// <summary>Retry com backoff exponencial + jitter, com orçamentos separados para falhas transitórias e persistentes.</summary>
public static class RetryExecutor
{
    public static async Task<RetryResult<T>> ExecuteAsync<T>(
        Func<int, Task<T>> operation,
        ProcessingOptions options,
        Action<int, Exception, bool, TimeSpan>? onRetry = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        CancellationToken cancellationToken = default)
    {
        delay ??= Task.Delay;

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return new RetryResult<T>(RetryResultKind.Succeeded, await operation(attempt), null, attempt);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                var transient = TransientFailures.IsTransient(ex);
                var maxAttempts = transient ? options.MaxTransientAttempts : options.MaxPersistentAttempts;

                if (attempt >= maxAttempts)
                {
                    var kind = transient ? RetryResultKind.TransientFailure : RetryResultKind.PersistentFailure;
                    return new RetryResult<T>(kind, default, ex, attempt);
                }

                var wait = ComputeBackoff(attempt, options);
                onRetry?.Invoke(attempt, ex, transient, wait);
                await delay(wait, cancellationToken);
            }
        }
    }

    /// <summary>Exponencial (base × 2^(n-1)) limitado ao teto, com jitter de 50–100% para dessincronizar Workers.</summary>
    public static TimeSpan ComputeBackoff(int attempt, ProcessingOptions options)
    {
        var exponential = options.BaseDelay.TotalMilliseconds * Math.Pow(2, Math.Min(attempt - 1, 16));
        var capped = Math.Min(exponential, options.MaxDelay.TotalMilliseconds);
        return TimeSpan.FromMilliseconds(capped * (0.5 + Random.Shared.NextDouble() * 0.5));
    }
}
