<script setup lang="ts">
withDefaults(
  defineProps<{
    title?: string
    description?: string
    /** Remove o padding do corpo (útil para tabelas que vão de borda a borda). */
    flush?: boolean
  }>(),
  { flush: false },
)
</script>

<template>
  <section class="flex min-w-0 flex-col rounded-2xl border border-line bg-surface shadow-card">
    <header
      v-if="title || $slots.header || $slots.actions"
      class="flex flex-wrap items-start justify-between gap-3 px-5 pt-5 sm:px-6"
      :class="flush ? 'pb-4' : ''"
    >
      <slot name="header">
        <div class="min-w-0">
          <h2 class="text-[15px] font-semibold tracking-tight text-ink">{{ title }}</h2>
          <p v-if="description" class="mt-0.5 text-[13px] text-ink-muted">{{ description }}</p>
        </div>
      </slot>
      <div v-if="$slots.actions" class="flex shrink-0 flex-wrap items-center gap-2">
        <slot name="actions" />
      </div>
    </header>

    <div :class="flush ? '' : 'px-5 pt-4 pb-5 sm:px-6 sm:pb-6'" class="min-w-0 flex-1">
      <slot />
    </div>

    <footer v-if="$slots.footer" class="border-t border-line px-5 py-3 sm:px-6">
      <slot name="footer" />
    </footer>
  </section>
</template>
