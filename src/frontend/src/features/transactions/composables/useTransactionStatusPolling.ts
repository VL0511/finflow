import { usePoller } from '@/shared/composables/usePoller'
import { useTransactionsStore } from '../stores/useTransactionsStore'
import { isFinalStatus } from '../utils/transactionStatus'

/** Intervalos entre consultas: rápido no início (o Worker costuma levar segundos), depois espaça. */
export const STATUS_POLL_DELAYS = [1000, 1500, 2000, 3000, 5000] as const
/** Tempo máximo de acompanhamento ativo; depois disso a UI oferece "Verificar novamente". */
export const STATUS_POLL_TIMEOUT_MS = 2 * 60_000

/**
 * Acompanha uma transação via GET /api/transactions/{id}/status até um status final
 * (Completed, Failed ou Cancelled). Cada resposta é aplicada ao store; ao concluir,
 * o store reconsulta os saldos das contas envolvidas.
 *
 * Para sozinho ao desmontar, pausa com a aba oculta e desiste após 3 falhas seguidas
 * ou STATUS_POLL_TIMEOUT_MS — sem nunca presumir o resultado.
 */
export function useTransactionStatusPolling(getId: () => string) {
  const store = useTransactionsStore()

  return usePoller({
    delays: STATUS_POLL_DELAYS,
    timeoutMs: STATUS_POLL_TIMEOUT_MS,
    maxConsecutiveErrors: 3,
    async task(signal) {
      const response = await store.fetchStatus(getId(), signal)
      return isFinalStatus(response.status)
    },
  })
}
