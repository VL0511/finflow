<script setup lang="ts">
import { computed } from 'vue'
import TransactionsTable from '@/features/transactions/components/TransactionsTable.vue'
import TransactionsTableSkeleton from '@/features/transactions/components/TransactionsTableSkeleton.vue'
import { useTransactionsPage } from '@/features/transactions/composables/useTransactionsPage'
import BaseCard from '@/shared/components/ui/BaseCard.vue'
import ErrorState from '@/shared/components/ui/ErrorState.vue'
import PaginationBar from '@/shared/components/ui/PaginationBar.vue'
import { useAccountsStore } from '../stores/useAccountsStore'
import type { Account } from '../types/account'

const props = defineProps<{ account: Account }>()

/**
 * Histórico COMPLETO da conta, paginado no servidor (GET /api/transactions?accountNumber=…).
 * O total exibido é o da API (todas as páginas), não o da página atual.
 */
const { page, views } = useTransactionsPage(
  { accountNumber: props.account.accountNumber },
  { perspective: props.account.accountNumber },
)
void page.load()
void useAccountsStore().ensureDirectory() // nomes das contrapartes

const description = computed(() =>
  page.hasLoaded
    ? `${page.totalItems} ${page.totalItems === 1 ? 'transferência' : 'transferências'} enviadas e recebidas`
    : `Transferências enviadas e recebidas pela conta ${props.account.accountNumber}`,
)
</script>

<template>
  <BaseCard title="Movimentações da conta" :description="description" flush>
    <TransactionsTableSkeleton v-if="page.isInitialLoading" :rows="3" />
    <ErrorState
      v-else-if="page.error && !page.hasLoaded"
      compact
      :error="page.error"
      :retrying="page.isLoading"
      @retry="page.refresh({ force: true })"
    />
    <template v-else>
      <div :class="page.isRefreshing && 'opacity-60 transition-opacity'" :aria-busy="page.isLoading || undefined">
        <TransactionsTable
          :transactions="views"
          empty-title="Nenhuma movimentação"
          empty-description="Esta conta ainda não enviou nem recebeu transferências."
        />
      </div>
      <PaginationBar
        v-if="!page.isEmpty"
        id="account-transactions-pagination"
        item-label="transferências"
        :page="page.page"
        :page-size="page.pageSize"
        :total-items="page.totalItems"
        :total-pages="page.totalPages"
        :has-previous-page="page.hasPreviousPage"
        :has-next-page="page.hasNextPage"
        :range-start="page.range.start"
        :range-end="page.range.end"
        :loading="page.isLoading"
        @update:page="page.setPage"
        @update:page-size="page.setPageSize"
      />
    </template>
  </BaseCard>
</template>
