<script setup lang="ts">
import { Coins, Plus } from '@lucide/vue'
import { reactive, ref, shallowRef } from 'vue'
import { isApiError, normalizeApiError, useRequestState, type ApiError } from '@/shared/api'
import PageHeader from '@/shared/components/PageHeader.vue'
import BaseBadge from '@/shared/components/ui/BaseBadge.vue'
import BaseButton from '@/shared/components/ui/BaseButton.vue'
import BaseCard from '@/shared/components/ui/BaseCard.vue'
import EmptyState from '@/shared/components/ui/EmptyState.vue'
import ErrorState from '@/shared/components/ui/ErrorState.vue'
import FormField from '@/shared/components/ui/FormField.vue'
import InlineAlert from '@/shared/components/ui/InlineAlert.vue'
import SkeletonBlock from '@/shared/components/ui/SkeletonBlock.vue'
import { currenciesApi, type Currency } from '../api/currenciesApi'
import { useCurrenciesStore } from '../stores/useCurrenciesStore'

/** Cadastro, ativação e desativação de moedas (somente Administrator). */
const store = useCurrenciesStore()
const currencies = shallowRef<Currency[]>([])
const state = useRequestState()
function load() {
  return state.run(async () => {
    currencies.value = await currenciesApi.list(true)
  })
}
void load()

const form = reactive({ code: '', name: '', decimalPlaces: 2 })
const errors = ref<{ code?: string; name?: string; decimalPlaces?: string }>({})
const actionError = shallowRef<ApiError | null>(null)
const saving = ref(false)

async function create() {
  actionError.value = null
  errors.value = {
    code: /^[A-Za-z]{3}$/.test(form.code.trim()) ? undefined : 'Use o código ISO 4217 de 3 letras (ex.: GBP).',
    name: form.name.trim() ? undefined : 'Nome obrigatório.',
  }
  if (errors.value.code || errors.value.name) return

  saving.value = true
  try {
    await currenciesApi.create({ code: form.code.trim().toUpperCase(), name: form.name.trim(), decimalPlaces: form.decimalPlaces })
    form.code = ''
    form.name = ''
    await Promise.all([load(), store.fetchActive()])
  } catch (error) {
    const apiError = isApiError(error) ? error : normalizeApiError(error)
    errors.value = { code: apiError.fieldError('code') ?? (apiError.status === 409 ? apiError.detail ?? undefined : undefined), name: apiError.fieldError('name') }
    actionError.value = errors.value.code || errors.value.name ? null : apiError
  } finally {
    saving.value = false
  }
}

async function toggle(currency: Currency) {
  const verb = currency.isActive ? 'Desativar' : 'Ativar'
  if (currency.isActive && !window.confirm(`${verb} ${currency.code}? Novas contas e transferências nessa moeda serão recusadas.`)) return
  actionError.value = null
  try {
    await currenciesApi.update(currency.code, { isActive: !currency.isActive })
    await Promise.all([load(), store.fetchActive()])
  } catch (error) {
    actionError.value = isApiError(error) ? error : normalizeApiError(error)
  }
}
</script>

<template>
  <div class="space-y-6">
    <PageHeader title="Moedas" description="Moedas aceitas em contas e transferências. Desativar não altera saldos nem transações existentes." />

    <InlineAlert v-if="actionError" tone="critical" :title="actionError.title">{{ actionError.detail }}</InlineAlert>

    <BaseCard title="Cadastradas" flush>
      <div v-if="state.isInitialLoading.value" class="space-y-2 p-5"><SkeletonBlock class="h-10" /><SkeletonBlock class="h-10" /></div>
      <ErrorState v-else-if="state.error.value && !currencies.length" compact :error="state.error.value" @retry="load" />
      <EmptyState v-else-if="!currencies.length" :icon="Coins" title="Nenhuma moeda cadastrada" />
      <table v-else class="w-full text-sm">
        <thead class="border-b border-line text-left text-xs text-ink-muted">
          <tr>
            <th class="px-5 py-2.5 font-medium sm:px-6">Código</th>
            <th class="px-3 py-2.5 font-medium">Nome</th>
            <th class="px-3 py-2.5 font-medium">Casas decimais</th>
            <th class="px-3 py-2.5 font-medium">Situação</th>
            <th class="px-5 py-2.5 sm:px-6"><span class="sr-only">Ações</span></th>
          </tr>
        </thead>
        <tbody class="divide-y divide-line">
          <tr v-for="currency in currencies" :key="currency.code">
            <td class="px-5 py-3 font-semibold text-ink sm:px-6">{{ currency.code }}</td>
            <td class="px-3 py-3 text-ink-secondary">{{ currency.name }}</td>
            <td class="px-3 py-3 text-ink-secondary tabular">{{ currency.decimalPlaces }}</td>
            <td class="px-3 py-3">
              <BaseBadge :tone="currency.isActive ? 'positive' : 'neutral'" dot>{{ currency.isActive ? 'Ativa' : 'Inativa' }}</BaseBadge>
            </td>
            <td class="px-5 py-3 text-right sm:px-6">
              <BaseButton size="sm" variant="ghost" @click="toggle(currency)">{{ currency.isActive ? 'Desativar' : 'Ativar' }}</BaseButton>
            </td>
          </tr>
        </tbody>
      </table>
    </BaseCard>

    <BaseCard title="Nova moeda" description="Depois de cadastrar, defina as taxas de câmbio em Câmbio.">
      <form class="space-y-4" novalidate @submit.prevent="create">
        <div class="grid grid-cols-1 gap-4 sm:grid-cols-3">
          <FormField id="currency-code" v-slot="field" label="Código (ISO 4217)" :error="errors.code">
            <input :id="field.id" v-model="form.code" class="field-control uppercase" maxlength="3" placeholder="GBP"
              autocomplete="off" :aria-invalid="field.invalid" :aria-describedby="field.describedBy" />
          </FormField>
          <FormField id="currency-name" v-slot="field" label="Nome" :error="errors.name">
            <input :id="field.id" v-model="form.name" class="field-control" maxlength="60" placeholder="Libra esterlina"
              :aria-invalid="field.invalid" :aria-describedby="field.describedBy" />
          </FormField>
          <FormField id="currency-decimals" v-slot="field" label="Casas decimais" hint="0 (ex.: JPY) a 2.">
            <select :id="field.id" v-model.number="form.decimalPlaces" class="field-control">
              <option :value="2">2</option>
              <option :value="1">1</option>
              <option :value="0">0</option>
            </select>
          </FormField>
        </div>
        <div class="flex justify-end border-t border-line pt-4">
          <BaseButton type="submit" variant="primary" :icon="Plus" :loading="saving">Cadastrar moeda</BaseButton>
        </div>
      </form>
    </BaseCard>
  </div>
</template>
