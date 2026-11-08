using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FinFlow.Application.Common;
using FinFlow.Infrastructure;
using FinFlow.Infrastructure.Identity;
using FinFlow.Infrastructure.Persistence.Context;
using FinFlow.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;

namespace FinFlow.IntegrationTests.Api;

/// <summary>Autenticação: login, sessão, troca de senha, bloqueios e seed do administrador.</summary>
[Collection(IntegrationCollection.Name)]
public sealed class AuthTests(IntegrationTestEnvironment env)
{
    private async Task<HttpResponseMessage> LoginAsync(HttpClient client, string email, string password) =>
        await client.PostAsJsonAsync("/api/auth/login", new { email, password });

    // ---------- Login ----------

    [Fact]
    public async Task LoginValido_EmiteSessaoHttpOnly_EMeRetornaOPerfil()
    {
        var user = await env.CreateUserAsync(Roles.Treasurer);
        var client = env.ApiFactory.CreateClient();

        var response = await LoginAsync(client, user.Email, user.Password);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"), c => c.StartsWith("finflow.session="));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);

        var me = await client.GetFromJsonAsync<JsonElement>("/api/auth/me");
        Assert.Equal(user.Email, me.GetProperty("email").GetString());
        Assert.Equal(Roles.Treasurer, me.GetProperty("role").GetString());
        Assert.False(me.GetProperty("mustChangePassword").GetBoolean());
    }

