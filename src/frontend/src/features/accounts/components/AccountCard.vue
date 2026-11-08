<script setup lang="ts">
import { ChevronRight, Landmark } from '@lucide/vue'
import BaseBadge from '@/shared/components/ui/BaseBadge.vue'
import CopyButton from '@/shared/components/ui/CopyButton.vue'
import { formatCurrency, formatDate } from '@/shared/utils/formatters'
import type { Account } from '../types/account'

defineProps<{ account: Account }>()
</script>

<template>
  <RouterLink
    :to="{ name: 'account-details', params: { id: account.id } }"
    class="group flex flex-col rounded-2xl border border-line bg-surface p-5 shadow-card transition-colors hover:border-line-strong"
  >
    <div class="flex items-start justify-between gap-3">
      <span class="inline-flex size-10 items-center justify-center rounded-xl bg-neutral-soft text-ink-secondary">
        <Landmark class="size-5" aria-hidden="true" />
      </span>
      <BaseBadge :tone="account.isActive ? 'positive' : 'neutral'" dot>
        {{ account.isActive ? 'Ativa' : 'Inativa' }}
      </BaseBadge>
    </div>

    <h3 class="mt-4 flex items-center gap-1 text-sm font-semibold text-ink">
      <span class="truncate">{{ account.holderName }}</span>
      <ChevronRight
        class="size-4 shrink-0 text-ink-muted transition-transform group-hover:translate-x-0.5"
        aria-hidden="true"
      />
    </h3>
    <p class="flex items-center gap-0.5 text-xs text-ink-muted tabular">
      Conta {{ account.accountNumber }} · {{ account.currency }}
      <CopyButton :value="account.accountNumber" label="Copiar número da conta" />
    </p>

    <p
      class="mt-5 text-2xl font-semibold tracking-tight tabular"
      :class="account.isActive ? 'text-ink' : 'text-ink-muted'"
    >
      {{ formatCurrency(account.balance, account.currency) }}
    </p>

    <dl class="mt-5 grid grid-cols-2 gap-3 border-t border-line pt-4 text-xs">
      <div class="min-w-0">
        <dt class="text-ink-muted">Moeda</dt>
        <dd class="mt-0.5 font-medium text-ink-secondary">{{ account.currency }}</dd>
      </div>
      <div>
        <dt class="text-ink-muted">Aberta em</dt>
        <dd class="mt-0.5 font-medium text-ink-secondary">{{ formatDate(account.createdAt) }}</dd>
      </div>
    </dl>
  </RouterLink>
</template>
