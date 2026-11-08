<script setup lang="ts">
import { ArrowLeft, ArrowLeftRight, Landmark, RefreshCw, SearchX } from '@lucide/vue'
import { computed } from 'vue'
import { useRoute } from 'vue-router'
import { useAuthStore } from '@/features/auth/stores/useAuthStore'
import PageHeader from '@/shared/components/PageHeader.vue'
import BaseBadge from '@/shared/components/ui/BaseBadge.vue'
import BaseButton from '@/shared/components/ui/BaseButton.vue'
import BaseCard from '@/shared/components/ui/BaseCard.vue'
import CopyButton from '@/shared/components/ui/CopyButton.vue'
import InlineAlert from '@/shared/components/ui/InlineAlert.vue'
import EmptyState from '@/shared/components/ui/EmptyState.vue'
import ErrorState from '@/shared/components/ui/ErrorState.vue'
import SkeletonBlock from '@/shared/components/ui/SkeletonBlock.vue'
import { formatCurrency, formatDate, formatDateTime } from '@/shared/utils/formatters'
import AccountTransactionsCard from '../components/AccountTransactionsCard.vue'
import { useAccountDetails } from '../composables/useAccountDetails'

const props = defineProps<{ id: string }>()

const { account, detailState, balanceState, balanceCheckedAt, notFound, load, refreshBalance } = useAccountDetails(
  computed(() => props.id),
)

const route = useRoute()
const isNew = computed(() => route.query.nova === '1')
const auth = useAuthStore()
// Transferências saem só de contas ativas no escopo do usuário (a API revalida).
const canTransfer = computed(() => auth.hasCapability('banking') && account.value?.isActive === true)
</script>

<template>
  <div class="space-y-6">
    <BaseButton variant="ghost" size="sm" :icon="ArrowLeft" :to="{ name: 'accounts' }" class="-ml-2">
      Contas
    </BaseButton>

    <!-- Carregando pela primeira vez (conta ainda não está no store) -->
    <div v-if="!account && detailState.isLoading" class="space-y-6" aria-busy="true">
      <div>
        <SkeletonBlock class="h-7 w-56" />
        <SkeletonBlock class="mt-2 h-4 w-40" />
      </div>
      <SkeletonBlock class="h-40 rounded-2xl" />
      <span class="sr-only" role="status">Carregando conta…</span>
    </div>

    <BaseCard v-else-if="notFound">
      <EmptyState :icon="SearchX" title="Conta não encontrada" description="A conta pode ter sido removida ou o endereço está incorreto.">
        <BaseButton :to="{ name: 'accounts' }">Ver todas as contas</BaseButton>
      </EmptyState>
    </BaseCard>

    <BaseCard v-else-if="!account">
      <ErrorState :error="detailState.error" :retrying="detailState.isLoading" @retry="load" />
    </BaseCard>

    <template v-else>
      <InlineAlert v-if="isNew" tone="positive" title="Conta criada">
        O número gerado é <strong class="tabular">{{ account.accountNumber }}</strong>. Use-o para receber transferências.
      </InlineAlert>

      <PageHeader :title="account.holderName" :description="`Conta ${account.accountNumber} · ${account.currency}`">
        <template #actions>
          <BaseBadge :tone="account.isActive ? 'positive' : 'neutral'" dot>
            {{ account.isActive ? 'Ativa' : 'Inativa' }}
          </BaseBadge>
          <BaseButton
            v-if="canTransfer"
            variant="primary"
            :icon="ArrowLeftRight"
            :to="{ name: 'transaction-create', query: { origem: account.accountNumber } }"
          >
            Transferir
          </BaseButton>
        </template>
      </PageHeader>

      <div class="grid grid-cols-1 gap-4 lg:grid-cols-3">
        <!-- Saldo -->
        <article class="relative overflow-hidden rounded-2xl border border-nav-line bg-nav p-5 text-white lg:col-span-2">
          <div class="pointer-events-none absolute -top-16 -right-10 size-44 rounded-full bg-brand-500/25 blur-3xl" aria-hidden="true" />
          <div class="relative flex items-start justify-between gap-3">
            <div class="flex items-center gap-2.5">
              <span class="inline-flex size-8 items-center justify-center rounded-lg bg-white/10 text-brand-400">
                <Landmark class="size-4" aria-hidden="true" />
              </span>
              <h2 class="text-[13px] font-medium text-nav-ink">Saldo atual</h2>
            </div>
            <button
              type="button"
              class="inline-flex items-center gap-1.5 rounded-lg px-2 py-1 text-xs font-medium text-nav-ink hover:bg-white/10 hover:text-white disabled:opacity-60"
              :disabled="balanceState.isLoading"
              @click="refreshBalance"
            >
              <RefreshCw class="size-3.5" :class="balanceState.isLoading && 'animate-spin'" aria-hidden="true" />
              Consultar saldo
            </button>
          </div>
          <p class="relative mt-5 text-3xl font-semibold tracking-tight tabular" aria-live="polite">
            {{ formatCurrency(account.balance, account.currency) }}
          </p>
          <p class="relative mt-3 text-xs text-nav-ink-muted">
            <template v-if="balanceState.error">
              <span class="text-red-300">Falha ao consultar o saldo: {{ balanceState.error.title }}</span>
            </template>
            <template v-else-if="balanceCheckedAt">Consultado em {{ formatDateTime(balanceCheckedAt) }}</template>
          </p>
        </article>

        <!-- Dados cadastrais -->
        <BaseCard title="Dados da conta">
          <dl class="space-y-3 text-sm">
            <div class="flex justify-between gap-3">
              <dt class="text-ink-muted">Número</dt>
              <dd class="flex items-center gap-1 font-medium text-ink tabular">
                {{ account.accountNumber }}
                <CopyButton :value="account.accountNumber" label="Copiar número da conta" />
              </dd>
            </div>
            <div class="flex justify-between gap-3">
              <dt class="text-ink-muted">Moeda</dt>
              <dd class="font-medium text-ink">{{ account.currency }}</dd>
            </div>
            <div class="flex justify-between gap-3">
              <dt class="text-ink-muted">Aberta em</dt>
              <dd class="font-medium text-ink">{{ formatDate(account.createdAt) }}</dd>
            </div>
            <div class="flex flex-col gap-1">
              <dt class="text-ink-muted">ID</dt>
              <dd class="font-mono text-xs break-all text-ink-secondary">{{ account.id }}</dd>
            </div>
          </dl>
        </BaseCard>
      </div>

      <AccountTransactionsCard :key="account.id" :account="account" />
    </template>
  </div>
</template>
