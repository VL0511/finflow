<script setup lang="ts">
import { TrendingDown, TrendingUp } from '@lucide/vue'
import { computed } from 'vue'
import { formatPercent } from '@/shared/utils/formatters'

const props = withDefaults(
  defineProps<{
    /** Variação relativa (0.08 = +8%). null = sem base de comparação. */
    change: number | null
    /** Para métricas como "saídas", subir não é bom. */
    invert?: boolean
  }>(),
  { invert: false },
)

const isUp = computed(() => (props.change ?? 0) >= 0)
const isGood = computed(() => (props.invert ? !isUp.value : isUp.value))
const label = computed(() =>
  props.change === null ? '—' : `${isUp.value ? '+' : '−'}${formatPercent(Math.abs(props.change))}`,
)
</script>

<template>
  <span
    class="inline-flex items-center gap-1 rounded-md px-1.5 py-0.5 text-xs font-semibold tabular"
    :class="
      change === null
        ? 'bg-neutral-soft text-neutral'
        : isGood
          ? 'bg-positive-soft text-positive'
          : 'bg-critical-soft text-critical'
    "
  >
    <template v-if="change !== null">
      <TrendingUp v-if="isUp" class="size-3.5" aria-hidden="true" />
      <TrendingDown v-else class="size-3.5" aria-hidden="true" />
      <span class="sr-only">{{ isUp ? 'Alta de' : 'Queda de' }}</span>
    </template>
    {{ label }}
  </span>
</template>
