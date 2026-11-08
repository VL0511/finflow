using FinFlow.Infrastructure.Messaging.Outbox;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace FinFlow.IntegrationTests.Infrastructure;

/// <summary>
/// Sobe a FinFlow.API em memória (TestServer) apontando para os containers de teste.
/// Ambiente "Testing" (nunca Development, que carregaria a connection string do banco de dev).
/// </summary>
public sealed class FinFlowApiFactory(
    IntegrationTestEnvironment environment,
    bool enableOutboxPublisher,
    IReadOnlyDictionary<string, string?>? overrides = null)
    : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        // UseSetting é aplicado antes do Program.cs ler a configuração.
        foreach (var (key, value) in environment.Settings)
            builder.UseSetting(key, value);
        foreach (var (key, value) in overrides ?? new Dictionary<string, string?>())
            builder.UseSetting(key, value);

        builder.ConfigureTestServices(services =>
        {
            if (enableOutboxPublisher)
                return;

            var publisher = services.Where(d => d.ImplementationType == typeof(OutboxPublisherService)).ToList();
            foreach (var descriptor in publisher)
                services.Remove(descriptor);
        });
    }
}
