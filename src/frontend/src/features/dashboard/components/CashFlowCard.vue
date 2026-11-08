<script setup lang="ts">
import { Landmark } from '@lucide/vue'
import { storeToRefs } from 'pinia'
import { computed } from 'vue'
import { useAccountsStore } from '@/features/accounts/stores/useAccountsStore'
import BaseCard from '@/shared/components/ui/BaseCard.vue'
import EmptyState from '@/shared/components/ui/EmptyState.vue'
import ErrorState from '@/shared/components/ui/ErrorState.vue'
import SegmentedControl, { type SegmentedOption } from '@/shared/components/ui/SegmentedControl.vue'
import SkeletonBlock from '@/shared/components/ui/SkeletonBlock.vue'
import { useMediaQuery } from '@/shared/composables/useMediaQuery'
import { formatCompactCurrency } from '@/shared/utils/formatters'
import { useDashboardStore } from '../stores/useDashboardStore'
import type { CashFlowPeriod } from '../types/dashboard'
import CashFlowChart from './CashFlowChart.vue'

const dashboard = useDashboardStore()
const accounts = useAccountsStore()
const { period, chartPoints, periodTotals, chartCurrency } = storeToRefs(dashboard)
const isSmall = useMediaQuery('(max-width: 639px)')

const periodOptions: SegmentedOption<CashFlowPeriod>[] = [
  { value: '7d', label: '7 dias' },
  { value: '30d', label: '30 dias' },
  { value: '12m', label: '12 meses' },
]

const selected = computed({
  get: () => dashboard.selectedAccount?.accountNumber ?? '',
  set: (value: string) => dashboard.selectAccount(value),
})

const money = (value: number) => formatCompactCurrency(value, chartCurrency.value, 1)
const signed = (value: number) => `${value >= 0 ? '+' : '−'}${money(Math.abs(value))}`
</script>

<template>
  <BaseCard title="Movimentações" description="Entradas e saídas liquidadas (transferências concluídas) da conta">
    <template #actions>
      <label v-if="accounts.directory.length" class="sr-only" for="cashflow-account">Conta</label>
      <select
        v-if="accounts.directory.length"
        id="cashflow-account"
        v-model="selected"
        class="field-control h-8 w-auto max-w-[14rem] py-0 text-[13px]"
      >
        <option v-for="account in accounts.directory" :key="account.id" :value="account.accountNumber">
          {{ account.holderName }} · {{ account.accountNumber }}
        </option>
      </select>
      <SegmentedControl v-model="period" :options="periodOptions" label="Período do gráfico" />
    </template>

    <div v-if="accounts.directoryState.isInitialLoading || dashboard.isCashFlowInitialLoading" aria-hidden="true">
      <div class="mb-5 grid grid-cols-3 gap-3 sm:max-w-md">
        <SkeletonBlock v-for="n in 3" :key="n" class="h-10" />
      </div>
      <SkeletonBlock :class="isSmall ? 'h-[220px]' : 'h-[272px]'" />
    </div>

    <ErrorState
      v-else-if="dashboard.cashFlowStatus === 'error'"
      compact
      :error="dashboard.cashFlowError"
      title="Não foi possível carregar as movimentações."
      @retry="dashboard.loadCashFlow()"
    />

    <EmptyState
      v-else-if="!dashboard.selectedAccount"
      :icon="Landmark"
      title="Nenhuma conta cadastrada"
      description="As movimentações aparecem aqui quando houver contas e transferências concluídas."
    />

    <template v-else>
      <!-- Legenda + totais do período: identidade da série nunca depende só da cor -->
      <dl class="mb-5 grid grid-cols-3 gap-3 sm:max-w-md">
        <div>
          <dt class="flex items-center gap-1.5 text-xs text-ink-muted">
            <span class="size-2.5 rounded-[3px] bg-inflow" aria-hidden="true" />Entradas
          </dt>
          <dd class="mt-1 text-base font-semibold text-ink tabular sm:text-lg">{{ money(periodTotals.inflow) }}</dd>
        </div>
        <div>
          <dt class="flex items-center gap-1.5 text-xs text-ink-muted">
            <span class="size-2.5 rounded-[3px] bg-outflow" aria-hidden="true" />Saídas
          </dt>
          <dd class="mt-1 text-base font-semibold text-ink tabular sm:text-lg">{{ money(periodTotals.outflow) }}</dd>
        </div>
        <div>
          <dt class="text-xs text-ink-muted">Resultado líquido</dt>
          <dd
            class="mt-1 text-base font-semibold tabular sm:text-lg"
            :class="periodTotals.net >= 0 ? 'text-positive' : 'text-critical'"
          >
            {{ signed(periodTotals.net) }}
          </dd>
        </div>
      </dl>

      <CashFlowChart :points="chartPoints" :currency="chartCurrency" :height="isSmall ? 220 : 272" />

      <p v-if="!dashboard.hasMovementInPeriod" class="mt-3 text-center text-xs text-ink-muted">
        Nenhuma transferência concluída desta conta no período.
      </p>
    </template>
  </BaseCard>
</template>
