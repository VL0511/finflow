using FinFlow.API.Security;
using FinFlow.Application.Common;
using FinFlow.Infrastructure.Identity;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FinFlow.API.Controllers;

public sealed record LoginRequest
{
    /// <example>admin@finflow.com</example>
    public string Email { get; init; } = string.Empty;

    /// <example>admin</example>
    public string Password { get; init; } = string.Empty;

    /// <summary>Mantém a sessão após fechar o navegador (ainda sujeita à expiração por inatividade).</summary>
    public bool RememberMe { get; init; }
}

public sealed record ChangePasswordRequest
{
    public string CurrentPassword { get; init; } = string.Empty;
    public string NewPassword { get; init; } = string.Empty;
}

/// <summary>Usuário autenticado (GET /api/auth/me).</summary>
public sealed record MeResponse
{
    public string Id { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string FullName { get; init; } = string.Empty;

    /// <summary>Administrator, Treasurer ou Customer. Apenas informativo: o backend sempre reaplica as permissões.</summary>
    public string Role { get; init; } = string.Empty;

    public bool MustChangePassword { get; init; }
}

/// <summary>Autenticação por e-mail e senha (cookie de sessão HttpOnly).</summary>
[ApiController]
[Route("api/auth")]
[Produces("application/json")]
public sealed class AuthController(
    SignInManager<AppUser> signInManager,
    UserManager<AppUser> userManager,
    IAuditTrail audit,
    IUnitOfWork unitOfWork) : ControllerBase
{
    private const string InvalidCredentials = "E-mail ou senha inválidos.";

    /// <summary>Entra no sistema.</summary>
    /// <remarks>
    /// Em desenvolvimento, o administrador inicial é admin@finflow.com / admin (troca obrigatória
    /// no primeiro login). Após 5 senhas erradas o usuário fica bloqueado por 15 minutos; o IP
    /// também é limitado a 10 tentativas por minuto.
    /// </remarks>
    /// <response code="200">Autenticado; o cookie de sessão foi emitido.</response>
    /// <response code="401">Credenciais inválidas ou usuário desativado.</response>
    /// <response code="423">Usuário bloqueado temporariamente por tentativas inválidas.</response>
    /// <response code="429">Muitas tentativas a partir deste IP.</response>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(SecuritySetup.LoginRateLimitPolicy)]
    [ProducesResponseType<MeResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status423Locked)]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        var user = string.IsNullOrWhiteSpace(request.Email) ? null : await userManager.FindByEmailAsync(request.Email.Trim());
        if (user is null || string.IsNullOrEmpty(request.Password))
        {
            await AuditLoginAsync(null, request.Email, "UnknownUserOrEmptyPassword", cancellationToken);
            return Problem(statusCode: StatusCodes.Status401Unauthorized, title: InvalidCredentials);
        }

        // lockoutOnFailure: conta tentativas erradas e bloqueia após o limite.
        var result = await signInManager.PasswordSignInAsync(user, request.Password, request.RememberMe, lockoutOnFailure: true);

        if (result.Succeeded)
        {
            await AuditLoginAsync(user, user.Email, null, cancellationToken);
            return Ok(await ToMeAsync(user));
        }

        if (result.IsLockedOut)
        {
            await AuditLoginAsync(user, user.Email, "LockedOut", cancellationToken);
            return Problem(statusCode: StatusCodes.Status423Locked,
                title: "Usuário bloqueado temporariamente por excesso de tentativas. Tente novamente mais tarde.");
        }

        // Senha errada ou usuário desativado: mesma resposta (não revela qual dos dois).
        await AuditLoginAsync(user, user.Email, result.IsNotAllowed ? "NotAllowed" : "InvalidPassword", cancellationToken);
        return Problem(statusCode: StatusCodes.Status401Unauthorized, title: InvalidCredentials);
    }

    /// <summary>Encerra a sessão.</summary>
    /// <response code="204">Sessão encerrada.</response>
    [HttpPost("logout")]
    [Authorize(Policy = Policies.Authenticated)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout()
    {
        await signInManager.SignOutAsync();
        return NoContent();
    }

    /// <summary>Usuário da sessão atual.</summary>
    /// <response code="200">Dados do usuário.</response>
    /// <response code="401">Sem sessão ou sessão expirada.</response>
    [HttpGet("me")]
    [Authorize(Policy = Policies.Authenticated)]
    [ProducesResponseType<MeResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Me()
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null || !user.IsActive)
        {
            await signInManager.SignOutAsync();
            return Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Sessão inválida. Faça login novamente.");
        }
        return Ok(await ToMeAsync(user));
    }

    /// <summary>Troca a própria senha (obrigatório no primeiro login).</summary>
    /// <response code="200">Senha alterada; a sessão é renovada.</response>
    /// <response code="400">Senha atual incorreta ou nova senha fora da política.</response>
    [HttpPost("change-password")]
    [Authorize(Policy = Policies.Authenticated)]
    [ProducesResponseType<MeResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request, CancellationToken cancellationToken)
    {
        var user = await userManager.GetUserAsync(User)
            ?? throw new ForbiddenException("Sessão inválida.");

        if (string.IsNullOrEmpty(request.NewPassword) || request.NewPassword == request.CurrentPassword)
            throw new ValidationException([new ValidationFailure(nameof(request.NewPassword), "A nova senha deve ser diferente da atual.")]);

        var result = await userManager.ChangePasswordAsync(user, request.CurrentPassword ?? string.Empty, request.NewPassword);
        if (!result.Succeeded)
        {
            var property = result.Errors.Any(e => e.Code == "PasswordMismatch") ? nameof(request.CurrentPassword) : nameof(request.NewPassword);
            var messages = result.Errors.Select(e => e.Code == "PasswordMismatch" ? "Senha atual incorreta." : e.Description);
            throw new ValidationException(messages.Select(m => new ValidationFailure(property, m)));
        }

        user.MustChangePassword = false;
        await userManager.UpdateAsync(user);

        audit.RecordFor(user.Id, user.Email, AuditActions.PasswordChanged, "User", user.Id);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        // Novo cookie sem a claim de troca pendente (o security stamp mudou com a senha).
        await signInManager.RefreshSignInAsync(user);
        return Ok(await ToMeAsync(user));
    }

    private async Task<MeResponse> ToMeAsync(AppUser user) => new()
    {
        Id = user.Id,
        Email = user.Email ?? string.Empty,
        FullName = user.FullName,
        Role = (await userManager.GetRolesAsync(user)).FirstOrDefault() ?? string.Empty,
        MustChangePassword = user.MustChangePassword
    };

    private async Task AuditLoginAsync(AppUser? user, string? email, string? failure, CancellationToken cancellationToken)
    {
        audit.RecordFor(user?.Id, user?.Email ?? email?.Trim(),
            failure is null ? AuditActions.LoginSucceeded : AuditActions.LoginFailed,
            "User", user?.Id,
            failure is null ? null : new { reason = failure },
            failure is null ? AuditResults.Succeeded : AuditResults.Failed);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
