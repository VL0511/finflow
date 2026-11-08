<script setup lang="ts">
import { Inbox } from '@lucide/vue'
import EmptyState from '@/shared/components/ui/EmptyState.vue'
import { formatDateTime, formatRelativeTime } from '@/shared/utils/formatters'
import type { TransactionView } from '../types/transaction'
import TransactionAmount from './TransactionAmount.vue'
import TransactionDirectionIcon from './TransactionDirectionIcon.vue'
import TransactionStatusBadge from './TransactionStatusBadge.vue'

withDefaults(
  defineProps<{
    transactions: TransactionView[]
    emptyTitle?: string
    emptyDescription?: string
  }>(),
  {
    emptyTitle: 'Nenhuma transação encontrada',
    emptyDescription: 'Ajuste os filtros para ver outras movimentações.',
  },
)

const detailsRoute = (tx: TransactionView) => ({ name: 'transaction-details', params: { id: tx.id } })
</script>

<template>
  <EmptyState v-if="!transactions.length" :icon="Inbox" :title="emptyTitle" :description="emptyDescription">
    <slot name="empty-action" />
  </EmptyState>

  <template v-else>
    <!-- Desktop / tablet: tabela -->
    <div class="hidden overflow-x-auto md:block">
      <table class="w-full text-left text-sm">
        <thead>
          <tr class="border-y border-line bg-surface-muted text-xs font-medium text-ink-muted">
            <th scope="col" class="py-2.5 pr-3 pl-6 font-medium">Origem → Destino</th>
            <th scope="col" class="px-3 py-2.5 font-medium">Data</th>
            <th scope="col" class="px-3 py-2.5 font-medium">Status</th>
            <th scope="col" class="py-2.5 pr-6 pl-3 text-right font-medium">Valor</th>
          </tr>
        </thead>
        <tbody class="divide-y divide-line">
          <tr v-for="tx in transactions" :key="tx.id" class="relative transition-colors hover:bg-surface-muted/70">
            <td class="py-3 pr-3 pl-6">
              <div class="flex items-center gap-3">
                <TransactionDirectionIcon :direction="tx.direction" />
                <div class="min-w-0">
                  <!-- O link cobre a linha inteira (after:absolute), mantendo um único alvo acessível. -->
                  <RouterLink
                    :to="detailsRoute(tx)"
                    class="block truncate font-medium text-ink after:absolute after:inset-0 focus-visible:outline-none focus-visible:after:outline-2 focus-visible:after:outline-brand-500"
                  >
                    {{ tx.source.name }} <span class="text-ink-muted" aria-label="para">→</span> {{ tx.destination.name }}
                  </RouterLink>
                  <p class="truncate text-xs text-ink-muted">
                    <span v-if="tx.failureReason" class="text-critical">{{ tx.failureReason }}</span>
                    <template v-else>Conta {{ tx.sourceAccount }} → {{ tx.destinationAccount }}</template>
                  </p>
                </div>
              </div>
            </td>
            <td class="px-3 py-3 whitespace-nowrap">
              <time :datetime="tx.createdAt" :title="formatDateTime(tx.createdAt)" class="text-ink-secondary">
                {{ formatRelativeTime(tx.createdAt) }}
              </time>
            </td>
            <td class="px-3 py-3"><TransactionStatusBadge :status="tx.status" /></td>
            <td class="py-3 pr-6 pl-3 text-right"><TransactionAmount :transaction="tx" /></td>
          </tr>
        </tbody>
      </table>
    </div>

    <!-- Mobile: lista empilhada -->
    <ul class="divide-y divide-line border-t border-line md:hidden">
      <li v-for="tx in transactions" :key="tx.id" class="relative flex items-start gap-3 px-5 py-3.5">
        <TransactionDirectionIcon :direction="tx.direction" />
        <div class="min-w-0 flex-1">
          <div class="flex items-start justify-between gap-3">
            <RouterLink :to="detailsRoute(tx)" class="truncate text-sm font-medium text-ink after:absolute after:inset-0">
              {{ tx.source.name }} → {{ tx.destination.name }}
            </RouterLink>
            <TransactionAmount :transaction="tx" class="text-sm" />
          </div>
          <div class="mt-1 flex items-center justify-between gap-3">
            <time :datetime="tx.createdAt" class="truncate text-xs text-ink-muted">
              {{ formatRelativeTime(tx.createdAt) }} · {{ tx.sourceAccount }} → {{ tx.destinationAccount }}
            </time>
            <TransactionStatusBadge :status="tx.status" />
          </div>
          <p v-if="tx.failureReason" class="mt-1 text-xs text-critical">{{ tx.failureReason }}</p>
        </div>
      </li>
    </ul>
  </template>
</template>
