using System.Security.Claims;
using System.Threading.RateLimiting;
using FinFlow.Application.Common;
using FinFlow.Application.Users;
using FinFlow.Infrastructure.Identity;
using FinFlow.Infrastructure.Persistence.Context;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace FinFlow.API.Security;

/// <summary>
/// Policies de autorização — ÚNICO lugar que liga perfis a capacidades. Controllers só
/// referenciam os nomes. Para permissões mais finas no futuro (aprovação, limites, dupla
/// autorização), novas policies/requirements entram aqui sem espalhar checagens pelo código.
/// </summary>
public static class Policies
{
    /// <summary>Apenas autenticado (inclusive com troca de senha pendente): /me, troca de senha, logout.</summary>
    public const string Authenticated = nameof(Authenticated);

    /// <summary>Operar contas e transferências no próprio escopo: todos os perfis.</summary>
    public const string Banking = nameof(Banking);

    /// <summary>Abrir conta: Administrator (para qualquer usuário) e Customer (para si).</summary>
    public const string AccountOpening = nameof(AccountOpening);

    /// <summary>Operações da instituição: Administrator e Treasurer.</summary>
    public const string Operations = nameof(Operations);

    /// <summary>Administração: usuários, perfis, moedas, taxas, auditoria. Só Administrator.</summary>
    public const string Administration = nameof(Administration);
}

public static class FinFlowClaims
{
    public const string FullName = "finflow:full_name";

    /// <summary>Presente enquanto o usuário precisa trocar a senha: bloqueia tudo exceto /auth.</summary>
    public const string MustChangePassword = "finflow:must_change_password";
}

/// <summary>Opções de sessão e proteção do login (seção Auth).</summary>
public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    /// <summary>Inatividade máxima da sessão (expiração deslizante).</summary>
    public TimeSpan SessionTimeout { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>
    /// A cada quanto o cookie é revalidado contra o banco (security stamp): perfil alterado,
    /// usuário desativado ou senha redefinida derrubam a sessão em no máximo este tempo.
    /// </summary>
    public TimeSpan SecurityStampValidationInterval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>Tentativas de login por IP por minuto (além do bloqueio por usuário do Identity).</summary>
    public int LoginAttemptsPerMinute { get; set; } = 10;

    /// <summary>Senhas erradas seguidas até bloquear o usuário.</summary>
    public int MaxFailedAccessAttempts { get; set; } = 5;

    public TimeSpan LockoutDuration { get; set; } = TimeSpan.FromMinutes(15);
}

public static class SecuritySetup
{
    public const string LoginRateLimitPolicy = "login";

