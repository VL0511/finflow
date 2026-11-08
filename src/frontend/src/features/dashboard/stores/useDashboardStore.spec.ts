import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { accountsApi } from '@/features/accounts/api/accountsApi'
import type { AccountCashFlow } from '@/features/accounts/types/account'
import { transactionsApi } from '@/features/transactions/api/transactionsApi'
import { useTransactionsStore } from '@/features/transactions/stores/useTransactionsStore'
import { ana, bruno, carla, diego, paged } from '@/test/fixtures'
import { cashFlowRange, toDailySeries, toLocalIsoDate } from '../utils/cashFlowFromTransactions'
import { CASH_FLOW_DAYS, useDashboardStore } from './useDashboardStore'

vi.mock('@/features/accounts/api/accountsApi', () => ({
  accountsApi: { list: vi.fn(), summary: vi.fn(), getById: vi.fn(), getBalance: vi.fn(), cashFlow: vi.fn(), create: vi.fn() },
}))
vi.mock('@/features/transactions/api/transactionsApi', () => ({
  transactionsApi: { list: vi.fn(), summary: vi.fn(), getById: vi.fn(), getStatus: vi.fn(), create: vi.fn(), cancel: vi.fn() },
}))

const accApi = vi.mocked(accountsApi)
const txApi = vi.mocked(transactionsApi)

const today = new Date()
const daysAgo = (n: number) => {
  const d = new Date(today)
  d.setDate(d.getDate() - n)
  return toLocalIsoDate(d)
}

function cashFlowOf(accountId: string, days: AccountCashFlow['days'], currency: 'BRL' | 'USD' = 'BRL'): AccountCashFlow {
  return { accountId, accountNumber: '', currency, from: daysAgo(399), to: daysAgo(0), utcOffsetMinutes: -180, days }
}

describe('utils de fluxo de caixa', () => {
  it('cashFlowRange pede N dias até hoje no fuso local (offset no sinal da API)', () => {
    const range = cashFlowRange(400, today)
    expect(range.to).toBe(toLocalIsoDate(today))
    expect(range.from).toBe(daysAgo(399))
    expect(range.utcOffsetMinutes).toBe(-today.getTimezoneOffset())
  })

  it('toDailySeries preenche dias sem movimento com zero', () => {
    const series = toDailySeries([{ date: daysAgo(1), inflow: 120, outflow: 0 }], { days: 7, today })
    expect(series).toHaveLength(7)
    expect(series.at(-1)!.date).toBe(toLocalIsoDate(today))
    expect(series.reduce((s, d) => s + d.inflow, 0)).toBe(120)
    expect(series.filter((d) => d.inflow === 0 && d.outflow === 0)).toHaveLength(6)
  })
})

