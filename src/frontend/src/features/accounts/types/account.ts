import type { CurrencyCode } from '@/shared/utils/formatters'

/** Espelha FinFlow.Application.Accounts.AccountResponse. */
export interface Account {
  id: string
  /** Gerado pelo backend (10 dígitos com verificador); nunca escolhido pelo usuário. */
  accountNumber: string
  holderName: string
  balance: number
  currency: CurrencyCode
  isActive: boolean
  createdAt: string
  /** Dono (Customer); nulo em contas institucionais ou anteriores à autenticação. */
  ownerUserId?: string | null
}

/** Espelha AccountBalanceResponse (GET /api/accounts/{id}/balance). */
export interface AccountBalanceResponse {
  accountId: string
  accountNumber: string
  balance: number
  currency: CurrencyCode
}

/** Espelha CreateAccountRequest (POST /api/accounts). O número da conta é gerado pelo backend. */
export interface CreateAccountRequest {
  /** Máx. 100 caracteres. */
  holderName: string
  /** Não pode ser negativo; casas decimais conforme a moeda. */
  initialBalance: number
  /** Moeda ativa (GET /api/currencies). */
  currency: CurrencyCode
  /** Padrão no backend: true. */
  isActive: boolean
  /** Só o Administrator escolhe o dono; para o Customer é sempre ele mesmo. */
  ownerUserId?: string | null
}

/** Espelha AccountCurrencySummary. */
export interface AccountCurrencySummary {
  currency: CurrencyCode
  accounts: number
  activeAccounts: number
  /** Soma dos saldos (contas ativas e inativas) nesta moeda. */
  totalBalance: number
}

/** Espelha AccountSummaryResponse (GET /api/accounts/summary) — calculado no banco, sobre todas as contas. */
export interface AccountSummary {
  totalAccounts: number
  activeAccounts: number
  inactiveAccounts: number
  balances: AccountCurrencySummary[]
}

/** Espelha DailyCashFlow. Data no formato YYYY-MM-DD (fuso pedido). */
export interface AccountDailyCashFlow {
  date: string
  inflow: number
  outflow: number
}

/** Espelha AccountCashFlowResponse (GET /api/accounts/{id}/cash-flow). */
export interface AccountCashFlow {
  accountId: string
  accountNumber: string
  currency: CurrencyCode
  from: string
  to: string
  utcOffsetMinutes: number
  /** Só os dias com movimento, em ordem crescente. */
  days: AccountDailyCashFlow[]
}

export interface AccountCashFlowQuery {
  /** YYYY-MM-DD, inclusivo. */
  from: string
  /** YYYY-MM-DD, inclusivo. */
  to: string
  /** Deslocamento do fuso em minutos (ex.: -180). */
  utcOffsetMinutes: number
}
