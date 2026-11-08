using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FinFlow.Application.Accounts;
using FinFlow.Application.Common;
using FinFlow.Application.Transactions;
using FinFlow.IntegrationTests.Infrastructure;

namespace FinFlow.IntegrationTests.Api;

/// <summary>Perfis, policies e isolamento de dados por usuário, sempre pelo backend.</summary>
[Collection(IntegrationCollection.Name)]
public sealed class RbacTests(IntegrationTestEnvironment env)
{
    // ---------- Administrator ----------

    [Fact]
    public async Task Administrator_AcessaFuncionalidadesAdministrativas()
    {
        var admin = env.AdminClient;

        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/admin/users")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/admin/audit-logs")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/exchange-rates/history")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/currencies?includeInactive=true")).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await admin.PostAsJsonAsync("/api/exchange-rates",
            new { fromCurrency = "EUR", toCurrency = "USD", rate = 1.08m })).StatusCode);
    }

    // ---------- Treasurer ----------

    [Fact]
    public async Task Treasurer_ConsultaTodasAsContas_ETransfere()
    {
        var (_, treasurer) = await env.CreateUserClientAsync(Roles.Treasurer);
        var origem = await env.CreateAccountAsync(100m, ownerUserId: (await env.CreateUserAsync(Roles.Customer)).Id);
        var destino = await env.CreateAccountAsync(0m);

        var contas = await treasurer.GetAllPagesAsync<AccountResponse>("/api/accounts");
        Assert.Contains(contas, a => a.AccountNumber == "12345");      // legada, sem dono
        Assert.Contains(contas, a => a.AccountNumber == origem.AccountNumber); // de um cliente

        var transferencia = await treasurer.PostAsJsonAsync("/api/transactions",
            new { sourceAccount = origem.AccountNumber, destinationAccount = destino.AccountNumber, amount = 10m });
        Assert.Equal(HttpStatusCode.Accepted, transferencia.StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await treasurer.GetAsync("/api/exchange-rates")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await treasurer.GetAsync($"/api/accounts/{origem.Id}/balance")).StatusCode);
    }

    [Fact]
    public async Task Treasurer_NaoAdministraUsuariosMoedasTaxasNemAuditoria()
    {
        var (_, treasurer) = await env.CreateUserClientAsync(Roles.Treasurer);
        var cliente = await env.CreateUserAsync(Roles.Customer);

        var proibidos = new List<HttpResponseMessage>
        {
            await treasurer.GetAsync("/api/admin/users"),
            await treasurer.PostAsJsonAsync("/api/admin/users", new { email = "x@teste.finflow", fullName = "X", password = "Senha#Forte2026", role = "Customer" }),
            await treasurer.PatchAsJsonAsync($"/api/admin/users/{cliente.Id}", new { role = Roles.Administrator }),
            await treasurer.GetAsync("/api/admin/audit-logs"),
            await treasurer.PostAsJsonAsync("/api/exchange-rates", new { fromCurrency = "USD", toCurrency = "BRL", rate = 9.99m }),
            await treasurer.DeleteAsync("/api/exchange-rates/USD/BRL"),
            await treasurer.GetAsync("/api/exchange-rates/history"),
            await treasurer.PostAsJsonAsync("/api/currencies", new { code = "GBP", name = "Libra", decimalPlaces = 2 }),
            await treasurer.PatchAsJsonAsync("/api/currencies/EUR", new { isActive = false }),
            await treasurer.PostAsJsonAsync("/api/accounts", new { holderName = "X", initialBalance = 0m, currency = "BRL" }),
        };

        Assert.All(proibidos, r => Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode));
    }

    // ---------- Customer ----------

    [Fact]
    public async Task Customer_VeSomenteAsProprias_Contas_Saldos_ETransacoes()
    {
        var (cliente, client) = await env.CreateUserClientAsync(Roles.Customer);
        var minha = await env.CreateAccountAsync(500m, ownerUserId: cliente.Id);
        var deOutro = await env.CreateAccountAsync(900m, ownerUserId: (await env.CreateUserAsync(Roles.Customer)).Id);
        var legada = await env.CreateAccountAsync(100m);

        // Uma transação dele e outra entre terceiros.
        var minhaTransacao = await env.AdminClient.CreateTransactionAsync(minha.AccountNumber, legada.AccountNumber, 5m);
        var deTerceiros = await env.AdminClient.CreateTransactionAsync(deOutro.AccountNumber, legada.AccountNumber, 5m);

        var contas = await client.GetAllPagesAsync<AccountResponse>("/api/accounts");
        Assert.Equal([minha.AccountNumber], contas.Select(a => a.AccountNumber));

        var resumo = await client.GetFromJsonAsync<AccountSummaryResponse>("/api/accounts/summary");
        Assert.Equal(1, resumo!.TotalAccounts);
        Assert.Equal(500m, Assert.Single(resumo.Balances).TotalBalance);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/accounts/{minha.Id}/balance")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/accounts/{deOutro.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/accounts/{deOutro.Id}/balance")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/accounts/{legada.Id}/cash-flow")).StatusCode);

        var transacoes = await client.GetAllPagesAsync<TransactionResponse>("/api/transactions");
        Assert.Equal([minhaTransacao.Id], transacoes.Select(t => t.Id));
        // Filtrar pela conta de outro não fura o escopo.
        Assert.Empty(await client.GetAllPagesAsync<TransactionResponse>($"/api/transactions?accountNumber={deOutro.AccountNumber}"));
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/transactions/{deTerceiros.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/transactions/{deTerceiros.Id}/status")).StatusCode);

        var resumoTransacoes = await client.GetFromJsonAsync<TransactionSummaryResponse>("/api/transactions/summary");
        Assert.Equal(1, resumoTransacoes!.TotalTransactions);
    }

    [Fact]
    public async Task Customer_TransfereSoAPartirDasPropriasContas()
    {
        var (cliente, client) = await env.CreateUserClientAsync(Roles.Customer);
        var minha = await env.CreateAccountAsync(500m, ownerUserId: cliente.Id);
        var deOutro = await env.CreateAccountAsync(900m, ownerUserId: (await env.CreateUserAsync(Roles.Customer)).Id);

        var daMinha = await client.PostAsJsonAsync("/api/transactions",
            new { sourceAccount = minha.AccountNumber, destinationAccount = deOutro.AccountNumber, amount = 10m });
        Assert.Equal(HttpStatusCode.Accepted, daMinha.StatusCode);
        var criada = await daMinha.Content.ReadFromJsonAsync<TransactionResponse>();
        Assert.Equal(cliente.Id, criada!.RequestedByUserId);

        // Debitar a conta de outro cliente: recusado como se a conta não existisse.
        var daOutra = await client.PostAsJsonAsync("/api/transactions",
            new { sourceAccount = deOutro.AccountNumber, destinationAccount = minha.AccountNumber, amount = 10m });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, daOutra.StatusCode);
        Assert.Equal(900m, await env.GetBalanceAsync(deOutro.AccountNumber));
    }

    [Fact]
    public async Task Customer_QueRecebe_NaoCancelaATransferenciaDeOutro()
    {
        var (destinatario, client) = await env.CreateUserClientAsync(Roles.Customer);
        var origem = await env.CreateAccountAsync(100m);
        var minha = await env.CreateAccountAsync(0m, ownerUserId: destinatario.Id);
        var criada = await env.AdminClient.CreateTransactionAsync(origem.AccountNumber, minha.AccountNumber, 10m);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/transactions/{criada.Id}")).StatusCode); // pode ver
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PatchAsync($"/api/transactions/{criada.Id}/cancel", null)).StatusCode);
        Assert.Equal("Pending", (await client.GetFromJsonAsync<TransactionStatusResponse>($"/api/transactions/{criada.Id}/status"))!.Status);
    }

    [Fact]
    public async Task Customer_AbreContaParaSiMesmo_ComNumeroGerado()
    {
        var (cliente, client) = await env.CreateUserClientAsync(Roles.Customer);

        var response = await client.PostAsJsonAsync("/api/accounts",
            new { holderName = "Cliente Teste", initialBalance = 0m, currency = "USD", ownerUserId = "tentativa-de-outro-dono" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var conta = await response.Content.ReadFromJsonAsync<AccountResponse>();
        Assert.Equal(cliente.Id, conta!.OwnerUserId);
        Assert.Equal("USD", conta.Currency);
    }

    [Fact]
    public async Task Customer_NaoAcessaFuncionalidadesAdministrativas()
    {
        var (_, client) = await env.CreateUserClientAsync(Roles.Customer);

        foreach (var url in new[] { "/api/admin/users", "/api/admin/audit-logs", "/api/exchange-rates/history" })
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/exchange-rates",
            new { fromCurrency = "USD", toCurrency = "BRL", rate = 1m })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PatchAsJsonAsync("/api/currencies/BRL", new { isActive = false })).StatusCode);

        // Pode consultar moedas ativas e taxas vigentes (necessárias para operar).
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/currencies")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/exchange-rates")).StatusCode);
    }

    // ---------- Autoelevação ----------

    [Fact]
    public async Task NinguemElevaOProprioPerfil()
    {
        var (cliente, client) = await env.CreateUserClientAsync(Roles.Customer);

        // Customer tentando se promover: a rota administrativa nem é acessível.
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PatchAsJsonAsync($"/api/admin/users/{cliente.Id}",
            new { role = Roles.Administrator })).StatusCode);

        // Nem o Administrator altera o próprio perfil ou se desativa.
        var me = await env.AdminClient.GetFromJsonAsync<JsonElement>("/api/auth/me");
        var adminId = me.GetProperty("id").GetString();
        Assert.Equal(HttpStatusCode.Forbidden, (await env.AdminClient.PatchAsJsonAsync($"/api/admin/users/{adminId}",
            new { role = Roles.Customer })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await env.AdminClient.PatchAsJsonAsync($"/api/admin/users/{adminId}",
            new { isActive = false })).StatusCode);

        var depois = await client.GetFromJsonAsync<JsonElement>("/api/auth/me");
        Assert.Equal(Roles.Customer, depois.GetProperty("role").GetString());
    }

    [Fact]
    public async Task MudancaDePerfil_PeloAdministrador_InvalidaASessao_EVaiParaONovoPerfil()
    {
        var (cliente, client) = await env.CreateUserClientAsync(Roles.Customer);

        var patch = await env.AdminClient.PatchAsJsonAsync($"/api/admin/users/{cliente.Id}", new { role = Roles.Treasurer });
        Assert.Equal(HttpStatusCode.OK, patch.StatusCode);

        // A sessão antiga (com o perfil antigo nas claims) não vale mais.
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/accounts")).StatusCode);

        var novo = await env.ApiFactory.LoginAsync(cliente.Email, cliente.Password);
        Assert.Equal(Roles.Treasurer, (await novo.GetFromJsonAsync<JsonElement>("/api/auth/me")).GetProperty("role").GetString());
    }

    [Fact]
    public async Task Administrator_GerenciaUsuarios_ComAuditoria()
    {
        var created = await env.AdminClient.PostAsJsonAsync("/api/admin/users", new
        {
            email = $"novo-{Guid.NewGuid():N}@teste.finflow",
            fullName = "Novo Usuário",
            password = "Senha#Forte2026",
            role = Roles.Customer
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var user = await created.Content.ReadFromJsonAsync<JsonElement>();
        var id = user.GetProperty("id").GetString();
        Assert.True(user.GetProperty("mustChangePassword").GetBoolean()); // padrão: troca no 1º login

        Assert.Equal(HttpStatusCode.Conflict, (await env.AdminClient.PostAsJsonAsync("/api/admin/users", new
        {
            email = user.GetProperty("email").GetString(), fullName = "Duplicado", password = "Senha#Forte2026", role = Roles.Customer
        })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await env.AdminClient.PostAsJsonAsync("/api/admin/users", new
        {
            email = $"fraca-{Guid.NewGuid():N}@teste.finflow", fullName = "Senha fraca", password = "123", role = Roles.Customer
        })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await env.AdminClient.PostAsJsonAsync("/api/admin/users", new
        {
            email = $"perfil-{Guid.NewGuid():N}@teste.finflow", fullName = "Perfil inválido", password = "Senha#Forte2026", role = "SuperAdmin"
        })).StatusCode);

        await env.AdminClient.PatchAsJsonAsync($"/api/admin/users/{id}", new { role = Roles.Treasurer });

        var audit = await env.AdminClient.GetFromJsonAsync<PagedResult<AuditLogResponse>>($"/api/admin/audit-logs?entityType=User&entityId={id}");
        Assert.Contains(audit!.Items, a => a.Action == AuditActions.UserCreated);
        Assert.Contains(audit.Items, a => a.Action == AuditActions.UserRoleChanged && a.Details.Contains(Roles.Treasurer));
        Assert.All(audit.Items, a => Assert.DoesNotContain("Senha#Forte2026", a.Details));
        Assert.All(audit.Items, a => Assert.Equal(IntegrationTestEnvironment.AdminEmail, a.UserEmail));
    }
}
