import { computed, watch } from 'vue'
import { usePoller } from '@/shared/composables/usePoller'
import { useTransactionsStore } from '../stores/useTransactionsStore'

const DELAYS = [5000, 10_000, 15_000] as const
const TIMEOUT_MS = 3 * 60_000

/**
 * Mantém a tela atualizada enquanto houver transações Pending/Processing NO SISTEMA
 * (contagem do resumo global, não da página exibida).
 *
 * Cada ciclo: GET /transactions/summary + o `refresh` da tela (ex.: a página atual).
 * Para quando não há mais nada em andamento, após 3 minutos (transações podem ficar
 * Pending indefinidamente com o Worker parado) ou em falhas seguidas — e recomeça quando
 * as contagens em andamento mudam (ex.: nova transferência).
 */
export function useInFlightAutoRefresh(refresh: () => Promise<unknown>) {
  const store = useTransactionsStore()

  const inFlightKey = computed(() =>
    store.inFlightCount === 0 ? '' : `${store.countByStatus.Pending}:${store.countByStatus.Processing}`,
  )

  const poller = usePoller({
    delays: DELAYS,
    timeoutMs: TIMEOUT_MS,
    maxConsecutiveErrors: 3,
    async task() {
      await Promise.all([store.fetchSummary(), refresh()])
      if (store.summaryState.error) throw store.summaryState.error
      return store.inFlightCount === 0
    },
  })

  watch(
    inFlightKey,
    (key, previousKey) => {
      if (!key) {
        poller.stop()
        return
      }
      const isActive = poller.state.value === 'running' || poller.state.value === 'paused'
      if (key !== previousKey && !isActive) poller.start({ immediate: false })
    },
    { immediate: true },
  )

  return { state: poller.state, restart: () => poller.start() }
}
