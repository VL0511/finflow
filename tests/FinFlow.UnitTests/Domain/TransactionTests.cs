using FinFlow.Domain.Entities;
using FinFlow.Domain.Enums;
using FinFlow.UnitTests.Mocks;

namespace FinFlow.UnitTests.Domain;

public class TransactionTests
{
    [Fact]
    public void Criar_ComDadosValidos_DeveIniciarPendente()
    {
        var antes = DateTimeOffset.UtcNow;

        var transaction = new Transaction("12345", "67890", 500.00m, "brl");

        Assert.NotEqual(Guid.Empty, transaction.Id);
        Assert.Equal("12345", transaction.SourceAccount);
        Assert.Equal("67890", transaction.DestinationAccount);
        Assert.Equal(500.00m, transaction.Amount);
        Assert.Equal("BRL", transaction.Currency);
        Assert.Equal(TransactionStatus.Pending, transaction.Status);
        Assert.Null(transaction.FailureReason);
        Assert.Null(transaction.UpdatedAt);
        Assert.False(transaction.IsFinal);
        Assert.InRange(transaction.CreatedAt, antes, DateTimeOffset.UtcNow);
    }

    [Fact]
    public void Criar_DuasTransacoes_DevemTerIdsDiferentes()
    {
        var a = TransactionMockData.TransacaoPendente();
        var b = TransactionMockData.TransacaoPendente();

        Assert.NotEqual(a.Id, b.Id);
    }

    [Theory]
    [InlineData("", "67890", 100, "Conta de origem obrigatória.")]
    [InlineData("   ", "67890", 100, "Conta de origem obrigatória.")]
    [InlineData("12345", "", 100, "Conta de destino obrigatória.")]
    [InlineData("12345", "12345", 100, "As contas devem ser diferentes.")]
    [InlineData("12345", "67890", 0, "O valor deve ser maior que zero.")]
    [InlineData("12345", "67890", -50, "O valor deve ser maior que zero.")]
    public void Criar_ComDadosInvalidos_DeveLancarArgumentException(
        string origem, string destino, decimal valor, string mensagemEsperada)
    {
        var ex = Assert.Throws<ArgumentException>(() => new Transaction(origem, destino, valor));

        Assert.Equal(mensagemEsperada, ex.Message);
    }

    // ---------- Máquina de estados ----------

    [Fact]
    public void FluxoFeliz_PendingProcessingCompleted()
    {
        var transaction = TransactionMockData.TransacaoPendente();

        transaction.StartProcessing();
        Assert.Equal(TransactionStatus.Processing, transaction.Status);
        Assert.NotNull(transaction.UpdatedAt);

        transaction.Complete();
        Assert.Equal(TransactionStatus.Completed, transaction.Status);
        Assert.True(transaction.IsFinal);
    }

    [Fact]
    public void Falhar_EmProcessamento_DeveRegistrarMotivo()
    {
        var transaction = TransactionMockData.TransacaoEmProcessamento();

        transaction.Fail("Saldo insuficiente na conta de origem.");

        Assert.Equal(TransactionStatus.Failed, transaction.Status);
        Assert.Equal("Saldo insuficiente na conta de origem.", transaction.FailureReason);
        Assert.True(transaction.IsFinal);
    }

    [Fact]
    public void Falhar_SemMotivo_DeveLancarArgumentException()
    {
        var transaction = TransactionMockData.TransacaoEmProcessamento();

        Assert.Throws<ArgumentException>(() => transaction.Fail(" "));
    }

    [Fact]
    public void Cancelar_TransacaoPendente_DeveMudarStatusParaCancelled()
    {
        var transaction = TransactionMockData.TransacaoPendente();

        transaction.Cancel();

        Assert.Equal(TransactionStatus.Cancelled, transaction.Status);
        Assert.True(transaction.IsFinal);
    }

    [Fact]
    public void Cancelar_TransacaoJaCancelada_DeveLancarInvalidOperationException()
    {
        var transaction = TransactionMockData.TransacaoCancelada();

        var ex = Assert.Throws<InvalidOperationException>(transaction.Cancel);

        Assert.Equal("Somente transações pendentes podem ser canceladas.", ex.Message);
    }

    [Fact]
    public void Cancelar_TransacaoEmProcessamento_DeveLancarInvalidOperationException()
    {
        var transaction = TransactionMockData.TransacaoEmProcessamento();

        Assert.Throws<InvalidOperationException>(transaction.Cancel);
    }

    [Fact]
    public void Concluir_TransacaoPendente_SemPassarPorProcessing_DeveLancar()
    {
        var transaction = TransactionMockData.TransacaoPendente();

        Assert.Throws<InvalidOperationException>(transaction.Complete);
    }

    [Fact]
    public void IniciarProcessamento_TransacaoJaConcluida_DeveLancar()
    {
        var transaction = TransactionMockData.TransacaoConcluida();

        Assert.Throws<InvalidOperationException>(transaction.StartProcessing);
    }
}
