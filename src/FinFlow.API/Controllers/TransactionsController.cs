using FinFlow.API.Security;
using FinFlow.Application.Common;
using FinFlow.Application.Transactions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinFlow.API.Controllers;

/// <summary>
/// Transferências entre contas (processadas de forma assíncrona pelo Worker). Customer opera e
/// consulta só o que envolve as próprias contas; Administrator e Treasurer, tudo.
/// </summary>
[ApiController]
[Route("api/transactions")]
[Produces("application/json")]
[Authorize(Policy = Policies.Banking)]
public sealed class TransactionsController(ITransactionService service) : ControllerBase
{
    /// <summary>Solicita uma transferência.</summary>
    /// <remarks>
    /// A transação é registrada como <c>Pending</c> e enviada ao RabbitMQ. O Worker a processa
    /// em seguida (<c>Processing</c> → <c>Completed</c> ou <c>Failed</c>). Acompanhe pelo
    /// <c>GET /api/transactions/{id}/status</c>.
    /// </remarks>
    /// <response code="202">Transferência aceita para processamento.</response>
    /// <response code="400">Dados inválidos.</response>
    /// <response code="422">Conta inexistente/fora do seu escopo, moeda desativada ou sem taxa de câmbio vigente.</response>
    [HttpPost]
    [ProducesResponseType<TransactionResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Create(CreateTransactionRequest request, CancellationToken cancellationToken)
    {
        var transaction = await service.CreateAsync(request, cancellationToken);
        return AcceptedAtAction(nameof(GetStatus), new { id = transaction.Id }, transaction);
    }

    /// <summary>Prévia de uma transferência: moedas, taxa vigente e valor a receber.</summary>
    /// <remarks>
    /// Mesmo cálculo da criação, sem gravar nada. A criação recalcula tudo no momento em que é
    /// feita — se a taxa mudar entre a prévia e a confirmação, vale a da criação.
    /// </remarks>
    /// <response code="200">Prévia calculada.</response>
    /// <response code="400">Dados inválidos.</response>
    /// <response code="422">Conta inexistente/fora do seu escopo, moeda desativada ou sem taxa vigente.</response>
    [HttpPost("preview")]
    [ProducesResponseType<TransferPreviewResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Preview(CreateTransactionRequest request, CancellationToken cancellationToken) =>
        Ok(await service.PreviewAsync(request, cancellationToken));

    /// <summary>Lista as transações, paginadas, da mais recente para a mais antiga.</summary>
    /// <remarks>
    /// Filtros opcionais, combinados entre si e aplicados antes da paginação (<c>totalItems</c>
    /// conta só o que atende aos filtros):
    /// - <c>status</c>: um ou mais status, repetidos (<c>status=Pending&amp;status=Processing</c>)
    ///   ou separados por vírgula (<c>status=Pending,Processing</c>);
    /// - <c>accountNumber</c>: conta presente como origem ou destino.
    ///
    /// Paginação: <c>page</c> padrão 1; <c>pageSize</c> padrão 10, máximo 100 (valores fora da
    /// faixa são normalizados e devolvidos na resposta).
    /// </remarks>
    /// <param name="page">Página (começa em 1). Padrão: 1.</param>
    /// <param name="pageSize">Registros por página. Padrão: 10; máximo: 100.</param>
    /// <param name="status">Status aceitos (Pending, Processing, Completed, Failed, Cancelled).</param>
    /// <param name="accountNumber">Número da conta (origem ou destino).</param>
    /// <param name="cancellationToken"></param>
    /// <response code="200">Página de transações.</response>
    /// <response code="400">Status inválido ou parâmetro não numérico.</response>
    [HttpGet]
    [ProducesResponseType<PagedResult<TransactionResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> List(
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromQuery] string[]? status,
        [FromQuery] string? accountNumber,
        CancellationToken cancellationToken) =>
        Ok(await service.ListAsync(
            new TransactionListQuery { Status = status, AccountNumber = accountNumber },
            PageRequest.Create(page, pageSize),
            cancellationToken));

    /// <summary>Quantidade de transações por status (todas as transações).</summary>
    /// <remarks>Calculado no banco — não depende da paginação.</remarks>
    /// <response code="200">Contagens por status.</response>
    [HttpGet("summary")]
    [ProducesResponseType<TransactionSummaryResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Summary(CancellationToken cancellationToken) =>
        Ok(await service.GetSummaryAsync(cancellationToken));

    /// <summary>Consulta uma transação pelo ID.</summary>
    /// <param name="id">ID da transação.</param>
    /// <param name="cancellationToken"></param>
    /// <response code="200">Transação encontrada.</response>
    /// <response code="404">Transação não encontrada.</response>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<TransactionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken) =>
        Ok(await service.GetByIdAsync(id, cancellationToken));

    /// <summary>Consulta o status atual de uma transação.</summary>
    /// <remarks>
    /// Use após o <c>POST</c> para acompanhar o processamento assíncrono.
    ///
    /// Status possíveis:
    /// - <c>Pending</c>: aguardando o Worker (pode ser cancelada)
    /// - <c>Processing</c>: em processamento pelo Worker
    /// - <c>Completed</c>: saldos movimentados com sucesso
    /// - <c>Failed</c>: rejeitada; o motivo vem em <c>failureReason</c>
    /// - <c>Cancelled</c>: cancelada pelo cliente antes do processamento
    ///
    /// O status é alterado apenas pelo Worker e pelo endpoint de cancelamento.
    /// </remarks>
    /// <param name="id">ID da transação.</param>
    /// <param name="cancellationToken"></param>
    /// <response code="200">Status atual (e motivo, se falhou).</response>
    /// <response code="404">Transação não encontrada.</response>
    [HttpGet("{id:guid}/status")]
    [ProducesResponseType<TransactionStatusResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetStatus(Guid id, CancellationToken cancellationToken) => Ok(await service.GetStatusAsync(id, cancellationToken));

    /// <summary>Solicita o cancelamento de uma transação.</summary>
    /// <remarks>Só é possível enquanto a transação está <c>Pending</c>.</remarks>
    /// <param name="id">ID da transação.</param>
    /// <param name="cancellationToken"></param>
    /// <response code="200">Transação cancelada.</response>
    /// <response code="404">Transação não encontrada.</response>
    /// <response code="409">A transação já está em processamento ou finalizada.</response>
    [HttpPatch("{id:guid}/cancel")]
    [ProducesResponseType<TransactionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken cancellationToken) =>
        Ok(await service.CancelAsync(id, cancellationToken));
}
