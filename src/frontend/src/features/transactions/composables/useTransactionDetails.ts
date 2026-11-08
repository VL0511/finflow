import { computed, reactive, ref, shallowRef, watch, type Ref } from 'vue'
import { useAccountsStore } from '@/features/accounts/stores/useAccountsStore'
import { isApiError, normalizeApiError, useRequestState, type ApiError } from '@/shared/api'
import { useTransactionsStore } from '../stores/useTransactionsStore'
import { isFinalStatus } from '../utils/transactionStatus'
import { useTransactionStatusPolling } from './useTransactionStatusPolling'

/**
 * Detalhe de uma transação: carrega (GET /transactions/{id}), acompanha o status
 * por polling enquanto não for final e oferece o cancelamento (só em Pending).
 */
export function useTransactionDetails(id: Ref<string>) {
  const store = useTransactionsStore()
  const accounts = useAccountsStore()

  const detailState = useRequestState()
  const polling = useTransactionStatusPolling(() => id.value)

  const transaction = computed(() => store.viewOf(id.value))
  const notFound = computed(() => detailState.error.value?.status === 404)
  const isFinal = computed(() => (transaction.value ? isFinalStatus(transaction.value.status) : false))
  const canCancel = computed(() => transaction.value?.status === 'Pending')

  const isCancelling = ref(false)
  const cancelError = shallowRef<ApiError | null>(null)

  function startPollingIfNeeded() {
    if (transaction.value && !isFinalStatus(transaction.value.status)) polling.start({ immediate: false })
    else polling.stop()
  }

  function load() {
    return detailState.run(async () => {
      // Nomes dos titulares vêm das contas; carrega em paralelo (sem bloquear o detalhe).
      void accounts.ensureDirectory()
      await store.fetchTransaction(id.value)
      startPollingIfNeeded()
    })
  }

  /** Reinicia o acompanhamento após timeout/erro (ação manual do usuário). */
  function checkAgain() {
    polling.start()
  }

  async function cancel(): Promise<boolean> {
    if (!canCancel.value || isCancelling.value) return false
    isCancelling.value = true
    cancelError.value = null
    try {
      await store.cancelTransaction(id.value)
      return true
    } catch (error) {
      cancelError.value = isApiError(error) ? error : normalizeApiError(error)
      // 409: o Worker já pegou a transação; o store recarregou o status real. Continua acompanhando.
      startPollingIfNeeded()
      return false
    } finally {
      isCancelling.value = false
    }
  }

  watch(id, () => void load(), { immediate: true })

  // Status final observado (pelo polling ou pelo cancelamento): encerra o acompanhamento.
  watch(isFinal, (final) => {
    if (final) polling.stop()
  })

  return {
    transaction,
    detailState: reactive(detailState),
    notFound,
    isFinal,
    canCancel,
    isCancelling,
    cancelError,
    pollingState: polling.state,
    load,
    cancel,
    checkAgain,
  }
}
