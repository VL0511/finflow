import { api } from '@/shared/api'
import type { PagedResult, PageParams } from '@/shared/api/pagination'
import type {
  CreateTransactionRequest,
  Transaction,
  TransactionListFilters,
  TransactionStatusResponse,
  TransactionSummary,
  TransferPreview,
} from '../types/transaction'

/**
 * Endpoints de TransactionsController. Único ponto do frontend que conhece as URLs de transações.
 * Falhas rejeitam com ApiError (ver shared/api).
 */
export const transactionsApi = {
  /**
   * GET /api/transactions?page&pageSize&status&accountNumber — paginado, mais recentes primeiro.
   * Vários status vão separados por vírgula (`status=Pending,Processing`).
   */
  list(params: PageParams & TransactionListFilters, signal?: AbortSignal): Promise<PagedResult<Transaction>> {
    return api.get<PagedResult<Transaction>>('/transactions', {
      params: {
        page: params.page,
        pageSize: params.pageSize,
        status: params.status?.length ? params.status.join(',') : undefined,
        accountNumber: params.accountNumber || undefined,
      },
      signal,
    })
  },

  /** GET /api/transactions/summary — contagem por status de TODAS as transações. */
  summary(signal?: AbortSignal): Promise<TransactionSummary> {
    return api.get<TransactionSummary>('/transactions/summary', { signal })
  },

  /** GET /api/transactions/{id} — 404 se não existir. */
  getById(id: string, signal?: AbortSignal): Promise<Transaction> {
    return api.get<Transaction>(`/transactions/${encodeURIComponent(id)}`, { signal })
  },

  /** GET /api/transactions/{id}/status — consulta leve para acompanhar o processamento. */
  getStatus(id: string, signal?: AbortSignal): Promise<TransactionStatusResponse> {
    return api.get<TransactionStatusResponse>(`/transactions/${encodeURIComponent(id)}/status`, { signal })
  },

  /**
   * POST /api/transactions — 202 Accepted: a transferência foi REGISTRADA como Pending,
   * não concluída. O Worker é quem a processa. 400 (validação) ou 422 (conta inexistente).
   */
  create(request: CreateTransactionRequest): Promise<Transaction> {
    return api.post<Transaction>('/transactions', request)
  },

  /**
   * POST /api/transactions/preview — moedas, taxa vigente e valor a receber, calculados pelo
   * mesmo código da criação. Não grava nada. 422: conta fora do escopo, sem taxa, moeda inativa.
   */
  preview(request: CreateTransactionRequest, signal?: AbortSignal): Promise<TransferPreview> {
    return api.post<TransferPreview>('/transactions/preview', request, { signal })
  },

  /** PATCH /api/transactions/{id}/cancel — só enquanto Pending; 409 se já em processamento/finalizada. */
  cancel(id: string): Promise<Transaction> {
    return api.patch<Transaction>(`/transactions/${encodeURIComponent(id)}/cancel`)
  },
}
