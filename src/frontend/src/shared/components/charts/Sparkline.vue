<script setup lang="ts">
import { computed, useId } from 'vue'

const props = withDefaults(
  defineProps<{
    values: number[]
    /** Série única: quem nomeia a série é o título do card, não uma legenda. */
    color?: string
    width?: number
    height?: number
  }>(),
  { color: 'var(--color-ink-secondary)', width: 96, height: 32 },
)

const gradientId = `spark-${useId()}`
const PAD = 3

const points = computed(() => {
  const { values, width, height } = props
  if (values.length < 2) return []
  const min = Math.min(...values)
  const max = Math.max(...values)
  const range = max - min || 1
  const step = (width - PAD * 2) / (values.length - 1)
  return values.map((value, index) => ({
    x: PAD + index * step,
    y: PAD + (1 - (value - min) / range) * (height - PAD * 2),
  }))
})

const linePath = computed(() =>
  points.value.map((p, i) => `${i ? 'L' : 'M'}${p.x.toFixed(1)},${p.y.toFixed(1)}`).join(''),
)

const areaPath = computed(() => {
  const pts = points.value
  const first = pts[0]
  const last = pts[pts.length - 1]
  if (!first || !last) return ''
  return `${linePath.value}L${last.x},${props.height}L${first.x},${props.height}Z`
})

const lastPoint = computed(() => points.value[points.value.length - 1])
</script>

<template>
  <svg
    :width="width"
    :height="height"
    :viewBox="`0 0 ${width} ${height}`"
    aria-hidden="true"
    class="overflow-visible"
  >
    <defs>
      <linearGradient :id="gradientId" x1="0" x2="0" y1="0" y2="1">
        <stop offset="0%" :stop-color="color" stop-opacity="0.16" />
        <stop offset="100%" :stop-color="color" stop-opacity="0" />
      </linearGradient>
    </defs>
    <path :d="areaPath" :fill="`url(#${gradientId})`" />
    <path
      :d="linePath"
      fill="none"
      :stroke="color"
      stroke-width="2"
      stroke-linecap="round"
      stroke-linejoin="round"
    />
    <circle
      v-if="lastPoint"
      :cx="lastPoint.x"
      :cy="lastPoint.y"
      r="3"
      :fill="color"
      stroke="var(--color-surface)"
      stroke-width="2"
    />
  </svg>
</template>
