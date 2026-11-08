<script setup lang="ts">
import { computed } from 'vue'
import { formatCurrency } from '@/shared/utils/formatters'
import { useAccountsStore } from '../stores/useAccountsStore'

/** Totais por moeda de TODAS as contas (GET /accounts/summary), sem conversão cambial. */
const accounts = useAccountsStore()
const totals = computed(() => accounts.summary?.balances ?? [])
</script>

<template>
  <div class="rounded-xl border border-line bg-surface px-4 py-2 text-right shadow-card">
    <p class="text-xs text-ink-muted">Saldo total por moeda</p>
    <p v-for="item in totals" :key="item.currency" class="text-sm font-semibold text-ink tabular">
      {{ formatCurrency(item.totalBalance, item.currency) }}
    </p>
  </div>
</template>
