import { computed, ref, shallowRef } from 'vue'
import type { ApiError } from './ApiError'
import { normalizeApiError } from './normalizeApiError'

export type RequestStatus = 'idle' | 'loading' | 'success' | 'error'

/**
 * Estado de uma requisição de leitura dentro de um store (loading / erro / sucesso)
 * com deduplicação: chamadas concorrentes de `run` compartilham a mesma promise,
 * então dois componentes montando juntos não disparam duas requisições iguais.
 *
 * `run` nunca rejeita: a falha fica em `error` para a UI exibir. Os dados
 * anteriores são mantidos durante um recarregamento (`isRefreshing`).
 */
export function useRequestState() {
  const status = ref<RequestStatus>('idle')
  const error = shallowRef<ApiError | null>(null)
  const loadedAt = ref<number | null>(null)
  let pending: Promise<void> | null = null

  function run(task: () => Promise<void>): Promise<void> {
    if (pending) return pending

    status.value = 'loading'
    error.value = null

    pending = task()
      .then(() => {
        status.value = 'success'
        loadedAt.value = Date.now()
      })
      .catch((reason: unknown) => {
        error.value = normalizeApiError(reason)
        status.value = 'error'
      })
      .finally(() => {
        pending = null
      })

    return pending
  }

  const hasLoaded = computed(() => loadedAt.value !== null)
  const isLoading = computed(() => status.value === 'loading')
  /** Primeira carga (ainda sem dados para mostrar): a UI exibe skeleton. */
  const isInitialLoading = computed(() => isLoading.value && !hasLoaded.value)
  /** Recarga com dados já na tela: a UI mantém o conteúdo e sinaliza discretamente. */
  const isRefreshing = computed(() => isLoading.value && hasLoaded.value)

  return { status, error, loadedAt, hasLoaded, isLoading, isInitialLoading, isRefreshing, run }
}

export type RequestState = ReturnType<typeof useRequestState>
