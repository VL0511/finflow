<script setup lang="ts">
import { Plus, RefreshCw } from '@lucide/vue'
import { computed } from 'vue'
import { useAccountsStore } from '@/features/accounts/stores/useAccountsStore'
import PageHeader from '@/shared/components/PageHeader.vue'
import BaseButton from '@/shared/components/ui/BaseButton.vue'
import BaseCard from '@/shared/components/ui/BaseCard.vue'
import ErrorState from '@/shared/components/ui/ErrorState.vue'
import PaginationBar from '@/shared/components/ui/PaginationBar.vue'
import SegmentedControl, { type SegmentedOption } from '@/shared/components/ui/SegmentedControl.vue'
import AutoRefreshIndicator from '../components/AutoRefreshIndicator.vue'
import TransactionsTable from '../components/TransactionsTable.vue'
import TransactionsTableSkeleton from '../components/TransactionsTableSkeleton.vue'
import { useInFlightAutoRefresh } from '../composables/useInFlightAutoRefresh'
import { useTransactionsStore } from '../stores/useTransactionsStore'
import type { StatusFilter } from '../utils/transactionStatus'

const store = useTransactionsStore()
const accounts = useAccountsStore()
const list = store.list

// Recarrega a página atual, preservando página, tamanho e filtro (estado do store).
void list.refresh()
void store.fetchSummary()
void accounts.ensureDirectory() // nomes dos titulares

const autoRefresh = useInFlightAutoRefresh(() => list.refresh())

function refresh() {
  void list.refresh({ force: true })
  void store.fetchSummary()
  autoRefresh.restart()
}

/** Filtro de status aplicado no servidor; mudar o filtro volta para a página 1. */
const filter = computed<StatusFilter>({
  get: () => list.filters.statusFilter,
  set: (statusFilter) => void list.setFilters({ statusFilter }),
})

/** Contagens do resumo global (todas as transações), não da página exibida. */
const options = computed<SegmentedOption<StatusFilter>[]>(() => {
  const c = store.countByStatus
  const n = (value: number) => (store.summary ? ` · ${value}` : '')
  return [
    { value: 'all', label: `Todas${n(store.totalCount)}` },
    { value: 'in-flight', label: `Em andamento${n(store.inFlightCount)}` },
    { value: 'Completed', label: `Concluídas${n(c.Completed)}` },
    { value: 'Failed', label: `Falhas${n(c.Failed)}` },
    { value: 'Cancelled', label: `Canceladas${n(c.Cancelled)}` },
  ]
})

const hasNoTransactionsAtAll = computed(() => list.isEmpty && filter.value === 'all')
</script>

<template>
  <div class="space-y-6">
    <PageHeader
      title="Transações"
      description="Transferências entre contas e o estado de cada uma no fluxo de processamento."
    >
      <template #actions>
        <BaseButton :icon="RefreshCw" :loading="list.isRefreshing" :disabled="list.isLoading" @click="refresh">
          Atualizar
        </BaseButton>
        <BaseButton variant="primary" :icon="Plus" :to="{ name: 'transaction-create' }">Nova transferência</BaseButton>
      </template>
    </PageHeader>

    <BaseCard flush>
      <template #header>
        <div>
          <h2 class="text-[15px] font-semibold tracking-tight text-ink">Histórico</h2>
          <p class="mt-0.5 text-[13px] text-ink-muted">Mais recentes primeiro</p>
          <AutoRefreshIndicator class="mt-1" :state="autoRefresh.state.value" :in-flight="store.inFlightCount" @restart="refresh" />
        </div>
      </template>
      <template #actions>
        <SegmentedControl v-model="filter" :options="options" label="Filtrar por status" />
      </template>

      <TransactionsTableSkeleton v-if="list.isInitialLoading" :rows="list.pageSize > 10 ? 10 : list.pageSize" />

      <ErrorState
        v-else-if="list.error && !list.hasLoaded"
        :error="list.error"
        title="Não foi possível carregar as transações."
        :retrying="list.isLoading"
        @retry="refresh"
      />

      <template v-else>
        <ErrorState
          v-if="list.error"
          compact
          class="border-t border-line"
          :error="list.error"
          title="Não foi possível atualizar. Exibindo os últimos dados carregados."
          :retrying="list.isLoading"
          @retry="refresh"
        />
        <div :class="list.isRefreshing && 'opacity-60 transition-opacity'" :aria-busy="list.isLoading || undefined">
          <TransactionsTable
            :transactions="store.listViews"
            :empty-title="hasNoTransactionsAtAll ? 'Nenhuma transação ainda' : undefined"
            :empty-description="hasNoTransactionsAtAll ? 'As transferências solicitadas aparecerão aqui.' : undefined"
          >
            <template v-if="hasNoTransactionsAtAll" #empty-action>
              <BaseButton variant="primary" :icon="Plus" :to="{ name: 'transaction-create' }">Nova transferência</BaseButton>
            </template>
          </TransactionsTable>
        </div>
        <PaginationBar
          v-if="!list.isEmpty"
          id="transactions-pagination"
          item-label="transações"
          :page="list.page"
          :page-size="list.pageSize"
          :total-items="list.totalItems"
          :total-pages="list.totalPages"
          :has-previous-page="list.hasPreviousPage"
          :has-next-page="list.hasNextPage"
          :range-start="list.range.start"
          :range-end="list.range.end"
          :loading="list.isLoading"
          @update:page="list.setPage"
          @update:page-size="list.setPageSize"
        />
      </template>
    </BaseCard>
  </div>
</template>
