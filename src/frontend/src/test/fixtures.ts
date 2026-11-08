import type { Account } from '@/features/accounts/types/account'
import type { Transaction } from '@/features/transactions/types/transaction'

/**
 * Fixtures de teste no formato EXATO devolvido pela API (capturado de GET /api/accounts
 * e GET /api/transactions em desenvolvimento). Usadas só em *.spec.ts — nunca na aplicação.
 */
export function makeAccount(overrides: Partial<Account> = {}): Account {
  return {
    id: '11111111-1111-1111-1111-111111111111',
    accountNumber: '12345',
    holderName: 'Ana Souza',
    balance: 1000,
    currency: 'BRL',
    isActive: true,
    createdAt: '2026-11-01T00:00:00+00:00',
    ...overrides,
  }
}

export const ana = makeAccount()
export const bruno = makeAccount({
  id: '22222222-2222-2222-2222-222222222222',
  accountNumber: '67890',
  holderName: 'Bruno Lima',
  balance: 250,
})
export const carla = makeAccount({
  id: '33333333-3333-3333-3333-333333333333',
  accountNumber: '11223',
  holderName: 'Carla Mendes',
  balance: 5000,
  currency: 'USD',
})
export const diego = makeAccount({
  id: '44444444-4444-4444-4444-444444444444',
  accountNumber: '44556',
  holderName: 'Diego Rocha',
  balance: 300,
  isActive: false,
})

export function makeTransaction(overrides: Partial<Transaction> = {}): Transaction {
  return {
    id: '66ee6acc-dd9b-48c3-92cd-35f87d03d369',
    sourceAccount: '12345',
    destinationAccount: '67890',
    amount: 10,
    currency: 'BRL',
    destinationCurrency: 'BRL',
    destinationAmount: 10,
    exchangeRate: 1,
    exchangeRateId: null,
    status: 'Pending',
    failureReason: null,
    createdAt: '2026-11-06T01:03:04.4455973+00:00',
    updatedAt: null,
    ...overrides,
  }
}

/** Resposta paginada no formato da API (PagedResult), calculando os metadados. */
export function paged<T>(items: T[], options: { page?: number; pageSize?: number; totalItems?: number } = {}) {
  const page = options.page ?? 1
  const pageSize = options.pageSize ?? 10
  const totalItems = options.totalItems ?? items.length
  const totalPages = totalItems === 0 ? 0 : Math.ceil(totalItems / pageSize)
  return { items, page, pageSize, totalItems, totalPages, hasPreviousPage: page > 1, hasNextPage: page < totalPages }
}

/** Fatia uma coleção como o backend faria (para mocks de fetchPage). */
export function pageOf<T>(all: T[], page: number, pageSize: number) {
  return paged(all.slice((page - 1) * pageSize, page * pageSize), { page, pageSize, totalItems: all.length })
}
