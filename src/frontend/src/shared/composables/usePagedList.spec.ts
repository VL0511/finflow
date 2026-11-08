import { describe, expect, it, vi } from 'vitest'
import { ApiError } from '@/shared/api'
import type { PagedResult, PageParams } from '@/shared/api/pagination'
import { pageOf, paged } from '@/test/fixtures'
import { usePagedList } from './usePagedList'

type Filters = { status: string }
const ALL = Array.from({ length: 45 }, (_, i) => i + 1)

/** Backend simulado: filtra (status "even" = pares) e pagina como a API (página além da última = vazia). */
function fakeBackend() {
  return vi.fn(async (params: PageParams & Filters) => {
    const source = params.status === 'even' ? ALL.filter((n) => n % 2 === 0) : ALL
    return pageOf(source, params.page, params.pageSize)
  })
}

function deferred<T>() {
  let resolve!: (value: T) => void
  let reject!: (reason: unknown) => void
  const promise = new Promise<T>((res, rej) => ((resolve = res), (reject = rej)))
  return { promise, resolve, reject }
}

describe('usePagedList', () => {
  it('carrega a primeira página com os metadados da API', async () => {
    const fetchPage = fakeBackend()
    const list = usePagedList({ fetchPage, filters: { status: 'all' } })

    expect(list.isInitialLoading).toBe(false)
    const loading = list.load()
    expect(list.isInitialLoading).toBe(true)
    await loading

    expect(fetchPage).toHaveBeenCalledWith({ status: 'all', page: 1, pageSize: 10 }, expect.any(AbortSignal))
    expect(list.items).toEqual([1, 2, 3, 4, 5, 6, 7, 8, 9, 10])
    expect(list.totalItems).toBe(45)
    expect(list.totalPages).toBe(5)
    expect(list.hasPreviousPage).toBe(false)
    expect(list.hasNextPage).toBe(true)
    expect(list.range).toEqual({ start: 1, end: 10 })
  })

  it('navega entre páginas e desabilita os extremos', async () => {
    const list = usePagedList({ fetchPage: fakeBackend(), filters: { status: 'all' } })
    await list.load()

    await list.nextPage()
    expect(list.page).toBe(2)
    expect(list.items[0]).toBe(11)
    expect(list.hasPreviousPage).toBe(true)

    await list.setPage(5)
    expect(list.items).toEqual([41, 42, 43, 44, 45])
    expect(list.range).toEqual({ start: 41, end: 45 })
    expect(list.hasNextPage).toBe(false)

    await list.nextPage() // sem próxima: não faz nada
    expect(list.page).toBe(5)

    await list.previousPage()
    expect(list.page).toBe(4)
  })

  it('mudar o pageSize volta para a página 1', async () => {
    const fetchPage = fakeBackend()
    const list = usePagedList({ fetchPage, filters: { status: 'all' } })
    await list.load()
    await list.setPage(3)

    await list.setPageSize(20)

    expect(list.page).toBe(1)
    expect(list.pageSize).toBe(20)
    expect(list.totalPages).toBe(3)
    expect(fetchPage).toHaveBeenLastCalledWith({ status: 'all', page: 1, pageSize: 20 }, expect.any(AbortSignal))
  })

  it('mudar filtros volta para a página 1 e os filtros são preservados na navegação', async () => {
    const fetchPage = fakeBackend()
    const list = usePagedList({ fetchPage, filters: { status: 'all' } })
    await list.load()
    await list.setPage(4)

    await list.setFilters({ status: 'even' })
    expect(list.page).toBe(1)
    expect(list.totalItems).toBe(22)
    expect(list.items).toEqual([2, 4, 6, 8, 10, 12, 14, 16, 18, 20])

    await list.nextPage()
    await list.refresh()
    expect(fetchPage).toHaveBeenLastCalledWith({ status: 'even', page: 2, pageSize: 10 }, expect.any(AbortSignal))
    expect(list.filters).toEqual({ status: 'even' })
  })

  it('não refaz a carga se o filtro não mudou', async () => {
    const fetchPage = fakeBackend()
    const list = usePagedList({ fetchPage, filters: { status: 'all' } })
    await list.load()
    await list.setPage(2)

    await list.setFilters({ status: 'all' })

    expect(list.page).toBe(2)
    expect(fetchPage).toHaveBeenCalledTimes(2)
  })

  it('descarta respostas antigas: a página mais recente vence mesmo se a anterior responder depois', async () => {
    const slow = deferred<PagedResult<number>>()
    const fetchPage = vi.fn((params: PageParams & Filters, signal: AbortSignal) => {
      if (params.page === 2) {
        signal.addEventListener('abort', () => slow.reject(new DOMException('aborted', 'AbortError')))
        return slow.promise
      }
      return Promise.resolve(pageOf(ALL, params.page, params.pageSize))
    })
    const list = usePagedList({ fetchPage, filters: { status: 'all' } })
    await list.load()

    const toPage2 = list.setPage(2) // lenta
    const toPage3 = list.setPage(3) // rápida, mais nova
    await toPage3
    slow.resolve(pageOf(ALL, 2, 10)) // chega depois, deve ser ignorada
    await toPage2

    expect(list.page).toBe(3)
    expect(list.items[0]).toBe(21)
    expect(fetchPage.mock.calls[1]![1].aborted).toBe(true) // a requisição superada foi cancelada
    expect(list.error).toBeNull()
  })

  it('não duplica requisições idênticas em voo; force ignora a deduplicação', async () => {
    const fetchPage = fakeBackend()
    const list = usePagedList({ fetchPage, filters: { status: 'all' } })

    await Promise.all([list.load(), list.load(), list.refresh()])
    expect(fetchPage).toHaveBeenCalledTimes(1)

    await Promise.all([list.refresh(), list.refresh({ force: true })])
    expect(fetchPage).toHaveBeenCalledTimes(3)
  })

  it('página que deixou de existir cai na última página real', async () => {
    let total = 45
    const fetchPage = vi.fn(async (params: PageParams) => pageOf(ALL.slice(0, total), params.page, params.pageSize))
    const list = usePagedList({ fetchPage, filters: {} })
    await list.load()
    await list.setPage(5)

    total = 38 // ex.: transações canceladas saíram do filtro "em andamento"
    await list.refresh({ force: true })

    expect(list.page).toBe(4)
    expect(list.totalPages).toBe(4)
    expect(list.items).toEqual([31, 32, 33, 34, 35, 36, 37, 38])
  })

  it('lista vazia: isEmpty, página 1 e range zerado', async () => {
    const list = usePagedList({ fetchPage: async () => paged<number>([]), filters: {} })
    await list.load()

    expect(list.isEmpty).toBe(true)
    expect(list.page).toBe(1)
    expect(list.totalPages).toBe(0)
    expect(list.hasNextPage).toBe(false)
    expect(list.range).toEqual({ start: 0, end: 0 })
  })

  it('erro na primeira carga: status de erro, sem itens inventados; nova tentativa recupera', async () => {
    const fetchPage = vi
      .fn<(p: PageParams) => Promise<PagedResult<number>>>()
      .mockRejectedValueOnce(new ApiError({ kind: 'network', status: 0, title: 'Não foi possível conectar ao servidor.' }))
      .mockResolvedValueOnce(paged([1, 2]))
    const list = usePagedList({ fetchPage, filters: {} })

    await list.load()
    expect(list.status).toBe('error')
    expect(list.error?.kind).toBe('network')
    expect(list.items).toEqual([])
    expect(list.hasLoaded).toBe(false)

    await list.refresh({ force: true })
    expect(list.status).toBe('success')
    expect(list.error).toBeNull()
    expect(list.items).toEqual([1, 2])
  })

  it('erro numa recarga mantém os itens anteriores visíveis', async () => {
    const fetchPage = vi
      .fn<(p: PageParams) => Promise<PagedResult<number>>>()
      .mockResolvedValueOnce(paged([1, 2]))
      .mockRejectedValueOnce(new ApiError({ kind: 'http', status: 500, title: 'Erro interno no servidor.' }))
    const list = usePagedList({ fetchPage, filters: {} })
    await list.load()

    const reloading = list.refresh({ force: true })
    expect(list.isRefreshing).toBe(true)
    await reloading

    expect(list.items).toEqual([1, 2])
    expect(list.error?.status).toBe(500)
  })

  it('usa o pageSize efetivo devolvido pela API (limite de 100)', async () => {
    const list = usePagedList({
      fetchPage: async () => paged([1], { pageSize: 100, totalItems: 1 }),
      filters: {},
      pageSize: 500,
    })
    await list.load()

    expect(list.pageSize).toBe(100)
  })
})
