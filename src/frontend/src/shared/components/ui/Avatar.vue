<script setup lang="ts">
import { computed } from 'vue'
import { getInitials } from '@/shared/utils/formatters'

const props = withDefaults(defineProps<{ name: string; size?: 'sm' | 'md' }>(), { size: 'md' })

// Paleta discreta: a cor só ajuda a distinguir; quem carrega a identidade é o nome ao lado.
const palette = [
  'bg-slate-100 text-slate-700',
  'bg-emerald-50 text-emerald-800',
  'bg-indigo-50 text-indigo-700',
  'bg-amber-50 text-amber-800',
  'bg-sky-50 text-sky-800',
  'bg-rose-50 text-rose-700',
]

const initials = computed(() => getInitials(props.name))
const colorClass = computed(() => {
  const hash = [...props.name].reduce((acc, char) => acc + char.charCodeAt(0), 0)
  return palette[hash % palette.length]
})
</script>

<template>
  <span
    class="inline-flex shrink-0 items-center justify-center rounded-full font-semibold"
    :class="[colorClass, size === 'sm' ? 'size-7 text-[11px]' : 'size-9 text-xs']"
    aria-hidden="true"
  >
    {{ initials }}
  </span>
</template>
