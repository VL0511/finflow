using System.Net.Http.Json;
using System.Text.Json;
using FinFlow.Application;
using FinFlow.Domain.Entities;
using FinFlow.Infrastructure;
using FinFlow.Infrastructure.Persistence.Context;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.MsSql;
using Testcontainers.RabbitMq;

namespace FinFlow.IntegrationTests.Infrastructure;

/// <summary>
/// Sobe um SQL Server e um RabbitMQ descartáveis (Testcontainers) uma única vez
/// para toda a suíte, aplica as migrations e oferece helpers de acesso.
/// Cada teste cria as próprias contas (números aleatórios), então os testes
/// não dependem uns dos outros nem das contas semeadas.
/// </summary>
public sealed class IntegrationTestEnvironment : IAsyncLifetime
{
    private const string RabbitUser = "finflow";
    private const string RabbitPassword = "finflow_test";

    // Mesmas imagens do docker-compose (já estão no cache local do Docker).
    private readonly MsSqlContainer _sql = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest")
        .Build();

    private readonly RabbitMqContainer _rabbit = new RabbitMqBuilder("rabbitmq:4-management")
        .WithUsername(RabbitUser)
        .WithPassword(RabbitPassword)
        .Build();

    private FinFlowApiFactory? _apiFactory;

    public string ConnectionString { get; private set; } = string.Empty;

    public IReadOnlyDictionary<string, string?> Settings { get; private set; } =
        new Dictionary<string, string?>();

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_sql.StartAsync(), _rabbit.StartAsync());

        ConnectionString = new SqlConnectionStringBuilder(_sql.GetConnectionString())
        {
            InitialCatalog = "FinFlowIntegrationTests"
        }.ConnectionString;

        Settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:FinFlowDb"] = ConnectionString,
            ["RabbitMq:HostName"] = _rabbit.Hostname,
            ["RabbitMq:Port"] = _rabbit.GetMappedPublicPort(5672).ToString(),
            ["RabbitMq:UserName"] = RabbitUser,
            ["RabbitMq:Password"] = RabbitPassword,
            ["RabbitMq:ClientName"] = "finflow-integration-tests",
            ["Logging:LogLevel:Default"] = "Warning",
            // Ambiente "Testing" (não Development): o seed EXIGE senha forte configurada, como em produção.
            ["Seed:Admin:Email"] = AdminEmail,
            ["Seed:Admin:Password"] = InitialAdminPassword,
            // Muitos logins na suíte, todos do mesmo "IP" do TestServer.
            ["Auth:LoginAttemptsPerMinute"] = "100000",
            // Revalida a sessão a cada requisição: desativação/troca de perfil valem na hora nos testes.
            ["Auth:SecurityStampValidationInterval"] = "00:00:00"
        };

        await using (var db = CreateDbContext())
            await db.Database.MigrateAsync();

        // Primeiro login do administrador semeado: troca de senha obrigatória, como em produção.
        AdminClient = await ApiFactory.LoginAsync(AdminEmail, InitialAdminPassword, newPassword: AdminPassword);
    }

    // ---------- Autenticação ----------

    public const string AdminEmail = "admin@finflow.com";
    private const string InitialAdminPassword = "Inicial#Admin2026";
    public const string AdminPassword = "Testes#Admin2026";

    /// <summary>Cliente autenticado como o Administrator semeado (senha já trocada).</summary>
    public HttpClient AdminClient { get; private set; } = null!;

    public sealed record TestUser(string Id, string Email, string Password, string Role);

    /// <summary>Cria um usuário pela API administrativa (sem troca de senha pendente).</summary>
    public async Task<TestUser> CreateUserAsync(string role, bool requirePasswordChange = false, string? password = null)
    {
        var email = $"{role.ToLowerInvariant()}-{Guid.NewGuid():N}@teste.finflow";
        password ??= $"Senha#{Guid.NewGuid():N}"[..20] + "A1";
        var response = await AdminClient.PostAsJsonAsync("/api/admin/users", new
        {
            email,
            fullName = $"Usuário {role}",
            password,
            role,
            requirePasswordChange
        });
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Criar usuário {role} falhou: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return new TestUser(body.GetProperty("id").GetString()!, email, password, role);
    }

    /// <summary>Usuário novo do perfil + cliente autenticado como ele.</summary>
    public async Task<(TestUser User, HttpClient Client)> CreateUserClientAsync(string role)
    {
        var user = await CreateUserAsync(role);
        return (user, await ApiFactory.LoginAsync(user.Email, user.Password));
    }

    public async Task DisposeAsync()
    {
        if (_apiFactory is not null)
            await _apiFactory.DisposeAsync();

        await Task.WhenAll(_sql.DisposeAsync().AsTask(), _rabbit.DisposeAsync().AsTask());
    }

    /// <summary>API em memória SEM o publicador do outbox (mensagens ficam no banco).</summary>
    public FinFlowApiFactory ApiFactory => _apiFactory ??= new FinFlowApiFactory(this, enableOutboxPublisher: false);

    public FinFlowDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<FinFlowDbContext>().UseSqlServer(ConnectionString).Options);

    /// <summary>Container de DI com Application + Infrastructure reais (como o Worker usa).</summary>
    public ServiceProvider CreateServices()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(Settings).Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplication();
        services.AddInfrastructure(configuration);

        return services.BuildServiceProvider();
    }

    // ---------- Helpers de dados ----------

    public static string NewAccountNumber() =>
        Random.Shared.NextInt64(1_000_000_000, 9_999_999_999).ToString();

    public async Task<Account> CreateAccountAsync(
        decimal balance, string currency = "BRL", bool isActive = true, string holder = "Titular Teste", string? ownerUserId = null)
    {
        var account = new Account(NewAccountNumber(), holder, balance, currency, isActive, ownerUserId);

        await using var db = CreateDbContext();
        db.Accounts.Add(account);
        await db.SaveChangesAsync();

        return account;
    }

    public async Task<decimal> GetBalanceAsync(string accountNumber)
    {
        await using var db = CreateDbContext();
        return await db.Accounts
            .Where(a => a.AccountNumber == accountNumber)
            .Select(a => a.Balance)
            .SingleAsync();
    }

    public async Task<Transaction?> GetTransactionAsync(Guid id)
    {
        await using var db = CreateDbContext();
        return await db.Transactions.AsNoTracking().SingleOrDefaultAsync(t => t.Id == id);
    }
}

/// <summary>Todos os testes de integração compartilham os containers e rodam em sequência.</summary>
[CollectionDefinition(Name)]
public sealed class IntegrationCollection : ICollectionFixture<IntegrationTestEnvironment>
{
    public const string Name = "Integration";
}
