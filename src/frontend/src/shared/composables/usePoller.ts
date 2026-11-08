import { onScopeDispose, ref, shallowRef } from 'vue'
import { createPoller, type PollerOptions, type PollerState } from '@/shared/utils/poller'

/**
 * Liga o poller ao ciclo de vida do componente:
 * - para sozinho quando o componente é desmontado;
 * - pausa com a aba oculta e retoma (com verificação imediata) ao voltar.
 */
export function usePoller(options: Omit<PollerOptions, 'onStateChange'>) {
  const state = ref<PollerState>('idle')
  const lastError = shallowRef<unknown>(null)

  const poller = createPoller({
    ...options,
    onStateChange(next, error) {
      state.value = next
      lastError.value = next === 'error' ? error : null
    },
  })

  function onVisibilityChange() {
    if (document.visibilityState === 'hidden') poller.pause()
    else poller.resume()
  }

  if (typeof document !== 'undefined') {
    document.addEventListener('visibilitychange', onVisibilityChange)
  }

  onScopeDispose(() => {
    poller.stop()
    if (typeof document !== 'undefined') {
      document.removeEventListener('visibilitychange', onVisibilityChange)
    }
  })

  function start(startOptions?: { immediate?: boolean }) {
    poller.start(startOptions)
    // Se a aba já estiver oculta, não faz sentido consultar agora.
    if (typeof document !== 'undefined' && document.visibilityState === 'hidden') poller.pause()
  }

  return { state, lastError, start, stop: poller.stop }
}
