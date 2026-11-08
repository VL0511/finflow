<script setup lang="ts">
import { Ban, History, Plus, RefreshCw, WalletCards } from '@lucide/vue'
import { computed, reactive, ref, shallowRef } from 'vue'
import { useAuthStore } from '@/features/auth/stores/useAuthStore'
import { isApiError, normalizeApiError, useRequestState, type ApiError } from '@/shared/api'
import type { PagedResult } from '@/shared/api/pagination'
import PageHeader from '@/shared/components/PageHeader.vue'
import BaseButton from '@/shared/components/ui/BaseButton.vue'
import BaseCard from '@/shared/components/ui/BaseCard.vue'
import EmptyState from '@/shared/components/ui/EmptyState.vue'
import ErrorState from '@/shared/components/ui/ErrorState.vue'
import FormField from '@/shared/components/ui/FormField.vue'
import InlineAlert from '@/shared/components/ui/InlineAlert.vue'
import SkeletonBlock from '@/shared/components/ui/SkeletonBlock.vue'
import { formatDateTime } from '@/shared/utils/formatters'
import { exchangeRatesApi, type ExchangeRate } from '../api/currenciesApi'
import { useCurrenciesStore } from '../stores/useCurrenciesStore'

/**
 * Taxas vigentes (todos os perfis). Administrator também define novas taxas, encerra taxas e
 * consulta o histórico. Taxas nunca são editadas: cada alteração é uma nova versão.
 */
const auth = useAuthStore()
const currencies = useCurrenciesStore()
const isAdmin = computed(() => auth.hasCapability('administration'))

const rates = shallowRef<ExchangeRate[]>([])
const ratesState = useRequestState()
function loadRates() {
  return ratesState.run(async () => {
    rates.value = await exchangeRatesApi.listActive()
  })
}
void loadRates()
void currencies.ensureActive()

const rateFormatter = new Intl.NumberFormat('pt-BR', { minimumFractionDigits: 2, maximumFractionDigits: 8 })

// ---- definir taxa (Administrator) -----------------------------------------

const form = reactive({ fromCurrency: 'USD', toCurrency: 'BRL', rate: '' })
const formErrors = ref<{ rate?: string; toCurrency?: string }>({})
const formError = shallowRef<ApiError | null>(null)
const saving = ref(false)
const saved = ref<string | null>(null)

/** Taxa digitada com vírgula ou ponto decimal, até 8 casas. Texto → número só no envio. */
function parseRate(text: string): number | null {
  const normalized = text.trim().replace(/\s/g, '').replace(',', '.')
  if (!/^\d+(\.\d{1,8})?$/.test(normalized)) return null
  const value = Number(normalized)
  return value > 0 ? value : null
}

async function saveRate() {
  saved.value = null
  formError.value = null
  const rate = parseRate(form.rate)
  formErrors.value = {
    rate: rate === null ? 'Informe uma taxa maior que zero, com até 8 casas decimais (ex.: 5,20).' : undefined,
    toCurrency: form.fromCurrency === form.toCurrency ? 'Escolha moedas diferentes.' : undefined,
  }
  if (rate === null || form.fromCurrency === form.toCurrency) return

  saving.value = true
  try {
    const created = await exchangeRatesApi.set({ fromCurrency: form.fromCurrency, toCurrency: form.toCurrency, rate })
    saved.value = `Taxa ${created.fromCurrency} → ${created.toCurrency} = ${rateFormatter.format(created.rate)} em vigor.`
    form.rate = ''
    await loadRates()
    if (history.value) await loadHistory(1)
  } catch (error) {
    const apiError = isApiError(error) ? error : normalizeApiError(error)
    formErrors.value = { rate: apiError.fieldError('rate'), toCurrency: apiError.fieldError('toCurrency') }
    formError.value = apiError.isValidationError ? null : apiError
  } finally {
    saving.value = false
  }
}

