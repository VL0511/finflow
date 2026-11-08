<script setup lang="ts">
import { ArrowLeftRight, Landmark, ShieldCheck, Wallet } from '@lucide/vue'
import { computed } from 'vue'
import { useAccountsStore } from '@/features/accounts/stores/useAccountsStore'
import { useTransactionsStore } from '@/features/transactions/stores/useTransactionsStore'
import SkeletonBlock from '@/shared/components/ui/SkeletonBlock.vue'
import { formatCurrency, formatPercent } from '@/shared/utils/formatters'
import { useDashboardStore } from '../stores/useDashboardStore'
import KpiCard from './KpiCard.vue'

/** Indicadores globais: vêm dos endpoints de resumo, nunca de uma página de listagem. */
const dashboard = useDashboardStore()
const accounts = useAccountsStore()
const transactions = useTransactionsStore()

const plural = (n: number, one: string, many: string) => `${n} ${n === 1 ? one : many}`
const unavailable = 'Indisponível'

const balanceCaption = computed(() => {
  if (!accounts.summary) return unavailable
  const others = dashboard.otherCurrencyTotals.map(({ currency, total }) => formatCurrency(total, currency))
  const base = plural(dashboard.brlAccountsCount, 'conta em BRL', 'contas em BRL')
  return others.length ? `${base} · fora do total: ${others.join(', ')}` : base
})

const accountsCaption = computed(() => {
  if (!accounts.summary) return unavailable
  const inactive = accounts.inactiveCount
  return `${plural(accounts.activeCount, 'ativa', 'ativas')}${inactive ? ` · ${plural(inactive, 'inativa', 'inativas')}` : ''}`
})

const transactionsCaption = computed(() => {
  if (!transactions.summary) return unavailable
  const n = transactions.inFlightCount
  return n === 0 ? 'Nenhuma em andamento' : `${n} em andamento`
})

const successCaption = computed(() => {
  if (!transactions.summary) return unavailable
  const { Completed, Failed } = transactions.countByStatus
  return Completed + Failed === 0 ? 'Nenhuma processada ainda' : `${Completed} concluídas · ${Failed} com falha`
})

const value = (loaded: boolean, text: string) => (loaded ? text : '—')
</script>

<template>
  <div class="grid grid-cols-1 gap-4 sm:grid-cols-2 2xl:grid-cols-[1.4fr_1fr_1fr_1fr]">
    <template v-if="dashboard.isSummaryLoading">
      <div v-for="n in 4" :key="n" class="rounded-2xl border border-line bg-surface p-5 shadow-card" aria-hidden="true">
        <SkeletonBlock class="h-8 w-32" />
        <SkeletonBlock class="mt-5 h-7 w-40" />
        <SkeletonBlock class="mt-3 h-3 w-28" />
      </div>
      <span class="sr-only" role="status">Carregando indicadores…</span>
    </template>

    <template v-else>
      <KpiCard
        featured
        label="Saldo total (BRL)"
        :value="value(!!accounts.summary, formatCurrency(dashboard.totalBrlBalance))"
        :icon="Wallet"
        :caption="balanceCaption"
      />
      <KpiCard
        label="Contas"
        :value="value(!!accounts.summary, String(accounts.totalCount))"
        :icon="Landmark"
        :caption="accountsCaption"
      />
      <KpiCard
        label="Transações"
        :value="value(!!transactions.summary, String(transactions.totalCount))"
        :icon="ArrowLeftRight"
        :caption="transactionsCaption"
      />
      <KpiCard
        label="Taxa de sucesso"
        :value="transactions.successRate === null ? '—' : formatPercent(transactions.successRate)"
        :icon="ShieldCheck"
        :caption="successCaption"
      />
    </template>
  </div>
</template>
