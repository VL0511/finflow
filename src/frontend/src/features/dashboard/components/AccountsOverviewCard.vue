<script setup lang="ts">
import { ChevronRight, Landmark } from '@lucide/vue'
import { computed } from 'vue'
import { useAccountsStore } from '@/features/accounts/stores/useAccountsStore'
import BaseBadge from '@/shared/components/ui/BaseBadge.vue'
import BaseCard from '@/shared/components/ui/BaseCard.vue'
import EmptyState from '@/shared/components/ui/EmptyState.vue'
import SkeletonBlock from '@/shared/components/ui/SkeletonBlock.vue'
import { formatCurrency, formatPercent } from '@/shared/utils/formatters'

const accounts = useAccountsStore()

/** Quantas contas listar no card (o restante fica na página de contas). */
const MAX_ROWS = 6

/** Participação de cada conta BRL no saldo total em BRL (total do resumo: todas as contas). */
const rows = computed(() =>
  accounts.directory.slice(0, MAX_ROWS).map((account) => {
    const share =
      account.currency === 'BRL' && accounts.totalBrlBalance > 0 ? account.balance / accounts.totalBrlBalance : null
    return { account, share }
  }),
)
</script>

<template>
  <BaseCard title="Contas" description="Saldo atual por conta">
    <template #actions>
      <RouterLink
        to="/contas"
        class="inline-flex items-center gap-0.5 text-[13px] font-medium text-ink-secondary hover:text-ink"
      >
        Ver todas <ChevronRight class="size-4" aria-hidden="true" />
      </RouterLink>
    </template>

    <ul v-if="accounts.directoryState.isInitialLoading" class="space-y-5" aria-hidden="true">
      <li v-for="n in 4" :key="n" class="space-y-2">
        <div class="flex justify-between"><SkeletonBlock class="h-4 w-28" /><SkeletonBlock class="h-4 w-20" /></div>
        <SkeletonBlock class="h-1.5" />
      </li>
    </ul>

    <EmptyState
      v-else-if="!accounts.directory.length"
      :icon="Landmark"
      :title="accounts.directoryState.error ? 'Contas indisponíveis' : 'Nenhuma conta cadastrada'"
      :description="accounts.directoryState.error?.title"
    />

    <ul v-else class="space-y-4">
      <li v-for="{ account, share } in rows" :key="account.id">
        <RouterLink :to="{ name: 'account-details', params: { id: account.id } }" class="group block">
          <div class="flex items-center justify-between gap-3">
            <div class="min-w-0">
              <p class="flex min-w-0 items-center gap-2 text-sm font-medium text-ink">
                <span class="truncate group-hover:underline">{{ account.holderName }}</span>
                <BaseBadge v-if="!account.isActive" tone="neutral">Inativa</BaseBadge>
              </p>
              <p class="text-xs text-ink-muted tabular">Conta {{ account.accountNumber }} · {{ account.currency }}</p>
            </div>
            <div class="shrink-0 text-right">
              <p class="text-sm font-semibold tabular" :class="account.isActive ? 'text-ink' : 'text-ink-muted'">
                {{ formatCurrency(account.balance, account.currency) }}
              </p>
              <p v-if="share !== null" class="text-xs text-ink-muted tabular">{{ formatPercent(share) }} do total</p>
            </div>
          </div>
          <div v-if="share !== null" class="mt-2 h-1.5 overflow-hidden rounded-full bg-neutral-soft" role="presentation">
            <div class="h-full rounded-full bg-brand-600" :style="{ width: `${share * 100}%` }" />
          </div>
        </RouterLink>
      </li>
    </ul>

    <template #footer>
      <p class="text-xs text-ink-muted">
        <template v-if="accounts.totalCount > rows.length">
          Exibindo {{ rows.length }} de {{ accounts.totalCount }} contas.
        </template>
        Saldos em moeda estrangeira não entram no total em BRL (sem conversão cambial).
      </p>
    </template>
  </BaseCard>
</template>
