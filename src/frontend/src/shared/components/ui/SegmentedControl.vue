<script setup lang="ts" generic="T extends string">
export interface SegmentedOption<V extends string> {
  value: V
  label: string
}

defineProps<{ options: SegmentedOption<T>[]; label: string }>()
const model = defineModel<T>({ required: true })
</script>

<template>
  <div
    role="radiogroup"
    :aria-label="label"
    class="inline-flex max-w-full overflow-x-auto rounded-lg border border-line bg-surface-muted p-0.5"
  >
    <button
      v-for="option in options"
      :key="option.value"
      type="button"
      role="radio"
      :aria-checked="model === option.value"
      class="h-7 shrink-0 rounded-md px-2.5 text-[13px] font-medium whitespace-nowrap transition-colors"
      :class="
        model === option.value
          ? 'bg-surface text-ink shadow-card ring-1 ring-line'
          : 'text-ink-muted hover:text-ink'
      "
      @click="model = option.value"
    >
      {{ option.label }}
    </button>
  </div>
</template>
