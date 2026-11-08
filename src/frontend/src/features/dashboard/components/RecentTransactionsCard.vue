<script setup lang="ts">
import { ChevronRight } from '@lucide/vue'
import TransactionsTable from '@/features/transactions/components/TransactionsTable.vue'
import TransactionsTableSkeleton from '@/features/transactions/components/TransactionsTableSkeleton.vue'
import type { Transaction, TransactionView } from '@/features/transactions/types/transaction'
import BaseCard from '@/shared/components/ui/BaseCard.vue'
import ErrorState from '@/shared/components/ui/ErrorState.vue'
import type { PagedList } from '@/shared/composables/usePagedList'

/** Primeira página do histórico (GET /api/transactions?page=1&pageSize=N), carregada pela view. */
defineProps<{ page: PagedList<Transaction, object>; views: TransactionView[] }>()
</script>

<template>
  <BaseCard title="Transações recentes" description="Últimas transferências solicitadas" flush>
    <template #actions>
      <RouterLink
        to="/transacoes"
        class="inline-flex items-center gap-0.5 text-[13px] font-medium text-ink-secondary hover:text-ink"
      >
        Ver histórico<template v-if="page.hasLoaded"> ({{ page.totalItems }})</template>
        <ChevronRight class="size-4" aria-hidden="true" />
      </RouterLink>
    </template>

    <TransactionsTableSkeleton v-if="page.isInitialLoading" :rows="4" />
    <ErrorState
      v-else-if="page.error && !page.hasLoaded"
      compact
      :error="page.error"
      :retrying="page.isLoading"
      @retry="page.refresh({ force: true })"
    />
    <TransactionsTable
      v-else
      :transactions="views"
      empty-title="Nenhuma transação ainda"
      empty-description="As transferências solicitadas aparecerão aqui."
    />
  </BaseCard>
</template>
