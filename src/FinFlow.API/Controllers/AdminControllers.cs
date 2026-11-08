using FinFlow.API.Security;
using FinFlow.Application.Common;
using FinFlow.Application.Currencies;
using FinFlow.Application.Users;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinFlow.API.Controllers;

/// <summary>Usuários e perfis (somente Administrator).</summary>
[ApiController]
[Route("api/admin/users")]
[Produces("application/json")]
[Authorize(Policy = Policies.Administration)]
public sealed class UsersController(IUserManagementService users) : ControllerBase
{
    /// <summary>Lista os usuários (paginado, por e-mail).</summary>
    [HttpGet]
    [ProducesResponseType<PagedResult<UserResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] int? page, [FromQuery] int? pageSize,
        [FromQuery] string? search, [FromQuery] string? role, CancellationToken cancellationToken) =>
        Ok(await users.ListAsync(new UserListQuery { Search = search, Role = role }, PageRequest.Create(page, pageSize), cancellationToken));

    /// <summary>Consulta um usuário.</summary>
    [HttpGet("{id}")]
    [ProducesResponseType<UserResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(string id, CancellationToken cancellationToken) =>
        Ok(await users.GetAsync(id, cancellationToken));

    /// <summary>Cria um usuário com perfil e senha inicial.</summary>
    /// <response code="201">Criado.</response>
    /// <response code="400">Dados inválidos ou senha fora da política (mín. 10 caracteres, maiúscula, minúscula e dígito).</response>
    /// <response code="409">E-mail já cadastrado.</response>
    [HttpPost]
    [ProducesResponseType<UserResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(CreateUserRequest request, CancellationToken cancellationToken)
    {
        var user = await users.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = user.Id }, user);
    }

    /// <summary>Altera nome, perfil ou situação (ativo/inativo).</summary>
    /// <remarks>Ninguém altera o próprio perfil nem desativa a si mesmo.</remarks>
    /// <response code="403">Tentativa de alterar o próprio perfil ou se desativar.</response>
    [HttpPatch("{id}")]
    [ProducesResponseType<UserResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(string id, UpdateUserRequest request, CancellationToken cancellationToken) =>
        Ok(await users.UpdateAsync(id, request, cancellationToken));

    /// <summary>Define uma senha temporária (troca exigida no próximo login; sessões abertas caem).</summary>
    [HttpPost("{id}/reset-password")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ResetPassword(string id, ResetPasswordRequest request, CancellationToken cancellationToken)
    {
        await users.ResetPasswordAsync(id, request, cancellationToken);
        return NoContent();
    }
}

/// <summary>Auditoria (somente leitura, somente Administrator).</summary>
[ApiController]
[Route("api/admin/audit-logs")]
[Produces("application/json")]
[Authorize(Policy = Policies.Administration)]
public sealed class AuditLogsController(IAuditLogReader reader) : ControllerBase
{
    /// <summary>Registros de auditoria, mais recentes primeiro.</summary>
    [HttpGet]
    [ProducesResponseType<PagedResult<AuditLogResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] int? page, [FromQuery] int? pageSize,
        [FromQuery] AuditLogQuery query, CancellationToken cancellationToken) =>
        Ok(await reader.ListAsync(query, PageRequest.Create(page, pageSize), cancellationToken));
}

/// <summary>Moedas. Consulta: todos os perfis; cadastro e ativação: Administrator.</summary>
[ApiController]
[Route("api/currencies")]
[Produces("application/json")]
public sealed class CurrenciesController(ICurrencyService currencies) : ControllerBase
{
    /// <summary>Moedas cadastradas.</summary>
    /// <param name="includeInactive">Incluir desativadas (efetivo só para Administrator).</param>
    /// <param name="cancellationToken"></param>
    [HttpGet]
    [Authorize(Policy = Policies.Banking)]
    [ProducesResponseType<IReadOnlyList<CurrencyResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] bool includeInactive, CancellationToken cancellationToken) =>
        Ok(await currencies.ListAsync(includeInactive && User.IsInRole(Roles.Administrator), cancellationToken));

    /// <summary>Cadastra uma moeda.</summary>
    /// <response code="409">Moeda já cadastrada.</response>
    [HttpPost]
    [Authorize(Policy = Policies.Administration)]
    [ProducesResponseType<CurrencyResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(CreateCurrencyRequest request, CancellationToken cancellationToken)
    {
        var currency = await currencies.CreateAsync(request, cancellationToken);
        return Created($"/api/currencies/{currency.Code}", currency);
    }

    /// <summary>Renomeia, ativa ou desativa uma moeda.</summary>
    [HttpPatch("{code}")]
    [Authorize(Policy = Policies.Administration)]
    [ProducesResponseType<CurrencyResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(string code, UpdateCurrencyRequest request, CancellationToken cancellationToken) =>
        Ok(await currencies.UpdateAsync(code, request, cancellationToken));
}

/// <summary>Taxas de câmbio (manuais). Consulta das vigentes e cotação: todos; alteração e histórico: Administrator.</summary>
[ApiController]
[Route("api/exchange-rates")]
[Produces("application/json")]
public sealed class ExchangeRatesController(IExchangeRateService rates) : ControllerBase
{
    /// <summary>Taxas vigentes.</summary>
    [HttpGet]
    [Authorize(Policy = Policies.Banking)]
    [ProducesResponseType<IReadOnlyList<ExchangeRateResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListActive(CancellationToken cancellationToken) =>
        Ok(await rates.ListActiveAsync(cancellationToken));

    /// <summary>Converte um valor com a taxa vigente (cálculo no backend).</summary>
    /// <response code="422">Moeda inexistente/desativada ou sem taxa vigente.</response>
    [HttpGet("quote")]
    [Authorize(Policy = Policies.Banking)]
    [ProducesResponseType<ExchangeQuote>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Quote([FromQuery] string from, [FromQuery] string to, [FromQuery] decimal amount,
        CancellationToken cancellationToken) =>
        Ok(await rates.QuoteAsync(from, to, amount, cancellationToken));

    /// <summary>Histórico de todas as versões de taxas (vigentes e encerradas).</summary>
    [HttpGet("history")]
    [Authorize(Policy = Policies.Administration)]
    [ProducesResponseType<PagedResult<ExchangeRateResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> History([FromQuery] string? from, [FromQuery] string? to,
        [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken cancellationToken) =>
        Ok(await rates.ListHistoryAsync(from, to, PageRequest.Create(page, pageSize), cancellationToken));

    /// <summary>Define a taxa vigente de uma direção (cria nova versão; a anterior é encerrada).</summary>
    /// <remarks>A direção é explícita: USD→BRL não define BRL→USD. Transações já criadas não mudam.</remarks>
    /// <response code="201">Nova taxa vigente.</response>
    /// <response code="409">Alteração simultânea da mesma taxa.</response>
    /// <response code="422">Moeda inexistente ou desativada.</response>
    [HttpPost]
    [Authorize(Policy = Policies.Administration)]
    [ProducesResponseType<ExchangeRateResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Set(SetExchangeRateRequest request, CancellationToken cancellationToken)
    {
        var rate = await rates.SetAsync(request, cancellationToken);
        return Created("/api/exchange-rates", rate);
    }

    /// <summary>Encerra a taxa vigente de uma direção (conversões nessa direção passam a ser recusadas).</summary>
    [HttpDelete("{from}/{to}")]
    [Authorize(Policy = Policies.Administration)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Deactivate(string from, string to, CancellationToken cancellationToken)
    {
        await rates.DeactivateAsync(from, to, cancellationToken);
        return NoContent();
    }
}
