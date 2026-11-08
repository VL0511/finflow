<script setup lang="ts">
import { Check, Copy } from '@lucide/vue'
import { onBeforeUnmount, ref } from 'vue'

/** Copia um texto (ex.: número da conta) para a área de transferência, com confirmação visual. */
const props = withDefaults(defineProps<{ value: string; label?: string }>(), { label: 'Copiar' })

const copied = ref(false)
let timer: ReturnType<typeof setTimeout> | undefined

async function copy(event: Event) {
  // Dentro de links/cartões clicáveis: copiar não navega.
  event.preventDefault()
  event.stopPropagation()
  try {
    await navigator.clipboard.writeText(props.value)
    copied.value = true
    clearTimeout(timer)
    timer = setTimeout(() => (copied.value = false), 1500)
  } catch {
    copied.value = false
  }
}

onBeforeUnmount(() => clearTimeout(timer))
</script>

<template>
  <button
    type="button"
    class="inline-flex size-7 items-center justify-center rounded-md text-ink-muted transition-colors hover:bg-neutral-soft hover:text-ink"
    :aria-label="copied ? 'Copiado' : `${label} ${value}`"
    :title="copied ? 'Copiado!' : label"
    @click="copy"
  >
    <Check v-if="copied" class="size-3.5 text-positive" aria-hidden="true" />
    <Copy v-else class="size-3.5" aria-hidden="true" />
    <span class="sr-only" aria-live="polite">{{ copied ? 'Copiado' : '' }}</span>
  </button>
</template>
