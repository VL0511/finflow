<script setup lang="ts">
import { ArrowLeft, Check } from '@lucide/vue'
import { shallowRef } from 'vue'
import { useRouter } from 'vue-router'
import { adminApi, type AdminUser } from '@/features/admin/api/adminApi'
import { useAuthStore } from '@/features/auth/stores/useAuthStore'
import { useCurrenciesStore } from '@/features/currencies/stores/useCurrenciesStore'
import PageHeader from '@/shared/components/PageHeader.vue'
import BaseButton from '@/shared/components/ui/BaseButton.vue'
import BaseCard from '@/shared/components/ui/BaseCard.vue'
import FormField from '@/shared/components/ui/FormField.vue'
import InlineAlert from '@/shared/components/ui/InlineAlert.vue'
import MoneyInput from '@/shared/components/ui/MoneyInput.vue'
import { useCreateAccountForm } from '../composables/useCreateAccountForm'

const router = useRouter()
const auth = useAuthStore()
const currencies = useCurrenciesStore()
void currencies.ensureActive()

const { values, errors, submitError, isSubmitting, decimals, submit } = useCreateAccountForm()
const isAdmin = auth.hasCapability('administration')

// Administrator escolhe o dono entre os clientes ativos (até 100; o backend valida o usuário).
const customers = shallowRef<AdminUser[]>([])
if (isAdmin) {
  adminApi
    .listUsers({ page: 1, pageSize: 100, role: 'Customer' })
    .then((page) => (customers.value = page.items.filter((u) => u.isActive)))
    .catch(() => (customers.value = []))
}

async function onSubmit() {
  const account = await submit()
  if (account) await router.push({ name: 'account-details', params: { id: account.id }, query: { nova: '1' } })
}
</script>

<template>
  <div class="mx-auto max-w-2xl space-y-6">
    <BaseButton variant="ghost" size="sm" :icon="ArrowLeft" :to="{ name: 'accounts' }" class="-ml-2">
      Contas
    </BaseButton>

    <PageHeader title="Nova conta" description="O número da conta é gerado automaticamente e exibido após a criação." />

    <BaseCard>
      <form class="space-y-5" novalidate @submit.prevent="onSubmit">
        <InlineAlert v-if="submitError" tone="critical" :title="submitError.title">
          {{ submitError.detail ?? 'Tente novamente em instantes.' }}
        </InlineAlert>

        <div class="grid grid-cols-1 gap-5 sm:grid-cols-2">
          <FormField id="holder-name" v-slot="field" label="Titular" :error="errors.holderName">
            <input
              :id="field.id"
              v-model="values.holderName"
              class="field-control"
              autocomplete="off"
              maxlength="100"
              :aria-invalid="field.invalid"
              :aria-describedby="field.describedBy"
            />
          </FormField>

          <FormField id="currency" v-slot="field" label="Moeda" hint="O saldo é mantido sempre nesta moeda." :error="errors.currency">
            <select
              :id="field.id"
              v-model="values.currency"
              class="field-control"
              :aria-invalid="field.invalid"
              :aria-describedby="field.describedBy"
            >
              <option v-for="currency in currencies.active" :key="currency.code" :value="currency.code">
                {{ currency.code }} — {{ currency.name }}
              </option>
            </select>
          </FormField>

          <FormField id="initial-balance" v-slot="field" label="Saldo inicial" hint="Em branco = 0." :error="errors.initialBalance">
            <MoneyInput
              :id="field.id"
              v-model="values.initialBalance"
              :currency="values.currency"
              :decimals="decimals"
              :invalid="field.invalid"
              :described-by="field.describedBy"
            />
          </FormField>

          <FormField
            v-if="isAdmin"
            id="owner"
            v-slot="field"
            label="Dono da conta"
            hint="Opcional. Sem dono = conta institucional."
            :error="errors.ownerUserId"
            optional
          >
            <select
              :id="field.id"
              v-model="values.ownerUserId"
              class="field-control"
              :aria-invalid="field.invalid"
              :aria-describedby="field.describedBy"
            >
              <option value="">Conta institucional (sem dono)</option>
              <option v-for="user in customers" :key="user.id" :value="user.id">{{ user.fullName }} · {{ user.email }}</option>
            </select>
          </FormField>
        </div>

        <label class="flex items-start gap-3 rounded-xl border border-line bg-surface-muted p-3.5">
          <input v-model="values.isActive" type="checkbox" class="mt-0.5 size-4 accent-brand-600" />
          <span class="text-sm">
            <span class="font-medium text-ink">Conta ativa</span>
            <span class="block text-xs text-ink-muted">Contas inativas não podem enviar nem receber transferências.</span>
          </span>
        </label>

        <div class="flex flex-col-reverse gap-2 border-t border-line pt-5 sm:flex-row sm:justify-end">
          <BaseButton :to="{ name: 'accounts' }">Cancelar</BaseButton>
          <BaseButton type="submit" variant="primary" :icon="Check" :loading="isSubmitting">Criar conta</BaseButton>
        </div>
      </form>
    </BaseCard>
  </div>
</template>
