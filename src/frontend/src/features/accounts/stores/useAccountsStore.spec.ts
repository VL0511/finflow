import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { ApiError } from '@/shared/api'
import { ana, bruno, carla, diego, makeAccount, pageOf, paged } from '@/test/fixtures'
import { accountsApi } from '../api/accountsApi'
import type { AccountSummary } from '../types/account'
import { useAccountsStore } from './useAccountsStore'

vi.mock('../api/accountsApi', () => ({
  accountsApi: { list: vi.fn(), summary: vi.fn(), getById: vi.fn(), getBalance: vi.fn(), cashFlow: vi.fn(), create: vi.fn() },
}))

const api = vi.mocked(accountsApi)
const ALL = [carla, ana, diego, bruno] // ordem do backend (por número)

const summary: AccountSummary = {
  totalAccounts: 4,
  activeAccounts: 3,
  inactiveAccounts: 1,
  balances: [
    { currency: 'BRL', accounts: 3, activeAccounts: 2, totalBalance: 1550 },
    { currency: 'USD', accounts: 1, activeAccounts: 1, totalBalance: 5000 },
  ],
}

describe('useAccountsStore', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.resetAllMocks()
    api.list.mockImplementation(async (params) => pageOf(ALL, params.page, params.pageSize))
    api.summary.mockResolvedValue(summary)
  })

  describe('listagem paginada', () => {
    it('carrega a página pedida, com os metadados da API', async () => {
      const store = useAccountsStore()
      await store.list.setPageSize(20)
      await store.list.load()

      expect(api.list).toHaveBeenLastCalledWith({ page: 1, pageSize: 20 }, expect.any(AbortSignal))
      expect(store.pageAccounts.map((a) => a.accountNumber)).toEqual(['11223', '12345', '44556', '67890'])
      expect(store.list.totalItems).toBe(4)
    })

    it('navega entre páginas e preserva página e tamanho ao recarregar (voltar à tela)', async () => {
      const store = useAccountsStore()
      await store.list.setPageSize(2) // volta à página 1
      await store.list.nextPage()
      expect(store.pageAccounts.map((a) => a.accountNumber)).toEqual(['44556', '67890'])

      await store.list.refresh()
      expect(api.list).toHaveBeenLastCalledWith({ page: 2, pageSize: 2 }, expect.any(AbortSignal))
      expect(store.list.hasNextPage).toBe(false)
    })

    it('expõe o erro e não usa dados de fallback', async () => {
      api.list.mockRejectedValue(new ApiError({ kind: 'network', status: 0, title: 'Não foi possível conectar ao servidor.' }))
      const store = useAccountsStore()

      await store.list.load()

      expect(store.pageAccounts).toEqual([])
      expect(store.list.status).toBe('error')
      expect(store.list.error?.kind).toBe('network')
    })

    it('lista vazia', async () => {
      api.list.mockResolvedValue(paged([]))
      const store = useAccountsStore()

      await store.list.load()

      expect(store.list.isEmpty).toBe(true)
    })
  })

  describe('resumo global', () => {
    it('totais e quantidades vêm do resumo (todas as contas), não da página', async () => {
      const store = useAccountsStore()
      await store.list.load() // página com 4 itens não influencia
      await store.fetchSummary()

      expect(store.totalBrlBalance).toBe(1550)
      expect(store.balanceByCurrency.get('USD')).toBe(5000)
      expect(store.totalCount).toBe(4)
      expect(store.activeCount).toBe(3)
      expect(store.inactiveCount).toBe(1)
    })

    it('resumo indisponível: zeros, sem inventar valores', async () => {
      api.summary.mockRejectedValue(new ApiError({ kind: 'http', status: 500, title: 'x' }))
      const store = useAccountsStore()
      await store.fetchSummary()

      expect(store.summary).toBeNull()
      expect(store.summaryState.status).toBe('error')
      expect(store.totalBrlBalance).toBe(0)
    })
  })

  describe('diretório (seletores e nomes)', () => {
    it('carrega até 100 contas numa única requisição e resolve nomes por número', async () => {
      const store = useAccountsStore()
      await Promise.all([store.ensureDirectory(), store.fetchDirectory(), store.ensureDirectory()])
      await store.ensureDirectory()

      expect(api.list).toHaveBeenCalledTimes(1)
      expect(api.list).toHaveBeenCalledWith({ page: 1, pageSize: 100 })
      expect(store.directory).toHaveLength(4)
      expect(store.findByNumber('67890')?.holderName).toBe('Bruno Lima')
      expect(store.isDirectoryTruncated).toBe(false)
    })

    it('sinaliza quando há mais contas do que o diretório comporta', async () => {
      api.list.mockResolvedValue(paged(ALL, { pageSize: 100, totalItems: 250 }))
      const store = useAccountsStore()
      await store.fetchDirectory()

      expect(store.isDirectoryTruncated).toBe(true)
      expect(store.directoryTotal).toBe(250)
    })
  })

  describe('saldos e criação', () => {
    it('consulta de saldo atualiza a conta exibida na página', async () => {
      api.getBalance.mockResolvedValue({ accountId: ana.id, accountNumber: '12345', balance: 750, currency: 'BRL' })
      const store = useAccountsStore()
      await store.list.load()

      await store.fetchBalance(ana.id)

      expect(store.pageAccounts.find((a) => a.id === ana.id)?.balance).toBe(750)
    })

    it('refreshBalancesByNumber consulta só contas conhecidas, tolera falhas e atualiza o resumo', async () => {
      api.getBalance.mockImplementation(async (id) => {
        if (id === ana.id) throw new ApiError({ kind: 'http', status: 500, title: 'x' })
        return { accountId: bruno.id, accountNumber: '67890', balance: 260, currency: 'BRL' }
      })
      const store = useAccountsStore()
      await store.fetchDirectory()

      await store.refreshBalancesByNumber(['12345', '67890', '99999', '67890'])

      expect(api.getBalance).toHaveBeenCalledTimes(2)
      expect(store.findById(bruno.id)?.balance).toBe(260)
      expect(store.findById(ana.id)?.balance).toBe(1000)
      expect(api.summary).toHaveBeenCalledTimes(1)
    })

    it('criar conta atualiza a listagem (mesma página), o resumo e o diretório', async () => {
      const created = makeAccount({ id: 'new-id', accountNumber: '99887', holderName: 'Fernanda Lima', balance: 1500 })
      api.create.mockResolvedValue(created)
      const store = useAccountsStore()
      await store.list.setPageSize(2)
      await store.list.nextPage()
      await Promise.all([store.fetchSummary(), store.fetchDirectory()])
      vi.clearAllMocks()
      api.list.mockImplementation(async (params) => pageOf([...ALL, created], params.page, params.pageSize))
      api.summary.mockResolvedValue({ ...summary, totalAccounts: 5 })

      const request = { accountNumber: '99887', holderName: 'Fernanda Lima', initialBalance: 1500, currency: 'BRL' as const, isActive: true }
      await expect(store.createAccount(request)).resolves.toEqual(created)
      await vi.waitFor(() => expect(store.list.totalItems).toBe(5))

      expect(api.create).toHaveBeenCalledWith(request)
      expect(api.list).toHaveBeenCalledWith({ page: 2, pageSize: 2 }, expect.any(AbortSignal)) // listagem, mesma página
      expect(api.list).toHaveBeenCalledWith({ page: 1, pageSize: 100 }) // diretório
      await vi.waitFor(() => expect(store.totalCount).toBe(5))
      expect(store.list.totalPages).toBe(3)
    })

    it('createAccount repassa o ApiError (ex.: 422 moeda desativada) e não recarrega nada', async () => {
      const rejected = new ApiError({ kind: 'http', status: 422, title: 'Regra de negócio violada.', detail: "A moeda 'EUR' está desativada." })
      api.create.mockRejectedValue(rejected)
      const store = useAccountsStore()

      await expect(
        store.createAccount({ holderName: 'X', initialBalance: 0, currency: 'EUR', isActive: true }),
      ).rejects.toBe(rejected)
      expect(api.list).not.toHaveBeenCalled()
    })
  })
})
