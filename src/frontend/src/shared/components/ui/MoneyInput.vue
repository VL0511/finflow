<script setup lang="ts">
import { nextTick, ref, watch } from 'vue'
import type { CurrencyCode } from '@/shared/utils/formatters'
import { amountToDigits, digitsToAmount, extractDigits, formatMoneyDigits } from '@/shared/utils/moneyInput'

/**
 * Campo monetário reutilizável com máscara da moeda (ex.: "R$ 1.500,50", "$1,500.50").
 * Entrada "estilo caixa eletrônico": cada dígito entra pela direita; Backspace remove o último;
 * colar "1.234,56" vira 1234,56. O cursor fica sempre no fim (sem saltos ao reformatar).
 *
 * v-model: número (ou null quando vazio), já normalizado para enviar à API. O backend sempre
 * revalida casas decimais e valores — esta máscara é só conforto de digitação.
 */
const props = withDefaults(
  defineProps<{
    modelValue: number | null
    currency: CurrencyCode
    /** Casas decimais da moeda (GET /api/currencies). */
    decimals?: number
    id?: string
    invalid?: boolean
    describedBy?: string
    disabled?: boolean
  }>(),
  { decimals: 2, invalid: false, disabled: false },
)

const emit = defineEmits<{ 'update:modelValue': [value: number | null] }>()

const input = ref<HTMLInputElement | null>(null)
const digits = ref(amountToDigits(props.modelValue, props.decimals))
const display = ref(formatMoneyDigits(digits.value, props.currency, props.decimals))

function render() {
  display.value = formatMoneyDigits(digits.value, props.currency, props.decimals)
  if (input.value) input.value.value = display.value
  void nextTick(moveCaretToEnd)
}

function moveCaretToEnd() {
  const el = input.value
  if (el && document.activeElement === el) el.setSelectionRange(el.value.length, el.value.length)
}

function commit(nextDigits: string) {
  digits.value = nextDigits
  render()
  emit('update:modelValue', digitsToAmount(nextDigits, props.decimals))
}

/** Digitação, colagem, autocompletar: o texto inteiro é reinterpretado só pelos dígitos. */
function onInput(event: Event) {
  const raw = (event.target as HTMLInputElement).value
  // Apagar quando o texto só tinha a máscara (ex.: Backspace sobre o símbolo) remove o último dígito.
  const nextDigits = extractDigits(raw)
  const removedOnlyMask = raw.length < display.value.length && nextDigits === digits.value
  commit(removedOnlyMask ? digits.value.slice(0, -1) : nextDigits)
}

function onKeydown(event: KeyboardEvent) {
  if (event.key === 'Delete') {
    event.preventDefault()
    commit('')
  }
}

// Valor alterado de fora (reset do formulário) ou moeda/casas trocadas.
watch(
  () => [props.modelValue, props.currency, props.decimals] as const,
  ([value]) => {
    const current = digitsToAmount(digits.value, props.decimals)
    if (value !== current) digits.value = amountToDigits(value, props.decimals)
    render()
  },
)
</script>

<template>
  <input
    :id="id"
    ref="input"
    :value="display"
    type="text"
    class="field-control tabular"
    inputmode="numeric"
    autocomplete="off"
    :placeholder="formatMoneyDigits('0', currency, decimals)"
    :disabled="disabled"
    :aria-invalid="invalid || undefined"
    :aria-describedby="describedBy"
    @input="onInput"
    @keydown="onKeydown"
    @focus="moveCaretToEnd"
    @click="moveCaretToEnd"
  />
</template>
