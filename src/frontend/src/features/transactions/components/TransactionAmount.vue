<script setup lang="ts">
import { computed } from 'vue'
import { formatCurrency } from '@/shared/utils/formatters'
import type { TransactionView } from '../types/transaction'

const props = defineProps<{ transaction: TransactionView }>()

/** Falhas e cancelamentos não movimentaram dinheiro: valor riscado e esmaecido. */
const isVoid = computed(() => props.transaction.status === 'Failed' || props.transaction.status === 'Cancelled')

/**
 * Quem recebe vê o valor CREDITADO, na moeda do destino; quem envia (ou a visão geral) vê o
 * valor debitado, na moeda da origem. Em transferências com câmbio os dois diferem.
 */
const shown = computed(() =>
  props.transaction.direction === 'inbound'
    ? { amount: props.transaction.destinationAmount, currency: props.transaction.destinationCurrency }
    : { amount: props.transaction.amount, currency: props.transaction.currency },
)

const sign = computed(() => {
  if (props.transaction.direction === 'inbound') return '+'
  if (props.transaction.direction === 'outbound') return '−'
  return ''
})
</script>

<template>
  <span
    class="font-semibold whitespace-nowrap tabular"
    :class="[
      isVoid
        ? 'text-ink-muted line-through decoration-ink-muted/50'
        : transaction.direction === 'inbound'
          ? 'text-positive'
          : 'text-ink',
    ]"
  >
    {{ sign }}{{ formatCurrency(shown.amount, shown.currency) }}
  </span>
</template>
