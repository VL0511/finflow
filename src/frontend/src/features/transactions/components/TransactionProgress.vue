<script setup lang="ts">
import { Check, LoaderCircle, X } from '@lucide/vue'
import { computed } from 'vue'
import type { TransactionStatus } from '../types/transaction'

/**
 * Etapas do ciclo de vida real (Transaction no domínio):
 * Pending → Processing → Completed | Failed, ou Pending → Cancelled.
 */
const props = defineProps<{ status: TransactionStatus; live?: boolean }>()

type StepState = 'done' | 'current' | 'upcoming' | 'failed'

interface Step {
  label: string
  state: StepState
}

/** Registrada → Processamento → Resultado. */
const steps = computed<Step[]>(() => {
  const registered: Step = { label: 'Registrada', state: 'done' }
  switch (props.status) {
    case 'Pending':
      return [registered, { label: 'Aguardando o Worker', state: 'current' }, { label: 'Resultado', state: 'upcoming' }]
    case 'Processing':
      return [registered, { label: 'Em processamento', state: 'current' }, { label: 'Resultado', state: 'upcoming' }]
    case 'Completed':
      return [registered, { label: 'Processada', state: 'done' }, { label: 'Concluída', state: 'done' }]
    case 'Failed':
      return [registered, { label: 'Processada', state: 'done' }, { label: 'Falhou', state: 'failed' }]
    case 'Cancelled':
      return [registered, { label: 'Cancelada', state: 'failed' }, { label: 'Não processada', state: 'upcoming' }]
  }
})

const statusMessage = computed(() => {
  switch (props.status) {
    case 'Pending':
      return 'Aceita pela API e aguardando o processamento. O saldo ainda não foi movimentado.'
    case 'Processing':
      return 'O Worker está processando a transferência.'
    case 'Completed':
      return 'Débito e crédito efetivados.'
    case 'Failed':
      return 'O processamento rejeitou a transferência. Nenhum saldo foi movimentado.'
    case 'Cancelled':
      return 'Cancelada antes do processamento. Nenhum saldo foi movimentado.'
  }
})

const dotClasses: Record<StepState, string> = {
  done: 'bg-positive text-white',
  current: 'bg-info-soft text-info ring-2 ring-info/30',
  upcoming: 'bg-neutral-soft text-ink-muted',
  failed: 'bg-critical text-white',
}
</script>

<template>
  <div>
    <ol class="grid grid-cols-3 gap-2">
      <li v-for="(step, index) in steps" :key="index" class="flex flex-col items-center gap-2 text-center">
        <div class="flex w-full items-center">
          <span class="h-0.5 flex-1" :class="index === 0 ? '' : step.state === 'upcoming' ? 'bg-line' : 'bg-positive/50'" />
          <span class="inline-flex size-7 shrink-0 items-center justify-center rounded-full" :class="dotClasses[step.state]">
            <Check v-if="step.state === 'done'" class="size-4" aria-hidden="true" />
            <X v-else-if="step.state === 'failed'" class="size-4" aria-hidden="true" />
            <LoaderCircle v-else-if="step.state === 'current' && live" class="size-4 animate-spin" aria-hidden="true" />
            <span v-else class="size-2 rounded-full bg-current" aria-hidden="true" />
          </span>
          <span
            class="h-0.5 flex-1"
            :class="index === steps.length - 1 ? '' : steps[index + 1]!.state === 'upcoming' ? 'bg-line' : 'bg-positive/50'"
          />
        </div>
        <span class="text-xs font-medium" :class="step.state === 'upcoming' ? 'text-ink-muted' : 'text-ink'">
          {{ step.label }}
        </span>
      </li>
    </ol>
    <p class="mt-4 text-center text-[13px] text-ink-secondary" aria-live="polite">{{ statusMessage }}</p>
  </div>
</template>
