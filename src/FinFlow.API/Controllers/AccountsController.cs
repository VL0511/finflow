using FinFlow.API.Security;
using FinFlow.Application.Accounts;
using FinFlow.Application.Common;
using FinFlow.Application.Transactions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinFlow.API.Controllers;

/// <summary>
/// Contas. Customer enxerga só as próprias; Administrator e Treasurer, todas. Conta fora do
/// escopo responde 404 (não revela que existe).
/// </summary>
[ApiController]
[Route("api/accounts")]
[Produces("application/json")]
[Authorize(Policy = Policies.Banking)]
public sealed class AccountsController(IAccountService service, ITransactionService transactions) : ControllerBase
{
    /// <summary>Abre uma conta; o número é gerado pelo backend.</summary>
    /// <remarks>
    /// O número (10 dígitos, com dígito verificador) é gerado e garantido único pelo servidor e
    /// volta na resposta. Customer abre conta para si; Administrator pode indicar o dono
    /// (<c>ownerUserId</c>) ou omiti-lo (conta institucional). A moeda precisa estar ativa.
    ///
    /// Contas legadas para teste: 12345 (Ana, R$ 1.000, BRL), 67890 (Bruno, R$ 250, BRL),
    /// 11223 (Carla, US$ 5.000, USD) e 44556 (Diego, R$ 300, BRL, inativa).
    /// </remarks>
    /// <response code="201">Conta criada.</response>
    /// <response code="400">Dados inválidos.</response>
    /// <response code="403">Perfil sem permissão (Treasurer não abre contas).</response>
    /// <response code="422">Moeda inexistente/desativada ou dono inválido.</response>
    [HttpPost]
    [Authorize(Policy = Policies.AccountOpening)]
    [ProducesResponseType<AccountResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Create(CreateAccountRequest request, CancellationToken cancellationToken)
    {
        var account = await service.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = account.Id }, account);
    }

    /// <summary>Lista as contas, paginadas e ordenadas pelo número da conta.</summary>
    /// <remarks>
    /// Valores fora da faixa são normalizados (e devolvidos na resposta): <c>page</c> &lt; 1 vira 1;
    /// <c>pageSize</c> ausente ou &lt; 1 vira 10; acima de 100 vira 100. Página além da última
    /// devolve <c>items</c> vazio com os totais corretos.
    /// </remarks>
    /// <param name="page">Página (começa em 1). Padrão: 1.</param>
    /// <param name="pageSize">Registros por página. Padrão: 10; máximo: 100.</param>
    /// <param name="cancellationToken"></param>
    /// <response code="200">Página de contas.</response>
    /// <response code="400">Parâmetro não numérico.</response>
    [HttpGet]
    [ProducesResponseType<PagedResult<AccountResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> List(
        [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken cancellationToken) =>
        Ok(await service.ListAsync(PageRequest.Create(page, pageSize), cancellationToken));

    /// <summary>Indicadores globais das contas (quantidades e saldos por moeda).</summary>
    /// <remarks>Calculado no banco sobre todas as contas — não depende da paginação.</remarks>
    /// <response code="200">Resumo das contas.</response>
    [HttpGet("summary")]
    [ProducesResponseType<AccountSummaryResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Summary(CancellationToken cancellationToken) =>
        Ok(await service.GetSummaryAsync(cancellationToken));

    /// <summary>Consulta uma conta pelo ID.</summary>
    /// <param name="id">ID da conta (ex.: 11111111-1111-1111-1111-111111111111).</param>
    /// <param name="cancellationToken"></param>
    /// <response code="200">Conta encontrada.</response>
    /// <response code="404">Conta não encontrada.</response>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<AccountResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken) =>
        Ok(await service.GetByIdAsync(id, cancellationToken));

    /// <summary>Consulta o saldo de uma conta.</summary>
    /// <param name="id">ID da conta.</param>
    /// <param name="cancellationToken"></param>
    /// <response code="200">Saldo atual.</response>
    /// <response code="404">Conta não encontrada.</response>
    [HttpGet("{id:guid}/balance")]
    [ProducesResponseType<AccountBalanceResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetBalance(Guid id, CancellationToken cancellationToken) =>
        Ok(await service.GetBalanceAsync(id, cancellationToken));

    /// <summary>Entradas e saídas liquidadas da conta, por dia.</summary>
    /// <remarks>
    /// Considera apenas transferências <c>Completed</c>, na data da liquidação (<c>updatedAt</c>)
    /// no fuso informado. Entrada = conta é destino; saída = conta é origem. Somente dias com
    /// movimento são retornados. Período máximo: 731 dias.
    /// </remarks>
    /// <param name="id">ID da conta.</param>
    /// <param name="query">Período (from/to, inclusivos) e fuso (utcOffsetMinutes, ex.: -180).</param>
    /// <param name="cancellationToken"></param>
    /// <response code="200">Série diária.</response>
    /// <response code="400">Período ou fuso inválido.</response>
    /// <response code="404">Conta não encontrada.</response>
    [HttpGet("{id:guid}/cash-flow")]
    [ProducesResponseType<AccountCashFlowResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CashFlow(
        Guid id, [FromQuery] AccountCashFlowQuery query, CancellationToken cancellationToken) =>
        Ok(await transactions.GetAccountCashFlowAsync(id, query, cancellationToken));
}
