<script setup lang="ts">
import { Landmark, Plus, RefreshCw } from '@lucide/vue'
import PageHeader from '@/shared/components/PageHeader.vue'
import BaseButton from '@/shared/components/ui/BaseButton.vue'
import BaseCard from '@/shared/components/ui/BaseCard.vue'
import EmptyState from '@/shared/components/ui/EmptyState.vue'
import ErrorState from '@/shared/components/ui/ErrorState.vue'
import PaginationBar from '@/shared/components/ui/PaginationBar.vue'
import AccountCard from '../components/AccountCard.vue'
import AccountCardSkeleton from '../components/AccountCardSkeleton.vue'
import CurrencyTotals from '../components/CurrencyTotals.vue'
import { useAuthStore } from '@/features/auth/stores/useAuthStore'
import { useAccountsStore } from '../stores/useAccountsStore'

const store = useAccountsStore()
// Treasurer consulta todas as contas mas não abre contas (a API também recusa: 403).
const canOpen = useAuthStore().hasCapability('openAccount')
const list = store.list

// Recarrega a página atual (saldos mudam com o Worker), preservando página e tamanho.
void list.refresh()
void store.fetchSummary()

function refresh() {
  void list.refresh({ force: true })
  void store.fetchSummary()
}
</script>

<template>
  <div class="space-y-6">
    <PageHeader title="Contas" description="Contas cadastradas na API e seus saldos atuais.">
      <template #actions>
        <CurrencyTotals v-if="store.summary?.balances.length" />
        <BaseButton :icon="RefreshCw" :loading="list.isRefreshing" :disabled="list.isLoading" @click="refresh">
          Atualizar
        </BaseButton>
        <BaseButton v-if="canOpen" variant="primary" :icon="Plus" :to="{ name: 'account-create' }">Nova conta</BaseButton>
      </template>
    </PageHeader>

    <div v-if="list.isInitialLoading" class="grid grid-cols-1 gap-4 sm:grid-cols-2 xl:grid-cols-4" aria-busy="true">
      <AccountCardSkeleton v-for="n in 4" :key="n" />
      <span class="sr-only" role="status">Carregando contas…</span>
    </div>

    <BaseCard v-else-if="list.error && !list.hasLoaded">
      <ErrorState :error="list.error" title="Não foi possível carregar as contas." :retrying="list.isLoading" @retry="refresh" />
    </BaseCard>

    <BaseCard v-else-if="list.isEmpty">
      <EmptyState :icon="Landmark" title="Nenhuma conta cadastrada" description="Crie a primeira conta para começar a transferir.">
        <BaseButton v-if="canOpen" variant="primary" :icon="Plus" :to="{ name: 'account-create' }">Nova conta</BaseButton>
      </EmptyState>
    </BaseCard>

    <template v-else>
      <!-- Falha ao recarregar: mantém os dados anteriores e avisa. -->
      <ErrorState
        v-if="list.error"
        compact
        class="rounded-2xl border border-line bg-surface"
        :error="list.error"
        title="Não foi possível atualizar a lista. Exibindo os últimos dados carregados."
        :retrying="list.isLoading"
        @retry="refresh"
      />
      <div
        class="grid grid-cols-1 gap-4 sm:grid-cols-2 xl:grid-cols-4"
        :class="list.isRefreshing && 'opacity-60 transition-opacity'"
        :aria-busy="list.isLoading || undefined"
      >
        <AccountCard v-for="account in store.pageAccounts" :key="account.id" :account="account" />
      </div>
      <div class="overflow-hidden rounded-2xl border border-line bg-surface shadow-card [&>nav]:border-t-0">
        <PaginationBar
          id="accounts-pagination"
          item-label="contas"
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
      </div>
    </template>
  </div>
</template>
