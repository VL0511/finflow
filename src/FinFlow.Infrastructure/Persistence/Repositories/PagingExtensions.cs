using FinFlow.Application.Common;
using Microsoft.EntityFrameworkCore;

namespace FinFlow.Infrastructure.Persistence.Repositories;

internal static class PagingExtensions
{
    /// <summary>
    /// Pagina no banco: COUNT(*) e OFFSET/FETCH sobre a MESMA consulta (mesmos filtros).
    /// A consulta precisa chegar já ordenada de forma determinística — sem ordem total,
    /// o SQL Server pode repetir ou pular registros entre páginas.
    /// Se a página pedida estiver além da última, devolve itens vazios sem consultar a página.
    /// </summary>
    public static async Task<PagedResult<T>> ToPagedResultAsync<T>(
        this IOrderedQueryable<T> query, PageRequest page, CancellationToken cancellationToken)
    {
        var totalItems = await query.CountAsync(cancellationToken);

        IReadOnlyList<T> items = page.Skip >= totalItems
            ? []
            : await query.Skip(page.Skip).Take(page.PageSize).ToListAsync(cancellationToken);

        return PagedResult<T>.Create(items, page, totalItems);
    }
}
