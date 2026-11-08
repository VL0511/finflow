import { createPinia, setActivePinia } from 'pinia'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { effectScope } from 'vue'
import { makeTransaction } from '@/test/fixtures'
import { transactionsApi } from '../api/transactionsApi'
import { useTransactionsStore } from '../stores/useTransactionsStore'
import type { TransactionStatus } from '../types/transaction'
import { STATUS_POLL_TIMEOUT_MS, useTransactionStatusPolling } from './useTransactionStatusPolling'

vi.mock('../api/transactionsApi', () => ({
  transactionsApi: { list: vi.fn(), summary: vi.fn(), getById: vi.fn(), getStatus: vi.fn(), create: vi.fn(), cancel: vi.fn() },
}))
vi.mock('@/features/accounts/api/accountsApi', () => ({
  accountsApi: { list: vi.fn(), summary: vi.fn(), getById: vi.fn(), getBalance: vi.fn(), cashFlow: vi.fn(), create: vi.fn() },
}))

const txApi = vi.mocked(transactionsApi)

function statusSequence(...statuses: TransactionStatus[]) {
  for (const status of statuses) {
    txApi.getStatus.mockResolvedValueOnce({ id: 'tx-1', status, failureReason: null, updatedAt: null })
  }
}

describe('useTransactionStatusPolling', () => {
  beforeEach(async () => {
    vi.useFakeTimers()
    setActivePinia(createPinia())
    vi.resetAllMocks()
    txApi.getById.mockResolvedValue(makeTransaction({ id: 'tx-1', status: 'Pending' }))
    await useTransactionsStore().fetchTransaction('tx-1')
  })
  afterEach(() => vi.useRealTimers())

  it('acompanha até o status final e para', async () => {
    statusSequence('Pending', 'Processing', 'Completed')
    const scope = effectScope()
    const polling = scope.run(() => useTransactionStatusPolling(() => 'tx-1'))!

    polling.start()
    await vi.advanceTimersByTimeAsync(30_000)

    expect(txApi.getStatus).toHaveBeenCalledTimes(3)
    expect(polling.state.value).toBe('done')
    expect(useTransactionsStore().findById('tx-1')?.status).toBe('Completed')
    scope.stop()
  })

  it('transação parada em Pending (Worker fora do ar) termina em timeout, sem inventar status', async () => {
    txApi.getStatus.mockResolvedValue({ id: 'tx-1', status: 'Pending', failureReason: null, updatedAt: null })
    const scope = effectScope()
    const polling = scope.run(() => useTransactionStatusPolling(() => 'tx-1'))!

    polling.start()
    await vi.advanceTimersByTimeAsync(STATUS_POLL_TIMEOUT_MS + 10_000)

    expect(polling.state.value).toBe('timeout')
    expect(useTransactionsStore().findById('tx-1')?.status).toBe('Pending')
    // Intervalos crescentes: bem menos de uma consulta por segundo.
    expect(txApi.getStatus.mock.calls.length).toBeLessThan(STATUS_POLL_TIMEOUT_MS / 1000 / 3)
    scope.stop()
  })

  it('para de consultar quando o componente é desmontado', async () => {
    txApi.getStatus.mockResolvedValue({ id: 'tx-1', status: 'Processing', failureReason: null, updatedAt: null })
    const scope = effectScope()
    const polling = scope.run(() => useTransactionStatusPolling(() => 'tx-1'))!

    polling.start()
    await vi.advanceTimersByTimeAsync(3_000)
    const calls = txApi.getStatus.mock.calls.length
    scope.stop()
    await vi.advanceTimersByTimeAsync(60_000)

    expect(txApi.getStatus.mock.calls.length).toBe(calls)
    expect(polling.state.value).toBe('stopped')
  })
})