describe('useDashboardStore', () => {
  beforeEach(async () => {
    setActivePinia(createPinia())
    vi.resetAllMocks()
    accApi.summary.mockResolvedValue({
      totalAccounts: 4,
      activeAccounts: 3,
      inactiveAccounts: 1,
      balances: [
        { currency: 'BRL', accounts: 3, activeAccounts: 2, totalBalance: 1550 },
        { currency: 'USD', accounts: 1, activeAccounts: 1, totalBalance: 5000 },
      ],
    })
    txApi.summary.mockResolvedValue({ totalTransactions: 11, pending: 4, processing: 0, completed: 1, failed: 4, cancelled: 2 })
    accApi.list.mockResolvedValue(paged([carla, ana, diego, bruno], { pageSize: 100 }))
    accApi.cashFlow.mockImplementation(async (id) =>
      id === ana.id
        ? cashFlowOf(ana.id, [
            { date: daysAgo(1), inflow: 0, outflow: 500 },
            { date: daysAgo(3), inflow: 120, outflow: 0 },
            { date: daysAgo(60), inflow: 999, outflow: 0 },
          ])
        : cashFlowOf(id, []),
    )
    await useDashboardStore().refresh()
  })

  it('KPIs vêm dos resumos globais (sem calcular sobre páginas)', () => {
    const dashboard = useDashboardStore()
    const transactions = useTransactionsStore()

    expect(dashboard.totalBrlBalance).toBe(1550)
    expect(dashboard.brlAccountsCount).toBe(3)
    expect(dashboard.otherCurrencyTotals).toEqual([{ currency: 'USD', total: 5000 }])
    expect(transactions.totalCount).toBe(11)
    expect(transactions.inFlightCount).toBe(4)
    expect(transactions.successRate).toBe(0.2)
    expect(txApi.list).not.toHaveBeenCalled()
  })

  it('conta padrão: a primeira ativa do diretório; pede 400 dias no fuso local', () => {
    const dashboard = useDashboardStore()
    expect(dashboard.selectedAccount?.accountNumber).toBe('11223') // Carla (primeira por número, ativa)
    expect(accApi.cashFlow).toHaveBeenCalledWith(carla.id, cashFlowRange(CASH_FLOW_DAYS), expect.any(AbortSignal))
    expect(dashboard.chartCurrency).toBe('USD')
    expect(dashboard.hasMovementInPeriod).toBe(false)
  })

  it('entradas/saídas do período vêm da série agregada da conta selecionada', async () => {
    const dashboard = useDashboardStore()
    dashboard.selectAccount('12345')
    await vi.waitFor(() => expect(dashboard.cashFlowStatus).toBe('success'))
    await vi.waitFor(() => expect(dashboard.periodTotals.outflow).toBe(500))

    dashboard.period = '7d'
    expect(dashboard.periodTotals).toEqual({ inflow: 120, outflow: 500, net: -380 })

    dashboard.period = '12m'
    expect(dashboard.periodTotals.inflow).toBe(1119) // inclui a de 60 dias atrás
  })

  it('troca rápida de conta: a resposta da conta anterior é descartada', async () => {
    let releaseAna!: () => void
    accApi.cashFlow.mockImplementation((id) =>
      id === ana.id
        ? new Promise((resolve) => (releaseAna = () => resolve(cashFlowOf(ana.id, [{ date: daysAgo(1), inflow: 0, outflow: 500 }]))))
        : Promise.resolve(cashFlowOf(id, [{ date: daysAgo(1), inflow: 7, outflow: 0 }])),
    )
    const dashboard = useDashboardStore()

    dashboard.selectAccount('12345') // lenta
    await vi.waitFor(() => expect(accApi.cashFlow).toHaveBeenLastCalledWith(ana.id, expect.anything(), expect.any(AbortSignal)))
    dashboard.selectAccount('67890') // rápida
    await vi.waitFor(() => expect(dashboard.periodTotals.inflow).toBe(7))
    releaseAna()
    await Promise.resolve()

    expect(dashboard.selectedAccount?.accountNumber).toBe('67890')
    expect(dashboard.periodTotals).toEqual({ inflow: 7, outflow: 0, net: 7 })
  })

  it('erro no fluxo de caixa fica no estado, sem série inventada', async () => {
    accApi.cashFlow.mockRejectedValue(new Error('rede'))
    const dashboard = useDashboardStore()
    dashboard.selectAccount('67890')

    await vi.waitFor(() => expect(dashboard.cashFlowStatus).toBe('error'))
    expect(dashboard.chartPoints).toEqual([])
  })

  it('refresh deduplica: uma requisição por recurso mesmo chamado em paralelo', async () => {
    vi.clearAllMocks()
    const dashboard = useDashboardStore()
    await Promise.all([dashboard.refresh(), dashboard.refresh()])

    expect(accApi.summary).toHaveBeenCalledTimes(1)
    expect(txApi.summary).toHaveBeenCalledTimes(1)
    expect(accApi.list).toHaveBeenCalledTimes(1)
    expect(accApi.cashFlow).toHaveBeenCalledTimes(1)
  })
})
