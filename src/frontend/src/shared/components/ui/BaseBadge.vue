<script setup lang="ts">
export type BadgeTone = 'positive' | 'warning' | 'info' | 'critical' | 'neutral' | 'brand'

withDefaults(defineProps<{ tone?: BadgeTone; dot?: boolean }>(), {
  tone: 'neutral',
  dot: false,
})

const toneClasses: Record<BadgeTone, string> = {
  positive: 'bg-positive-soft text-positive',
  warning: 'bg-warning-soft text-warning',
  info: 'bg-info-soft text-info',
  critical: 'bg-critical-soft text-critical',
  neutral: 'bg-neutral-soft text-neutral',
  brand: 'bg-brand-50 text-brand-700',
}

const dotClasses: Record<BadgeTone, string> = {
  positive: 'bg-positive',
  warning: 'bg-warning',
  info: 'bg-info',
  critical: 'bg-critical',
  neutral: 'bg-neutral',
  brand: 'bg-brand-600',
}
</script>

<template>
  <span
    class="inline-flex items-center gap-1.5 rounded-full px-2 py-0.5 text-xs font-medium whitespace-nowrap"
    :class="toneClasses[tone]"
  >
    <span v-if="dot" class="size-1.5 rounded-full" :class="dotClasses[tone]" aria-hidden="true" />
    <slot />
  </span>
</template>
