using FinFlow.Application.Common;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FinFlow.Infrastructure.Identity;

/// <summary>Credenciais do administrador inicial (seção Seed:Admin; use User Secrets ou variáveis de ambiente).</summary>
public sealed class AdminSeedOptions
{
    public const string SectionName = "Seed:Admin";

    public string Email { get; set; } = "admin@finflow.com";

    /// <summary>
    /// Senha inicial. Fora de Development é OBRIGATÓRIA e precisa atender à política de senha.
    /// Em Development, se omitida, usa "admin" (troca exigida no primeiro login).
    /// </summary>
    public string? Password { get; set; }

    public string FullName { get; set; } = "Administrador";
}

/// <summary>
/// Seed idempotente de roles e do administrador inicial, executado na inicialização da API.
/// Nunca recria nem altera um administrador existente (não sobrescreve a senha). Não aplica
/// migrations: exige o banco já atualizado.
/// </summary>
public static class IdentitySeeder
{
    public const string DevelopmentDefaultPassword = "admin";

    public static async Task SeedAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var provider = scope.ServiceProvider;
        var environment = provider.GetRequiredService<IHostEnvironment>();
        var options = provider.GetRequiredService<IOptions<AdminSeedOptions>>().Value;
        var roles = provider.GetRequiredService<RoleManager<IdentityRole>>();
        var users = provider.GetRequiredService<UserManager<AppUser>>();
        var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(IdentitySeeder));

        foreach (var role in Roles.All)
        {
            if (!await roles.RoleExistsAsync(role))
                Ensure(await roles.CreateAsync(new IdentityRole(role)), $"criar a role {role}");
        }

        var existing = await users.FindByEmailAsync(options.Email);
        if (existing is not null)
        {
            // Proteção de produção: a senha padrão de desenvolvimento nunca pode estar em uso fora de Development.
            if (!environment.IsDevelopment()
                && existing.PasswordHash is not null
                && users.PasswordHasher.VerifyHashedPassword(existing, existing.PasswordHash, DevelopmentDefaultPassword) != PasswordVerificationResult.Failed)
            {
                throw new InvalidOperationException(
                    $"O administrador '{options.Email}' ainda usa a senha padrão de desenvolvimento. " +
                    "Redefina a senha antes de iniciar fora do ambiente Development.");
            }
            return;
        }

        var password = options.Password;
        if (!environment.IsDevelopment())
        {
            if (string.IsNullOrWhiteSpace(password) || password == DevelopmentDefaultPassword)
                throw new InvalidOperationException(
                    $"Configure {AdminSeedOptions.SectionName}:Password (User Secrets ou variável de ambiente " +
                    "Seed__Admin__Password) com uma senha forte para criar o administrador inicial.");
        }

        var admin = new AppUser
        {
            UserName = options.Email,
            Email = options.Email,
            EmailConfirmed = true,
            FullName = options.FullName,
            MustChangePassword = true
        };

        if (string.IsNullOrWhiteSpace(password) || (environment.IsDevelopment() && password == DevelopmentDefaultPassword))
        {
            // Só em Development: "admin" não atende à política, então o hash é gerado sem os
            // validadores. A troca é obrigatória no primeiro login (MustChangePassword).
            admin.PasswordHash = users.PasswordHasher.HashPassword(admin, DevelopmentDefaultPassword);
            Ensure(await users.CreateAsync(admin), "criar o administrador");
        }
        else
        {
            Ensure(await users.CreateAsync(admin, password), "criar o administrador (verifique a política de senha)");
        }

        Ensure(await users.AddToRoleAsync(admin, Roles.Administrator), "atribuir a role Administrator");
        logger.LogWarning("Administrador inicial {Email} criado; a troca de senha será exigida no primeiro login.", options.Email);
    }

    private static void Ensure(IdentityResult result, string what)
    {
        if (!result.Succeeded)
            throw new InvalidOperationException($"Falha ao {what}: {string.Join("; ", result.Errors.Select(e => e.Description))}");
    }
}
