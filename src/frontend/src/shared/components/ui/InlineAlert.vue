<script setup lang="ts">
import { CircleAlert, CircleCheck, Info, TriangleAlert } from '@lucide/vue'

type Tone = 'info' | 'warning' | 'critical' | 'positive'

withDefaults(defineProps<{ tone?: Tone; title?: string }>(), { tone: 'info', title: undefined })

const config: Record<Tone, { icon: typeof Info; classes: string }> = {
  info: { icon: Info, classes: 'border-info/20 bg-info-soft text-info' },
  warning: { icon: TriangleAlert, classes: 'border-warning/20 bg-warning-soft text-warning' },
  critical: { icon: CircleAlert, classes: 'border-critical/20 bg-critical-soft text-critical' },
  positive: { icon: CircleCheck, classes: 'border-positive/20 bg-positive-soft text-positive' },
}
</script>

<template>
  <div
    class="flex gap-3 rounded-xl border px-4 py-3 text-[13px]"
    :class="config[tone].classes"
    :role="tone === 'critical' ? 'alert' : 'status'"
  >
    <component :is="config[tone].icon" class="mt-0.5 size-4 shrink-0" aria-hidden="true" />
    <div class="min-w-0 flex-1">
      <p v-if="title" class="font-semibold">{{ title }}</p>
      <div :class="title ? 'mt-0.5 text-ink-secondary' : ''"><slot /></div>
    </div>
    <div v-if="$slots.actions" class="shrink-0 self-center"><slot name="actions" /></div>
  </div>
</template>
