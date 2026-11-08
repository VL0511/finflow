<script setup lang="ts">
import { KeyRound, LogOut } from '@lucide/vue'
import { computed, reactive, ref, shallowRef } from 'vue'
import { useRouter } from 'vue-router'
import BrandLogo from '@/features/layout/components/BrandLogo.vue'
import { isApiError, normalizeApiError, type ApiError } from '@/shared/api'
import BaseButton from '@/shared/components/ui/BaseButton.vue'
import FormField from '@/shared/components/ui/FormField.vue'
import InlineAlert from '@/shared/components/ui/InlineAlert.vue'
import { useAuthStore } from '../stores/useAuthStore'

const auth = useAuthStore()
const router = useRouter()

const values = reactive({ currentPassword: '', newPassword: '', confirmation: '' })
const errors = ref<{ currentPassword?: string; newPassword?: string; confirmation?: string }>({})
const submitError = shallowRef<ApiError | null>(null)
const isSubmitting = ref(false)

/** Mesma política do Identity no backend (que é quem decide). */
const rules = computed(() => [
  { ok: values.newPassword.length >= 10, text: 'Ao menos 10 caracteres' },
  { ok: /[a-z]/.test(values.newPassword), text: 'Uma letra minúscula' },
  { ok: /[A-Z]/.test(values.newPassword), text: 'Uma letra maiúscula' },
  { ok: /\d/.test(values.newPassword), text: 'Um número' },
])

function validate(): boolean {
  const next: typeof errors.value = {}
  if (!values.currentPassword) next.currentPassword = 'Informe a senha atual.'
  if (rules.value.some((r) => !r.ok)) next.newPassword = 'A nova senha não atende à política.'
  else if (values.newPassword === values.currentPassword) next.newPassword = 'A nova senha deve ser diferente da atual.'
  if (values.confirmation !== values.newPassword) next.confirmation = 'As senhas não conferem.'
  errors.value = next
  return Object.keys(next).length === 0
}

async function onSubmit() {
  if (isSubmitting.value || !validate()) return
  submitError.value = null
  isSubmitting.value = true
  try {
    await auth.changePassword({ currentPassword: values.currentPassword, newPassword: values.newPassword })
    await router.replace({ name: 'dashboard' })
  } catch (error) {
    const apiError = isApiError(error) ? error : normalizeApiError(error)
    errors.value = {
      currentPassword: apiError.fieldError('currentPassword'),
      newPassword: apiError.fieldError('newPassword'),
    }
    submitError.value = apiError.isValidationError ? null : apiError
  } finally {
    isSubmitting.value = false
  }
}
</script>

<template>
  <div class="flex min-h-dvh items-center justify-center bg-canvas px-4 py-10">
    <div class="w-full max-w-sm">
      <div class="mb-8 flex justify-center">
        <BrandLogo />
      </div>

      <div class="rounded-2xl border border-line bg-surface p-6 shadow-card sm:p-8">
        <h1 class="text-lg font-semibold tracking-tight text-ink">Defina uma nova senha</h1>
        <p class="mt-1 text-[13px] text-ink-muted">
          <template v-if="auth.mustChangePassword">Por segurança, a troca é obrigatória antes do primeiro acesso.</template>
          <template v-else>Altere a senha da sua conta.</template>
        </p>

        <form class="mt-6 space-y-4" novalidate @submit.prevent="onSubmit">
          <InlineAlert v-if="submitError" tone="critical" :title="submitError.title">{{ submitError.detail }}</InlineAlert>

          <FormField id="current-password" v-slot="field" label="Senha atual" :error="errors.currentPassword">
            <input :id="field.id" v-model="values.currentPassword" type="password" class="field-control"
              autocomplete="current-password" :aria-invalid="field.invalid" :aria-describedby="field.describedBy" />
          </FormField>

          <FormField id="new-password" v-slot="field" label="Nova senha" :error="errors.newPassword">
            <input :id="field.id" v-model="values.newPassword" type="password" class="field-control"
              autocomplete="new-password" :aria-invalid="field.invalid" :aria-describedby="field.describedBy" />
          </FormField>

          <ul class="grid grid-cols-2 gap-x-3 gap-y-1 text-xs" aria-label="Requisitos da senha">
            <li v-for="rule in rules" :key="rule.text" :class="rule.ok ? 'text-positive' : 'text-ink-muted'">
              {{ rule.ok ? '✓' : '•' }} {{ rule.text }}
            </li>
          </ul>

          <FormField id="confirm-password" v-slot="field" label="Confirme a nova senha" :error="errors.confirmation">
            <input :id="field.id" v-model="values.confirmation" type="password" class="field-control"
              autocomplete="new-password" :aria-invalid="field.invalid" :aria-describedby="field.describedBy" />
          </FormField>

          <BaseButton type="submit" variant="primary" :icon="KeyRound" :loading="isSubmitting" class="w-full">
            Salvar nova senha
          </BaseButton>
          <BaseButton variant="ghost" :icon="LogOut" class="w-full" @click="auth.logout()">Sair</BaseButton>
        </form>
      </div>
    </div>
  </div>
</template>
