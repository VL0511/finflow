using FinFlow.Application.Accounts;
using FinFlow.Application.Transactions;
using FinFlow.Domain.Entities;

namespace FinFlow.UnitTests.Mocks;

/// <summary>
/// Dados mockados usados pelos testes. Centralizar aqui facilita visualizar
/// e reaproveitar os cenários. Cada método devolve uma instância nova, então
/// um teste nunca interfere no outro.
/// </summary>
public static class TransactionMockData
{
    // Contas fictícias (os mesmos números semeados no banco).
    public const string ContaAna = "12345";    // BRL, R$ 1.000,00, ativa
    public const string ContaBruno = "67890";  // BRL, R$   250,00, ativa
    public const string ContaCarla = "11223";  // USD, US$ 5.000,00, ativa
    public const string ContaDiego = "44556";  // BRL, R$   300,00, INATIVA

    // ---------- Contas ----------

    public static Account Ana() => new(ContaAna, "Ana Souza", 1_000.00m, "BRL");
    public static Account Bruno() => new(ContaBruno, "Bruno Lima", 250.00m, "BRL");
    public static Account Carla() => new(ContaCarla, "Carla Mendes", 5_000.00m, "USD");
    public static Account Diego() => new(ContaDiego, "Diego Rocha", 300.00m, "BRL", isActive: false);

    public static List<Account> ListaDeContas() => [Ana(), Bruno(), Carla(), Diego()];

    public static Currency Brl() => new("BRL", "Real brasileiro", 2);
    public static Currency Usd() => new("USD", "Dólar americano", 2);
    public static Currency Eur() => new("EUR", "Euro", 2);

    public static CreateAccountRequest RequestNovaConta() => new()
    {
        HolderName = "Fernanda Lima",
        InitialBalance = 1_500.00m,
        Currency = "BRL",
        IsActive = true
    };

    // ---------- Transações ----------

    /// <summary>Request do exemplo da documentação: 12345 → 67890, R$ 500,00.</summary>
    public static CreateTransactionRequest RequestValido() => new()
    {
        SourceAccount = ContaAna,
        DestinationAccount = ContaBruno,
        Amount = 500.00m
    };

    public static Transaction TransacaoPendente(decimal valor = 500.00m) =>
        new(ContaAna, ContaBruno, valor, "BRL");

    public static Transaction TransacaoEmProcessamento(
        string origem = ContaAna, string destino = ContaBruno, decimal valor = 500.00m, string moeda = "BRL")
    {
        var transaction = new Transaction(origem, destino, valor, moeda);
        transaction.StartProcessing();
        return transaction;
    }

    public static Transaction TransacaoCancelada()
    {
        var transaction = new Transaction(ContaCarla, ContaDiego, 75.90m, "BRL");
        transaction.Cancel();
        return transaction;
    }

    public static Transaction TransacaoConcluida()
    {
        var transaction = TransacaoEmProcessamento();
        transaction.Complete();
        return transaction;
    }

    /// <summary>Lista de transações variadas, simulando o retorno do banco.</summary>
    public static List<Transaction> ListaDeTransacoes() =>
    [
        new(ContaAna, ContaBruno, 500.00m),
        new(ContaBruno, ContaCarla, 1_250.75m),
        new(ContaCarla, ContaDiego, 89.90m),
        new(ContaDiego, ContaAna, 10_000.00m),
        TransacaoCancelada()
    ];
}

/// <summary>Usuário atual configurável para os testes de escopo e RBAC.</summary>
public sealed class FakeCurrentUser(string? userId = null, string? role = null, string? email = null) : FinFlow.Application.Common.ICurrentUser
{
    public static FakeCurrentUser System() => new();
    public static FakeCurrentUser Customer(string id = "cliente-1") => new(id, FinFlow.Application.Common.Roles.Customer, $"{id}@teste");
    public static FakeCurrentUser Treasurer(string id = "tesoureiro-1") => new(id, FinFlow.Application.Common.Roles.Treasurer, $"{id}@teste");
    public static FakeCurrentUser Administrator(string id = "admin-1") => new(id, FinFlow.Application.Common.Roles.Administrator, $"{id}@teste");

    public string? UserId => userId;
    public string? Email => email;
    public bool IsAuthenticated => userId is not null;
    public bool IsInRole(string r) => role == r;
}
