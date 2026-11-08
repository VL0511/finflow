<script setup lang="ts">
import { ChevronLeft, ChevronRight } from '@lucide/vue'
import { PAGE_SIZE_OPTIONS } from '@/shared/api/pagination'

/**
 * Controles de paginação. Só apresenta e emite; quem busca os dados é a lista paginada.
 * Os botões ficam desabilitados quando não há página anterior/seguinte.
 */
const props = withDefaults(
  defineProps<{
    page: number
    pageSize: number
    totalItems: number
    totalPages: number
    hasPreviousPage: boolean
    hasNextPage: boolean
    rangeStart: number
    rangeEnd: number
    /** Rótulo dos registros no plural (ex.: "transações"). */
    itemLabel?: string
    loading?: boolean
    pageSizeOptions?: readonly number[]
    /** Id único (vários paginadores na mesma tela). */
    id?: string
  }>(),
  { itemLabel: 'registros', loading: false, pageSizeOptions: () => PAGE_SIZE_OPTIONS, id: 'pagination' },
)

const emit = defineEmits<{ 'update:page': [page: number]; 'update:pageSize': [size: number] }>()

function onPageSize(event: Event) {
  emit('update:pageSize', Number((event.target as HTMLSelectElement).value))
}

const buttonClass =
  'inline-flex h-8 items-center gap-1 rounded-lg border border-line bg-surface px-2.5 text-[13px] font-medium text-ink transition-colors hover:border-line-strong hover:bg-surface-muted disabled:pointer-events-none disabled:opacity-40'

// Mantém o seletor coerente se a API devolver um tamanho fora das opções (ex.: limitado a 100).
const options = () =>
  props.pageSizeOptions.includes(props.pageSize)
    ? props.pageSizeOptions
    : [...props.pageSizeOptions, props.pageSize].sort((a, b) => a - b)
</script>

<template>
  <nav
    class="flex flex-col gap-3 border-t border-line px-5 py-3 text-[13px] sm:flex-row sm:items-center sm:justify-between sm:px-6"
    :aria-label="`Paginação de ${itemLabel}`"
    :aria-busy="loading || undefined"
  >
    <p class="text-ink-muted tabular" aria-live="polite">
      <template v-if="totalItems === 0">Nenhum registro</template>
      <template v-else>
        Mostrando <span class="font-medium text-ink">{{ rangeStart }}–{{ rangeEnd }}</span> de
        <span class="font-medium text-ink">{{ totalItems }}</span> {{ itemLabel }}
      </template>
    </p>

    <div class="flex flex-wrap items-center gap-x-4 gap-y-2">
      <label class="flex items-center gap-2 text-ink-muted" :for="`${id}-size`">
        Por página
        <select
          :id="`${id}-size`"
          class="field-control h-8 w-auto py-0 pr-8 text-[13px]"
          :value="pageSize"
          @change="onPageSize"
        >
          <option v-for="size in options()" :key="size" :value="size">{{ size }}</option>
        </select>
      </label>

      <div class="flex items-center gap-2">
        <button
          type="button"
          :class="buttonClass"
          :disabled="!hasPreviousPage"
          aria-label="Página anterior"
          @click="emit('update:page', page - 1)"
        >
          <ChevronLeft class="size-4" aria-hidden="true" />
          <span class="hidden sm:inline">Anterior</span>
        </button>
        <span class="min-w-[6.5rem] text-center text-ink-secondary tabular">
          Página <span class="font-medium text-ink">{{ totalPages === 0 ? 0 : page }}</span> de {{ totalPages }}
        </span>
        <button
          type="button"
          :class="buttonClass"
          :disabled="!hasNextPage"
          aria-label="Próxima página"
          @click="emit('update:page', page + 1)"
        >
          <span class="hidden sm:inline">Próxima</span>
          <ChevronRight class="size-4" aria-hidden="true" />
        </button>
      </div>
    </div>
  </nav>
</template>
