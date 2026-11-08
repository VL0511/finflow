<script setup lang="ts">
import { LogIn } from '@lucide/vue'
import { computed, reactive, ref, shallowRef } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { safeRedirect } from '@/app/router/guard'
import BrandLogo from '@/features/layout/components/BrandLogo.vue'
import { isApiError, normalizeApiError, type ApiError } from '@/shared/api'
import BaseButton from '@/shared/components/ui/BaseButton.vue'
import FormField from '@/shared/components/ui/FormField.vue'
import InlineAlert from '@/shared/components/ui/InlineAlert.vue'
import { useAuthStore } from '../stores/useAuthStore'

const auth = useAuthStore()
const route = useRoute()
const router = useRouter()

const values = reactive({ email: '', password: '', rememberMe: false })
const errors = ref<{ email?: string; password?: string }>({})
const submitError = shallowRef<ApiError | null>(null)
const isSubmitting = ref(false)

const expired = computed(() => route.query.expirada === '1')

function validate(): boolean {
  const next: typeof errors.value = {}
  if (!values.email.trim()) next.email = 'Informe o e-mail.'
  else if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(values.email.trim())) next.email = 'E-mail inválido.'
  if (!values.password) next.password = 'Informe a senha.'
  errors.value = next
  return Object.keys(next).length === 0
}

async function onSubmit() {
  if (isSubmitting.value || !validate()) return
  submitError.value = null
  isSubmitting.value = true
  try {
    const me = await auth.login({ email: values.email.trim(), password: values.password, rememberMe: values.rememberMe })
    // Primeiro login: a troca de senha é obrigatória antes de qualquer outra tela.
    await router.replace(me.mustChangePassword ? { name: 'change-password' } : safeRedirect(route.query.redirect))
  } catch (error) {
    submitError.value = isApiError(error) ? error : normalizeApiError(error)
    values.password = ''
  } finally {
    isSubmitting.value = false
  }
}

const errorMessage = computed(() => {
  const error = submitError.value
  if (!error) return null
  if (error.status === 401) return 'E-mail ou senha inválidos.'
  if (error.status === 423) return 'Usuário bloqueado temporariamente por excesso de tentativas. Tente novamente mais tarde.'
  if (error.status === 429) return 'Muitas tentativas a partir deste dispositivo. Aguarde um minuto.'
  return error.detail ?? error.title
})
</script>

<template>
  <div class="flex min-h-dvh items-center justify-center bg-canvas px-4 py-10">
    <div class="w-full max-w-sm">
      <div class="mb-8 flex justify-center">
        <BrandLogo />
      </div>

      <div class="rounded-2xl border border-line bg-surface p-6 shadow-card sm:p-8">
        <h1 class="text-lg font-semibold tracking-tight text-ink">Entrar no FinFlow</h1>
        <p class="mt-1 text-[13px] text-ink-muted">Use o e-mail e a senha fornecidos pelo administrador.</p>

        <form class="mt-6 space-y-4" novalidate @submit.prevent="onSubmit">
          <InlineAlert v-if="expired && !submitError" tone="warning">Sua sessão expirou. Entre novamente.</InlineAlert>
          <InlineAlert v-if="errorMessage" tone="critical">{{ errorMessage }}</InlineAlert>

          <FormField id="login-email" v-slot="field" label="E-mail" :error="errors.email">
            <input
              :id="field.id"
              v-model="values.email"
              type="email"
              class="field-control"
              autocomplete="username"
              autofocus
              :aria-invalid="field.invalid"
              :aria-describedby="field.describedBy"
            />
          </FormField>

          <FormField id="login-password" v-slot="field" label="Senha" :error="errors.password">
            <input
              :id="field.id"
              v-model="values.password"
              type="password"
              class="field-control"
              autocomplete="current-password"
              :aria-invalid="field.invalid"
              :aria-describedby="field.describedBy"
            />
          </FormField>

          <label class="flex items-center gap-2 text-[13px] text-ink-secondary">
            <input v-model="values.rememberMe" type="checkbox" class="size-4 accent-brand-600" />
            Manter conectado neste dispositivo
          </label>

          <BaseButton type="submit" variant="primary" :icon="LogIn" :loading="isSubmitting" class="w-full">Entrar</BaseButton>
        </form>
      </div>
    </div>
  </div>
</template>