async function deactivate(rate: ExchangeRate) {
  if (!window.confirm(`Encerrar a taxa ${rate.fromCurrency} → ${rate.toCurrency}? Conversões nessa direção passarão a ser recusadas.`)) return
  try {
    await exchangeRatesApi.deactivate(rate.fromCurrency, rate.toCurrency)
    await loadRates()
  } catch (error) {
    formError.value = isApiError(error) ? error : normalizeApiError(error)
  }
}

// ---- histórico (Administrator) ----------------------------------------------

const history = shallowRef<PagedResult<ExchangeRate> | null>(null)
const historyState = useRequestState()
function loadHistory(page = 1) {
  return historyState.run(async () => {
    history.value = await exchangeRatesApi.history({ page, pageSize: 20 })
  })
}
</script>

<template>
  <div class="space-y-6">
    <PageHeader title="Câmbio" description="Taxas configuradas manualmente. A direção é explícita: USD → BRL não define BRL → USD.">
      <template #actions>
        <BaseButton :icon="RefreshCw" :loading="ratesState.isRefreshing.value" @click="loadRates">Atualizar</BaseButton>
      </template>
    </PageHeader>

    <BaseCard title="Taxas vigentes" description="Usadas em novas transferências entre moedas. Transferências já criadas guardam a taxa da época." flush>
      <div v-if="ratesState.isInitialLoading.value" class="space-y-2 p-5"><SkeletonBlock class="h-10" /><SkeletonBlock class="h-10" /></div>
      <ErrorState v-else-if="ratesState.error.value && !rates.length" compact :error="ratesState.error.value" @retry="loadRates" />
      <EmptyState v-else-if="!rates.length" :icon="WalletCards" title="Nenhuma taxa vigente" description="Sem taxa, transferências entre moedas diferentes são recusadas." />
      <div v-else class="overflow-x-auto">
        <table class="w-full text-sm">
          <thead class="border-b border-line text-left text-xs text-ink-muted">
            <tr>
              <th class="px-5 py-2.5 font-medium sm:px-6">Direção</th>
              <th class="px-3 py-2.5 text-right font-medium">Taxa</th>
              <th class="px-3 py-2.5 font-medium">Exemplo</th>
              <th class="px-3 py-2.5 font-medium">Desde</th>
              <th v-if="isAdmin" class="px-5 py-2.5 sm:px-6"><span class="sr-only">Ações</span></th>
            </tr>
          </thead>
          <tbody class="divide-y divide-line">
            <tr v-for="rate in rates" :key="rate.id">
              <td class="px-5 py-3 font-medium text-ink sm:px-6">{{ rate.fromCurrency }} → {{ rate.toCurrency }}</td>
              <td class="px-3 py-3 text-right font-semibold text-ink tabular">{{ rateFormatter.format(rate.rate) }}</td>
              <td class="px-3 py-3 text-ink-muted tabular">1 {{ rate.fromCurrency }} = {{ rateFormatter.format(rate.rate) }} {{ rate.toCurrency }}</td>
              <td class="px-3 py-3 text-ink-muted">{{ formatDateTime(rate.createdAt) }}<span v-if="rate.createdByEmail"> · {{ rate.createdByEmail }}</span></td>
              <td v-if="isAdmin" class="px-5 py-3 text-right sm:px-6">
                <BaseButton size="sm" variant="ghost" :icon="Ban" @click="deactivate(rate)">Encerrar</BaseButton>
              </td>
            </tr>
          </tbody>
        </table>
      </div>
    </BaseCard>

    <BaseCard v-if="isAdmin" title="Definir taxa" description="Cria uma nova versão vigente; a anterior fica no histórico, inalterada.">
      <form class="space-y-4" novalidate @submit.prevent="saveRate">
        <InlineAlert v-if="saved" tone="positive">{{ saved }}</InlineAlert>
        <InlineAlert v-if="formError" tone="critical" :title="formError.title">{{ formError.detail }}</InlineAlert>
        <div class="grid grid-cols-1 gap-4 sm:grid-cols-3">
          <FormField id="rate-from" v-slot="field" label="De">
            <select :id="field.id" v-model="form.fromCurrency" class="field-control">
              <option v-for="c in currencies.active" :key="c.code" :value="c.code">{{ c.code }} — {{ c.name }}</option>
            </select>
          </FormField>
          <FormField id="rate-to" v-slot="field" label="Para" :error="formErrors.toCurrency">
            <select :id="field.id" v-model="form.toCurrency" class="field-control" :aria-invalid="field.invalid" :aria-describedby="field.describedBy">
              <option v-for="c in currencies.active" :key="c.code" :value="c.code">{{ c.code }} — {{ c.name }}</option>
            </select>
          </FormField>
          <FormField id="rate-value" v-slot="field" label="Taxa" hint="Até 8 casas decimais." :error="formErrors.rate">
            <input :id="field.id" v-model="form.rate" class="field-control tabular" inputmode="decimal" placeholder="5,20"
              autocomplete="off" :aria-invalid="field.invalid" :aria-describedby="field.describedBy" />
          </FormField>
        </div>
        <p v-if="parseRate(form.rate)" class="text-xs text-ink-muted">
          100 {{ form.fromCurrency }} → {{ (100 * (parseRate(form.rate) ?? 0)).toLocaleString('pt-BR', { maximumFractionDigits: 6 }) }}
          {{ form.toCurrency }} (antes do arredondamento da moeda de destino)
        </p>
        <div class="flex justify-end border-t border-line pt-4">
          <BaseButton type="submit" variant="primary" :icon="Plus" :loading="saving">Salvar taxa</BaseButton>
        </div>
      </form>
    </BaseCard>

    <BaseCard v-if="isAdmin" title="Histórico de alterações" description="Todas as versões, vigentes e encerradas." flush>
      <div v-if="!history" class="p-5">
        <BaseButton :icon="History" :loading="historyState.isLoading.value" @click="loadHistory(1)">Carregar histórico</BaseButton>
      </div>
      <div v-else class="overflow-x-auto">
        <table class="w-full text-sm">
          <thead class="border-b border-line text-left text-xs text-ink-muted">
            <tr>
              <th class="px-5 py-2.5 font-medium sm:px-6">Direção</th>
              <th class="px-3 py-2.5 text-right font-medium">Taxa</th>
              <th class="px-3 py-2.5 font-medium">Criada</th>
              <th class="px-3 py-2.5 font-medium">Responsável</th>
              <th class="px-5 py-2.5 font-medium sm:px-6">Situação</th>
            </tr>
          </thead>
          <tbody class="divide-y divide-line">
            <tr v-for="rate in history.items" :key="rate.id">
              <td class="px-5 py-2.5 sm:px-6">{{ rate.fromCurrency }} → {{ rate.toCurrency }}</td>
              <td class="px-3 py-2.5 text-right tabular">{{ rateFormatter.format(rate.rate) }}</td>
              <td class="px-3 py-2.5 text-ink-muted">{{ formatDateTime(rate.createdAt) }}</td>
              <td class="px-3 py-2.5 text-ink-muted">{{ rate.createdByEmail ?? rate.createdByUserId }}</td>
              <td class="px-5 py-2.5 sm:px-6">
                <span v-if="rate.isActive" class="text-positive">Vigente</span>
                <span v-else class="text-ink-muted">Encerrada {{ rate.supersededAt ? formatDateTime(rate.supersededAt) : '' }}</span>
              </td>
            </tr>
          </tbody>
        </table>
        <div class="flex items-center justify-between border-t border-line px-5 py-3 text-xs text-ink-muted sm:px-6">
          <span>Página {{ history.page }} de {{ history.totalPages || 1 }} · {{ history.totalItems }} versões</span>
          <span class="flex gap-2">
            <BaseButton size="sm" :disabled="!history.hasPreviousPage" @click="loadHistory(history.page - 1)">Anterior</BaseButton>
            <BaseButton size="sm" :disabled="!history.hasNextPage" @click="loadHistory(history.page + 1)">Próxima</BaseButton>
          </span>
        </div>
      </div>
    </BaseCard>
  </div>
</template>
