<script setup lang="ts">
import { ArrowDownLeft, ArrowLeftRight, ArrowUpRight } from '@lucide/vue'
import type { TransactionDirection } from '../types/transaction'

defineProps<{ direction: TransactionDirection }>()

const config = {
  inbound: { icon: ArrowDownLeft, label: 'Recebida', classes: 'bg-brand-50 text-brand-700' },
  outbound: { icon: ArrowUpRight, label: 'Enviada', classes: 'bg-indigo-50 text-indigo-700' },
  transfer: { icon: ArrowLeftRight, label: 'Transferência entre contas', classes: 'bg-neutral-soft text-neutral' },
} as const
</script>

<template>
  <span
    class="inline-flex size-9 shrink-0 items-center justify-center rounded-full"
    :class="config[direction].classes"
    :title="config[direction].label"
  >
    <component :is="config[direction].icon" class="size-4" aria-hidden="true" />
    <span class="sr-only">{{ config[direction].label }}</span>
  </span>
</template>
