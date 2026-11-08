/**
 * Polling controlado, independente de Vue (testável com fake timers).
 *
 * - `task` devolve `true` quando não há mais nada a esperar (ex.: status final).
 * - Intervalos crescentes (`delays`; o último se repete) para não martelar a API.
 * - Para por conta própria ao atingir `timeoutMs` de tempo ATIVO (pausas não contam)
 *   ou após `maxConsecutiveErrors` falhas seguidas.
 * - `pause`/`resume` (aba oculta) abortam a requisição em voo e retomam com uma
 *   verificação imediata.
 * - Nunca há duas execuções de `task` ao mesmo tempo.
 */

export type PollerState = 'idle' | 'running' | 'paused' | 'done' | 'timeout' | 'error' | 'stopped'

export interface PollerOptions {
  task: (signal: AbortSignal) => Promise<boolean>
  delays: readonly number[]
  timeoutMs: number
  maxConsecutiveErrors?: number
  onStateChange?: (state: PollerState, error?: unknown) => void
  now?: () => number
}

export interface Poller {
  readonly state: PollerState
  start(options?: { immediate?: boolean }): void
  pause(): void
  resume(): void
  stop(): void
}

export function createPoller(options: PollerOptions): Poller {
  const { task, delays, timeoutMs, maxConsecutiveErrors = 3, onStateChange } = options
  const now = options.now ?? (() => Date.now())

  let state: PollerState = 'idle'
  let timer: ReturnType<typeof setTimeout> | undefined
  let controller: AbortController | undefined
  /** Incrementado a cada start/resume/stop: invalida ticks de ciclos anteriores. */
  let generation = 0
  let activeElapsed = 0
  let activeSince = 0
  let attempt = 0
  let consecutiveErrors = 0

  function setState(next: PollerState, error?: unknown) {
    if (state === next) return
    state = next
    onStateChange?.(next, error)
  }

  function clearPending() {
    if (timer !== undefined) clearTimeout(timer)
    timer = undefined
    controller?.abort()
    controller = undefined
  }

  function elapsed() {
    return activeElapsed + (state === 'running' ? now() - activeSince : 0)
  }

  function nextDelay() {
    const delay = delays[Math.min(attempt, delays.length - 1)] ?? 1000
    attempt++
    return delay
  }

  function schedule(ms: number, gen: number) {
    timer = setTimeout(() => void tick(gen), ms)
  }

  async function tick(gen: number) {
    timer = undefined
    if (gen !== generation || state !== 'running') return

    if (elapsed() >= timeoutMs) {
      setState('timeout')
      return
    }

    controller = new AbortController()
    try {
      const done = await task(controller.signal)
      if (gen !== generation) return
      consecutiveErrors = 0
      if (done) {
        setState('done')
        return
      }
    } catch (error) {
      if (gen !== generation) return
      consecutiveErrors++
      if (consecutiveErrors >= maxConsecutiveErrors) {
        setState('error', error)
        return
      }
    } finally {
      if (gen === generation) controller = undefined
    }

    schedule(nextDelay(), gen)
  }

  return {
    get state() {
      return state
    },

    start({ immediate = true } = {}) {
      clearPending()
      generation++
      activeElapsed = 0
      activeSince = now()
      attempt = 0
      consecutiveErrors = 0
      state = 'idle'
      setState('running')
      schedule(immediate ? 0 : nextDelay(), generation)
    },

    pause() {
      if (state !== 'running') return
      activeElapsed += now() - activeSince
      clearPending()
      generation++
      setState('paused')
    },

    resume() {
      if (state !== 'paused') return
      generation++
      activeSince = now()
      setState('running')
      schedule(0, generation)
    },

    stop() {
      clearPending()
      generation++
      if (state === 'running' || state === 'paused') setState('stopped')
    },
  }
}
