<script setup lang="ts">
import { CloudOff, RefreshCw, TriangleAlert } from '@lucide/vue'
import { computed } from 'vue'
import type { ApiError } from '@/shared/api'
import BaseButton from './BaseButton.vue'

const props = withDefaults(
  defineProps<{
    error: ApiError | null
    title?: string
    /** Mostra o botão "Tentar novamente". */
    retry?: boolean
    retrying?: boolean
    compact?: boolean
  }>(),
  { title: undefined, retry: true, retrying: false, compact: false },
)

const emit = defineEmits<{ retry: [] }>()

const isOffline = computed(() => props.error?.kind === 'network' || props.error?.kind === 'timeout')
const heading = computed(() => props.title ?? props.error?.title ?? 'Não foi possível carregar os dados.')
const detail = computed(() => {
  if (!props.error) return null
  // Evita repetir o título quando o detail é igual.
  return props.error.detail && props.error.detail !== heading.value ? props.error.detail : null
})
</script>

<template>
  <div
    role="alert"
    class="flex flex-col items-center justify-center text-center"
    :class="compact ? 'px-4 py-8' : 'px-6 py-14'"
  >
    <span class="mb-4 inline-flex size-11 items-center justify-center rounded-xl bg-critical-soft text-critical">
      <component :is="isOffline ? CloudOff : TriangleAlert" class="size-5" aria-hidden="true" />
    </span>
    <h3 class="text-sm font-semibold text-ink">{{ heading }}</h3>
    <p v-if="detail" class="mt-1 max-w-sm text-[13px] text-ink-muted">{{ detail }}</p>
    <p v-if="error?.traceId" class="mt-2 text-[11px] text-ink-muted tabular">Rastreamento: {{ error.traceId }}</p>
    <div v-if="retry" class="mt-5">
      <BaseButton size="sm" :icon="RefreshCw" :loading="retrying" @click="emit('retry')">Tentar novamente</BaseButton>
    </div>
  </div>
</template>
