<script setup lang="ts">
import type { Component } from 'vue'
import Sparkline from '@/shared/components/charts/Sparkline.vue'
import TrendBadge from '@/shared/components/ui/TrendBadge.vue'

withDefaults(
  defineProps<{
    label: string
    value: string
    icon: Component
    caption?: string
    /** Variação vs. período anterior; omitido = sem badge. */
    change?: number | null
    invertTrend?: boolean
    sparkline?: number[]
    sparklineColor?: string
    /** Destaque visual para o indicador principal. */
    featured?: boolean
  }>(),
  { change: undefined, invertTrend: false, featured: false },
)
</script>

<template>
  <article
    class="relative flex min-w-0 flex-col overflow-hidden rounded-2xl border p-5"
    :class="featured ? 'border-nav-line bg-nav text-white' : 'border-line bg-surface shadow-card'"
  >
    <!-- Brilho sutil apenas no card de destaque -->
    <div
      v-if="featured"
      class="pointer-events-none absolute -top-16 -right-10 size-44 rounded-full bg-brand-500/25 blur-3xl"
      aria-hidden="true"
    />

    <div class="relative flex items-center justify-between gap-3">
      <div class="flex min-w-0 items-center gap-2.5">
        <span
          class="inline-flex size-8 shrink-0 items-center justify-center rounded-lg"
          :class="featured ? 'bg-white/10 text-brand-400' : 'bg-neutral-soft text-ink-secondary'"
        >
          <component :is="icon" class="size-4" aria-hidden="true" />
        </span>
        <h3 class="truncate text-[13px] font-medium" :class="featured ? 'text-nav-ink' : 'text-ink-secondary'">
          {{ label }}
        </h3>
      </div>
      <TrendBadge v-if="change !== undefined" :change="change" :invert="invertTrend" />
    </div>

    <p
      class="relative mt-5 truncate text-2xl leading-none font-semibold tracking-tight tabular 2xl:text-[26px]"
      :class="featured ? 'text-white' : 'text-ink'"
      :title="value"
    >
      {{ value }}
    </p>

    <div class="relative mt-3 flex min-h-7 items-end justify-between gap-3">
      <p v-if="caption" class="truncate text-xs" :class="featured ? 'text-nav-ink-muted' : 'text-ink-muted'">
        {{ caption }}
      </p>
      <Sparkline
        v-if="sparkline?.length"
        :values="sparkline"
        :color="sparklineColor"
        :width="72"
        :height="28"
        class="shrink-0"
      />
    </div>
  </article>
</template>
