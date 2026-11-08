/** Espelha FinFlow.Application.Common.PagedResult&lt;T&gt;. */
export interface PagedResult<T> {
  items: T[]
  /** Página efetivamente devolvida (começa em 1). */
  page: number
  /** Tamanho efetivamente aplicado (a API limita a 100). */
  pageSize: number
  /** Total de registros que atendem aos filtros, em todas as páginas. */
  totalItems: number
  /** 0 quando não há registros. */
  totalPages: number
  hasPreviousPage: boolean
  hasNextPage: boolean
}

export interface PageParams {
  page: number
  pageSize: number
}

/** Opções do seletor "por página". */
export const PAGE_SIZE_OPTIONS = [10, 20, 50] as const

export const DEFAULT_PAGE_SIZE = 10

/** Mesmo limite de PageRequest.MaxPageSize no backend. */
export const MAX_PAGE_SIZE = 100
