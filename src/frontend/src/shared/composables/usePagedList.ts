import { computed, reactive, ref, shallowRef } from 'vue'
import { normalizeApiError, type ApiError, type RequestStatus } from '@/shared/api'
import { DEFAULT_PAGE_SIZE, type PagedResult, type PageParams } from '@/shared/api/pagination'

export interface PagedListOptions<T, F extends object> {
  /** Busca uma página. Deve repassar o `signal` ao HTTP para permitir o cancelamento. */
  fetchPage: (params: PageParams & F, signal: AbortSignal) => Promise<PagedResult<T>>
  filters: F
  pageSize?: number
}

/**
 * Estado de uma listagem paginada no servidor (página, tamanho, filtros, totais,
 * loading/erro). Pode ser usado dentro de um store (estado preservado entre telas)
 * ou direto num componente (estado local).
 *
 * Garantias:
 * - **Sem respostas antigas**: cada carga cancela a anterior; uma resposta que chega
 *   depois de uma requisição mais nova é descartada.
 * - **Sem duplicadas**: pedir os mesmos parâmetros com uma carga em voo reaproveita a promise.
 * - Mudar filtros ou `pageSize` volta para a página 1.
 * - Página além da última (ex.: após um cancelamento reduzir o total) cai na última página.
 * - Em falha de recarga, os itens anteriores continuam visíveis e o erro fica em `error`.
 */
export function usePagedList<T, F extends object>(options: PagedListOptions<T, F>) {
  const items = shallowRef<T[]>([])
  const page = ref(1)
  const pageSize = ref(options.pageSize ?? DEFAULT_PAGE_SIZE)
  const filters = shallowRef<F>({ ...options.filters })

  const totalItems = ref(0)
  const totalPages = ref(0)

  const status = ref<RequestStatus>('idle')
  const error = shallowRef<ApiError | null>(null)
  const loadedAt = ref<number | null>(null)

  let controller: AbortController | null = null
  let latestRequest = 0
  let pending: { key: string; promise: Promise<void> } | null = null

  const paramsKey = () => JSON.stringify([page.value, pageSize.value, filters.value])

  function apply(result: PagedResult<T>) {
    items.value = result.items
    page.value = result.totalPages === 0 ? 1 : result.page
    pageSize.value = result.pageSize
    totalItems.value = result.totalItems
    totalPages.value = result.totalPages
    error.value = null
    status.value = 'success'
    loadedAt.value = Date.now()
  }

  /**
   * Carrega a página atual. `force` ignora uma carga idêntica em voo (use após uma
   * mutação: a carga em voo pode ter sido iniciada antes dela).
   */
  function load({ force = false }: { force?: boolean } = {}): Promise<void> {
    const key = paramsKey()
    if (!force && pending?.key === key) return pending.promise

    controller?.abort()
    const ctrl = new AbortController()
    controller = ctrl
    const requestId = ++latestRequest

    status.value = 'loading'
    const params = { ...filters.value, page: page.value, pageSize: pageSize.value }

    const promise: Promise<void> = options
      .fetchPage(params, ctrl.signal)
      .then((result) => {
        if (requestId !== latestRequest) return // resposta de uma requisição superada
        if (result.totalPages > 0 && result.page > result.totalPages) {
          // A página deixou de existir: vai para a última página real.
          page.value = result.totalPages
          pending = null
          return load()
        }
        apply(result)
      })
      .catch((reason: unknown) => {
        if (requestId !== latestRequest) return
        const apiError = normalizeApiError(reason)
        if (apiError.kind === 'canceled') return
        error.value = apiError
        status.value = 'error'
      })
      .finally(() => {
        if (pending?.promise === promise) pending = null
        if (controller === ctrl) controller = null
      })

    pending = { key, promise }
    return promise
  }

  function setPage(target: number): Promise<void> {
    const next = Math.max(1, Math.floor(target))
    if (next === page.value && status.value !== 'idle') return pending?.promise ?? Promise.resolve()
    page.value = next
    return load()
  }

  const hasPreviousPage = computed(() => page.value > 1)
  // Calculado localmente (não pela última resposta) para não permitir avançar além da última página.
  const hasNextPage = computed(() => page.value < totalPages.value)

  function nextPage() {
    return hasNextPage.value ? setPage(page.value + 1) : Promise.resolve()
  }

  function previousPage() {
    return hasPreviousPage.value ? setPage(page.value - 1) : Promise.resolve()
  }

  function setPageSize(size: number): Promise<void> {
    if (size === pageSize.value) return Promise.resolve()
    pageSize.value = size
    page.value = 1
    return load()
  }

  /** Aplica filtros (mesclando) e volta para a página 1. */
  function setFilters(patch: Partial<F>): Promise<void> {
    const next = { ...filters.value, ...patch }
    if (JSON.stringify(next) === JSON.stringify(filters.value)) return Promise.resolve()
    filters.value = next
    page.value = 1
    return load()
  }

  /** Recarrega mantendo página e filtros. */
  function refresh(options?: { force?: boolean }) {
    return load(options)
  }

  const hasLoaded = computed(() => loadedAt.value !== null)
  const isLoading = computed(() => status.value === 'loading')
  const isInitialLoading = computed(() => isLoading.value && !hasLoaded.value)
  const isRefreshing = computed(() => isLoading.value && hasLoaded.value)
  const isEmpty = computed(() => hasLoaded.value && totalItems.value === 0)
  /** Posição do primeiro e do último item da página (1-based) para "11–20 de 45". */
  const range = computed(() => {
    if (items.value.length === 0) return { start: 0, end: 0 }
    const start = (page.value - 1) * pageSize.value + 1
    return { start, end: start + items.value.length - 1 }
  })

  return reactive({
    items,
    page,
    pageSize,
    filters,
    totalItems,
    totalPages,
    hasPreviousPage,
    hasNextPage,
    status,
    error,
    hasLoaded,
    isLoading,
    isInitialLoading,
    isRefreshing,
    isEmpty,
    range,
    load,
    refresh,
    setPage,
    nextPage,
    previousPage,
    setPageSize,
    setFilters,
  })
}

export type PagedList<T, F extends object> = ReturnType<typeof usePagedList<T, F>>
