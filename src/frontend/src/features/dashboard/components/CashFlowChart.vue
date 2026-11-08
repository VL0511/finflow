<script setup lang="ts">
import { computed, ref } from 'vue'
import { useElementSize } from '@/shared/composables/useElementSize'
import { formatCompactCurrency, formatCurrency, type CurrencyCode } from '@/shared/utils/formatters'
import type { CashFlowPoint } from '../types/dashboard'

const props = withDefaults(defineProps<{ points: CashFlowPoint[]; height?: number; currency?: CurrencyCode }>(), {
  height: 260,
  currency: 'BRL',
})

const money = (value: number) => formatCurrency(value, props.currency)

const MARGIN = { top: 12, right: 4, bottom: 28, left: 76 }
const BAR_GAP = 2 // espaço de superfície entre as barras do mesmo grupo
const MAX_BAR_WIDTH = 22
const LABEL_MIN_SPACING = 56
const LABEL_HALF_WIDTH = 22
const TOOLTIP_WIDTH = 208

const container = ref<HTMLElement | null>(null)
const { width } = useElementSize(container)
const activeIndex = ref<number | null>(null)

const innerWidth = computed(() => Math.max(0, width.value - MARGIN.left - MARGIN.right))
const innerHeight = computed(() => props.height - MARGIN.top - MARGIN.bottom)
const band = computed(() => (props.points.length ? innerWidth.value / props.points.length : 0))

/** Escala "redonda" para o eixo Y: 0 → topo múltiplo de 1, 2, 2.5 ou 5 × 10ⁿ. */
const yScale = computed(() => {
  const max = Math.max(1, ...props.points.flatMap((p) => [p.inflow, p.outflow]))
  const rough = max / 4
  const magnitude = 10 ** Math.floor(Math.log10(rough))
  const normalized = rough / magnitude
  const step = (normalized <= 1 ? 1 : normalized <= 2 ? 2 : normalized <= 2.5 ? 2.5 : normalized <= 5 ? 5 : 10) * magnitude
  const top = Math.ceil(max / step) * step
  const ticks = Array.from({ length: Math.round(top / step) + 1 }, (_, i) => i * step)
  return { top, ticks }
})

const y = (value: number) => MARGIN.top + innerHeight.value * (1 - value / yScale.value.top)

const barWidth = computed(() => {
  const groupSpace = band.value * (props.points.length > 20 ? 0.72 : 0.56)
  return Math.min(MAX_BAR_WIDTH, Math.max(2, (groupSpace - BAR_GAP) / 2))
})

/** Retângulo com topo arredondado e base reta, ancorado na linha de base. */
function barPath(x: number, value: number, w: number): string {
  const baseline = MARGIN.top + innerHeight.value
  const top = y(value)
  const h = baseline - top
  if (h <= 0) return ''
  const r = Math.min(4, w / 2, h)
  return `M${x},${baseline}V${top + r}Q${x},${top} ${x + r},${top}H${x + w - r}Q${x + w},${top} ${x + w},${top + r}V${baseline}Z`
}

const groups = computed(() =>
  props.points.map((point, index) => {
    const groupWidth = barWidth.value * 2 + BAR_GAP
    const x = MARGIN.left + index * band.value + (band.value - groupWidth) / 2
    return {
      point,
      center: MARGIN.left + index * band.value + band.value / 2,
      inflowPath: barPath(x, point.inflow, barWidth.value),
      outflowPath: barPath(x + barWidth.value + BAR_GAP, point.outflow, barWidth.value),
    }
  }),
)

/** Mostra 1 a cada N rótulos, sempre incluindo o mais recente. */
const labelEvery = computed(() => Math.max(1, Math.ceil(LABEL_MIN_SPACING / (band.value || 1))))
const showLabel = (index: number) => (props.points.length - 1 - index) % labelEvery.value === 0

/** Rótulos nas bordas se alinham para dentro em vez de serem cortados. */
function labelAnchor(center: number): 'start' | 'middle' | 'end' {
  if (center + LABEL_HALF_WIDTH > width.value - MARGIN.right) return 'end'
  if (center - LABEL_HALF_WIDTH < MARGIN.left) return 'start'
  return 'middle'
}

function labelX(center: number): number {
  const anchor = labelAnchor(center)
  if (anchor === 'end') return width.value - MARGIN.right
  if (anchor === 'start') return Math.max(MARGIN.left, center - band.value / 2)
  return center
}

const active = computed(() => (activeIndex.value === null ? null : (groups.value[activeIndex.value] ?? null)))

const tooltipLeft = computed(() => {
  if (!active.value) return 0
  const half = TOOLTIP_WIDTH / 2
  return Math.min(Math.max(active.value.center, half), width.value - half)
})

function onPointerMove(event: PointerEvent) {
  if (!container.value || !band.value) return
  const x = event.clientX - container.value.getBoundingClientRect().left - MARGIN.left
  activeIndex.value =
    x < 0 || x > innerWidth.value ? null : Math.min(props.points.length - 1, Math.floor(x / band.value))
}

