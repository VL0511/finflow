<script setup lang="ts">
import { ArrowLeft, ArrowRight, Landmark, Pencil, Send } from '@lucide/vue'
import { computed } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useAccountsStore } from '@/features/accounts/stores/useAccountsStore'
import { useAuthStore } from '@/features/auth/stores/useAuthStore'
import { useCurrenciesStore } from '@/features/currencies/stores/useCurrenciesStore'
import PageHeader from '@/shared/components/PageHeader.vue'
import BaseButton from '@/shared/components/ui/BaseButton.vue'
import BaseCard from '@/shared/components/ui/BaseCard.vue'
import EmptyState from '@/shared/components/ui/EmptyState.vue'
import ErrorState from '@/shared/components/ui/ErrorState.vue'
import FormField from '@/shared/components/ui/FormField.vue'
import InlineAlert from '@/shared/components/ui/InlineAlert.vue'
import MoneyInput from '@/shared/components/ui/MoneyInput.vue'
import SkeletonBlock from '@/shared/components/ui/SkeletonBlock.vue'
import { formatCurrency } from '@/shared/utils/formatters'
import { useTransferForm } from '../composables/useTransferForm'

const route = useRoute()
const router = useRouter()
const accounts = useAccountsStore()
const auth = useAuthStore()
// Contas do escopo do usuário (Customer: as dele; demais: até 100 da instituição).
void accounts.fetchDirectory()
void useCurrenciesStore().ensureActive()

const origin = typeof route.query.origem === 'string' ? route.query.origem : undefined
const form = useTransferForm({ sourceAccount: origin })
const { values, errors, step } = form

const sourceAccounts = computed(() => accounts.directory.filter((a) => a.isActive || a.accountNumber === values.sourceAccount))
/** Customer só lista as próprias contas: a API não expõe contas de outros clientes. */
const destinationHint = computed(() =>
  auth.hasCapability('operations')
    ? 'Contas cadastradas e ativas. Entre moedas diferentes, a taxa vigente é aplicada.'
    : 'Suas contas cadastradas e ativas. Entre moedas diferentes, a taxa vigente é aplicada.',
)

const rateText = computed(() =>
  form.preview.value ? form.preview.value.exchangeRate.toLocaleString('pt-BR', { maximumFractionDigits: 8 }) : '',
)

async function onReview() {
  await form.review()
}

async function onConfirm() {
  const transaction = await form.confirm()
  // 202 Accepted: segue para o detalhe, que acompanha o processamento.
  if (transaction) await router.push({ name: 'transaction-details', params: { id: transaction.id }, query: { nova: '1' } })
}

const optionLabel = (a: { holderName: string; accountNumber: string; currency: string; isActive: boolean }) =>
  `${a.holderName} · ${a.accountNumber} · ${a.currency}${a.isActive ? '' : ' (inativa)'}`
</script>

