import { defineStore } from 'pinia'
import { computed, ref, shallowRef } from 'vue'
import { useRequestState } from '@/shared/api'
import { MAX_PAGE_SIZE } from '@/shared/api/pagination'
import { usePagedList } from '@/shared/composables/usePagedList'
import type { CurrencyCode } from '@/shared/utils/formatters'
import { accountsApi } from '../api/accountsApi'
import type { Account, AccountSummary, CreateAccountRequest } from '../types/account'

/**
 * Estado das contas no frontend, sempre alimentado pela API. Três fontes distintas,
 * para que nenhum total seja calculado a partir de uma página:
 *
 * - `list`: a listagem paginada da tela de contas (página/tamanho preservados entre telas);
 * - `summary`: quantidades e saldos por moeda de TODAS as contas (GET /accounts/summary);
 * - `directory`: até 100 contas (1 página no tamanho máximo) para seletores e nomes dos titulares.
 *
 * Toda conta recebida (de qualquer fonte) entra no cache `byId`, usado para resolver nomes.
 */
export const useAccountsStore = defineStore('accounts', () => {
  const byId = ref<Record<string, Account>>({})

  function remember(account: Account) {
    byId.value[account.id] = account
  }

  const accountsByNumber = computed(() => {
    const map = new Map<string, Account>()
    for (const account of Object.values(byId.value)) map.set(account.accountNumber, account)
    return map
  })

  function findById(id: string) {
    return byId.value[id]
  }

  function findByNumber(accountNumber: string) {
    return accountsByNumber.value.get(accountNumber)
  }

  /** Versão mais recente (cache) de uma conta vinda de uma página. */
  function resolve(account: Account): Account {
    return byId.value[account.id] ?? account
  }

  // ---- listagem paginada (tela de contas) ---------------------------------

  const list = usePagedList<Account, Record<string, never>>({
    filters: {},
    async fetchPage(params, signal) {
      const result = await accountsApi.list(params, signal)
      result.items.forEach(remember)
      return result
    },
  })

  /** Itens da página atual com o saldo mais recente conhecido. */
  const pageAccounts = computed(() => list.items.map(resolve))

  // ---- resumo global -------------------------------------------------------

  const summary = shallowRef<AccountSummary | null>(null)
  const summaryState = useRequestState()

  function fetchSummary(): Promise<void> {
    return summaryState.run(async () => {
      summary.value = await accountsApi.summary()
    })
  }

  function ensureSummary(): Promise<void> {
    return summaryState.hasLoaded.value ? Promise.resolve() : fetchSummary()
  }

  /** Saldo total por moeda, de todas as contas (sem conversão cambial). */
  const balanceByCurrency = computed(() => {
    const totals = new Map<CurrencyCode, number>()
    for (const item of summary.value?.balances ?? []) totals.set(item.currency, item.totalBalance)
    return totals
  })

  const totalBrlBalance = computed(() => balanceByCurrency.value.get('BRL') ?? 0)
  const totalCount = computed(() => summary.value?.totalAccounts ?? 0)
  const activeCount = computed(() => summary.value?.activeAccounts ?? 0)
  const inactiveCount = computed(() => summary.value?.inactiveAccounts ?? 0)

  // ---- diretório (seletores e nomes) -----------------------------------------

  const directoryIds = ref<string[]>([])
  const directoryTotal = ref(0)
  const directoryState = useRequestState()

  /** Até MAX_PAGE_SIZE contas, ordenadas pelo número. */
  const directory = computed(() =>
    directoryIds.value.map((id) => byId.value[id]).filter((a): a is Account => a !== undefined),
  )
  /** Existem mais contas do que o diretório comporta (seletores ficam incompletos). */
  const isDirectoryTruncated = computed(() => directoryTotal.value > directoryIds.value.length)

  function fetchDirectory(): Promise<void> {
    return directoryState.run(async () => {
      const result = await accountsApi.list({ page: 1, pageSize: MAX_PAGE_SIZE })
      result.items.forEach(remember)
      directoryIds.value = result.items.map((a) => a.id)
      directoryTotal.value = result.totalItems
    })
  }

  function ensureDirectory(): Promise<void> {
    return directoryState.hasLoaded.value ? Promise.resolve() : fetchDirectory()
  }

  // ---- consultas e mutações ------------------------------------------------

  /** GET /accounts/{id}: atualiza o cache. Rejeita com ApiError. */
  async function fetchAccount(id: string, signal?: AbortSignal): Promise<Account> {
    const account = await accountsApi.getById(id, signal)
    remember(account)
    return account
  }

  /** GET /accounts/{id}/balance: consulta o saldo atual e o reflete na conta. Rejeita com ApiError. */
  async function fetchBalance(id: string, signal?: AbortSignal) {
    const response = await accountsApi.getBalance(id, signal)
    const account = byId.value[response.accountId]
    if (account && account.balance !== response.balance) remember({ ...account, balance: response.balance })
    return response
  }

  /**
   * Atualiza o saldo das contas envolvidas numa transferência concluída (e o resumo,
   * que soma os saldos). Contas desconhecidas são ignoradas; falhas não interrompem as demais.
   */
  async function refreshBalancesByNumber(accountNumbers: string[]): Promise<void> {
    const ids = [...new Set(accountNumbers)]
      .map((number) => findByNumber(number)?.id)
      .filter((id): id is string => id !== undefined)
    await Promise.allSettled([...ids.map((id) => fetchBalance(id)), fetchSummary()])
  }

  /**
   * POST /accounts. Em seguida atualiza a listagem (mantendo a página), o resumo e o
   * diretório. Rejeita com ApiError (400 com erros por campo, 409 número duplicado).
   */
  async function createAccount(request: CreateAccountRequest): Promise<Account> {
    const account = await accountsApi.create(request)
    remember(account)
    void Promise.all([
      list.hasLoaded ? list.refresh({ force: true }) : undefined,
      summaryState.hasLoaded.value ? fetchSummary() : undefined,
      directoryState.hasLoaded.value ? fetchDirectory() : undefined,
    ])
    return account
  }

  return {
    byId,
    list,
    pageAccounts,
    summary,
    summaryState,
    balanceByCurrency,
    totalBrlBalance,
    totalCount,
    activeCount,
    inactiveCount,
    directory,
    directoryState,
    directoryTotal,
    isDirectoryTruncated,
    findById,
    findByNumber,
    fetchSummary,
    ensureSummary,
    fetchDirectory,
    ensureDirectory,
    fetchAccount,
    fetchBalance,
    refreshBalancesByNumber,
    createAccount,
  }
})
