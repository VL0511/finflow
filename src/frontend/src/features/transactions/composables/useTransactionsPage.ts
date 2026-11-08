import { computed } from 'vue'
import { usePagedList } from '@/shared/composables/usePagedList'
import { useTransactionsStore } from '../stores/useTransactionsStore'
import type { Transaction, TransactionListFilters } from '../types/transaction'

/**
 * Lista paginada de transações com estado LOCAL ao componente (ex.: movimentações de
 * uma conta, transações recentes do dashboard). Os itens passam pelo cache do store,
 * então mudanças de status vistas em outro lugar aparecem aqui também.
 *
 * @param perspective número da conta de referência para calcular entrada/saída.
 */
export function useTransactionsPage(filters: TransactionListFilters, options: { pageSize?: number; perspective?: string } = {}) {
  const store = useTransactionsStore()

  const page = usePagedList<Transaction, TransactionListFilters>({
    filters,
    pageSize: options.pageSize,
    fetchPage: (params, signal) => store.fetchPage(params, signal),
  })

  const views = computed(() => page.items.map((t) => store.toView(t, options.perspective)))

  return { page, views }
}
