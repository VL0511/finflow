using FinFlow.Application.Common;
using FinFlow.Domain.Exceptions;
using FinFlow.Worker.Processing;
using Microsoft.EntityFrameworkCore.Storage;

namespace FinFlow.IntegrationTests.Worker;

/// <summary>Política de retry do Worker (sem containers: lógica pura).</summary>
public class RetryExecutorTests
{
    private static readonly ProcessingOptions Options = new()
    {
        MaxTransientAttempts = 6,
        MaxPersistentAttempts = 3,
        BaseDelay = TimeSpan.FromMilliseconds(100),
        MaxDelay = TimeSpan.FromSeconds(5)
    };

    private static readonly Func<TimeSpan, CancellationToken, Task> NoDelay = (_, _) => Task.CompletedTask;

    [Fact]
    public void Classificacao_ConflitoDeadlockETimeout_SaoTransitorios_RegraDeNegocioNao()
    {
        Assert.True(TransientFailures.IsTransient(new ConcurrencyConflictException("x")));
        Assert.True(TransientFailures.IsTransient(new RetryLimitExceededException("x")));
        Assert.True(TransientFailures.IsTransient(new TimeoutException()));
        Assert.True(TransientFailures.IsTransient(
            new InvalidOperationException("embrulho", new ConcurrencyConflictException("x"))));

        Assert.False(TransientFailures.IsTransient(new DomainException("Saldo insuficiente.")));
        Assert.False(TransientFailures.IsTransient(new InvalidOperationException("bug")));
        Assert.False(TransientFailures.IsTransient(new NullReferenceException()));
    }

    [Fact]
    public async Task ConflitoQueSeResolve_ConcluiNaTentativaSeguinte()
    {
        var result = await RetryExecutor.ExecuteAsync(
            attempt => attempt < 3 ? throw new ConcurrencyConflictException("x") : Task.FromResult("ok"),
            Options, delay: NoDelay);

        Assert.Equal(RetryResultKind.Succeeded, result.Kind);
        Assert.Equal("ok", result.Value);
        Assert.Equal(3, result.Attempts);
    }

    [Fact]
    public async Task ConflitoQueNuncaSeResolve_EsgotaOrcamentoTransitorio_ENaoEClassificadoComoPersistente()
    {
        var waits = new List<TimeSpan>();
        var result = await RetryExecutor.ExecuteAsync<string>(
            _ => throw new ConcurrencyConflictException("x"),
            Options, onRetry: (_, _, _, wait) => waits.Add(wait), delay: NoDelay);

        // TransientFailure → o consumer devolve à fila, NUNCA manda para a DLQ.
        Assert.Equal(RetryResultKind.TransientFailure, result.Kind);
        Assert.Equal(Options.MaxTransientAttempts, result.Attempts);
        Assert.Equal(Options.MaxTransientAttempts - 1, waits.Count);
    }

    [Fact]
    public async Task ErroPersistente_UsaOrcamentoMenor_EViraPersistentFailure()
    {
        var calls = 0;
        var result = await RetryExecutor.ExecuteAsync<string>(
            _ => { calls++; throw new InvalidOperationException("bug"); },
            Options, delay: NoDelay);

        Assert.Equal(RetryResultKind.PersistentFailure, result.Kind);
        Assert.Equal(Options.MaxPersistentAttempts, calls);
    }

    [Theory]
    [InlineData(1, 50, 100)]
    [InlineData(2, 100, 200)]
    [InlineData(4, 400, 800)]
    [InlineData(10, 2500, 5000)] // limitado pelo teto (MaxDelay)
    public void Backoff_ExponencialComJitter_RespeitaOTeto(int attempt, int minMs, int maxMs)
    {
        for (var i = 0; i < 200; i++)
        {
            var wait = RetryExecutor.ComputeBackoff(attempt, Options).TotalMilliseconds;
            Assert.InRange(wait, minMs, maxMs);
        }
    }
}
