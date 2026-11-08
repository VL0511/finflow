<script setup lang="ts">
import SkeletonBlock from '@/shared/components/ui/SkeletonBlock.vue'

withDefaults(defineProps<{ rows?: number }>(), { rows: 5 })
</script>

<template>
  <div class="border-t border-line" aria-busy="true">
    <div v-for="n in rows" :key="n" class="flex items-center gap-3 border-b border-line px-5 py-3.5 last:border-b-0 sm:px-6">
      <SkeletonBlock class="size-9 shrink-0 rounded-full" />
      <div class="flex-1 space-y-2">
        <SkeletonBlock class="h-3.5 w-1/3" />
        <SkeletonBlock class="h-3 w-1/5" />
      </div>
      <SkeletonBlock class="hidden h-5 w-20 rounded-full sm:block" />
      <SkeletonBlock class="h-4 w-20" />
    </div>
    <span class="sr-only" role="status">Carregando transações…</span>
  </div>
</template>
