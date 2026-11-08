using System.Net;
using System.Net.Http.Json;
using FinFlow.Application.Accounts;
using FinFlow.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace FinFlow.IntegrationTests.Api;

[Collection(IntegrationCollection.Name)]
public class AccountsEndpointsTests(IntegrationTestEnvironment env)
{
    private readonly HttpClient _client = env.AdminClient;

    [Fact]
    public async Task Post_ContaValida_Retorna201ComNumeroGeradoEPersisteNoBanco()
    {
        var response = await _client.PostAsJsonAsync("/api/accounts", new
        {
            holderName = "Fernanda Lima",
            initialBalance = 1500.00m,
            currency = "brl"
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<AccountResponse>();
        Assert.Equal($"/api/accounts/{body!.Id}", response.Headers.Location!.AbsolutePath, ignoreCase: true);
        Assert.Equal("BRL", body.Currency);
        Assert.True(body.IsActive);
        Assert.True(RandomAccountNumberGenerator.IsValid(body.AccountNumber), body.AccountNumber);

        await using var db = env.CreateDbContext();
        var salva = await db.Accounts.SingleAsync(a => a.Id == body.Id);
        Assert.Equal(body.AccountNumber, salva.AccountNumber);
        Assert.Equal(1500.00m, salva.Balance);
    }

    [Fact]
    public async Task Post_NumeroEnviadoPeloCliente_EIgnorado()
    {
        // O contrato não aceita mais número: um campo extra não muda o número gerado.
        var response = await _client.PostAsJsonAsync("/api/accounts", new
        {
            accountNumber = "12345",
            holderName = "Outra Pessoa",
            initialBalance = 10m,
            currency = "BRL"
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<AccountResponse>();
        Assert.NotEqual("12345", body!.AccountNumber);
        Assert.True(RandomAccountNumberGenerator.IsValid(body.AccountNumber));
    }

    [Fact]
    public async Task Post_DadosInvalidos_Retorna400ComErrosPorCampo()
    {
        var response = await _client.PostAsJsonAsync("/api/accounts", new
        {
            holderName = "",
            initialBalance = -10m,
            currency = "J1"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await response.ReadProblemAsync()).GetProperty("errors");
        Assert.True(errors.TryGetProperty("HolderName", out _));
        Assert.True(errors.TryGetProperty("InitialBalance", out _));
        Assert.True(errors.TryGetProperty("Currency", out _));
    }

    [Fact]
    public async Task Post_MoedaNaoCadastrada_Retorna422()
    {
        var response = await _client.PostAsJsonAsync("/api/accounts", new
        {
            holderName = "Hiro Tanaka",
            initialBalance = 10m,
            currency = "JPY"
        });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Get_ListaIncluiContasSemeadasPelaMigration()
    {
        // A lista é paginada: percorre todas as páginas (máx. 100 por página).
        var contas = await _client.GetAllPagesAsync<AccountResponse>("/api/accounts");

        Assert.Contains(contas!, a => a.AccountNumber == "12345" && a.HolderName == "Ana Souza");
        Assert.Contains(contas!, a => a.AccountNumber == "11223" && a.Currency == "USD");
        Assert.Contains(contas!, a => a.AccountNumber == "44556" && !a.IsActive);
    }

    [Fact]
    public async Task GetById_E_GetBalance_RetornamDadosDoBanco()
    {
        var conta = await env.CreateAccountAsync(321.45m, "EUR", holder: "Gabriel Costa");

        var porId = await _client.GetFromJsonAsync<AccountResponse>($"/api/accounts/{conta.Id}");
        var saldo = await _client.GetFromJsonAsync<AccountBalanceResponse>($"/api/accounts/{conta.Id}/balance");

        Assert.Equal("Gabriel Costa", porId!.HolderName);
        Assert.Equal(conta.AccountNumber, saldo!.AccountNumber);
        Assert.Equal(321.45m, saldo.Balance);
        Assert.Equal("EUR", saldo.Currency);
    }

    [Theory]
    [InlineData("/api/accounts/{0}")]
    [InlineData("/api/accounts/{0}/balance")]
    public async Task ContaInexistente_Retorna404(string rota)
    {
        var response = await _client.GetAsync(string.Format(rota, Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
