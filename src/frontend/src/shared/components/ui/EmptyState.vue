<script setup lang="ts">
import type { Component } from 'vue'

defineProps<{ icon: Component; title: string; description?: string }>()
</script>

<template>
  <div class="flex flex-col items-center justify-center px-6 py-14 text-center">
    <span class="mb-4 inline-flex size-11 items-center justify-center rounded-xl bg-neutral-soft text-ink-secondary">
      <component :is="icon" class="size-5" aria-hidden="true" />
    </span>
    <h3 class="text-sm font-semibold text-ink">{{ title }}</h3>
    <p v-if="description" class="mt-1 max-w-sm text-[13px] text-ink-muted">{{ description }}</p>
    <div v-if="$slots.default" class="mt-5"><slot /></div>
  </div>
</template>
