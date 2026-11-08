<script setup lang="ts">
/**
 * Rótulo + controle + dica/erro. O controle vem pelo slot e recebe `id` e
 * `aria-describedby` prontos, para que o erro seja anunciado por leitores de tela.
 */
const props = defineProps<{ id: string; label: string; hint?: string; error?: string | null; optional?: boolean }>()

const describedBy = () => (props.error ? `${props.id}-error` : props.hint ? `${props.id}-hint` : undefined)
</script>

<template>
  <div class="min-w-0">
    <label :for="id" class="mb-1.5 flex items-baseline justify-between gap-2 text-[13px] font-medium text-ink">
      {{ label }}
      <span v-if="optional" class="text-xs font-normal text-ink-muted">opcional</span>
    </label>
    <slot :id="id" :invalid="!!error" :described-by="describedBy()" />
    <p v-if="error" :id="`${id}-error`" class="mt-1.5 text-xs text-critical">{{ error }}</p>
    <p v-else-if="hint" :id="`${id}-hint`" class="mt-1.5 text-xs text-ink-muted">{{ hint }}</p>
  </div>
</template>
