using FinFlow.Domain.Enums;
using FinFlow.Domain.Services;
using FinFlow.UnitTests.Mocks;

namespace FinFlow.UnitTests.Domain;

/// <summary>
/// Regras financeiras da transferência. Em toda falha: nenhum saldo muda,
/// a transação fica Failed e o motivo é registrado.
/// </summary>
public class FundsTransferTests
{
    [Fact]
    public void Transferir_500_DeAnaParaBruno_DeveMovimentarSaldosEConcluir()
    {
        var ana = TransactionMockData.Ana();     // R$ 1.000
        var bruno = TransactionMockData.Bruno(); // R$   250
        var transaction = TransactionMockData.TransacaoEmProcessamento(valor: 500.00m);

        var result = FundsTransfer.Execute(transaction, ana, bruno);

        Assert.True(result.Succeeded);
        Assert.Equal(500.00m, ana.Balance);
        Assert.Equal(750.00m, bruno.Balance);
        Assert.Equal(TransactionStatus.Completed, transaction.Status);
        Assert.Null(transaction.FailureReason);
    }

    [Fact]
    public void Transferir_SaldoInsuficiente_DeveFalharSemMovimentar()
    {
        var ana = TransactionMockData.Ana();
        var bruno = TransactionMockData.Bruno(); // R$ 250
        var transaction = TransactionMockData.TransacaoEmProcessamento(
            TransactionMockData.ContaBruno, TransactionMockData.ContaAna, valor: 500.00m);

        var result = FundsTransfer.Execute(transaction, bruno, ana);

        AssertFalhou(result, transaction, TransferFailureReasons.InsufficientFunds);
        Assert.Equal(250.00m, bruno.Balance);
        Assert.Equal(1_000.00m, ana.Balance);
    }

    [Fact]
    public void Transferir_ContaOrigemInexistente_DeveFalhar()
    {
        var transaction = TransactionMockData.TransacaoEmProcessamento();

        var result = FundsTransfer.Execute(transaction, source: null, TransactionMockData.Bruno());

        AssertFalhou(result, transaction, TransferFailureReasons.SourceNotFound);
    }

    [Fact]
    public void Transferir_ContaDestinoInexistente_DeveFalhar()
    {
        var ana = TransactionMockData.Ana();
        var transaction = TransactionMockData.TransacaoEmProcessamento();

        var result = FundsTransfer.Execute(transaction, ana, destination: null);

        AssertFalhou(result, transaction, TransferFailureReasons.DestinationNotFound);
        Assert.Equal(1_000.00m, ana.Balance);
    }

    [Fact]
    public void Transferir_ParaAPropriaConta_DeveFalhar()
    {
        var ana = TransactionMockData.Ana();
        var transaction = TransactionMockData.TransacaoEmProcessamento();

        var result = FundsTransfer.Execute(transaction, ana, ana);

        AssertFalhou(result, transaction, TransferFailureReasons.SameAccount);
        Assert.Equal(1_000.00m, ana.Balance);
    }

    [Fact]
    public void Transferir_OrigemInativa_DeveFalhar()
    {
        var diego = TransactionMockData.Diego(); // inativa
        var transaction = TransactionMockData.TransacaoEmProcessamento(
            TransactionMockData.ContaDiego, TransactionMockData.ContaBruno, 100m);

        var result = FundsTransfer.Execute(transaction, diego, TransactionMockData.Bruno());

        AssertFalhou(result, transaction, TransferFailureReasons.SourceInactive);
    }

    [Fact]
    public void Transferir_DestinoInativo_DeveFalhar()
    {
        var ana = TransactionMockData.Ana();
        var transaction = TransactionMockData.TransacaoEmProcessamento(
            TransactionMockData.ContaAna, TransactionMockData.ContaDiego, 100m);

        var result = FundsTransfer.Execute(transaction, ana, TransactionMockData.Diego());

        AssertFalhou(result, transaction, TransferFailureReasons.DestinationInactive);
        Assert.Equal(1_000.00m, ana.Balance);
    }

    [Fact]
    public void Transferir_EntreMoedasDiferentes_BRLparaUSD_DeveFalhar()
    {
        var ana = TransactionMockData.Ana();     // BRL
        var carla = TransactionMockData.Carla(); // USD
        var transaction = TransactionMockData.TransacaoEmProcessamento(
            TransactionMockData.ContaAna, TransactionMockData.ContaCarla, 100m);

        var result = FundsTransfer.Execute(transaction, ana, carla);

        AssertFalhou(result, transaction,
            TransferFailureReasons.IncompatibleCurrencies("BRL", "USD", "BRL"));
        Assert.Equal(1_000.00m, ana.Balance);
        Assert.Equal(5_000.00m, carla.Balance);
    }

    [Fact]
    public void Transferir_MoedaDaTransacaoDiferenteDasContas_DeveFalhar()
    {
        var transaction = TransactionMockData.TransacaoEmProcessamento(moeda: "EUR");

        var result = FundsTransfer.Execute(transaction, TransactionMockData.Ana(), TransactionMockData.Bruno());

        AssertFalhou(result, transaction,
            TransferFailureReasons.IncompatibleCurrencies("BRL", "BRL", "EUR"));
    }

    [Fact]
    public void Transferir_TransacaoQueNaoEstaEmProcessamento_DeveLancar()
    {
        var transaction = TransactionMockData.TransacaoPendente();

        Assert.Throws<InvalidOperationException>(() =>
            FundsTransfer.Execute(transaction, TransactionMockData.Ana(), TransactionMockData.Bruno()));
    }

    private static void AssertFalhou(TransferResult result, FinFlow.Domain.Entities.Transaction transaction, string motivo)
    {
        Assert.False(result.Succeeded);
        Assert.Equal(motivo, result.FailureReason);
        Assert.Equal(TransactionStatus.Failed, transaction.Status);
        Assert.Equal(motivo, transaction.FailureReason);
    }
}
