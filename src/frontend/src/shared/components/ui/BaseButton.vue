<script setup lang="ts">
import { LoaderCircle } from '@lucide/vue'
import type { Component } from 'vue'
import { computed } from 'vue'
import { RouterLink, type RouteLocationRaw } from 'vue-router'

type Variant = 'primary' | 'secondary' | 'ghost' | 'danger'
type Size = 'sm' | 'md'

const props = withDefaults(
  defineProps<{
    variant?: Variant
    size?: Size
    icon?: Component
    /** Quando informado, renderiza um RouterLink com aparência de botão. */
    to?: RouteLocationRaw
    type?: 'button' | 'submit'
    disabled?: boolean
    /** Mostra spinner no lugar do ícone e desabilita o botão (evita duplo envio). */
    loading?: boolean
  }>(),
  { variant: 'secondary', size: 'md', type: 'button', disabled: false, loading: false },
)

const variants: Record<Variant, string> = {
  primary: 'bg-ink text-white hover:bg-ink/85 shadow-sm',
  secondary: 'bg-surface text-ink border border-line hover:bg-surface-muted hover:border-line-strong',
  ghost: 'text-ink-secondary hover:bg-neutral-soft hover:text-ink',
  danger: 'bg-surface text-critical border border-critical/30 hover:bg-critical-soft',
}

const sizes: Record<Size, string> = {
  sm: 'h-8 px-2.5 text-[13px] gap-1.5',
  md: 'h-9 px-3.5 text-sm gap-2',
}

const classes = computed(() => [
  'inline-flex items-center justify-center rounded-lg font-medium whitespace-nowrap transition-colors',
  'disabled:pointer-events-none disabled:opacity-50',
  variants[props.variant],
  sizes[props.size],
])
</script>

<template>
  <RouterLink v-if="to" :to="to" :class="classes">
    <component :is="icon" v-if="icon" class="size-4 shrink-0" aria-hidden="true" />
    <slot />
  </RouterLink>
  <button v-else :type="type" :class="classes" :disabled="disabled || loading" :aria-busy="loading || undefined">
    <LoaderCircle v-if="loading" class="size-4 shrink-0 animate-spin" aria-hidden="true" />
    <component :is="icon" v-else-if="icon" class="size-4 shrink-0" aria-hidden="true" />
    <slot />
  </button>
</template>