    public static IServiceCollection AddFinFlowSecurity(this IServiceCollection services, IConfiguration configuration)
    {
        var auth = configuration.GetSection(AuthOptions.SectionName).Get<AuthOptions>() ?? new AuthOptions();
        services.Configure<AuthOptions>(configuration.GetSection(AuthOptions.SectionName));
        services.Configure<AdminSeedOptions>(configuration.GetSection(AdminSeedOptions.SectionName));

        // ASP.NET Core Identity: hash PBKDF2, bloqueio por tentativas, security stamp, roles.
        services.AddIdentityCore<AppUser>(o =>
            {
                o.User.RequireUniqueEmail = true;
                o.Password.RequiredLength = 10;
                o.Password.RequireDigit = true;
                o.Password.RequireLowercase = true;
                o.Password.RequireUppercase = true;
                o.Password.RequireNonAlphanumeric = false;
                o.Lockout.AllowedForNewUsers = true;
                o.Lockout.MaxFailedAccessAttempts = auth.MaxFailedAccessAttempts;
                o.Lockout.DefaultLockoutTimeSpan = auth.LockoutDuration;
            })
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<FinFlowDbContext>()
            .AddSignInManager<FinFlowSignInManager>()
            .AddClaimsPrincipalFactory<FinFlowClaimsPrincipalFactory>();

        // Cookie HttpOnly + SameSite=Strict: o JavaScript não lê a sessão e outro site não a envia.
        // O frontend chama /api pelo proxy do Vite (mesma origem).
        services.AddAuthentication(IdentityConstants.ApplicationScheme).AddIdentityCookies();
        services.ConfigureApplicationCookie(o =>
        {
            o.Cookie.Name = "finflow.session";
            o.Cookie.HttpOnly = true;
            o.Cookie.SameSite = SameSiteMode.Strict;
            o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            o.ExpireTimeSpan = auth.SessionTimeout;
            o.SlidingExpiration = true;
            // API: sem redirecionar para página de login; 401/403 com ProblemDetails.
            o.Events.OnRedirectToLogin = ctx => WriteProblemAsync(ctx, StatusCodes.Status401Unauthorized, "Sessão ausente ou expirada. Faça login.");
            o.Events.OnRedirectToAccessDenied = ctx => WriteProblemAsync(ctx, StatusCodes.Status403Forbidden, "Seu perfil não tem permissão para esta operação.");
        });
        services.Configure<SecurityStampValidatorOptions>(o => o.ValidationInterval = auth.SecurityStampValidationInterval);

        services.AddAuthorization(o =>
        {
            AuthorizationPolicy Build(params string[] roles)
            {
                var builder = new AuthorizationPolicyBuilder().RequireAuthenticatedUser();
                if (roles.Length > 0)
                    builder.RequireRole(roles);
                // Enquanto a troca de senha estiver pendente, nada além de /auth funciona.
                builder.RequireAssertion(ctx => !ctx.User.HasClaim(c => c.Type == FinFlowClaims.MustChangePassword));
                return builder.Build();
            }

            o.AddPolicy(Policies.Authenticated, p => p.RequireAuthenticatedUser());
            o.AddPolicy(Policies.Banking, Build(Roles.Administrator, Roles.Treasurer, Roles.Customer));
            o.AddPolicy(Policies.AccountOpening, Build(Roles.Administrator, Roles.Customer));
            o.AddPolicy(Policies.Operations, Build(Roles.Administrator, Roles.Treasurer));
            o.AddPolicy(Policies.Administration, Build(Roles.Administrator));

            // Negado por padrão: endpoint sem [Authorize]/[AllowAnonymous] exige perfil válido.
            o.FallbackPolicy = Build(Roles.Administrator, Roles.Treasurer, Roles.Customer);
        });

        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.AddPolicy(LoginRateLimitPolicy, ctx => RateLimitPartition.GetFixedWindowLimiter(
                ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = auth.LoginAttemptsPerMinute,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0
                }));
        });

        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, HttpCurrentUser>();
        services.AddScoped<IUserManagementService, UserManagementService>();
        return services;
    }

    private static async Task WriteProblemAsync(Microsoft.AspNetCore.Authentication.RedirectContext<CookieAuthenticationOptions> ctx, int status, string title)
    {
        ctx.Response.StatusCode = status;
        await ctx.Response.WriteAsJsonAsync(new ProblemDetails { Status = status, Title = title },
            options: null, contentType: "application/problem+json");
    }
}

/// <summary>Usuário desativado não entra (nem com a senha certa).</summary>
public sealed class FinFlowSignInManager(
    UserManager<AppUser> userManager,
    IHttpContextAccessor contextAccessor,
    IUserClaimsPrincipalFactory<AppUser> claimsFactory,
    IOptions<IdentityOptions> optionsAccessor,
    ILogger<SignInManager<AppUser>> logger,
    Microsoft.AspNetCore.Authentication.IAuthenticationSchemeProvider schemes,
    IUserConfirmation<AppUser> confirmation)
    : SignInManager<AppUser>(userManager, contextAccessor, claimsFactory, optionsAccessor, logger, schemes, confirmation)
{
    public override async Task<bool> CanSignInAsync(AppUser user) => user.IsActive && await base.CanSignInAsync(user);
}

/// <summary>Claims emitidas no login (roles vêm do banco via Identity, nunca do cliente).</summary>
public sealed class FinFlowClaimsPrincipalFactory(
    UserManager<AppUser> userManager,
    RoleManager<IdentityRole> roleManager,
    IOptions<IdentityOptions> options)
    : UserClaimsPrincipalFactory<AppUser, IdentityRole>(userManager, roleManager, options)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(AppUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);
        identity.AddClaim(new Claim(FinFlowClaims.FullName, user.FullName));
        if (user.MustChangePassword)
            identity.AddClaim(new Claim(FinFlowClaims.MustChangePassword, "true"));
        return identity;
    }
}

/// <summary>Usuário da requisição, a partir das claims do cookie validado pelo servidor.</summary>
public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public string? UserId => IsAuthenticated ? Principal!.FindFirstValue(ClaimTypes.NameIdentifier) : null;

    public string? Email => IsAuthenticated ? Principal!.FindFirstValue(ClaimTypes.Email) ?? Principal!.Identity!.Name : null;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    public bool IsInRole(string role) => IsAuthenticated && Principal!.IsInRole(role);
}