    [Fact]
    public async Task SenhaIncorreta_EEmailInexistente_Retornam401ComAMesmaMensagem()
    {
        var user = await env.CreateUserAsync(Roles.Customer);
        var client = env.ApiFactory.CreateClient();

        var senhaErrada = await LoginAsync(client, user.Email, "Errada#12345");
        var inexistente = await LoginAsync(client, "ninguem@teste.finflow", "Errada#12345");

        Assert.Equal(HttpStatusCode.Unauthorized, senhaErrada.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, inexistente.StatusCode);
        Assert.Equal(
            (await senhaErrada.ReadProblemAsync()).GetProperty("title").GetString(),
            (await inexistente.ReadProblemAsync()).GetProperty("title").GetString());
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task SenhasErradasSeguidas_BloqueiamOUsuario_MesmoComASenhaCertaDepois()
    {
        var user = await env.CreateUserAsync(Roles.Customer);
        var client = env.ApiFactory.CreateClient();

        for (var i = 0; i < 5; i++)
            await LoginAsync(client, user.Email, "Errada#12345");

        var comSenhaCerta = await LoginAsync(client, user.Email, user.Password);
        Assert.Equal(HttpStatusCode.Locked, comSenhaCerta.StatusCode);

        // A auditoria registra as falhas (sem a senha).
        await using var db = env.CreateDbContext();
        var falhas = await db.AuditLogs.Where(a => a.UserId == user.Id && a.Action == AuditActions.LoginFailed).ToListAsync();
        Assert.True(falhas.Count >= 5);
        Assert.All(falhas, f => Assert.DoesNotContain("Errada#12345", f.Details));
    }

    [Fact]
    public async Task UsuarioDesativado_NaoEntra_ESessaoAbertaCai()
    {
        var (user, client) = await env.CreateUserClientAsync(Roles.Customer);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/accounts")).StatusCode);

        var patch = await env.AdminClient.PatchAsJsonAsync($"/api/admin/users/{user.Id}", new { isActive = false });
        Assert.Equal(HttpStatusCode.OK, patch.StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/accounts")).StatusCode);
        var novoLogin = await LoginAsync(env.ApiFactory.CreateClient(), user.Email, user.Password);
        Assert.Equal(HttpStatusCode.Unauthorized, novoLogin.StatusCode);
    }

    [Fact]
    public async Task Logout_EncerraASessao()
    {
        var (_, client) = await env.CreateUserClientAsync(Roles.Customer);

        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/auth/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task SessaoExpirada_PorInatividade_Retorna401()
    {
        await using var factory = new FinFlowApiFactory(env, enableOutboxPublisher: false,
            new Dictionary<string, string?> { ["Auth:SessionTimeout"] = "00:00:02" });
        var user = await env.CreateUserAsync(Roles.Customer);
        var client = await factory.LoginAsync(user.Email, user.Password);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/me")).StatusCode);

        await Task.Delay(TimeSpan.FromSeconds(3));

        var response = await client.GetAsync("/api/accounts");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task LimiteDeTentativasPorIp_Retorna429()
    {
        await using var factory = new FinFlowApiFactory(env, enableOutboxPublisher: false,
            new Dictionary<string, string?> { ["Auth:LoginAttemptsPerMinute"] = "3" });
        var client = factory.CreateClient();

        var status = new List<HttpStatusCode>();
        for (var i = 0; i < 4; i++)
            status.Add((await LoginAsync(client, "ninguem@teste.finflow", "x")).StatusCode);

        Assert.Equal(HttpStatusCode.TooManyRequests, status[^1]);
    }

    // ---------- Primeiro login ----------

    [Fact]
    public async Task PrimeiroLogin_ExigeTrocaDeSenha_AntesDeQualquerOperacao()
    {
        var user = await env.CreateUserAsync(Roles.Customer, requirePasswordChange: true);
        var client = env.ApiFactory.CreateClient();

        var login = await LoginAsync(client, user.Email, user.Password);
        Assert.True((await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("mustChangePassword").GetBoolean());
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/accounts")).StatusCode);

        // Senha fraca, igual à atual ou com a atual errada: recusadas.
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/change-password",
            new { currentPassword = user.Password, newPassword = "fraca" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/change-password",
            new { currentPassword = user.Password, newPassword = user.Password })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/change-password",
            new { currentPassword = "Errada#12345", newPassword = "Nova#Senha2026" })).StatusCode);

        var troca = await client.PostAsJsonAsync("/api/auth/change-password",
            new { currentPassword = user.Password, newPassword = "Nova#Senha2026" });
        Assert.Equal(HttpStatusCode.OK, troca.StatusCode);
        Assert.False((await troca.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("mustChangePassword").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/accounts")).StatusCode);

        // A senha antiga deixa de valer.
        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(env.ApiFactory.CreateClient(), user.Email, user.Password)).StatusCode);
    }

    [Fact]
    public async Task SemSessao_EndpointsProtegidos_Retornam401()
    {
        var client = env.ApiFactory.CreateClient();
        foreach (var url in new[] { "/api/accounts", "/api/transactions", "/api/accounts/summary", "/api/currencies",
                     "/api/exchange-rates", "/api/admin/users", "/api/admin/audit-logs" })
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(url)).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/transactions",
            new { sourceAccount = "12345", destinationAccount = "67890", amount = 1m })).StatusCode);
    }

    [Fact]
    public async Task CookieAdulterado_NaoAutentica()
    {
        var client = env.ApiFactory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/accounts");
        request.Headers.Add("Cookie", "finflow.session=CfDJ8-adulterado-pelo-cliente");

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(request)).StatusCode);
    }

    // ---------- Seed do administrador ----------

    [Fact]
    public async Task Seed_EIdempotente_NaoDuplicaNemSobrescreveASenha()
    {
        // Nova instância da API = novo seed sobre o mesmo banco.
        await using var outraApi = new FinFlowApiFactory(env, enableOutboxPublisher: false);
        _ = outraApi.CreateClient();

        await using var db = env.CreateDbContext();
        Assert.Equal(1, await db.Users.CountAsync(u => u.Email == IntegrationTestEnvironment.AdminEmail));
        // A senha trocada no primeiro login continua valendo (o seed não a redefiniu).
        await outraApi.LoginAsync(IntegrationTestEnvironment.AdminEmail, IntegrationTestEnvironment.AdminPassword);
    }

    [Theory]
    [InlineData("")]       // sem senha configurada
    [InlineData("admin")]  // senha padrão de desenvolvimento
    public async Task Seed_ForaDeDevelopment_RecusaSenhaAusenteOuPadrao(string senha)
    {
        await using var api = new FinFlowApiFactory(env, enableOutboxPublisher: false, new Dictionary<string, string?>
        {
            ["Seed:Admin:Email"] = $"admin-{Guid.NewGuid():N}@teste.finflow",
            ["Seed:Admin:Password"] = senha
        });

        var ex = Assert.ThrowsAny<Exception>(() => api.CreateClient());
        Assert.Contains("Seed:Admin:Password", ex.ToString());
    }

    [Fact]
    public async Task Seed_EmDevelopment_CriaAdminComSenhaPadraoSoComHash_EProducaoRecusaIniciarComEla()
    {
        var email = $"admin-dev-{Guid.NewGuid():N}@teste.finflow";

        await using (var dev = SeedServices(Environments.Development, email))
        {
            await IdentitySeeder.SeedAsync(dev);
            await IdentitySeeder.SeedAsync(dev); // idempotente
        }

        await using var db = env.CreateDbContext();
        var admin = await db.Users.SingleAsync(u => u.Email == email);
        Assert.True(admin.MustChangePassword);
        Assert.NotEqual("admin", admin.PasswordHash);
        Assert.NotEqual(PasswordVerificationResult.Failed,
            new PasswordHasher<AppUser>().VerifyHashedPassword(admin, admin.PasswordHash!, "admin"));
        var roles = await (from ur in db.UserRoles join r in db.Roles on ur.RoleId equals r.Id where ur.UserId == admin.Id select r.Name).ToListAsync();
        Assert.Equal([Roles.Administrator], roles);

        // Fora de Development, com a senha padrão ainda em uso: a aplicação não inicia.
        await using var prod = SeedServices(Environments.Production, email);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => IdentitySeeder.SeedAsync(prod));
        Assert.Contains("senha padrão", ex.Message);
    }

    private ServiceProvider SeedServices(string environmentName, string adminEmail)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(env.Settings).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure(configuration);
        services.AddIdentityCore<AppUser>().AddRoles<IdentityRole>().AddEntityFrameworkStores<FinFlowDbContext>();
        services.AddSingleton<IHostEnvironment>(new HostingEnvironment { EnvironmentName = environmentName });
        services.Configure<AdminSeedOptions>(o => { o.Email = adminEmail; o.Password = null; });
        return services.BuildServiceProvider();
    }
}