<template>
  <div class="mx-auto max-w-2xl space-y-6">
    <BaseButton variant="ghost" size="sm" :icon="ArrowLeft" :to="{ name: 'transactions' }" class="-ml-2">
      Transações
    </BaseButton>

    <PageHeader
      title="Nova transferência"
      description="Entre moedas diferentes, a taxa vigente é aplicada pelo sistema no momento da confirmação."
    />

    <BaseCard v-if="accounts.directoryState.isInitialLoading" aria-busy="true">
      <div class="space-y-5">
        <SkeletonBlock class="h-16" />
        <SkeletonBlock class="h-16" />
        <SkeletonBlock class="h-16" />
      </div>
    </BaseCard>

    <BaseCard v-else-if="accounts.directoryState.error && !accounts.directory.length">
      <ErrorState
        :error="accounts.directoryState.error"
        title="Não foi possível carregar as contas."
        :retrying="accounts.directoryState.isLoading"
        @retry="accounts.fetchDirectory()"
      />
    </BaseCard>

    <BaseCard v-else-if="!sourceAccounts.length">
      <EmptyState :icon="Landmark" title="Nenhuma conta disponível" description="Você precisa de uma conta ativa para transferir.">
        <BaseButton v-if="auth.hasCapability('openAccount')" variant="primary" :to="{ name: 'account-create' }">Nova conta</BaseButton>
      </EmptyState>
    </BaseCard>

    <!-- Etapa 1: preencher -->
    <BaseCard v-else-if="step === 'edit'">
      <form class="space-y-5" novalidate @submit.prevent="onReview">
        <InlineAlert v-if="form.submitError.value" tone="critical" :title="form.submitError.value.title">
          {{ form.submitError.value.detail ?? 'Tente novamente em instantes.' }}
        </InlineAlert>

        <FormField id="source" v-slot="field" label="Conta de origem" :error="errors.sourceAccount">
          <select
            :id="field.id"
            v-model="values.sourceAccount"
            class="field-control"
            :aria-invalid="field.invalid"
            :aria-describedby="field.describedBy"
          >
            <option value="" disabled>Selecione…</option>
            <option v-for="account in sourceAccounts" :key="account.id" :value="account.accountNumber">
              {{ optionLabel(account) }}
            </option>
          </select>
        </FormField>

        <!-- Saldo atual da origem, consultado na API -->
        <div v-if="form.source.value" class="flex items-center justify-between gap-3 rounded-xl border border-line bg-surface-muted px-4 py-3 text-sm">
          <span class="text-ink-muted">Saldo disponível</span>
          <SkeletonBlock v-if="form.isLoadingBalance.value" class="h-4 w-24" />
          <span v-else-if="form.sourceBalanceError.value" class="text-xs text-critical">Não foi possível consultar o saldo.</span>
          <span v-else-if="form.sourceBalance.value" class="font-semibold text-ink tabular">
            {{ formatCurrency(form.sourceBalance.value.balance, form.sourceBalance.value.currency) }}
          </span>
        </div>

        <FormField
          id="destination"
          v-slot="field"
          label="Conta de destino"
          :hint="destinationHint"
          :error="errors.destinationAccount"
        >
          <select
            :id="field.id"
            v-model="values.destinationAccount"
            class="field-control"
            :disabled="!values.sourceAccount"
            :aria-invalid="field.invalid"
            :aria-describedby="field.describedBy"
          >
            <option value="" disabled>{{ values.sourceAccount ? 'Selecione…' : 'Escolha a origem primeiro' }}</option>
            <option v-for="account in form.destinationOptions.value" :key="account.id" :value="account.accountNumber">
              {{ optionLabel(account) }}
            </option>
          </select>
        </FormField>

        <InlineAlert v-if="values.sourceAccount && !form.destinationOptions.value.length" tone="info">
          Nenhuma outra conta ativa disponível como destino.
        </InlineAlert>

        <FormField
          id="amount"
          v-slot="field"
          :label="`Valor a enviar (${form.sourceCurrency.value})`"
          hint="Na moeda da conta de origem."
          :error="errors.amount"
        >
          <MoneyInput
            :id="field.id"
            v-model="values.amount"
            :currency="form.sourceCurrency.value"
            :decimals="form.decimals.value"
            :invalid="field.invalid"
            :described-by="field.describedBy"
          />
        </FormField>

        <!-- Prévia calculada pelo backend -->
        <div aria-live="polite">
          <SkeletonBlock v-if="form.isLoadingPreview.value" class="h-16" />
          <InlineAlert v-else-if="form.previewError.value" tone="critical" :title="form.previewError.value.title">
            {{ form.previewError.value.detail }}
          </InlineAlert>
          <div v-else-if="form.preview.value" class="rounded-xl border border-line bg-surface-muted px-4 py-3 text-sm">
            <p class="flex items-center justify-between gap-3">
              <span class="text-ink-muted">Destinatário</span>
              <span class="font-medium text-ink">{{ form.preview.value.destinationHolder }}</span>
            </p>
            <template v-if="form.preview.value.isConversion">
              <p class="mt-1.5 flex items-center justify-between gap-3">
                <span class="text-ink-muted">Taxa vigente</span>
                <span class="text-ink tabular">
                  1 {{ form.preview.value.sourceCurrency }} = {{ rateText }} {{ form.preview.value.destinationCurrency }}
                </span>
              </p>
              <p class="mt-1.5 flex items-center justify-between gap-3">
                <span class="text-ink-muted">Valor estimado a receber</span>
                <span class="font-semibold text-ink tabular">
                  {{ formatCurrency(form.preview.value.destinationAmount, form.preview.value.destinationCurrency) }}
                </span>
              </p>
            </template>
          </div>
        </div>

        <InlineAlert v-if="form.warnings.value.length" tone="warning" title="Atenção">
          <ul class="list-disc space-y-0.5 pl-4">
            <li v-for="warning in form.warnings.value" :key="warning">{{ warning }}</li>
          </ul>
        </InlineAlert>

        <div class="flex flex-col-reverse gap-2 border-t border-line pt-5 sm:flex-row sm:justify-end">
          <BaseButton :to="{ name: 'transactions' }">Cancelar</BaseButton>
          <BaseButton type="submit" variant="primary" :icon="ArrowRight" :loading="form.isLoadingPreview.value">Revisar</BaseButton>
        </div>
      </form>
    </BaseCard>

    <!-- Etapa 2: confirmar -->
    <BaseCard v-else title="Confirme a transferência" description="Confira os dados antes de enviar.">
      <div v-if="form.preview.value" class="space-y-5">
        <InlineAlert v-if="form.submitError.value" tone="critical" :title="form.submitError.value.title">
          {{ form.submitError.value.detail }}
        </InlineAlert>

        <dl class="grid grid-cols-1 gap-4 text-sm sm:grid-cols-2">
          <div>
            <dt class="text-xs text-ink-muted">Origem</dt>
            <dd class="mt-0.5 font-medium text-ink">{{ form.source.value ? optionLabel(form.source.value) : values.sourceAccount }}</dd>
          </div>
          <div>
            <dt class="text-xs text-ink-muted">Destino</dt>
            <dd class="mt-0.5 font-medium text-ink">
              {{ form.preview.value.destinationHolder }}
              <span class="block text-xs font-normal text-ink-muted tabular">Conta {{ values.destinationAccount }}</span>
            </dd>
          </div>
          <div>
            <dt class="text-xs text-ink-muted">Você envia</dt>
            <dd class="mt-0.5 text-lg font-semibold text-ink tabular">
              {{ formatCurrency(form.preview.value.amount, form.preview.value.sourceCurrency) }}
            </dd>
          </div>
          <div>
            <dt class="text-xs text-ink-muted">{{ form.preview.value.isConversion ? 'Destino recebe (estimado)' : 'Destino recebe' }}</dt>
            <dd class="mt-0.5 text-lg font-semibold text-ink tabular">
              {{ formatCurrency(form.preview.value.destinationAmount, form.preview.value.destinationCurrency) }}
            </dd>
          </div>
          <div v-if="form.preview.value.isConversion" class="sm:col-span-2">
            <dt class="text-xs text-ink-muted">Taxa aplicada</dt>
            <dd class="mt-0.5 text-ink tabular">
              1 {{ form.preview.value.sourceCurrency }} = {{ rateText }} {{ form.preview.value.destinationCurrency }}
            </dd>
          </div>
        </dl>

        <InlineAlert v-if="form.preview.value.isConversion" tone="info">
          Se a taxa for alterada antes da confirmação, vale a taxa vigente no momento do envio — o valor final
          aparece no detalhe da transferência e não muda depois.
        </InlineAlert>

        <div class="flex flex-col-reverse gap-2 border-t border-line pt-5 sm:flex-row sm:items-center sm:justify-between">
          <p class="text-xs text-ink-muted">O saldo só é movimentado quando o processamento concluir.</p>
          <div class="flex flex-col-reverse gap-2 sm:flex-row">
            <BaseButton :icon="Pencil" :disabled="form.isSubmitting.value" @click="form.backToEdit()">Editar</BaseButton>
            <BaseButton variant="primary" :icon="Send" :loading="form.isSubmitting.value" @click="onConfirm">
              Confirmar transferência
            </BaseButton>
          </div>
        </div>
      </div>
    </BaseCard>
  </div>
</template>
