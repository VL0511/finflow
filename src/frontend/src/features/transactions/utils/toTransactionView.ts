import type { Account } from '@/features/accounts/types/account'
import type { Transaction, TransactionDirection, TransactionParty, TransactionView } from '../types/transaction'

interface ViewContext {
  findAccount: (accountNumber: string) => Account | undefined
  /** Número da conta de referência. Sem ela, o sentido é sempre `transfer`. */
  perspective?: string
}

function toParty(accountNumber: string, context: ViewContext): TransactionParty {
  const account = context.findAccount(accountNumber)
  return {
    accountNumber,
    name: account?.holderName ?? `Conta ${accountNumber}`,
    accountId: account?.id ?? null,
  }
}

function directionOf(transaction: Transaction, perspective?: string): TransactionDirection {
  if (!perspective) return 'transfer'
  if (transaction.destinationAccount === perspective) return 'inbound'
  if (transaction.sourceAccount === perspective) return 'outbound'
  return 'transfer'
}

/** Converte a transação da API no modelo de apresentação da UI. */
export function toTransactionView(transaction: Transaction, context: ViewContext): TransactionView {
  return {
    ...transaction,
    direction: directionOf(transaction, context.perspective),
    source: toParty(transaction.sourceAccount, context),
    destination: toParty(transaction.destinationAccount, context),
  }
}

/** Mais recentes primeiro (comparação por instante, não por texto: os offsets/precisões variam). */
export function byCreatedAtDesc(a: Transaction, b: Transaction): number {
  return Date.parse(b.createdAt) - Date.parse(a.createdAt)
}
