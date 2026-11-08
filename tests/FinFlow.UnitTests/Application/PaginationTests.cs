using FinFlow.Application.Common;

namespace FinFlow.UnitTests.Application;

public class PaginationTests
{
    // ---------- PageRequest: normalização dos parâmetros ----------

    [Fact]
    public void PageRequest_SemParametros_UsaPadroes()
    {
        var request = PageRequest.Create();

        Assert.Equal(1, request.Page);
        Assert.Equal(10, request.PageSize);
        Assert.Equal(0, request.Skip);
    }

    [Theory]
    [InlineData(101, 100)]
    [InlineData(5_000, 100)]
    [InlineData(100, 100)]
    [InlineData(0, 10)]
    [InlineData(-3, 10)]
    [InlineData(1, 1)]
    public void PageRequest_PageSizeForaDaFaixa_ENormalizado(int pedido, int aplicado)
    {
        Assert.Equal(aplicado, PageRequest.Create(1, pedido).PageSize);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-10, 1)]
    [InlineData(3, 3)]
    [InlineData(int.MaxValue, PageRequest.MaxPage)]
    public void PageRequest_PaginaForaDaFaixa_ENormalizada(int pedida, int aplicada)
    {
        var request = PageRequest.Create(pedida, 100);

        Assert.Equal(aplicada, request.Page);
        Assert.True(request.Skip >= 0); // sem overflow
    }

    [Fact]
    public void PageRequest_CalculaDeslocamento()
    {
        Assert.Equal(40, PageRequest.Create(5, 10).Skip);
    }

    // ---------- PagedResult: metadados ----------

    [Fact]
    public void PagedResult_PrimeiraPagina()
    {
        var result = PagedResult<int>.Create([1, 2, 3, 4, 5, 6, 7, 8, 9, 10], PageRequest.Create(1, 10), totalItems: 45);

        Assert.Equal(5, result.TotalPages);
        Assert.False(result.HasPreviousPage);
        Assert.True(result.HasNextPage);
    }

    [Fact]
    public void PagedResult_UltimaPaginaIncompleta()
    {
        var result = PagedResult<int>.Create([41, 42, 43, 44, 45], PageRequest.Create(5, 10), totalItems: 45);

        Assert.Equal(5, result.TotalPages);
        Assert.True(result.HasPreviousPage);
        Assert.False(result.HasNextPage);
    }

    [Fact]
    public void PagedResult_PaginaAlemDaUltima_ItensVaziosComTotaisCorretos()
    {
        var result = PagedResult<int>.Create([], PageRequest.Create(9, 10), totalItems: 45);

        Assert.Empty(result.Items);
        Assert.Equal(9, result.Page);
        Assert.Equal(45, result.TotalItems);
        Assert.Equal(5, result.TotalPages);
        Assert.False(result.HasNextPage);
    }

    [Fact]
    public void PagedResult_SemRegistros_ZeroPaginas()
    {
        var result = PagedResult<int>.Create([], PageRequest.Default, totalItems: 0);

        Assert.Equal(0, result.TotalPages);
        Assert.False(result.HasPreviousPage);
        Assert.False(result.HasNextPage);
    }

    [Fact]
    public void PagedResult_TotalMultiploDoTamanho_NaoCriaPaginaExtra()
    {
        Assert.Equal(3, PagedResult<int>.Create([], PageRequest.Create(1, 10), totalItems: 30).TotalPages);
    }

    [Fact]
    public void PagedResult_Map_ConverteItensEPreservaMetadados()
    {
        var original = PagedResult<int>.Create([1, 2], PageRequest.Create(2, 2), totalItems: 5);

        var mapped = original.Map(i => $"#{i}");

        Assert.Equal(["#1", "#2"], mapped.Items);
        Assert.Equal((2, 2, 5, 3, true, true),
            (mapped.Page, mapped.PageSize, mapped.TotalItems, mapped.TotalPages, mapped.HasPreviousPage, mapped.HasNextPage));
    }
}
