using FinFlow.Application.Accounts;
using FinFlow.Application.Currencies;
using FinFlow.Application.Transactions;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace FinFlow.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly);

        services.AddSingleton<IAccountNumberGenerator, RandomAccountNumberGenerator>();
        services.AddScoped<IAccountService, AccountService>();
        services.AddScoped<ICurrencyService, CurrencyService>();
        services.AddScoped<IExchangeRateService, ExchangeRateService>();
        services.AddScoped<ITransactionService, TransactionService>();
        services.AddScoped<ProcessTransactionHandler>();

        return services;
    }
}