function onKeydown(event: KeyboardEvent) {
  const last = props.points.length - 1
  if (event.key === 'ArrowRight') activeIndex.value = Math.min(last, (activeIndex.value ?? -1) + 1)
  else if (event.key === 'ArrowLeft') activeIndex.value = Math.max(0, (activeIndex.value ?? last + 1) - 1)
  else if (event.key === 'Escape') activeIndex.value = null
  else return
  event.preventDefault()
}

const capitalize = (text: string) => text.charAt(0).toUpperCase() + text.slice(1)

const net = (point: CashFlowPoint) => point.inflow - point.outflow
const formatSigned = (value: number) => `${value >= 0 ? '+' : '−'}${money(Math.abs(value))}`
</script>

<template>
  <div ref="container" class="relative w-full select-none" :style="{ height: `${height}px` }">
    <svg
      v-if="width > 0"
      :width="width"
      :height="height"
      role="img"
      aria-label="Gráfico de barras de entradas e saídas por período. Use as setas para navegar."
      tabindex="0"
      class="block touch-pan-y rounded-lg focus-visible:outline-offset-4"
      @pointermove="onPointerMove"
      @pointerdown="onPointerMove"
      @pointerleave="activeIndex = null"
      @keydown="onKeydown"
      @blur="activeIndex = null"
    >
      <!-- Grade e eixo Y (recessivos) -->
      <g>
        <template v-for="tick in yScale.ticks" :key="tick">
          <line
            :x1="MARGIN.left"
            :x2="width - MARGIN.right"
            :y1="y(tick)"
            :y2="y(tick)"
            :stroke="tick === 0 ? 'var(--color-line-strong)' : 'var(--color-line)'"
            :stroke-dasharray="tick === 0 ? undefined : '3 4'"
          />
          <text
            :x="MARGIN.left - 10"
            :y="y(tick)"
            text-anchor="end"
            dominant-baseline="middle"
            class="fill-ink-muted text-[11px] tabular"
          >
            {{ formatCompactCurrency(tick, currency) }}
          </text>
        </template>
      </g>

      <!-- Faixa de hover -->
      <rect
        v-if="active"
        :x="MARGIN.left + (activeIndex ?? 0) * band"
        :y="MARGIN.top"
        :width="band"
        :height="innerHeight"
        rx="6"
        class="fill-ink/[0.04]"
      />

      <!-- Barras -->
      <g>
        <g
          v-for="(group, index) in groups"
          :key="group.point.key"
          :opacity="activeIndex === null || activeIndex === index ? 1 : 0.45"
          class="transition-opacity duration-150"
        >
          <path :d="group.inflowPath" fill="var(--color-inflow)" />
          <path :d="group.outflowPath" fill="var(--color-outflow)" />
        </g>
      </g>

      <!-- Eixo X -->
      <g>
        <template v-for="(group, index) in groups" :key="group.point.key">
          <text
            v-if="showLabel(index)"
            :x="labelX(group.center)"
            :y="height - 8"
            :text-anchor="labelAnchor(group.center)"
            class="text-[11px]"
            :class="activeIndex === index ? 'fill-ink font-medium' : 'fill-ink-muted'"
          >
            {{ group.point.label }}
          </text>
        </template>
      </g>
    </svg>

    <!-- Tooltip -->
    <div
      v-if="active"
      class="pointer-events-none absolute top-0 z-10 -translate-x-1/2 rounded-xl border border-line bg-surface p-3 shadow-pop"
      :style="{ left: `${tooltipLeft}px`, width: `${TOOLTIP_WIDTH}px` }"
      role="status"
      aria-live="polite"
    >
      <p class="mb-2 text-xs font-medium text-ink-secondary">{{ capitalize(active.point.fullLabel) }}</p>
      <dl class="space-y-1.5 text-[13px]">
        <div class="flex items-center justify-between gap-3">
          <dt class="flex items-center gap-2 text-ink-secondary">
            <span class="size-2.5 rounded-[3px] bg-inflow" aria-hidden="true" />Entradas
          </dt>
          <dd class="font-semibold text-ink tabular">{{ money(active.point.inflow) }}</dd>
        </div>
        <div class="flex items-center justify-between gap-3">
          <dt class="flex items-center gap-2 text-ink-secondary">
            <span class="size-2.5 rounded-[3px] bg-outflow" aria-hidden="true" />Saídas
          </dt>
          <dd class="font-semibold text-ink tabular">{{ money(active.point.outflow) }}</dd>
        </div>
        <div class="flex items-center justify-between gap-3 border-t border-line pt-1.5">
          <dt class="text-ink-secondary">Líquido</dt>
          <dd class="font-semibold text-ink tabular">{{ formatSigned(net(active.point)) }}</dd>
        </div>
      </dl>
    </div>

    <!-- Alternativa textual para leitores de tela -->
    <table class="sr-only">
      <caption>Entradas e saídas por período</caption>
      <thead>
        <tr><th scope="col">Período</th><th scope="col">Entradas</th><th scope="col">Saídas</th></tr>
      </thead>
      <tbody>
        <tr v-for="point in points" :key="point.key">
          <th scope="row">{{ point.fullLabel }}</th>
          <td>{{ money(point.inflow) }}</td>
          <td>{{ money(point.outflow) }}</td>
        </tr>
      </tbody>
    </table>
  </div>
</template>
