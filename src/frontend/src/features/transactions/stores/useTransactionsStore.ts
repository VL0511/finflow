import { defineStore } from 'pinia'
import { computed, ref, shallowRef } from 'vue'
import { useAccountsStore } from '@/features/accounts/stores/useAccountsStore'
import { isApiError, useRequestState } from '@/shared/api'
import type { PagedResult, PageParams } from '@/shared/api/pagination'
import { usePagedList } from '@/shared/composables/usePagedList'
import { transactionsApi } from '../api/transactionsApi'
import type {
  CreateTransactionRequest,
  Transaction,
  TransactionListFilters,
  TransactionStatus,
  TransactionStatusResponse,
  TransactionSummary,
  TransactionView,
} from '../types/transaction'
import { toTransactionView } from '../utils/toTransactionView'
import { isFinalStatus, statusFilterStatuses, statusRank, type StatusFilter } from '../utils/transactionStatus'

/**
 * Estado das transações no frontend, sempre alimentado pela API.
 *
 * - `list`: listagem paginada do histórico, com filtro de status no servidor
 *   (página, tamanho e filtro preservados ao navegar entre telas);
 * - `summary`: contagem por status de TODAS as transações (GET /transactions/summary) —
 *   abas, contador do menu e taxa de sucesso nunca são calculados a partir de uma página;
 * - `byId`: cache da versão mais recente de cada transação vista (lista, detalhe, polling).
 *   As páginas exibem a versão do cache, então um cancelamento ou um status vindo do
 *   polling aparece imediatamente em qualquer lista.
 *
 * O status só muda pelo Worker (ou pelo cancelamento): o store apenas reflete a API.
 */
export const useTransactionsStore = defineStore('transactions', () => {
  const accounts = useAccountsStore()

  // ---- cache ---------------------------------------------------------------

  const byId = ref<Record<string, Transaction>>({})

  /**
   * Guarda a versão recebida. Respostas fora de ordem não fazem o status regredir
   * (ex.: uma página lenta com "Pending" chegando depois do polling já ter visto "Completed").
   */
  function remember(next: Transaction): Transaction {
    const previous = byId.value[next.id]
    if (previous && statusRank(next.status) < statusRank(previous.status)) return previous
    byId.value[next.id] = next
    onTransition(previous, next)
    return next
  }

  /** Mudança de status observada: atualiza contagens e, se concluiu, os saldos envolvidos. */
  function onTransition(previous: Transaction | undefined, next: Transaction) {
    if (!previous || previous.status === next.status) return
    if (summaryState.hasLoaded.value) void fetchSummary()
    if (!isFinalStatus(previous.status) && next.status === 'Completed') {
      void accounts.refreshBalancesByNumber([next.sourceAccount, next.destinationAccount])
    }
  }

  function resolve(transaction: Transaction): Transaction {
    return byId.value[transaction.id] ?? transaction
  }

  function toView(transaction: Transaction, perspective?: string): TransactionView {
    return toTransactionView(resolve(transaction), { findAccount: accounts.findByNumber, perspective })
  }

  function findById(id: string) {
    return byId.value[id]
  }

  function viewOf(id: string): TransactionView | null {
    const transaction = byId.value[id]
    return transaction ? toView(transaction) : null
  }

  /** Busca uma página na API e registra os itens no cache. Usado por todas as listas paginadas. */
  async function fetchPage(
    params: PageParams & TransactionListFilters,
    signal?: AbortSignal,
  ): Promise<PagedResult<Transaction>> {
    const result = await transactionsApi.list(params, signal)
    result.items.forEach(remember)
    return result
  }

  // ---- histórico paginado ----------------------------------------------------

  const list = usePagedList<Transaction, { statusFilter: StatusFilter }>({
    filters: { statusFilter: 'all' },
    fetchPage: ({ statusFilter, ...params }, signal) =>
      fetchPage({ ...params, status: statusFilterStatuses[statusFilter] }, signal),
  })

  const listViews = computed(() => list.items.map((t) => toView(t)))

  // ---- resumo global -------------------------------------------------------

  const summary = shallowRef<TransactionSummary | null>(null)
  const summaryState = useRequestState()

  function fetchSummary(): Promise<void> {
    return summaryState.run(async () => {
      summary.value = await transactionsApi.summary()
    })
  }

  function ensureSummary(): Promise<void> {
    return summaryState.hasLoaded.value ? Promise.resolve() : fetchSummary()
  }

  const countByStatus = computed<Record<TransactionStatus, number>>(() => ({
    Pending: summary.value?.pending ?? 0,
    Processing: summary.value?.processing ?? 0,
    Completed: summary.value?.completed ?? 0,
    Failed: summary.value?.failed ?? 0,
    Cancelled: summary.value?.cancelled ?? 0,
  }))

  const totalCount = computed(() => summary.value?.totalTransactions ?? 0)

  /** Pendentes + em processamento (todas as transações): o que ainda depende do Worker. */
  const inFlightCount = computed(() => countByStatus.value.Pending + countByStatus.value.Processing)

  /** Concluídas / (concluídas + falhas). Canceladas não entram: não chegaram a ser processadas. */
  const successRate = computed(() => {
    const { Completed, Failed } = countByStatus.value
    const processed = Completed + Failed
    return processed === 0 ? null : Completed / processed
  })

  // ---- consultas e mutações ------------------------------------------------

  /** Após criar/cancelar: recarrega o histórico (mesma página e filtro) e as contagens. */
  function refreshAfterMutation(): Promise<unknown> {
    return Promise.all([list.hasLoaded ? list.refresh({ force: true }) : undefined, fetchSummary()])
  }

  /** GET /transactions/{id}. Rejeita com ApiError. */
  async function fetchTransaction(id: string, signal?: AbortSignal): Promise<Transaction> {
    return remember(await transactionsApi.getById(id, signal))
  }

  /** GET /transactions/{id}/status: consulta leve usada pelo polling. Rejeita com ApiError. */
  async function fetchStatus(id: string, signal?: AbortSignal): Promise<TransactionStatusResponse> {
    const response = await transactionsApi.getStatus(id, signal)
    const current = byId.value[response.id]
    if (current) {
      remember({ ...current, status: response.status, failureReason: response.failureReason, updatedAt: response.updatedAt })
    }
    return response
  }

  /**
   * POST /transactions. A resposta 202 significa apenas "aceita para processamento":
   * a transação volta como Pending e precisa ser acompanhada até um status final.
   * Rejeita com ApiError (400 por campo, 422 conta inexistente).
   */
  async function createTransfer(request: CreateTransactionRequest): Promise<Transaction> {
    const transaction = remember(await transactionsApi.create(request))
    void refreshAfterMutation()
    return transaction
  }

  /**
   * PATCH /transactions/{id}/cancel. Em 409 (o Worker já pegou a transação),
   * recarrega a transação para a tela refletir o status real e repassa o erro.
   */
  async function cancelTransaction(id: string): Promise<Transaction> {
    try {
      const transaction = remember(await transactionsApi.cancel(id))
      void refreshAfterMutation()
      return transaction
    } catch (error) {
      if (isApiError(error) && error.status === 409) {
        await fetchTransaction(id).catch(() => undefined)
        void refreshAfterMutation()
      }
      throw error
    }
  }

  return {
    byId,
    list,
    listViews,
    summary,
    summaryState,
    countByStatus,
    totalCount,
    inFlightCount,
    successRate,
    findById,
    viewOf,
    toView,
    fetchPage,
    fetchSummary,
    ensureSummary,
    fetchTransaction,
    fetchStatus,
    createTransfer,
    cancelTransaction,
  }
})
