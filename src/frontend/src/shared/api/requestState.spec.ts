import { describe, expect, it, vi } from 'vitest'
import { ApiError } from './ApiError'
import { useRequestState } from './requestState'

describe('useRequestState', () => {
  it('deduplica chamadas concorrentes', async () => {
    const state = useRequestState()
    const task = vi.fn(() => new Promise<void>((resolve) => setTimeout(resolve, 10)))

    const a = state.run(task)
    const b = state.run(task)
    expect(a).toBe(b)
    expect(state.isInitialLoading.value).toBe(true)
    await a

    expect(task).toHaveBeenCalledTimes(1)
    expect(state.status.value).toBe('success')
    expect(state.hasLoaded.value).toBe(true)
  })

  it('guarda o erro como ApiError sem rejeitar', async () => {
    const state = useRequestState()
    const error = new ApiError({ kind: 'http', status: 500, title: 'Erro interno no servidor.' })

    await expect(state.run(() => Promise.reject(error))).resolves.toBeUndefined()

    expect(state.status.value).toBe('error')
    expect(state.error.value).toBe(error)
    expect(state.hasLoaded.value).toBe(false)
  })

  it('recarga após sucesso é "refreshing", não "initial loading"', async () => {
    const state = useRequestState()
    await state.run(async () => {})

    const pending = state.run(() => new Promise<void>((resolve) => setTimeout(resolve, 5)))
    expect(state.isRefreshing.value).toBe(true)
    expect(state.isInitialLoading.value).toBe(false)
    await pending
  })
})
