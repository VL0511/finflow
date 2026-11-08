<script setup lang="ts">
import { Plus, RefreshCw } from '@lucide/vue'
import { computed } from 'vue'
import { useAccountsStore } from '@/features/accounts/stores/useAccountsStore'
import AutoRefreshIndicator from '@/features/transactions/components/AutoRefreshIndicator.vue'
import { useInFlightAutoRefresh } from '@/features/transactions/composables/useInFlightAutoRefresh'
import { useTransactionsPage } from '@/features/transactions/composables/useTransactionsPage'
import { useTransactionsStore } from '@/features/transactions/stores/useTransactionsStore'
import PageHeader from '@/shared/components/PageHeader.vue'
import BaseButton from '@/shared/components/ui/BaseButton.vue'
import ErrorState from '@/shared/components/ui/ErrorState.vue'
import AccountsOverviewCard from '../components/AccountsOverviewCard.vue'
import CashFlowCard from '../components/CashFlowCard.vue'
import KpiGrid from '../components/KpiGrid.vue'
import RecentTransactionsCard from '../components/RecentTransactionsCard.vue'
import { useDashboardStore } from '../stores/useDashboardStore'

const today = new Intl.DateTimeFormat('pt-BR', { weekday: 'long', day: 'numeric', month: 'long' }).format(new Date())

const dashboard = useDashboardStore()
const accounts = useAccountsStore()
const transactions = useTransactionsStore()

/** Últimas transferências: só a primeira página (7 itens) — o total vem da API. */
const recent = useTransactionsPage({}, { pageSize: 7 })

// Dados frescos a cada visita (os saldos mudam conforme o Worker processa).
void dashboard.refresh()
void recent.page.load()

const autoRefresh = useInFlightAutoRefresh(() =>
  Promise.all([recent.page.refresh(), dashboard.loadCashFlow(), accounts.fetchSummary()]),
)

function refresh() {
  void dashboard.refresh()
  void recent.page.refresh({ force: true })
  autoRefresh.restart()
}

const isRefreshing = computed(
  () =>
    accounts.summaryState.isLoading ||
    transactions.summaryState.isLoading ||
    accounts.directoryState.isLoading ||
    recent.page.isLoading,
)

const sources = computed(() => [accounts.summaryState, transactions.summaryState, accounts.directoryState])

/** Nenhuma fonte respondeu: nada real para mostrar. */
const fatalError = computed(() =>
  sources.value.every((s) => !s.hasLoaded) ? (sources.value.find((s) => s.error)?.error ?? null) : null,
)
/** Falha de uma fonte com outras já carregadas: mantém a tela e avisa. */
const staleError = computed(() => (fatalError.value ? null : (sources.value.find((s) => s.error)?.error ?? null)))
</script>

<template>
  <!-- A view só compõe seções; cada seção lê os próprios dados dos stores. -->
  <div class="space-y-6">
    <PageHeader
      :eyebrow="today"
      title="Visão geral"
      description="Saldo, movimentações e o andamento das suas transferências."
    >
      <template #actions>
        <BaseButton :icon="RefreshCw" :loading="isRefreshing && !dashboard.isSummaryLoading" :disabled="isRefreshing" @click="refresh">
          Atualizar
        </BaseButton>
        <BaseButton variant="primary" :icon="Plus" :to="{ name: 'transaction-create' }">Nova transferência</BaseButton>
      </template>
    </PageHeader>

    <div v-if="fatalError" class="rounded-2xl border border-line bg-surface shadow-card">
      <ErrorState :error="fatalError" title="Não foi possível carregar o dashboard." :retrying="isRefreshing" @retry="refresh" />
    </div>

    <template v-else>
      <ErrorState
        v-if="staleError"
        compact
        class="rounded-2xl border border-line bg-surface"
        :error="staleError"
        title="Parte dos dados não pôde ser atualizada. Exibindo os últimos dados carregados."
        :retrying="isRefreshing"
        @retry="refresh"
      />

      <AutoRefreshIndicator :state="autoRefresh.state.value" :in-flight="transactions.inFlightCount" @restart="refresh" />

      <KpiGrid />

      <div class="grid grid-cols-1 gap-4 xl:grid-cols-3">
        <CashFlowCard class="xl:col-span-2" />
        <AccountsOverviewCard />
      </div>

      <RecentTransactionsCard :page="recent.page" :views="recent.views.value" />
    </template>
  </div>
</template>
