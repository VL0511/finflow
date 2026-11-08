<script setup lang="ts">
import SkeletonBlock from '@/shared/components/ui/SkeletonBlock.vue'
</script>

<template>
  <div class="flex flex-col rounded-2xl border border-line bg-surface p-5 shadow-card">
    <div class="flex items-start justify-between">
      <SkeletonBlock class="size-10 rounded-xl" />
      <SkeletonBlock class="h-5 w-14 rounded-full" />
    </div>
    <SkeletonBlock class="mt-4 h-4 w-32" />
    <SkeletonBlock class="mt-2 h-3 w-24" />
    <SkeletonBlock class="mt-5 h-7 w-36" />
    <div class="mt-5 grid grid-cols-2 gap-3 border-t border-line pt-4">
      <SkeletonBlock class="h-7" />
      <SkeletonBlock class="h-7" />
    </div>
  </div>
</template>
