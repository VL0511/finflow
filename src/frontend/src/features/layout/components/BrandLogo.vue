<script setup lang="ts">
withDefaults(defineProps<{ compact?: boolean }>(), { compact: false })
</script>

<template>
  <div class="flex items-center gap-2.5">
    <svg viewBox="0 0 32 32" class="size-8 shrink-0" aria-hidden="true">
      <rect width="32" height="32" rx="9" fill="#141d2e" />
      <rect x="0.5" y="0.5" width="31" height="31" rx="8.5" fill="none" stroke="#24314a" />
      <path
        d="M9 21.5 14 16l3.5 3.5L23 12"
        fill="none"
        stroke="var(--color-brand-400)"
        stroke-width="2.6"
        stroke-linecap="round"
        stroke-linejoin="round"
      />
      <circle cx="23" cy="12" r="2" fill="var(--color-brand-400)" />
    </svg>
    <span v-if="!compact" class="text-[17px] font-semibold tracking-tight text-white">
      Fin<span class="text-brand-400">Flow</span>
    </span>
  </div>
</template>
