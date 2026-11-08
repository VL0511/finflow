import type { CurrencyCode } from '@/shared/utils/formatters'

/** Espelha FinFlow.Domain.Enums.TransactionStatus (a API serializa como string). */
export type TransactionStatus = 'Pending' | 'Processing' | 'Completed' | 'Failed' | 'Cancelled'

/** Espelha TransactionResponse. */
export interface Transaction {
  id: string
  sourceAccount: string
  destinationAccount: string
  /** Valor debitado da origem, na moeda da origem. */
  amount: number
  /** Moeda da origem. */
  currency: CurrencyCode
  /** Moeda do destino (igual a `currency` quando não há câmbio). */
  destinationCurrency: CurrencyCode
  /** Valor creditado no destino: snapshot fixado na criação, não muda se a taxa mudar. */
  destinationAmount: number
  /** Taxa aplicada na criação (1 na mesma moeda). */
  exchangeRate: number
  exchangeRateId: string | null
  /** Quem solicitou (nulo em transações anteriores à autenticação). */
  requestedByUserId?: string | null
  status: TransactionStatus
  failureReason: string | null
  createdAt: string
  updatedAt: string | null
}

/** Espelha TransactionStatusResponse (GET /api/transactions/{id}/status). */
export interface TransactionStatusResponse {
  id: string
  status: TransactionStatus
  failureReason: string | null
  /** Última mudança de status (a API usa createdAt quando ainda não houve mudança). */
  updatedAt: string | null
}

/** Espelha CreateTransactionRequest (POST /api/transactions). */
export interface CreateTransactionRequest {
  /** Número da conta de origem. */
  sourceAccount: string
  /** Número da conta de destino; deve ser diferente da origem. */
  destinationAccount: string
  /** Maior que zero, até 2 casas decimais. */
  amount: number
}

/**
 * Sentido da transferência:
 * - `inbound` / `outbound`: do ponto de vista de uma conta específica (ex.: detalhe da conta);
 * - `transfer`: visão geral, sem conta de referência (origem → destino).
 */
export type TransactionDirection = 'inbound' | 'outbound' | 'transfer'

/** Conta de uma ponta da transferência, como a UI a exibe. */
export interface TransactionParty {
  accountNumber: string
  /** Titular, se a conta for conhecida; senão "Conta 12345". */
  name: string
  accountId: string | null
}

/**
 * Modelo de apresentação: a transação da API enriquecida com o que a UI
 * precisa (nomes dos titulares, sentido). Derivado — nunca enviado à API.
 */
export interface TransactionView extends Transaction {
  direction: TransactionDirection
  source: TransactionParty
  destination: TransactionParty
}

/** Espelha TransactionSummaryResponse (GET /api/transactions/summary) — todas as transações. */
export interface TransactionSummary {
  totalTransactions: number
  pending: number
  processing: number
  completed: number
  failed: number
  cancelled: number
}

/** Filtros de GET /api/transactions (aplicados no banco, antes da paginação). */
export interface TransactionListFilters {
  /** Vazio = todos os status. */
  status?: TransactionStatus[]
  /** Conta como origem OU destino. */
  accountNumber?: string
}

/** Espelha TransferPreviewResponse (POST /api/transactions/preview) — calculado no backend. */
export interface TransferPreview {
  sourceCurrency: CurrencyCode
  destinationCurrency: CurrencyCode
  amount: number
  /** Valor estimado a receber (a criação recalcula com a taxa vigente no momento). */
  destinationAmount: number
  exchangeRate: number
  exchangeRateId: string | null
  isConversion: boolean
  /** Titular do destino mascarado (ex.: "Bruno S."), para conferência. */
  destinationHolder: string
}
