using System.Data.SqlTypes;
using System.Net;
using System.Net.Http.Json;
using FinFlow.Application.Accounts;
using FinFlow.Application.Common;
using FinFlow.Application.Transactions;
using FinFlow.Domain.Entities;
using FinFlow.Domain.Enums;
using FinFlow.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace FinFlow.IntegrationTests.Api;

/// <summary>
/// Paginação, filtros e agregações contra o SQL Server real (container de teste).
/// As transações são gravadas direto no banco de teste com status/datas controlados —
/// nenhum saldo é movimentado e nada passa pelo Worker.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class PaginationEndpointsTests(IntegrationTestEnvironment env)
{
    private readonly HttpClient _client = env.AdminClient;

    // =====================================================================
    // Contas
    // =====================================================================

    [Fact]
    public async Task Contas_SemParametros_UsaPagina1ETamanho10ComTotalDoBanco()
    {
        await GarantirContasAsync(15);
        await using var db = env.CreateDbContext();
        var total = await db.Accounts.CountAsync();

        var result = await GetAsync<AccountResponse>("/api/accounts");

        Assert.Equal(1, result.Page);
        Assert.Equal(10, result.PageSize);
        Assert.Equal(10, result.Items.Count);
        Assert.Equal(total, result.TotalItems);
        Assert.Equal((int)Math.Ceiling(total / 10.0), result.TotalPages);
        Assert.False(result.HasPreviousPage);
        Assert.True(result.HasNextPage);
    }

    [Fact]
    public async Task Contas_PageSizeAcimaDoLimite_AplicaMaximoDe100()
    {
        var result = await GetAsync<AccountResponse>("/api/accounts?pageSize=500");

        Assert.Equal(100, result.PageSize);
        Assert.True(result.Items.Count <= 100);
    }

    [Theory]
    [InlineData("page=0&pageSize=0", 1, 10)]
    [InlineData("page=-5&pageSize=-1", 1, 10)]
    public async Task Contas_ParametrosAbaixoDoMinimo_SaoNormalizados(string query, int pagina, int tamanho)
    {
        var result = await GetAsync<AccountResponse>($"/api/accounts?{query}");

        Assert.Equal(pagina, result.Page);
        Assert.Equal(tamanho, result.PageSize);
    }

    [Fact]
    public async Task Contas_PercorrerTodasAsPaginas_OrdemDeterministicaSemRepetirNemPular()
    {
        await GarantirContasAsync(25);
        await using var db = env.CreateDbContext();
        var esperado = (await db.Accounts.AsNoTracking().Select(a => new { a.Id, a.AccountNumber }).ToListAsync())
            .OrderBy(a => a.AccountNumber, StringComparer.Ordinal)
            .Select(a => a.Id)
            .ToList();

        var paginas = new List<PagedResult<AccountResponse>>();
        for (var page = 1; ; page++)
        {
            var result = await GetAsync<AccountResponse>($"/api/accounts?page={page}&pageSize=7");
            paginas.Add(result);
            if (!result.HasNextPage) break;
        }

        var obtido = paginas.SelectMany(p => p.Items).Select(a => a.Id).ToList();
        Assert.Equal(esperado, obtido);
        Assert.Equal(obtido.Count, obtido.Distinct().Count());
        Assert.Equal((int)Math.Ceiling(esperado.Count / 7.0), paginas.Count);
        Assert.All(paginas, p => Assert.Equal(esperado.Count, p.TotalItems));

        // Mesma página consultada duas vezes devolve exatamente os mesmos registros.
        var repetida = await GetAsync<AccountResponse>("/api/accounts?page=2&pageSize=7");
        Assert.Equal(paginas[1].Items.Select(a => a.Id), repetida.Items.Select(a => a.Id));
    }

    [Fact]
    public async Task Contas_UltimaPaginaEPaginaAlemDaUltima()
    {
        await GarantirContasAsync(12);
        var primeira = await GetAsync<AccountResponse>("/api/accounts?pageSize=5");

        var ultima = await GetAsync<AccountResponse>($"/api/accounts?page={primeira.TotalPages}&pageSize=5");
        var alem = await GetAsync<AccountResponse>($"/api/accounts?page={primeira.TotalPages + 1}&pageSize=5");

        Assert.True(ultima.HasPreviousPage);
        Assert.False(ultima.HasNextPage);
        Assert.Equal(primeira.TotalItems - (primeira.TotalPages - 1) * 5, ultima.Items.Count);

        Assert.Empty(alem.Items);
        Assert.Equal(primeira.TotalItems, alem.TotalItems);
        Assert.Equal(primeira.TotalPages, alem.TotalPages);
        Assert.False(alem.HasNextPage);
    }

    [Theory]
    [InlineData("/api/accounts?page=abc")]
    [InlineData("/api/accounts?pageSize=1.5")]
    [InlineData("/api/transactions?page=x")]
    public async Task Listagens_ParametroNaoNumerico_Retorna400(string url)
    {
        var response = await _client.GetAsync(url);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ResumoDeContas_ConfereComAgregacaoDoBanco()
    {
        await env.CreateAccountAsync(10.50m, "EUR", isActive: false);
        await using var db = env.CreateDbContext();
        var contas = await db.Accounts.AsNoTracking().ToListAsync();

        var summary = await _client.GetFromJsonAsync<AccountSummaryResponse>("/api/accounts/summary");

        Assert.Equal(contas.Count, summary!.TotalAccounts);
        Assert.Equal(contas.Count(a => a.IsActive), summary.ActiveAccounts);
        Assert.Equal(contas.Count(a => !a.IsActive), summary.InactiveAccounts);
        foreach (var grupo in contas.GroupBy(a => a.Currency))
        {
            var moeda = Assert.Single(summary.Balances, b => b.Currency == grupo.Key);
            Assert.Equal(grupo.Count(), moeda.Accounts);
            Assert.Equal(grupo.Sum(a => a.Balance), moeda.TotalBalance);
        }
    }

    // =====================================================================
    // Transações
    // =====================================================================

    [Fact]
    public async Task Transacoes_FiltradasPorConta_PaginasCorretasEOrdemDeterministica()
    {
        var (conta, ids) = await SemearTransacoesAsync();

        var paginas = new[]
        {
            await GetAsync<TransactionResponse>($"/api/transactions?accountNumber={conta}&page=1&pageSize=5"),
            await GetAsync<TransactionResponse>($"/api/transactions?accountNumber={conta}&page=2&pageSize=5"),
            await GetAsync<TransactionResponse>($"/api/transactions?accountNumber={conta}&page=3&pageSize=5")
        };

        Assert.Equal([5, 5, 2], paginas.Select(p => p.Items.Count));
        Assert.All(paginas, p => Assert.Equal(12, p.TotalItems));
        Assert.All(paginas, p => Assert.Equal(3, p.TotalPages));
        Assert.False(paginas[0].HasPreviousPage);
        Assert.True(paginas[0].HasNextPage);
        Assert.True(paginas[2].HasPreviousPage);
        Assert.False(paginas[2].HasNextPage);

        // CreatedAt desc; empates (4 transações no mesmo instante) resolvidos por Id desc, na ordem do SQL Server.
        Assert.Equal(ids, paginas.SelectMany(p => p.Items).Select(t => t.Id));
    }

    [Fact]
    public async Task Transacoes_PaginaAlemDaUltima_RetornaVaziaComTotais()
    {
        var (conta, _) = await SemearTransacoesAsync();

        var result = await GetAsync<TransactionResponse>($"/api/transactions?accountNumber={conta}&page=4&pageSize=5");

        Assert.Empty(result.Items);
        Assert.Equal(12, result.TotalItems);
        Assert.Equal(3, result.TotalPages);
        Assert.True(result.HasPreviousPage);
        Assert.False(result.HasNextPage);
    }

    [Theory]
    [InlineData("status=Pending", 5)]
    [InlineData("status=Completed", 3)]
    [InlineData("status=Failed,Cancelled", 4)]
    [InlineData("status=failed&status=CANCELLED", 4)]
    [InlineData("status=Pending,Processing", 5)]
    [InlineData("status=Processing", 0)]
    public async Task Transacoes_FiltroDeStatusCombinadoComConta_TotalEItensConsistentes(string filtro, int esperado)
    {
        var (conta, _) = await SemearTransacoesAsync();

        var result = await GetAsync<TransactionResponse>($"/api/transactions?accountNumber={conta}&{filtro}&pageSize=2");

        Assert.Equal(esperado, result.TotalItems);
        Assert.Equal((int)Math.Ceiling(esperado / 2.0), result.TotalPages);
        Assert.Equal(Math.Min(2, esperado), result.Items.Count);
        Assert.All(result.Items, t => Assert.True(t.SourceAccount == conta || t.DestinationAccount == conta));
        Assert.Equal(esperado > 2, result.HasNextPage);
    }

    [Fact]
    public async Task Transacoes_FiltroPorStatus_PercorrerPaginasTrazExatamenteOsDoFiltro()
    {
        var (conta, ids) = await SemearTransacoesAsync();
        await using var db = env.CreateDbContext();
        var falhasOuCanceladas = await db.Transactions.AsNoTracking()
            .Where(t => ids.Contains(t.Id) && (t.Status == TransactionStatus.Failed || t.Status == TransactionStatus.Cancelled))
            .Select(t => t.Id)
            .ToListAsync();

        var p1 = await GetAsync<TransactionResponse>($"/api/transactions?accountNumber={conta}&status=Failed,Cancelled&page=1&pageSize=3");
        var p2 = await GetAsync<TransactionResponse>($"/api/transactions?accountNumber={conta}&status=Failed,Cancelled&page=2&pageSize=3");

        var obtidos = p1.Items.Concat(p2.Items).Select(t => t.Id).ToList();
        Assert.Equal(falhasOuCanceladas.Order(), obtidos.Order());
        Assert.Equal(ids.Where(falhasOuCanceladas.Contains), obtidos); // mesma ordem da listagem completa
    }

    [Fact]
    public async Task Transacoes_SemFiltroDeConta_NaoMisturaNemPerdeOutrasContas()
    {
        var (conta, _) = await SemearTransacoesAsync();
        var outraOrigem = await env.CreateAccountAsync(100m);
        var outroDestino = await env.CreateAccountAsync(0m);
        var outra = await _client.CreateTransactionAsync(outraOrigem.AccountNumber, outroDestino.AccountNumber, 1m);

        var daConta = await GetAsync<TransactionResponse>($"/api/transactions?accountNumber={conta}&pageSize=100");
        var daOutra = await GetAsync<TransactionResponse>($"/api/transactions?accountNumber={outroDestino.AccountNumber}");

        Assert.DoesNotContain(daConta.Items, t => t.Id == outra.Id);
        Assert.Equal(outra.Id, Assert.Single(daOutra.Items).Id);
    }

    [Theory]
    [InlineData("status=Approved")]
    [InlineData("status=3")]
    public async Task Transacoes_StatusInvalido_Retorna400ComErroNoCampo(string filtro)
    {
        var response = await _client.GetAsync($"/api/transactions?{filtro}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await response.ReadProblemAsync()).GetProperty("errors").TryGetProperty("Status", out _));
    }

    [Fact]
    public async Task Transacoes_PageSizeAcimaDoLimite_AplicaMaximo()
    {
        var result = await GetAsync<TransactionResponse>("/api/transactions?pageSize=1000");

        Assert.Equal(100, result.PageSize);
        Assert.True(result.Items.Count <= 100);
    }

    [Fact]
    public async Task ResumoDeTransacoes_ConfereComContagemDoBanco()
    {
        await SemearTransacoesAsync();
        await using var db = env.CreateDbContext();
        var porStatus = await db.Transactions.AsNoTracking()
            .GroupBy(t => t.Status)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count);
        int Count(TransactionStatus s) => porStatus.GetValueOrDefault(s);

        var summary = await _client.GetFromJsonAsync<TransactionSummaryResponse>("/api/transactions/summary");

        Assert.Equal(porStatus.Values.Sum(), summary!.TotalTransactions);
        Assert.Equal(Count(TransactionStatus.Pending), summary.Pending);
        Assert.Equal(Count(TransactionStatus.Processing), summary.Processing);
        Assert.Equal(Count(TransactionStatus.Completed), summary.Completed);
        Assert.Equal(Count(TransactionStatus.Failed), summary.Failed);
        Assert.Equal(Count(TransactionStatus.Cancelled), summary.Cancelled);
    }

    // =====================================================================
    // Fluxo de caixa por conta
    // =====================================================================

    [Fact]
    public async Task FluxoDeCaixa_AgrupaLiquidacoesPorDiaNoFusoDoCliente()
    {
        var conta = await env.CreateAccountAsync(0m);
        var outra = await env.CreateAccountAsync(0m);
        var c = conta.AccountNumber;
        var o = outra.AccountNumber;

        await InserirAsync(
            Nova(o, c, 100m, TransactionStatus.Completed, Utc(2026, 3, 10, 2)),   // 09/03 23h em UTC-3
            Nova(o, c, 50m, TransactionStatus.Completed, Utc(2026, 3, 10, 12)),
            Nova(c, o, 30m, TransactionStatus.Completed, Utc(2026, 3, 10, 18)),
            Nova(c, o, 999m, TransactionStatus.Failed, Utc(2026, 3, 10, 13)),     // falha: não conta
            Nova(c, o, 999m, TransactionStatus.Cancelled, Utc(2026, 3, 10, 13)),  // cancelada: não conta
            Nova(o, c, 777m, TransactionStatus.Completed, Utc(2026, 3, 12, 12))); // fora do período

        var local = await _client.GetFromJsonAsync<AccountCashFlowResponse>(
            $"/api/accounts/{conta.Id}/cash-flow?from=2026-03-09&to=2026-03-10&utcOffsetMinutes=-180");
        var utc = await _client.GetFromJsonAsync<AccountCashFlowResponse>(
            $"/api/accounts/{conta.Id}/cash-flow?from=2026-03-09&to=2026-03-10");

        Assert.Equal(c, local!.AccountNumber);
        Assert.Equal(
            [(new DateOnly(2026, 3, 9), 100m, 0m), (new DateOnly(2026, 3, 10), 50m, 30m)],
            local.Days.Select(d => (d.Date, d.Inflow, d.Outflow)));

        // Em UTC, a liquidação das 02h já cai no dia 10.
        Assert.Equal([(new DateOnly(2026, 3, 10), 150m, 30m)], utc!.Days.Select(d => (d.Date, d.Inflow, d.Outflow)));
    }

    [Fact]
    public async Task FluxoDeCaixa_PeriodoInvalido400_ContaInexistente404()
    {
        var conta = await env.CreateAccountAsync(0m);

        var invertido = await _client.GetAsync($"/api/accounts/{conta.Id}/cash-flow?from=2026-03-10&to=2026-03-01");
        var longo = await _client.GetAsync($"/api/accounts/{conta.Id}/cash-flow?from=2020-01-01&to=2026-03-01");
        var inexistente = await _client.GetAsync($"/api/accounts/{Guid.NewGuid()}/cash-flow");

        Assert.Equal(HttpStatusCode.BadRequest, invertido.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, longo.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, inexistente.StatusCode);
    }

    // =====================================================================
    // Helpers
    // =====================================================================

    private async Task<PagedResult<T>> GetAsync<T>(string url)
    {
        var response = await _client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PagedResult<T>>())!;
    }

    private async Task GarantirContasAsync(int minimo)
    {
        await using var db = env.CreateDbContext();
        for (var total = await db.Accounts.CountAsync(); total < minimo; total++)
            await env.CreateAccountAsync(0m);
    }

    private static DateTimeOffset Utc(int y, int m, int d, int h, int min = 0) => new(y, m, d, h, min, 0, TimeSpan.Zero);

    /// <summary>
    /// 12 transações envolvendo uma conta nova: 5 Pending, 3 Completed, 2 Failed, 2 Cancelled,
    /// 4 delas criadas no MESMO instante (para exercitar o desempate por Id).
    /// Devolve os ids na ordem esperada da listagem (CreatedAt desc, Id desc segundo o SQL Server).
    /// </summary>
    private async Task<(string Conta, List<Guid> Ids)> SemearTransacoesAsync()
    {
        var conta = (await env.CreateAccountAsync(0m)).AccountNumber;
        var outra = (await env.CreateAccountAsync(0m)).AccountNumber;
        var baseTime = Utc(2026, 1, 15, 10);
        var empate = baseTime.AddMinutes(30);

        var statuses = new[]
        {
            TransactionStatus.Pending, TransactionStatus.Completed, TransactionStatus.Failed, TransactionStatus.Pending,
            TransactionStatus.Cancelled, TransactionStatus.Pending, TransactionStatus.Completed, TransactionStatus.Failed,
            TransactionStatus.Pending, TransactionStatus.Cancelled, TransactionStatus.Completed, TransactionStatus.Pending
        };

        var transacoes = statuses.Select((status, i) =>
        {
            var criadaEm = i is >= 3 and <= 6 ? empate : baseTime.AddMinutes(i);
            return i % 2 == 0
                ? Nova(conta, outra, 10m + i, status, criadaEm.AddSeconds(5), criadaEm)
                : Nova(outra, conta, 10m + i, status, criadaEm.AddSeconds(5), criadaEm);
        }).ToList();

        await InserirAsync([.. transacoes]);

        var ids = transacoes
            .OrderByDescending(t => t.CreatedAt)
            .ThenByDescending(t => new SqlGuid(t.Id)) // ordem de uniqueidentifier do SQL Server
            .Select(t => t.Id)
            .ToList();

        return (conta, ids);
    }

    /// <summary>Transação já no status pedido, com datas controladas (só para o banco de teste).</summary>
    private static Transaction Nova(
        string origem, string destino, decimal valor, TransactionStatus status,
        DateTimeOffset atualizadaEm, DateTimeOffset? criadaEm = null)
    {
        var t = new Transaction(origem, destino, valor);
        switch (status)
        {
            case TransactionStatus.Processing:
                t.StartProcessing();
                break;
            case TransactionStatus.Completed:
                t.StartProcessing();
                t.Complete();
                break;
            case TransactionStatus.Failed:
                t.StartProcessing();
                t.Fail("Saldo insuficiente na conta de origem.");
                break;
            case TransactionStatus.Cancelled:
                t.Cancel();
                break;
        }

        typeof(Transaction).GetProperty(nameof(Transaction.CreatedAt))!.SetValue(t, criadaEm ?? atualizadaEm.AddSeconds(-5));
        typeof(Transaction).GetProperty(nameof(Transaction.UpdatedAt))!
            .SetValue(t, status == TransactionStatus.Pending ? null : atualizadaEm);
        return t;
    }

    private async Task InserirAsync(params Transaction[] transacoes)
    {
        await using var db = env.CreateDbContext();
        db.Transactions.AddRange(transacoes);
        await db.SaveChangesAsync();
    }
}
