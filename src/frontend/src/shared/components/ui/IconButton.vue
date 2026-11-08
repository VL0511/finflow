<script setup lang="ts">
import type { Component } from 'vue'

defineProps<{ icon: Component; label: string; badge?: boolean }>()
</script>

<template>
  <button
    type="button"
    :aria-label="label"
    :title="label"
    class="relative inline-flex size-9 items-center justify-center rounded-lg text-ink-secondary transition-colors hover:bg-neutral-soft hover:text-ink"
  >
    <component :is="icon" class="size-[18px]" aria-hidden="true" />
    <span
      v-if="badge"
      class="absolute top-2 right-2 size-2 rounded-full bg-critical ring-2 ring-surface"
      aria-hidden="true"
    />
  </button>
</template>
