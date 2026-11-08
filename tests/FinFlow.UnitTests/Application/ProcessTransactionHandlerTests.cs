using FinFlow.Application.Accounts;
using FinFlow.Application.Common;
using FinFlow.Application.Transactions;
using FinFlow.Domain.Entities;
using FinFlow.Domain.Enums;
using FinFlow.Domain.Services;
using FinFlow.UnitTests.Mocks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace FinFlow.UnitTests.Application;

/// <summary>
/// Caso de uso do Worker: processa TransactionRequested.
/// Cenário base: transferência de R$ 500 de Ana (R$ 1.000) para Bruno (R$ 250).
/// </summary>
public class ProcessTransactionHandlerTests
{
    private readonly Mock<ITransactionRepository> _transactions = new();
    private readonly Mock<IAccountRepository> _accounts = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<IAuditTrail> _audit = new();
    private readonly ProcessTransactionHandler _handler;

    private readonly Account _ana = TransactionMockData.Ana();
    private readonly Account _bruno = TransactionMockData.Bruno();

    public ProcessTransactionHandlerTests()
    {
        _handler = new ProcessTransactionHandler(
            _transactions.Object, _accounts.Object, _unitOfWork.Object, _audit.Object,
            NullLogger<ProcessTransactionHandler>.Instance);

        SetupAccount(_ana);
        SetupAccount(_bruno);

        // Simula a transação SQL: executa a operação recebida.
        _unitOfWork
            .Setup(u => u.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task>>(), It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task>, CancellationToken>((op, ct) => op(ct));
    }

    [Fact]
    public async Task Processar_TransacaoPendente_DeveTransferirEConcluir()
    {
        var transaction = Setup(TransactionMockData.TransacaoPendente(500.00m));

        var outcome = await _handler.HandleAsync(transaction.Id);

        Assert.Equal(ProcessTransactionOutcome.Completed, outcome);
        Assert.Equal(TransactionStatus.Completed, transaction.Status);
        Assert.Equal(500.00m, _ana.Balance);
        Assert.Equal(750.00m, _bruno.Balance);
    }

    [Fact]
    public async Task Processar_DevePersistirProcessingAntes_EDepoisTudoNumaUnicaTransacaoSql()
    {
        var transaction = Setup(TransactionMockData.TransacaoPendente());
        var statusNoPrimeiroSave = (TransactionStatus?)null;
        _unitOfWork
            .Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Callback(() => statusNoPrimeiroSave ??= transaction.Status)
            .Returns(Task.CompletedTask);

        await _handler.HandleAsync(transaction.Id);

        // 1º passo: Pending → Processing salvo isoladamente.
        Assert.Equal(TransactionStatus.Processing, statusNoPrimeiroSave);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        // 2º passo: débito + crédito + Completed dentro de uma transação SQL.
        _unitOfWork.Verify(u => u.ExecuteInTransactionAsync(
            It.IsAny<Func<CancellationToken, Task>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Processar_SaldoInsuficiente_DeveMarcarFailedComMotivo()
    {
        var transaction = Setup(TransactionMockData.TransacaoPendente(5_000.00m));

        var outcome = await _handler.HandleAsync(transaction.Id);

        Assert.Equal(ProcessTransactionOutcome.Failed, outcome);
        Assert.Equal(TransactionStatus.Failed, transaction.Status);
        Assert.Equal(TransferFailureReasons.InsufficientFunds, transaction.FailureReason);
        Assert.Equal(1_000.00m, _ana.Balance);
        Assert.Equal(250.00m, _bruno.Balance);
    }

    [Fact]
    public async Task Processar_ContaDesativadaDepoisDaSolicitacao_DeveFalhar()
    {
        var transaction = Setup(TransactionMockData.TransacaoPendente());
        _bruno.Deactivate(); // situação mudou entre o POST e o processamento

        var outcome = await _handler.HandleAsync(transaction.Id);

        Assert.Equal(ProcessTransactionOutcome.Failed, outcome);
        Assert.Equal(TransferFailureReasons.DestinationInactive, transaction.FailureReason);
        Assert.Equal(1_000.00m, _ana.Balance);
    }

    // ---------- Idempotência ----------

    [Fact]
    public async Task Idempotencia_MesmaMensagemDuasVezes_DeveTransferirUmaUnicaVez()
    {
        var transaction = Setup(TransactionMockData.TransacaoPendente(500.00m));

        var primeira = await _handler.HandleAsync(transaction.Id);
        var segunda = await _handler.HandleAsync(transaction.Id); // reentrega da mesma mensagem

        Assert.Equal(ProcessTransactionOutcome.Completed, primeira);
        Assert.Equal(ProcessTransactionOutcome.Skipped, segunda);
        Assert.Equal(500.00m, _ana.Balance);   // debitado só uma vez
        Assert.Equal(750.00m, _bruno.Balance); // creditado só uma vez
    }

    [Theory]
    [InlineData(TransactionStatus.Completed)]
    [InlineData(TransactionStatus.Failed)]
    [InlineData(TransactionStatus.Cancelled)]
    public async Task Idempotencia_TransacaoJaFinalizada_DeveIgnorarSemTocarNoBanco(TransactionStatus status)
    {
        var transaction = Setup(status switch
        {
            TransactionStatus.Completed => TransactionMockData.TransacaoConcluida(),
            TransactionStatus.Cancelled => TransactionMockData.TransacaoCancelada(),
            _ => Falhada()
        });

        var outcome = await _handler.HandleAsync(transaction.Id);

        Assert.Equal(ProcessTransactionOutcome.Skipped, outcome);
        Assert.Equal(status, transaction.Status);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        _unitOfWork.Verify(u => u.ExecuteInTransactionAsync(
            It.IsAny<Func<CancellationToken, Task>>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Equal(1_000.00m, _ana.Balance);
        Assert.Equal(250.00m, _bruno.Balance);

        static Transaction Falhada()
        {
            var t = TransactionMockData.TransacaoEmProcessamento();
            t.Fail("Saldo insuficiente na conta de origem.");
            return t;
        }
    }

    [Fact]
    public async Task Reentrega_AposQuedaComTransacaoEmProcessing_DeveRetomarEConcluir()
    {
        // Worker caiu depois de marcar Processing e antes do commit da transferência:
        // nada foi movimentado, então a reentrega precisa concluir o trabalho.
        var transaction = Setup(TransactionMockData.TransacaoEmProcessamento());

        var outcome = await _handler.HandleAsync(transaction.Id);

        Assert.Equal(ProcessTransactionOutcome.Completed, outcome);
        Assert.Equal(500.00m, _ana.Balance);
        Assert.Equal(750.00m, _bruno.Balance);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ConflitoDeConcorrencia_DevePropagarParaOWorkerReprocessar()
    {
        var transaction = Setup(TransactionMockData.TransacaoPendente());
        _unitOfWork
            .Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ConcurrencyConflictException("alterado por outro processo"));

        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => _handler.HandleAsync(transaction.Id));

        Assert.Equal(1_000.00m, _ana.Balance);
        Assert.Equal(250.00m, _bruno.Balance);
    }

    [Fact]
    public async Task Etapa2_DeveLerTransacaoEContasComBloqueio_NuncaComLeituraSimples()
    {
        var transaction = Setup(TransactionMockData.TransacaoPendente());

        await _handler.HandleAsync(transaction.Id);

        _transactions.Verify(r => r.GetByIdForUpdateAsync(transaction.Id, It.IsAny<CancellationToken>()), Times.Once);
        _accounts.Verify(r => r.GetPairForUpdateAsync(
            transaction.SourceAccount, transaction.DestinationAccount, It.IsAny<CancellationToken>()), Times.Once);
        _accounts.Verify(r => r.GetByNumberAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Processar_TransacaoInexistente_DeveRetornarNotFound()
    {
        var outcome = await _handler.HandleAsync(Guid.NewGuid());

        Assert.Equal(ProcessTransactionOutcome.NotFound, outcome);
    }

    private Transaction Setup(Transaction transaction)
    {
        _transactions
            .Setup(r => r.GetByIdAsync(transaction.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(transaction);
        // Etapa 2 lê a transação com bloqueio (UPDLOCK).
        _transactions
            .Setup(r => r.GetByIdForUpdateAsync(transaction.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(transaction);
        return transaction;
    }

    private readonly Dictionary<string, Account> _accountsByNumber = [];

    private void SetupAccount(Account account)
    {
        _accountsByNumber[account.AccountNumber] = account;
        _accounts
            .Setup(r => r.GetByNumberAsync(account.AccountNumber, It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);
        // Etapa 2 bloqueia as duas contas de uma vez (ordem determinística fica no repositório).
        _accounts
            .Setup(r => r.GetPairForUpdateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string source, string destination, CancellationToken _) =>
                (_accountsByNumber.GetValueOrDefault(source), _accountsByNumber.GetValueOrDefault(destination)));
    }
}
