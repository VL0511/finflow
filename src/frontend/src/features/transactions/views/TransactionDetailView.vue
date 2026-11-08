<script setup lang="ts">
import { ArrowLeft, Ban, LoaderCircle, RefreshCw, SearchX } from '@lucide/vue'
import { computed, ref } from 'vue'
import { useRoute } from 'vue-router'
import BaseButton from '@/shared/components/ui/BaseButton.vue'
import BaseCard from '@/shared/components/ui/BaseCard.vue'
import EmptyState from '@/shared/components/ui/EmptyState.vue'
import ErrorState from '@/shared/components/ui/ErrorState.vue'
import InlineAlert from '@/shared/components/ui/InlineAlert.vue'
import SkeletonBlock from '@/shared/components/ui/SkeletonBlock.vue'
import { formatCurrency, formatDateTime } from '@/shared/utils/formatters'
import TransactionProgress from '../components/TransactionProgress.vue'
import TransactionStatusBadge from '../components/TransactionStatusBadge.vue'
import { useTransactionDetails } from '../composables/useTransactionDetails'
import { transactionStatusMeta } from '../utils/transactionStatus'

const props = defineProps<{ id: string }>()
const route = useRoute()

const {
  transaction,
  detailState,
  notFound,
  isFinal,
  canCancel,
  isCancelling,
  cancelError,
  pollingState,
  load,
  cancel,
  checkAgain,
} = useTransactionDetails(computed(() => props.id))

/** Veio do formulário de nova transferência (POST 202). */
const justCreated = computed(() => route.query.nova === '1')

const confirmingCancel = ref(false)
async function confirmCancel() {
  await cancel()
  confirmingCancel.value = false
}
</script>

