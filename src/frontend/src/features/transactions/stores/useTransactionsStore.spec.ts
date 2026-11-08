import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { accountsApi } from '@/features/accounts/api/accountsApi'
import { useAccountsStore } from '@/features/accounts/stores/useAccountsStore'
import { ApiError } from '@/shared/api'
import { ana, bruno, makeTransaction, pageOf, paged } from '@/test/fixtures'
import { transactionsApi } from '../api/transactionsApi'
import type { Transaction, TransactionSummary } from '../types/transaction'
import { useTransactionsStore } from './useTransactionsStore'

vi.mock('../api/transactionsApi', () => ({
  transactionsApi: { list: vi.fn(), summary: vi.fn(), getById: vi.fn(), getStatus: vi.fn(), create: vi.fn(), cancel: vi.fn() },
}))
vi.mock('@/features/accounts/api/accountsApi', () => ({
  accountsApi: { list: vi.fn(), summary: vi.fn(), getById: vi.fn(), getBalance: vi.fn(), cashFlow: vi.fn(), create: vi.fn() },
}))

const txApi = vi.mocked(transactionsApi)
const accApi = vi.mocked(accountsApi)

const summary: TransactionSummary = { totalTransactions: 7, pending: 1, processing: 1, completed: 3, failed: 1, cancelled: 1 }

/** "Banco" em memória: a API mockada filtra e pagina como o backend. */
let db: Transaction[] = []

function mockBackend() {
  txApi.list.mockImplementation(async (params) => {
    const filtered = db.filter(
      (t) =>
        (!params.status?.length || params.status.includes(t.status)) &&
        (!params.accountNumber || t.sourceAccount === params.accountNumber || t.destinationAccount === params.accountNumber),
    )
    return pageOf(filtered.map((t) => ({ ...t })), params.page, params.pageSize) // cópias, como num JSON real
  })
  txApi.summary.mockResolvedValue(summary)
}

async function withAccounts() {
  accApi.list.mockResolvedValue(paged([ana, bruno], { pageSize: 100 }))
  await useAccountsStore().fetchDirectory()
}

