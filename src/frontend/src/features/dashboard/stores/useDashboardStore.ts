import { defineStore } from 'pinia'
import { computed, ref, shallowRef, watch } from 'vue'
import { accountsApi } from '@/features/accounts/api/accountsApi'
import { useAccountsStore } from '@/features/accounts/stores/useAccountsStore'
import type { AccountDailyCashFlow } from '@/features/accounts/types/account'
import { useTransactionsStore } from '@/features/transactions/stores/useTransactionsStore'
import { normalizeApiError, type ApiError, type RequestStatus } from '@/shared/api'
import type { CurrencyCode } from '@/shared/utils/formatters'
import type { CashFlowPeriod } from '../types/dashboard'
import { buildCashFlowPoints } from '../utils/cashFlowAggregation'
import { cashFlowRange, toDailySeries } from '../utils/cashFlowFromTransactions'

/** Dias pedidos à API: cobre "12 meses" agrupado por mês civil (inclui o mês corrente parcial). */
export const CASH_FLOW_DAYS = 400

/**
 * Métricas do dashboard. Nenhum total é calculado a partir de uma página de listagem:
 * - saldo total e quantidade de contas → GET /api/accounts/summary;
 * - quantidade de transações, em andamento e taxa de sucesso → GET /api/transactions/summary;
 * - entradas/saídas por conta → GET /api/accounts/{id}/cash-flow (agregado por dia no banco).
 *
 * Limitações do backend refletidas aqui (documentadas em vez de simuladas):
 * - não há histórico de saldo → o saldo total não tem tendência/sparkline;
 * - não há "dono" de conta → entradas/saídas só existem por conta.
 */
export const useDashboardStore = defineStore('dashboard', () => {
  const accounts = useAccountsStore()
  const transactions = useTransactionsStore()

  const period = ref<CashFlowPeriod>('30d')
  const selectedAccountNumber = ref<string | null>(null)

  // ---- KPIs ---------------------------------------------------------------

  /** Primeira carga de algum dos resumos (sem dados ainda para mostrar). */
  const isSummaryLoading = computed(
    () => accounts.summaryState.isInitialLoading || transactions.summaryState.isInitialLoading,
  )

  const totalBrlBalance = computed(() => accounts.totalBrlBalance)

  /** Saldos em outras moedas: exibidos à parte (sem conversão cambial). */
  const otherCurrencyTotals = computed(() =>
    (accounts.summary?.balances ?? [])
      .filter((b) => b.currency !== 'BRL')
      .map((b) => ({ currency: b.currency, total: b.totalBalance })),
  )

  const brlAccountsCount = computed(
    () => accounts.summary?.balances.find((b) => b.currency === 'BRL')?.accounts ?? 0,
  )

  // ---- movimentações por conta -------------------------------------------

  /** Conta padrão: a primeira ativa do diretório (ou a primeira). */
  const selectedAccount = computed(() => {
    if (selectedAccountNumber.value) {
      const chosen = accounts.findByNumber(selectedAccountNumber.value)
      if (chosen) return chosen
    }
    return accounts.directory.find((a) => a.isActive) ?? accounts.directory[0] ?? null
  })

  const chartCurrency = computed<CurrencyCode>(() => selectedAccount.value?.currency ?? 'BRL')

  const movements = shallowRef<AccountDailyCashFlow[]>([])
  const cashFlowStatus = ref<RequestStatus>('idle')
  const cashFlowError = shallowRef<ApiError | null>(null)
  /** Conta a que `movements` se refere (evita mostrar a série de uma conta com o nome de outra). */
  const cashFlowAccountId = ref<string | null>(null)
  let cashFlowRequest = 0
  let cashFlowController: AbortController | null = null

  let pendingCashFlow: { accountId: string; promise: Promise<void> } | null = null

  /**
   * Busca a série da conta selecionada. Pedidos para a mesma conta com uma carga em voo
   * reaproveitam a promise; respostas de seleções anteriores são descartadas.
   */
  function loadCashFlow(): Promise<void> {
    const account = selectedAccount.value
    if (account && pendingCashFlow?.accountId === account.id) return pendingCashFlow.promise

    cashFlowController?.abort()
    if (!account) {
      movements.value = []
      cashFlowAccountId.value = null
      cashFlowStatus.value = 'idle'
      return Promise.resolve()
    }

    const requestId = ++cashFlowRequest
    const controller = new AbortController()
    cashFlowController = controller
    cashFlowStatus.value = 'loading'
    cashFlowError.value = null

    const promise = accountsApi
      .cashFlow(account.id, cashFlowRange(CASH_FLOW_DAYS), controller.signal)
      .then((response) => {
        if (requestId !== cashFlowRequest) return
        movements.value = response.days
        cashFlowAccountId.value = account.id
        cashFlowStatus.value = 'success'
      })
      .catch((error: unknown) => {
        if (requestId !== cashFlowRequest) return
        const apiError = normalizeApiError(error)
        if (apiError.kind === 'canceled') return
        cashFlowError.value = apiError
        cashFlowStatus.value = 'error'
      })
      .finally(() => {
        if (pendingCashFlow?.promise === promise) pendingCashFlow = null
      })

    pendingCashFlow = { accountId: account.id, promise }
    return promise
  }

  // Troca de conta → nova série.
  watch(() => selectedAccount.value?.id, () => void loadCashFlow())

  const isCashFlowInitialLoading = computed(
    () => cashFlowStatus.value === 'loading' && cashFlowAccountId.value !== selectedAccount.value?.id,
  )

  const dailyCashFlow = computed(() =>
    cashFlowAccountId.value && cashFlowAccountId.value === selectedAccount.value?.id
      ? toDailySeries(movements.value, { days: CASH_FLOW_DAYS })
      : [],
  )

  const chartPoints = computed(() => buildCashFlowPoints(dailyCashFlow.value, period.value))

  const periodTotals = computed(() => {
    const totals = chartPoints.value.reduce(
      (acc, p) => ({ inflow: acc.inflow + p.inflow, outflow: acc.outflow + p.outflow }),
      { inflow: 0, outflow: 0 },
    )
    return { ...totals, net: totals.inflow - totals.outflow }
  })

  const hasMovementInPeriod = computed(() => periodTotals.value.inflow > 0 || periodTotals.value.outflow > 0)

  function selectAccount(accountNumber: string) {
    selectedAccountNumber.value = accountNumber
  }

  // ---- carregamento -------------------------------------------------------

  /** Recarrega resumos, diretório e a série da conta selecionada (deduplicado pelos stores). */
  async function refresh(): Promise<void> {
    await Promise.all([
      accounts.fetchSummary(),
      transactions.fetchSummary(),
      accounts.fetchDirectory(),
    ])
    await loadCashFlow()
  }

  return {
    period,
    isSummaryLoading,
    refresh,
    loadCashFlow,
    totalBrlBalance,
    otherCurrencyTotals,
    brlAccountsCount,
    selectedAccount,
    chartCurrency,
    chartPoints,
    periodTotals,
    hasMovementInPeriod,
    cashFlowStatus,
    cashFlowError,
    isCashFlowInitialLoading,
    selectAccount,
  }
})
