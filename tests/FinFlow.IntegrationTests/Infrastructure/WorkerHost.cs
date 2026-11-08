using FinFlow.Application;
using FinFlow.Infrastructure;
using FinFlow.Worker;
using FinFlow.Worker.Consumers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace FinFlow.IntegrationTests.Infrastructure;

/// <summary>Sobe o FinFlow.Worker real (mesma composição do Program.cs, via AddFinFlowWorker) dentro do teste.</summary>
public sealed class WorkerHost : IAsyncDisposable
{
    private readonly IHost _host;
    private bool _stopped;

    private WorkerHost(IHost host) => _host = host;

    /// <summary>Consumidor da fila de transferências desta instância.</summary>
    public TransactionRequestedConsumer Consumer =>
        _host.Services.GetServices<IHostedService>().OfType<TransactionRequestedConsumer>().Single();

    /// <param name="environment">Containers de teste.</param>
    /// <param name="overrides">Configurações extras para este Worker (ex.: Worker:Recovery:Interval).</param>
    /// <param name="configureServices">Substituições de serviços (ex.: injeção de falhas), aplicadas por último.</param>
    public static async Task<WorkerHost> StartAsync(
        IntegrationTestEnvironment environment,
        IReadOnlyDictionary<string, string?>? overrides = null,
        Action<IServiceCollection>? configureServices = null)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            EnvironmentName = "Testing"
        });

        builder.Configuration.AddInMemoryCollection(environment.Settings);
        if (overrides is not null)
            builder.Configuration.AddInMemoryCollection(overrides);

        builder.Services.AddApplication();
        builder.Services.AddInfrastructure(builder.Configuration);
        builder.Services.AddFinFlowWorker(builder.Configuration);
        configureServices?.Invoke(builder.Services);

        var host = builder.Build();
        await host.StartAsync();

        return new WorkerHost(host);
    }

    /// <summary>Desligamento controlado (o mesmo que o host faz ao receber SIGTERM/Ctrl+C).</summary>
    public async Task StopAsync()
    {
        if (_stopped)
            return;
        _stopped = true;
        await _host.StopAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _host.Dispose();
    }
}