<template>
  <div class="mx-auto max-w-3xl space-y-6">
    <BaseButton variant="ghost" size="sm" :icon="ArrowLeft" :to="{ name: 'transactions' }" class="-ml-2">
      Transações
    </BaseButton>

    <div v-if="!transaction && detailState.isLoading" class="space-y-4" aria-busy="true">
      <SkeletonBlock class="h-8 w-48" />
      <SkeletonBlock class="h-44 rounded-2xl" />
      <SkeletonBlock class="h-56 rounded-2xl" />
      <span class="sr-only" role="status">Carregando transação…</span>
    </div>

    <BaseCard v-else-if="notFound">
      <EmptyState :icon="SearchX" title="Transação não encontrada" description="Verifique o endereço ou volte para o histórico.">
        <BaseButton :to="{ name: 'transactions' }">Ver histórico</BaseButton>
      </EmptyState>
    </BaseCard>

    <BaseCard v-else-if="!transaction">
      <ErrorState :error="detailState.error" :retrying="detailState.isLoading" @retry="load" />
    </BaseCard>

    <template v-else>
      <InlineAlert v-if="justCreated && !isFinal" tone="info" title="Transferência registrada">
        A API aceitou a solicitação, mas o processamento é assíncrono. O status abaixo é atualizado
        automaticamente — o saldo só muda quando a transferência for concluída.
      </InlineAlert>

      <!-- Resumo -->
      <div class="flex flex-col gap-4 sm:flex-row sm:items-end sm:justify-between">
        <div class="min-w-0">
          <p class="mb-1 text-xs font-medium tracking-wide text-ink-muted uppercase">Transferência</p>
          <h1 class="text-3xl font-semibold tracking-tight text-ink tabular">
            {{ formatCurrency(transaction.amount, transaction.currency) }}
          </h1>
          <p class="mt-1 truncate text-sm text-ink-secondary">
            {{ transaction.source.name }} → {{ transaction.destination.name }}
          </p>
        </div>
        <div class="flex items-center gap-2">
          <TransactionStatusBadge :status="transaction.status" />
        </div>
      </div>

      <!-- Andamento -->
      <BaseCard title="Andamento">
        <template #actions>
          <span v-if="pollingState === 'running'" class="inline-flex items-center gap-1.5 text-xs text-ink-muted">
            <LoaderCircle class="size-3.5 animate-spin" aria-hidden="true" /> Acompanhando
          </span>
          <span v-else-if="pollingState === 'paused'" class="text-xs text-ink-muted">Pausado (aba em segundo plano)</span>
        </template>

        <TransactionProgress :status="transaction.status" :live="pollingState === 'running'" />

        <InlineAlert v-if="!isFinal && pollingState === 'timeout'" tone="warning" class="mt-5" title="Ainda sem status final">
          A transação continua “{{ transactionStatusMeta[transaction.status].label }}”. Se demorar além do normal,
          o Worker pode estar parado.
          <template #actions>
            <BaseButton size="sm" :icon="RefreshCw" @click="checkAgain">Verificar novamente</BaseButton>
          </template>
        </InlineAlert>

        <InlineAlert v-if="!isFinal && pollingState === 'error'" tone="critical" class="mt-5" title="Falha ao consultar o status">
          Não foi possível falar com a API. O status exibido pode estar desatualizado.
          <template #actions>
            <BaseButton size="sm" :icon="RefreshCw" @click="checkAgain">Tentar novamente</BaseButton>
          </template>
        </InlineAlert>

        <InlineAlert v-if="transaction.failureReason" tone="critical" class="mt-5" title="Motivo da falha">
          {{ transaction.failureReason }}
        </InlineAlert>

        <InlineAlert v-if="cancelError" tone="critical" class="mt-5" :title="cancelError.title">
          {{ cancelError.detail ?? 'Não foi possível cancelar a transação.' }}
        </InlineAlert>

        <template v-if="canCancel" #footer>
          <div class="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
            <p class="text-xs text-ink-muted">O cancelamento só é aceito enquanto a transação estiver pendente.</p>
            <div v-if="!confirmingCancel">
              <BaseButton variant="danger" size="sm" :icon="Ban" @click="confirmingCancel = true">Cancelar transferência</BaseButton>
            </div>
            <div v-else class="flex gap-2">
              <BaseButton size="sm" :disabled="isCancelling" @click="confirmingCancel = false">Manter</BaseButton>
              <BaseButton variant="danger" size="sm" :icon="Ban" :loading="isCancelling" @click="confirmCancel">
                Confirmar cancelamento
              </BaseButton>
            </div>
          </div>
        </template>
      </BaseCard>

      <!-- Dados -->
      <BaseCard title="Detalhes">
        <dl class="grid grid-cols-1 gap-x-6 gap-y-4 text-sm sm:grid-cols-2">
          <div>
            <dt class="text-xs text-ink-muted">Origem</dt>
            <dd class="mt-0.5 font-medium text-ink">
              <RouterLink
                v-if="transaction.source.accountId"
                :to="{ name: 'account-details', params: { id: transaction.source.accountId } }"
                class="hover:underline"
              >
                {{ transaction.source.name }}
              </RouterLink>
              <template v-else>{{ transaction.source.name }}</template>
              <span class="block text-xs font-normal text-ink-muted tabular">Conta {{ transaction.sourceAccount }}</span>
            </dd>
          </div>
          <div>
            <dt class="text-xs text-ink-muted">Destino</dt>
            <dd class="mt-0.5 font-medium text-ink">
              <RouterLink
                v-if="transaction.destination.accountId"
                :to="{ name: 'account-details', params: { id: transaction.destination.accountId } }"
                class="hover:underline"
              >
                {{ transaction.destination.name }}
              </RouterLink>
              <template v-else>{{ transaction.destination.name }}</template>
              <span class="block text-xs font-normal text-ink-muted tabular">Conta {{ transaction.destinationAccount }}</span>
            </dd>
          </div>
          <div>
            <dt class="text-xs text-ink-muted">Valor enviado</dt>
            <dd class="mt-0.5 font-medium text-ink tabular">{{ formatCurrency(transaction.amount, transaction.currency) }}</dd>
          </div>
          <div>
            <dt class="text-xs text-ink-muted">Valor recebido</dt>
            <dd class="mt-0.5 font-medium text-ink tabular">
              {{ formatCurrency(transaction.destinationAmount, transaction.destinationCurrency) }}
            </dd>
          </div>
          <div v-if="transaction.currency !== transaction.destinationCurrency" class="sm:col-span-2">
            <dt class="text-xs text-ink-muted">Câmbio aplicado (fixado na criação)</dt>
            <dd class="mt-0.5 font-medium text-ink tabular">
              1 {{ transaction.currency }} = {{ transaction.exchangeRate.toLocaleString('pt-BR', { maximumFractionDigits: 8 }) }}
              {{ transaction.destinationCurrency }}
              <span class="block text-xs font-normal text-ink-muted">Alterações posteriores da taxa não afetam esta transferência.</span>
            </dd>
          </div>
          <div>
            <dt class="text-xs text-ink-muted">Criada em</dt>
            <dd class="mt-0.5 font-medium text-ink">{{ formatDateTime(transaction.createdAt) }}</dd>
          </div>
          <div>
            <dt class="text-xs text-ink-muted">Última atualização</dt>
            <dd class="mt-0.5 font-medium text-ink">
              {{ transaction.updatedAt ? formatDateTime(transaction.updatedAt) : '—' }}
            </dd>
          </div>
          <div class="sm:col-span-2">
            <dt class="text-xs text-ink-muted">ID</dt>
            <dd class="mt-0.5 font-mono text-xs break-all text-ink-secondary">{{ transaction.id }}</dd>
          </div>
        </dl>
      </BaseCard>
    </template>
  </div>
</template>