describe('useTransactionsStore', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.resetAllMocks()
    db = Array.from({ length: 25 }, (_, i) =>
      makeTransaction({
        id: `tx-${i + 1}`,
        status: i % 5 === 0 ? 'Pending' : i % 5 === 1 ? 'Failed' : 'Completed',
        createdAt: new Date(Date.UTC(2026, 10, 6, 12, 0, 0) - i * 60_000).toISOString(),
      }),
    )
    mockBackend()
  })

  describe('histórico paginado', () => {
    it('busca a página com filtro de status no servidor', async () => {
      const store = useTransactionsStore()
      await store.list.load()

      expect(txApi.list).toHaveBeenLastCalledWith({ page: 1, pageSize: 10, status: [] }, expect.any(AbortSignal))
      expect(store.listViews).toHaveLength(10)
      expect(store.list.totalItems).toBe(25)
      expect(store.list.totalPages).toBe(3)
    })

    it('mudar o filtro volta para a página 1 e envia os status ao backend', async () => {
      const store = useTransactionsStore()
      await store.list.load()
      await store.list.setPage(3)

      await store.list.setFilters({ statusFilter: 'in-flight' })

      expect(store.list.page).toBe(1)
      expect(txApi.list).toHaveBeenLastCalledWith(
        { page: 1, pageSize: 10, status: ['Pending', 'Processing'] },
        expect.any(AbortSignal),
      )
      expect(store.list.totalItems).toBe(5) // total do filtro, não da página anterior
      expect(store.listViews.every((t) => t.status === 'Pending')).toBe(true)
    })

    it('filtro e página são preservados ao sair e voltar para a tela (refresh)', async () => {
      const store = useTransactionsStore()
      await store.list.setFilters({ statusFilter: 'Completed' })
      await store.list.nextPage()

      await store.list.refresh()

      expect(store.list.filters).toEqual({ statusFilter: 'Completed' })
      expect(txApi.list).toHaveBeenLastCalledWith({ page: 2, pageSize: 10, status: ['Completed'] }, expect.any(AbortSignal))
    })

    it('mudar o tamanho da página volta para a página 1', async () => {
      const store = useTransactionsStore()
      await store.list.load()
      await store.list.setPage(2)

      await store.list.setPageSize(20)

      expect(store.list.page).toBe(1)
      expect(store.listViews).toHaveLength(20)
    })

    it('views mostram titulares conhecidos e caem para "Conta N" nos desconhecidos', async () => {
      db = [makeTransaction({ id: 'a', destinationAccount: '99999' })]
      await withAccounts()
      const store = useTransactionsStore()
      await store.list.load()

      expect(store.listViews[0]).toMatchObject({
        direction: 'transfer',
        source: { name: 'Ana Souza', accountId: ana.id },
        destination: { name: 'Conta 99999', accountId: null },
      })
    })
  })

  describe('resumo global', () => {
    it('contagens, em andamento e taxa de sucesso vêm do resumo, não da página', async () => {
      const store = useTransactionsStore()
      await Promise.all([store.list.load(), store.fetchSummary()])

      expect(store.totalCount).toBe(7)
      expect(store.inFlightCount).toBe(2)
      expect(store.successRate).toBe(0.75)
    })
  })

  describe('criação e cancelamento', () => {
    it('createTransfer guarda a transação como Pending e atualiza histórico e contagens', async () => {
      const store = useTransactionsStore()
      await Promise.all([store.list.setPage(2), store.fetchSummary()])
      vi.clearAllMocks()
      mockBackend()
      const created = makeTransaction({ id: 'new', status: 'Pending', createdAt: '2026-11-07T00:00:00Z' })
      txApi.create.mockResolvedValue(created)
      db = [created, ...db]

      const result = await store.createTransfer({ sourceAccount: '12345', destinationAccount: '67890', amount: 10 })

      expect(result.status).toBe('Pending')
      await vi.waitFor(() => expect(store.list.totalItems).toBe(26))
      expect(txApi.list).toHaveBeenCalledWith({ page: 2, pageSize: 10, status: [] }, expect.any(AbortSignal)) // mesma página
      expect(txApi.summary).toHaveBeenCalledTimes(1)
      expect(accApi.getBalance).not.toHaveBeenCalled() // nada mudou de saldo ainda
    })

    it('cancelar reflete Cancelled de imediato e recarrega a página filtrada e as contagens', async () => {
      const store = useTransactionsStore()
      await store.list.setFilters({ statusFilter: 'in-flight' })
      await store.fetchSummary()
      const target = db.find((t) => t.status === 'Pending')!
      txApi.cancel.mockImplementation(async () => {
        target.status = 'Cancelled'
        return { ...target }
      })
      txApi.summary.mockResolvedValue({ ...summary, pending: 0, cancelled: 2 })

      await store.cancelTransaction(target.id)

      expect(store.findById(target.id)?.status).toBe('Cancelled')
      await vi.waitFor(() => expect(store.list.totalItems).toBe(4))
      expect(store.listViews.some((t) => t.id === target.id)).toBe(false) // saiu do filtro "em andamento"
      await vi.waitFor(() => expect(store.countByStatus.Cancelled).toBe(2))
    })

    it('cancelamento recusado (409) recarrega o status real, atualiza a lista e repassa o erro', async () => {
      const store = useTransactionsStore()
      await store.list.load()
      const target = db[0]!
      const conflict = new ApiError({
        kind: 'http',
        status: 409,
        title: 'Operação não permitida.',
        detail: 'Somente transações pendentes podem ser canceladas.',
      })
      txApi.cancel.mockRejectedValue(conflict)
      txApi.getById.mockResolvedValue({ ...target, status: 'Processing' })
      const listCalls = txApi.list.mock.calls.length

      await expect(store.cancelTransaction(target.id)).rejects.toBe(conflict)

      expect(store.findById(target.id)?.status).toBe('Processing')
      await vi.waitFor(() => expect(txApi.list.mock.calls.length).toBe(listCalls + 1))
    })
  })

  describe('cache e processamento', () => {
    it('aplica o status do polling e, ao concluir, reconsulta os saldos das duas contas', async () => {
      await withAccounts()
      accApi.summary.mockResolvedValue({ totalAccounts: 2, activeAccounts: 2, inactiveAccounts: 0, balances: [] })
      accApi.getBalance.mockImplementation(async (id) =>
        id === ana.id
          ? { accountId: ana.id, accountNumber: '12345', balance: 990, currency: 'BRL' }
          : { accountId: bruno.id, accountNumber: '67890', balance: 260, currency: 'BRL' },
      )
      const store = useTransactionsStore()
      txApi.create.mockResolvedValue(makeTransaction({ id: 'tx-new', status: 'Pending' }))
      await store.createTransfer({ sourceAccount: '12345', destinationAccount: '67890', amount: 10 })

      txApi.getStatus.mockResolvedValueOnce({ id: 'tx-new', status: 'Processing', failureReason: null, updatedAt: '2026-11-06T01:03:05Z' })
      await store.fetchStatus('tx-new')
      expect(store.findById('tx-new')?.status).toBe('Processing')
      expect(accApi.getBalance).not.toHaveBeenCalled()

      txApi.getStatus.mockResolvedValueOnce({ id: 'tx-new', status: 'Completed', failureReason: null, updatedAt: '2026-11-06T01:03:07Z' })
      await store.fetchStatus('tx-new')
      await vi.waitFor(() => expect(useAccountsStore().findById(bruno.id)?.balance).toBe(260))
      expect(useAccountsStore().findById(ana.id)?.balance).toBe(990)
    })

    it('falha no processamento: guarda o motivo e não reconsulta saldos', async () => {
      const store = useTransactionsStore()
      txApi.create.mockResolvedValue(makeTransaction({ id: 'tx-f', status: 'Pending' }))
      await store.createTransfer({ sourceAccount: '12345', destinationAccount: '67890', amount: 10 })

      txApi.getStatus.mockResolvedValue({ id: 'tx-f', status: 'Failed', failureReason: 'Saldo insuficiente na conta de origem.', updatedAt: 'x' })
      await store.fetchStatus('tx-f')

      expect(store.findById('tx-f')).toMatchObject({ status: 'Failed', failureReason: 'Saldo insuficiente na conta de origem.' })
      expect(accApi.getBalance).not.toHaveBeenCalled()
    })

    it('página antiga chegando depois não faz o status regredir (e a lista mostra o mais recente)', async () => {
      const store = useTransactionsStore()
      await store.list.load()
      const id = db[0]!.id
      txApi.getStatus.mockResolvedValue({ id, status: 'Completed', failureReason: null, updatedAt: 'x' })
      await store.fetchStatus(id)

      await store.list.refresh({ force: true }) // o "banco" ainda devolve Pending

      expect(store.findById(id)?.status).toBe('Completed')
      expect(store.listViews.find((t) => t.id === id)?.status).toBe('Completed')
    })
  })
})
