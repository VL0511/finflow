using System.Net;
using System.Net.Http.Json;
using FinFlow.Application.Accounts;
using FinFlow.Application.Common;
using FinFlow.Application.Currencies;
using FinFlow.Application.Transactions;
using FinFlow.Domain.Entities;
using FinFlow.Domain.Enums;
using FinFlow.Domain.Services;
using FinFlow.IntegrationTests.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FinFlow.IntegrationTests.Api;

/// <summary>
/// Multimoedas ponta a ponta: a transferência é criada pela API (cotação e snapshot no backend) e
/// processada pelo ProcessTransactionHandler real (o mesmo do Worker), contra o SQL Server de teste.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class MultiCurrencyTests(IntegrationTestEnvironment env)
{
    private HttpClient Admin => env.AdminClient;

    // ---------- Mesma moeda ----------

    [Theory]
    [InlineData("BRL", 1_000.00, 250.50)]
    [InlineData("USD", 300.00, 99.99)]
    [InlineData("EUR", 50.00, 50.00)]
    public async Task MesmaMoeda_DebitaECreditaOMesmoValor(string moeda, decimal saldo, decimal valor)
    {
        var origem = await env.CreateAccountAsync(saldo, moeda);
        var destino = await env.CreateAccountAsync(0m, moeda);

        var criada = await Admin.CreateTransactionAsync(origem.AccountNumber, destino.AccountNumber, valor);
        Assert.Equal(moeda, criada.DestinationCurrency);
        Assert.Equal(valor, criada.DestinationAmount);
        Assert.Equal(1m, criada.ExchangeRate);
        Assert.Null(criada.ExchangeRateId);

        Assert.Equal(ProcessTransactionOutcome.Completed, await ProcessarAsync(criada.Id));
        Assert.Equal(saldo - valor, await env.GetBalanceAsync(origem.AccountNumber));
        Assert.Equal(valor, await env.GetBalanceAsync(destino.AccountNumber));
    }

    // ---------- Entre moedas ----------

    [Fact]
    public async Task UsdParaBrl_ExemploDaEspecificacao_100Vezes5_20_Recebe520()
    {
        await DefinirTaxaAsync("USD", "BRL", 5.20m);
        var origem = await env.CreateAccountAsync(1_000m, "USD");
        var destino = await env.CreateAccountAsync(10m, "BRL");

        var criada = await Admin.CreateTransactionAsync(origem.AccountNumber, destino.AccountNumber, 100.00m);

        Assert.Equal("USD", criada.Currency);
        Assert.Equal(100.00m, criada.Amount);
        Assert.Equal("BRL", criada.DestinationCurrency);
        Assert.Equal(520.00m, criada.DestinationAmount);
        Assert.Equal(5.20m, criada.ExchangeRate);
        Assert.NotNull(criada.ExchangeRateId);

        Assert.Equal(ProcessTransactionOutcome.Completed, await ProcessarAsync(criada.Id));
        Assert.Equal(900.00m, await env.GetBalanceAsync(origem.AccountNumber)); // USD
        Assert.Equal(530.00m, await env.GetBalanceAsync(destino.AccountNumber)); // BRL
    }

    [Fact]
    public async Task EurParaBrl_ArredondaNasCasasDoDestino()
    {
        await DefinirTaxaAsync("EUR", "BRL", 6.1234m);
        var origem = await env.CreateAccountAsync(100m, "EUR");
        var destino = await env.CreateAccountAsync(0m, "BRL");

        var criada = await Admin.CreateTransactionAsync(origem.AccountNumber, destino.AccountNumber, 10.01m);

        Assert.Equal(61.30m, criada.DestinationAmount); // 61,295234 → 61,30
        await ProcessarAsync(criada.Id);
        Assert.Equal(89.99m, await env.GetBalanceAsync(origem.AccountNumber));
        Assert.Equal(61.30m, await env.GetBalanceAsync(destino.AccountNumber));
    }

    [Fact]
    public async Task Previa_UsaOMesmoCalculo_EMascaraOTitular()
    {
        await DefinirTaxaAsync("USD", "BRL", 5.20m);
        var origem = await env.CreateAccountAsync(1_000m, "USD");
        var destino = await env.CreateAccountAsync(0m, "BRL", holder: "Bruno Lima Souza");

        var previa = await (await Admin.PostAsJsonAsync("/api/transactions/preview",
            new { sourceAccount = origem.AccountNumber, destinationAccount = destino.AccountNumber, amount = 100m }))
            .Content.ReadFromJsonAsync<TransferPreviewResponse>();

        Assert.True(previa!.IsConversion);
        Assert.Equal("USD", previa.SourceCurrency);
        Assert.Equal("BRL", previa.DestinationCurrency);
        Assert.Equal(520m, previa.DestinationAmount);
        Assert.Equal("Bruno S.", previa.DestinationHolder);

        // A prévia não grava nada.
        await using var db = env.CreateDbContext();
        Assert.False(await db.Transactions.AnyAsync(t => t.SourceAccount == origem.AccountNumber));
    }

    [Fact]
    public async Task SemTaxaVigente_Recusa422_ENaoGravaNada()
    {
        // Encerra BRL→EUR se algum teste anterior a tiver criado (direção nunca usada nos demais).
        await Admin.DeleteAsync("/api/exchange-rates/BRL/EUR");
        var origem = await env.CreateAccountAsync(100m, "BRL");
        var destino = await env.CreateAccountAsync(0m, "EUR");

        var response = await Admin.PostAsJsonAsync("/api/transactions",
            new { sourceAccount = origem.AccountNumber, destinationAccount = destino.AccountNumber, amount = 10m });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Contains("BRL para EUR", (await response.ReadProblemAsync()).GetProperty("detail").GetString());
        await using var db = env.CreateDbContext();
        Assert.False(await db.Transactions.AnyAsync(t => t.SourceAccount == origem.AccountNumber));
    }

    [Fact]
    public async Task TaxaInversa_NaoEUsadaAutomaticamente()
    {
        await DefinirTaxaAsync("USD", "BRL", 5.20m);
        await Admin.DeleteAsync("/api/exchange-rates/BRL/USD");
        var origem = await env.CreateAccountAsync(1_000m, "BRL");
        var destino = await env.CreateAccountAsync(0m, "USD");

        var response = await Admin.PostAsJsonAsync("/api/transactions",
            new { sourceAccount = origem.AccountNumber, destinationAccount = destino.AccountNumber, amount = 520m });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Theory]
    [InlineData("USD", "BRL", 0)]
    [InlineData("USD", "BRL", -5.2)]
    [InlineData("USD", "USD", 1)]
    [InlineData("USD", "BRL", 5.123456789)]
    public async Task TaxaInvalida_Recusa400(string de, string para, decimal taxa) =>
        Assert.Equal(HttpStatusCode.BadRequest, (await Admin.PostAsJsonAsync("/api/exchange-rates",
            new { fromCurrency = de, toCurrency = para, rate = taxa })).StatusCode);

    [Fact]
    public async Task TaxaParaMoedaInexistente_Recusa422() =>
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Admin.PostAsJsonAsync("/api/exchange-rates",
            new { fromCurrency = "USD", toCurrency = "XYZ", rate = 1m })).StatusCode);

    [Fact]
    public async Task MoedaDesativada_RecusaContasTaxasETransferencias_EReativacaoVoltaAFuncionar()
    {
        var codigo = await CriarMoedaAsync();
        await DefinirTaxaAsync(codigo, "BRL", 2m);
        var origem = await env.CreateAccountAsync(100m, codigo);
        var destino = await env.CreateAccountAsync(0m, "BRL");

        Assert.Equal(HttpStatusCode.OK, (await Admin.PatchAsJsonAsync($"/api/currencies/{codigo}", new { isActive = false })).StatusCode);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Admin.PostAsJsonAsync("/api/transactions",
            new { sourceAccount = origem.AccountNumber, destinationAccount = destino.AccountNumber, amount = 10m })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Admin.PostAsJsonAsync("/api/accounts",
            new { holderName = "Nova", initialBalance = 0m, currency = codigo })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Admin.PostAsJsonAsync("/api/exchange-rates",
            new { fromCurrency = codigo, toCurrency = "BRL", rate = 3m })).StatusCode);
        var ativas = await Admin.GetFromJsonAsync<List<CurrencyResponse>>("/api/currencies");
        Assert.DoesNotContain(ativas!, c => c.Code == codigo);

        await Admin.PatchAsJsonAsync($"/api/currencies/{codigo}", new { isActive = true });
        Assert.Equal(HttpStatusCode.Accepted, (await Admin.PostAsJsonAsync("/api/transactions",
            new { sourceAccount = origem.AccountNumber, destinationAccount = destino.AccountNumber, amount = 10m })).StatusCode);
    }

    [Fact]
    public async Task ComCambio_SaldoInsuficienteNaMoedaDeOrigem_FalhaSemMovimentar()
    {
        await DefinirTaxaAsync("USD", "BRL", 5.20m);
        var origem = await env.CreateAccountAsync(50m, "USD");
        var destino = await env.CreateAccountAsync(0m, "BRL");

        var criada = await Admin.CreateTransactionAsync(origem.AccountNumber, destino.AccountNumber, 100m);

        Assert.Equal(ProcessTransactionOutcome.Failed, await ProcessarAsync(criada.Id));
        var final = (await env.GetTransactionAsync(criada.Id))!;
        Assert.Equal(TransferFailureReasons.InsufficientFunds, final.FailureReason);
        Assert.Equal(50m, await env.GetBalanceAsync(origem.AccountNumber));
        Assert.Equal(0m, await env.GetBalanceAsync(destino.AccountNumber));
    }

    // ---------- Imutabilidade histórica ----------

    [Fact]
    public async Task AlterarATaxa_NaoModificaTransacoesJaCriadas_NemOProcessamentoPendente()
    {
        var taxaOriginal = await DefinirTaxaAsync("USD", "BRL", 5.20m);
        var origem = await env.CreateAccountAsync(1_000m, "USD");
        var destino = await env.CreateAccountAsync(0m, "BRL");
        var antes = await Admin.CreateTransactionAsync(origem.AccountNumber, destino.AccountNumber, 100m); // ainda Pending

        var novaTaxa = await DefinirTaxaAsync("USD", "BRL", 6.00m);

        // A transação já criada é processada com o snapshot, não com a taxa nova.
        await ProcessarAsync(antes.Id);
        var gravada = (await env.GetTransactionAsync(antes.Id))!;
        Assert.Equal(5.20m, gravada.ExchangeRate);
        Assert.Equal(520m, gravada.DestinationAmount);
        Assert.Equal(taxaOriginal.Id, gravada.ExchangeRateId);
        Assert.Equal(520m, await env.GetBalanceAsync(destino.AccountNumber));

        // A seguinte usa a nova.
        var depois = await Admin.CreateTransactionAsync(origem.AccountNumber, destino.AccountNumber, 100m);
        Assert.Equal(600m, depois.DestinationAmount);
        Assert.Equal(novaTaxa.Id, depois.ExchangeRateId);

        // A versão antiga continua no histórico, encerrada e com o valor original.
        var historico = await Admin.GetFromJsonAsync<PagedResult<ExchangeRateResponse>>("/api/exchange-rates/history?from=USD&to=BRL&pageSize=100");
        var original = Assert.Single(historico!.Items, r => r.Id == taxaOriginal.Id);
        Assert.False(original.IsActive);
        Assert.Equal(5.20m, original.Rate);
        Assert.Equal(IntegrationTestEnvironment.AdminEmail, original.CreatedByEmail);
        Assert.Single((await Admin.GetFromJsonAsync<List<ExchangeRateResponse>>("/api/exchange-rates"))!,
            r => r.FromCurrency == "USD" && r.ToCurrency == "BRL");
    }

    [Fact]
    public async Task ComCambio_MensagemDuplicada_NaoDuplicaMovimentacao()
    {
        await DefinirTaxaAsync("USD", "BRL", 5.20m);
        var origem = await env.CreateAccountAsync(1_000m, "USD");
        var destino = await env.CreateAccountAsync(0m, "BRL");
        var criada = await Admin.CreateTransactionAsync(origem.AccountNumber, destino.AccountNumber, 100m);

        // A mesma mensagem entregue 4 vezes (reentregas). Entregas PARALELAS são cobertas pelos testes do
        // Worker, que aplicam o retry do consumer a conflitos de concorrência.
        var resultados = new List<ProcessTransactionOutcome>();
        for (var i = 0; i < 4; i++)
            resultados.Add(await ProcessarAsync(criada.Id));

        Assert.Equal([ProcessTransactionOutcome.Completed, ProcessTransactionOutcome.Skipped,
            ProcessTransactionOutcome.Skipped, ProcessTransactionOutcome.Skipped], resultados);
        Assert.Equal(900m, await env.GetBalanceAsync(origem.AccountNumber));
        Assert.Equal(520m, await env.GetBalanceAsync(destino.AccountNumber));
    }

    [Fact]
    public async Task ComCambio_FalhaNoProcessamento_NaoDeixaMovimentacaoParcial()
    {
        await DefinirTaxaAsync("USD", "BRL", 5.20m);
        var origem = await env.CreateAccountAsync(1_000m, "USD");
        var destino = await env.CreateAccountAsync(0m, "BRL");
        var criada = await Admin.CreateTransactionAsync(origem.AccountNumber, destino.AccountNumber, 100m);

        // Falha logo antes do débito (etapa 2): rollback completo.
        var falhas = new FalhasSimuladas(origem.AccountNumber, restantes: 1, () => new InvalidOperationException("simulado"));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ProcessarAsync(criada.Id, s => s.InterceptarBloqueioDeContas(falhas.TalvezFalharAsync)));

        Assert.Equal(1_000m, await env.GetBalanceAsync(origem.AccountNumber));
        Assert.Equal(0m, await env.GetBalanceAsync(destino.AccountNumber));
        Assert.Equal(TransactionStatus.Processing, (await env.GetTransactionAsync(criada.Id))!.Status);

        // Reprocessamento (Worker/recuperação P1) conclui uma única vez.
        Assert.Equal(ProcessTransactionOutcome.Completed, await ProcessarAsync(criada.Id));
        Assert.Equal(520m, await env.GetBalanceAsync(destino.AccountNumber));
    }

    [Fact]
    public async Task FluxoDeCaixaDoDestino_SomaOValorCreditadoNaMoedaDaConta()
    {
        await DefinirTaxaAsync("USD", "BRL", 5.20m);
        var origem = await env.CreateAccountAsync(1_000m, "USD");
        var destino = await env.CreateAccountAsync(0m, "BRL");
        var criada = await Admin.CreateTransactionAsync(origem.AccountNumber, destino.AccountNumber, 100m);
        await ProcessarAsync(criada.Id);

        var entrada = await Admin.GetFromJsonAsync<AccountCashFlowResponse>($"/api/accounts/{destino.Id}/cash-flow");
        var saida = await Admin.GetFromJsonAsync<AccountCashFlowResponse>($"/api/accounts/{origem.Id}/cash-flow");

        Assert.Equal(520m, entrada!.Days.Sum(d => d.Inflow)); // BRL
        Assert.Equal(100m, saida!.Days.Sum(d => d.Outflow));  // USD
    }

    // ---------- Contas: geração e unicidade ----------

    [Fact]
    public async Task ContasCriadasSimultaneamente_TodasComNumerosUnicosEValidos()
    {
        var respostas = await Task.WhenAll(Enumerable.Range(0, 40).Select(i => Admin.PostAsJsonAsync("/api/accounts",
            new { holderName = $"Titular {i}", initialBalance = 0m, currency = "BRL" })));

        Assert.All(respostas, r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));
        var numeros = await Task.WhenAll(respostas.Select(async r => (await r.Content.ReadFromJsonAsync<AccountResponse>())!.AccountNumber));
        Assert.Equal(numeros.Length, numeros.Distinct().Count());
        Assert.All(numeros, n => Assert.True(RandomAccountNumberGenerator.IsValid(n)));
    }

    [Fact]
    public async Task NumeroDeContaDuplicado_EBloqueadoPeloBanco()
    {
        var existente = await env.CreateAccountAsync(0m);

        await using var db = env.CreateDbContext();
        db.Accounts.Add(new Account(existente.AccountNumber, "Duplicada", 0m, "BRL"));
        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Contains(((SqlException)ex.InnerException!).Number, new[] { 2601, 2627 });
    }

    // ---------- Auditoria ----------

    [Fact]
    public async Task Auditoria_RegistraSolicitanteDaTransferencia_ResultadoETaxa()
    {
        await DefinirTaxaAsync("USD", "BRL", 5.20m);
        var (cliente, client) = await env.CreateUserClientAsync(Roles.Customer);
        var origem = await env.CreateAccountAsync(1_000m, "USD", ownerUserId: cliente.Id);
        var destino = await env.CreateAccountAsync(0m, "BRL");

        var criada = await client.CreateTransactionAsync(origem.AccountNumber, destino.AccountNumber, 100m);
        await ProcessarAsync(criada.Id);

        var logs = (await Admin.GetFromJsonAsync<PagedResult<AuditLogResponse>>(
            $"/api/admin/audit-logs?entityType=Transaction&entityId={criada.Id}"))!.Items;

        var solicitada = Assert.Single(logs, l => l.Action == AuditActions.TransferRequested);
        Assert.Equal(cliente.Id, solicitada.UserId);
        Assert.Equal(cliente.Email, solicitada.UserEmail);
        Assert.Contains("\"exchangeRate\":5.2", solicitada.Details);
        Assert.Contains("\"destinationAmount\":520", solicitada.Details);

        var concluida = Assert.Single(logs, l => l.Action == AuditActions.TransferCompleted);
        Assert.Equal(AuditResults.Succeeded, concluida.Result);
        Assert.Null(concluida.UserId); // executada pelo sistema (Worker)
        Assert.Contains(cliente.Id, concluida.Details); // ...a pedido do cliente
    }

    [Fact]
    public async Task Auditoria_ESomenteInsercao_NoProprioBanco()
    {
        await using var db = env.CreateDbContext();
        var algum = await db.AuditLogs.AsNoTracking().FirstAsync();

        var update = await Assert.ThrowsAsync<SqlException>(() =>
            db.Database.ExecuteSqlAsync($"UPDATE [AuditLogs] SET [Result] = 'Adulterado' WHERE [Id] = {algum.Id}"));
        var delete = await Assert.ThrowsAsync<SqlException>(() =>
            db.Database.ExecuteSqlAsync($"DELETE FROM [AuditLogs] WHERE [Id] = {algum.Id}"));

        Assert.Contains("somente inserção", update.Message);
        Assert.Contains("somente inserção", delete.Message);
        Assert.Equal(algum.Result, (await db.AuditLogs.AsNoTracking().SingleAsync(a => a.Id == algum.Id)).Result);
    }

    // ---------- Helpers ----------

    private async Task<ExchangeRateResponse> DefinirTaxaAsync(string de, string para, decimal taxa)
    {
        var response = await Admin.PostAsJsonAsync("/api/exchange-rates", new { fromCurrency = de, toCurrency = para, rate = taxa });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ExchangeRateResponse>())!;
    }

    /// <summary>Moeda nova e exclusiva do teste (evita afetar BRL/USD/EUR, usadas pelos demais).</summary>
    private async Task<string> CriarMoedaAsync()
    {
        while (true)
        {
            var codigo = "Q" + (char)('A' + Random.Shared.Next(26)) + (char)('A' + Random.Shared.Next(26));
            var response = await Admin.PostAsJsonAsync("/api/currencies", new { code = codigo, name = $"Teste {codigo}", decimalPlaces = 2 });
            if (response.StatusCode == HttpStatusCode.Created)
                return codigo;
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        }
    }

    /// <summary>Processa com o handler real do Worker (sem RabbitMQ).</summary>
    private async Task<ProcessTransactionOutcome> ProcessarAsync(Guid id, Action<IServiceCollection>? configurar = null)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(env.Settings).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        FinFlow.Application.DependencyInjection.AddApplication(services);
        FinFlow.Infrastructure.DependencyInjection.AddInfrastructure(services, configuration);
        configurar?.Invoke(services);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ProcessTransactionHandler>().HandleAsync(id);
    }
}
