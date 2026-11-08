<script setup lang="ts">
import BaseBadge from '@/shared/components/ui/BaseBadge.vue'
import type { TransactionStatus } from '../types/transaction'
import { transactionStatusMeta } from '../utils/transactionStatus'

defineProps<{ status: TransactionStatus }>()
</script>

<template>
  <!-- Status sempre com texto + ponto: nunca só cor. -->
  <BaseBadge :tone="transactionStatusMeta[status].tone" dot>
    {{ transactionStatusMeta[status].label }}
  </BaseBadge>
</template>
