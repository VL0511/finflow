import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createPoller, type PollerState } from './poller'

describe('createPoller', () => {
  beforeEach(() => vi.useFakeTimers())
  afterEach(() => vi.useRealTimers())

  function setup(task: (signal: AbortSignal) => Promise<boolean>, overrides = {}) {
    const states: PollerState[] = []
    const poller = createPoller({
      task,
      delays: [100, 200, 400],
      timeoutMs: 10_000,
      maxConsecutiveErrors: 3,
      onStateChange: (s) => states.push(s),
      now: () => Date.now(),
      ...overrides,
    })
    return { poller, states }
  }

  it('consulta até a task indicar fim e para', async () => {
    const results = [false, false, true]
    const task = vi.fn(async () => results.shift() ?? true)
    const { poller, states } = setup(task)

    poller.start()
    await vi.advanceTimersByTimeAsync(0) // 1ª (imediata)
    await vi.advanceTimersByTimeAsync(100) // 2ª
    await vi.advanceTimersByTimeAsync(200) // 3ª → done
    await vi.advanceTimersByTimeAsync(5_000)

    expect(task).toHaveBeenCalledTimes(3)
    expect(poller.state).toBe('done')
    expect(states).toEqual(['running', 'done'])
  })

  it('usa intervalos crescentes e repete o último', async () => {
    const task = vi.fn(async () => false)
    const { poller } = setup(task)

    poller.start()
    await vi.advanceTimersByTimeAsync(0)
    expect(task).toHaveBeenCalledTimes(1)
    await vi.advanceTimersByTimeAsync(99)
    expect(task).toHaveBeenCalledTimes(1)
    await vi.advanceTimersByTimeAsync(1) // +100
    expect(task).toHaveBeenCalledTimes(2)
    await vi.advanceTimersByTimeAsync(200)
    expect(task).toHaveBeenCalledTimes(3)
    await vi.advanceTimersByTimeAsync(400)
    expect(task).toHaveBeenCalledTimes(4)
    await vi.advanceTimersByTimeAsync(400) // último intervalo se repete
    expect(task).toHaveBeenCalledTimes(5)
    poller.stop()
  })

  it('desiste por timeout sem presumir resultado', async () => {
    const task = vi.fn(async () => false)
    const { poller } = setup(task, { timeoutMs: 1_000 })

    poller.start()
    await vi.advanceTimersByTimeAsync(5_000)

    expect(poller.state).toBe('timeout')
    const calls = task.mock.calls.length
    await vi.advanceTimersByTimeAsync(5_000)
    expect(task.mock.calls.length).toBe(calls)
  })

  it('para após N falhas consecutivas e reseta o contador num sucesso', async () => {
    const outcomes: Array<'ok' | 'fail'> = ['fail', 'fail', 'ok', 'fail', 'fail', 'fail']
    const task = vi.fn(async () => {
      if (outcomes.shift() === 'fail') throw new Error('rede')
      return false
    })
    const { poller, states } = setup(task)

    poller.start()
    await vi.advanceTimersByTimeAsync(5_000)

    expect(task).toHaveBeenCalledTimes(6)
    expect(poller.state).toBe('error')
    expect(states.at(-1)).toBe('error')
  })

  it('nunca executa duas tasks em paralelo e aborta a requisição ao pausar', async () => {
    let inFlight = 0
    let maxInFlight = 0
    const signals: AbortSignal[] = []
    const task = vi.fn(
      (signal: AbortSignal) =>
        new Promise<boolean>((resolve, reject) => {
          inFlight++
          maxInFlight = Math.max(maxInFlight, inFlight)
          signals.push(signal)
          signal.addEventListener('abort', () => {
            inFlight--
            reject(new Error('aborted'))
          })
          setTimeout(() => {
            if (signal.aborted) return
            inFlight--
            resolve(false)
          }, 300)
        }),
    )
    const { poller } = setup(task)

    poller.start()
    await vi.advanceTimersByTimeAsync(50) // task em voo
    poller.pause()
    expect(signals[0]!.aborted).toBe(true)
    expect(poller.state).toBe('paused')

    await vi.advanceTimersByTimeAsync(2_000)
    expect(task).toHaveBeenCalledTimes(1) // nada roda pausado

    poller.resume()
    await vi.advanceTimersByTimeAsync(2_000)
    expect(maxInFlight).toBe(1)
    poller.stop()
    expect(poller.state).toBe('stopped')
  })

  it('tempo pausado não conta para o timeout', async () => {
    const task = vi.fn(async () => false)
    const { poller } = setup(task, { timeoutMs: 1_000 })

    poller.start()
    await vi.advanceTimersByTimeAsync(500)
    poller.pause()
    await vi.advanceTimersByTimeAsync(60_000)
    poller.resume()
    await vi.advanceTimersByTimeAsync(300)
    expect(poller.state).toBe('running')
    await vi.advanceTimersByTimeAsync(1_000)
    expect(poller.state).toBe('timeout')
  })

  it('stop impede novas consultas, mesmo com uma resposta chegando depois', async () => {
    let resolveTask: (done: boolean) => void = () => {}
    const task = vi.fn(() => new Promise<boolean>((resolve) => (resolveTask = resolve)))
    const { poller } = setup(task)

    poller.start()
    await vi.advanceTimersByTimeAsync(0)
    poller.stop()
    resolveTask(false)
    await vi.advanceTimersByTimeAsync(5_000)

    expect(task).toHaveBeenCalledTimes(1)
    expect(poller.state).toBe('stopped')
  })
})
