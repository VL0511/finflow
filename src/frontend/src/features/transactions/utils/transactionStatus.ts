import type { BadgeTone } from '@/shared/components/ui/BaseBadge.vue'
import type { TransactionStatus } from '../types/transaction'

interface StatusMeta {
  label: string
  tone: BadgeTone
}

export const transactionStatusMeta: Record<TransactionStatus, StatusMeta> = {
  Pending: { label: 'Pendente', tone: 'warning' },
  Processing: { label: 'Processando', tone: 'info' },
  Completed: { label: 'Concluída', tone: 'positive' },
  Failed: { label: 'Falhou', tone: 'critical' },
  Cancelled: { label: 'Cancelada', tone: 'neutral' },
}

/** Mesma regra de Transaction.IsFinal no domínio. */
export function isFinalStatus(status: TransactionStatus): boolean {
  return status === 'Completed' || status === 'Failed' || status === 'Cancelled'
}

/** Filtros de status oferecidos na listagem (mapeados para o parâmetro `status` da API). */
export type StatusFilter = 'all' | 'in-flight' | 'Completed' | 'Failed' | 'Cancelled'

export const statusFilterStatuses: Record<StatusFilter, TransactionStatus[]> = {
  all: [],
  'in-flight': ['Pending', 'Processing'],
  Completed: ['Completed'],
  Failed: ['Failed'],
  Cancelled: ['Cancelled'],
}

/** Ordem do ciclo de vida: o status nunca volta (Pending → Processing → final). */
export function statusRank(status: TransactionStatus): number {
  return status === 'Pending' ? 0 : status === 'Processing' ? 1 : 2
}
