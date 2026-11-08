using FinFlow.Worker.Consumers;
using FinFlow.Worker.Processing;

namespace FinFlow.Worker;

public static class DependencyInjection
{
    /// <summary>Composição do Worker (usada pelo Program.cs e pelos testes de integração).</summary>
    public static IServiceCollection AddFinFlowWorker(this IServiceCollection services, IConfiguration configuration)
    {
        // Configuração inválida derruba o Worker na inicialização, com mensagem clara,
        // em vez de falhar só quando a primeira mensagem chegar.
        services.AddOptions<ProcessingOptions>()
            .Bind(configuration.GetSection(ProcessingOptions.SectionName))
            .Validate(o => o.MaxTransientAttempts >= 1 && o.MaxPersistentAttempts >= 1,
                $"{ProcessingOptions.SectionName}: as quantidades de tentativas devem ser ≥ 1.")
            .Validate(o => o.BaseDelay >= TimeSpan.Zero && o.MaxDelay >= o.BaseDelay,
                $"{ProcessingOptions.SectionName}: use 0 ≤ BaseDelay ≤ MaxDelay.")
            .ValidateOnStart();

        services.AddOptions<RecoveryOptions>()
            .Bind(configuration.GetSection(RecoveryOptions.SectionName))
            .Validate(o => o.Interval > TimeSpan.Zero && o.BatchSize >= 1
                    && o.StaleAfter >= TimeSpan.Zero && o.PendingStaleAfter >= TimeSpan.Zero,
                $"{RecoveryOptions.SectionName}: Interval > 0, BatchSize ≥ 1 e limites de tempo ≥ 0.")
            .ValidateOnStart();

        services.AddOptions<ConsumerOptions>()
            .Bind(configuration.GetSection(ConsumerOptions.SectionName))
            .Validate(o => o.MaxConcurrency is >= 1 and <= 64,
                $"{ConsumerOptions.SectionName}:MaxConcurrency deve estar entre 1 e 64.")
            .Validate(o => o.PrefetchCount is >= 1 and <= ushort.MaxValue,
                $"{ConsumerOptions.SectionName}:PrefetchCount deve estar entre 1 e {ushort.MaxValue}.")
            .Validate(o => o.ReconnectDelay > TimeSpan.Zero,
                $"{ConsumerOptions.SectionName}:ReconnectDelay deve ser positivo.")
            .ValidateOnStart();

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<TransactionProcessor>();

        services.AddHostedService<TransactionRequestedConsumer>();
        services.AddHostedService<StuckTransactionRecoveryService>();

        return services;
    }
}
