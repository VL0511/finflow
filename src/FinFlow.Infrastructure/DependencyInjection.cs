using FinFlow.Application.Accounts;
using FinFlow.Application.Common;
using FinFlow.Application.Currencies;
using FinFlow.Application.Users;
using FinFlow.Infrastructure.Identity;
using FinFlow.Application.Messaging;
using FinFlow.Application.Transactions;
using FinFlow.Infrastructure.Messaging.Outbox;
using FinFlow.Infrastructure.Messaging.RabbitMq;
using FinFlow.Infrastructure.Persistence.Context;
using FinFlow.Infrastructure.Persistence.Repositories;
using FinFlow.Infrastructure.Persistence.UnitOfWork;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FinFlow.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Persistência (SQL Server) e conexão com o RabbitMQ.</summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("FinFlowDb")
            ?? throw new InvalidOperationException("Connection string 'FinFlowDb' não configurada.");

        services.AddDbContext<FinFlowDbContext>(options =>
            options.UseSqlServer(connectionString, sql =>
            {
                sql.MigrationsAssembly(typeof(FinFlowDbContext).Assembly.FullName);
                sql.EnableRetryOnFailure();
            }));

        services.AddScoped<IAccountRepository, AccountRepository>();
        services.AddScoped<ITransactionRepository, TransactionRepository>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        services.AddScoped<IMessageOutbox, EfMessageOutbox>();
        services.AddScoped<ICurrencyRepository, CurrencyRepository>();
        services.AddScoped<IExchangeRateRepository, ExchangeRateRepository>();
        services.AddScoped<IAuditTrail, EfAuditTrail>();
        services.AddScoped<IAuditLogReader, AuditLogReader>();
        services.AddScoped<IUserDirectory, UserDirectory>();

        // Fora da API (Worker, testes) não há usuário: ações registradas como "sistema".
        // A API substitui pelo usuário do cookie de autenticação.
        services.TryAddScoped<ICurrentUser, SystemCurrentUser>();

        services.Configure<RabbitMqOptions>(configuration.GetSection(RabbitMqOptions.SectionName));
        services.AddSingleton<RabbitMqConnectionProvider>();

        return services;
    }

    /// <summary>Publica em background as mensagens gravadas no outbox.</summary>
    public static IServiceCollection AddOutboxPublisher(this IServiceCollection services)
    {
        services.AddHostedService<OutboxPublisherService>();
        return services;
    }
}
