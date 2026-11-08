namespace FinFlow.Application.Common;

/// <summary>
/// Página solicitada, já normalizada: página &lt; 1 vira 1; tamanho ausente ou &lt; 1 vira
/// <see cref="DefaultPageSize"/>; tamanho acima de <see cref="MaxPageSize"/> é limitado a ele.
/// Os valores efetivos voltam na resposta (<see cref="PagedResult{T}"/>), então o cliente
/// sempre sabe o que foi aplicado.
/// </summary>
public sealed record PageRequest
{
    public const int DefaultPage = 1;
    public const int DefaultPageSize = 10;
    public const int MaxPageSize = 100;

    /// <summary>Maior página cujo deslocamento ainda cabe em int (evita overflow em Skip).</summary>
    public const int MaxPage = int.MaxValue / MaxPageSize;

    public int Page { get; }

    public int PageSize { get; }

    /// <summary>Quantidade de registros a pular (deslocamento no banco).</summary>
    public int Skip => (Page - 1) * PageSize;

    private PageRequest(int page, int pageSize)
    {
        Page = page;
        PageSize = pageSize;
    }

    public static PageRequest Create(int? page = null, int? pageSize = null) => new(
        page is null or < 1 ? DefaultPage : Math.Min(page.Value, MaxPage),
        pageSize is null or < 1 ? DefaultPageSize : Math.Min(pageSize.Value, MaxPageSize));

    public static PageRequest Default { get; } = Create();
}

/// <summary>Resposta paginada padrão das listagens.</summary>
public sealed record PagedResult<T>
{
    /// <summary>Registros da página (vazio se a página estiver além da última).</summary>
    public IReadOnlyList<T> Items { get; init; } = [];

    /// <summary>Página efetivamente retornada (começa em 1).</summary>
    /// <example>1</example>
    public int Page { get; init; }

    /// <summary>Tamanho de página efetivamente aplicado (máx. 100).</summary>
    /// <example>10</example>
    public int PageSize { get; init; }

    /// <summary>Total de registros que atendem aos filtros (todas as páginas).</summary>
    /// <example>45</example>
    public int TotalItems { get; init; }

    /// <summary>Total de páginas (0 quando não há registros).</summary>
    /// <example>5</example>
    public int TotalPages { get; init; }

    /// <example>false</example>
    public bool HasPreviousPage { get; init; }

    /// <example>true</example>
    public bool HasNextPage { get; init; }

    public static PagedResult<T> Create(IReadOnlyList<T> items, PageRequest request, int totalItems)
    {
        var totalPages = totalItems == 0 ? 0 : (int)Math.Ceiling(totalItems / (double)request.PageSize);
        return new PagedResult<T>
        {
            Items = items,
            Page = request.Page,
            PageSize = request.PageSize,
            TotalItems = totalItems,
            TotalPages = totalPages,
            HasPreviousPage = request.Page > 1,
            HasNextPage = request.Page < totalPages
        };
    }

    /// <summary>Converte os itens mantendo os metadados de paginação.</summary>
    public PagedResult<TOut> Map<TOut>(Func<T, TOut> map) => new()
    {
        Items = Items.Select(map).ToList(),
        Page = Page,
        PageSize = PageSize,
        TotalItems = TotalItems,
        TotalPages = TotalPages,
        HasPreviousPage = HasPreviousPage,
        HasNextPage = HasNextPage
    };
}
