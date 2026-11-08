import { api } from '@/shared/api'
import type { PagedResult, PageParams } from '@/shared/api/pagination'
import type {
  Account,
  AccountBalanceResponse,
  AccountCashFlow,
  AccountCashFlowQuery,
  AccountSummary,
  CreateAccountRequest,
} from '../types/account'

/**
 * Endpoints de AccountsController. Único ponto do frontend que conhece as URLs de contas.
 * Falhas rejeitam com ApiError (ver shared/api).
 *
 * Observação: a API não expõe atualização nem exclusão de contas.
 */
export const accountsApi = {
  /** GET /api/accounts?page&pageSize — paginado, ordenado pelo número da conta. */
  list(params: PageParams, signal?: AbortSignal): Promise<PagedResult<Account>> {
    return api.get<PagedResult<Account>>('/accounts', { params: { page: params.page, pageSize: params.pageSize }, signal })
  },

  /** GET /api/accounts/summary — quantidades e saldos por moeda de TODAS as contas. */
  summary(signal?: AbortSignal): Promise<AccountSummary> {
    return api.get<AccountSummary>('/accounts/summary', { signal })
  },

  /** GET /api/accounts/{id} — 404 se não existir. */
  getById(id: string, signal?: AbortSignal): Promise<Account> {
    return api.get<Account>(`/accounts/${encodeURIComponent(id)}`, { signal })
  },

  /** GET /api/accounts/{id}/balance — 404 se não existir. */
  getBalance(id: string, signal?: AbortSignal): Promise<AccountBalanceResponse> {
    return api.get<AccountBalanceResponse>(`/accounts/${encodeURIComponent(id)}/balance`, { signal })
  },

  /** GET /api/accounts/{id}/cash-flow — entradas/saídas liquidadas por dia (agregado no banco). */
  cashFlow(id: string, query: AccountCashFlowQuery, signal?: AbortSignal): Promise<AccountCashFlow> {
    return api.get<AccountCashFlow>(`/accounts/${encodeURIComponent(id)}/cash-flow`, { params: query, signal })
  },

  /** POST /api/accounts — 201; 400 (validação) ou 409 (número já existe). */
  create(request: CreateAccountRequest): Promise<Account> {
    return api.post<Account>('/accounts', request)
  },
}
