<script setup lang="ts">
import type { PollerState } from '@/shared/utils/poller'

/** Mostra, de forma discreta, se a lista está sendo atualizada sozinha. */
defineProps<{ state: PollerState; inFlight: number }>()
const emit = defineEmits<{ restart: [] }>()
</script>

<template>
  <p v-if="inFlight > 0" class="flex flex-wrap items-center gap-x-1.5 text-xs text-ink-muted" aria-live="polite">
    <template v-if="state === 'running'">
      <span class="size-1.5 animate-pulse rounded-full bg-info" aria-hidden="true" />
      Atualizando automaticamente · {{ inFlight }} em andamento
    </template>
    <template v-else-if="state === 'timeout' || state === 'error' || state === 'stopped'">
      <span class="size-1.5 rounded-full bg-warning" aria-hidden="true" />
      Atualização automática pausada · {{ inFlight }} aguardando o Worker ·
      <button type="button" class="font-medium text-ink-secondary underline-offset-2 hover:text-ink hover:underline" @click="emit('restart')">
        retomar
      </button>
    </template>
  </p>
</template>
