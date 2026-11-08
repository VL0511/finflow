<script setup lang="ts">
import { KeyRound, Plus, Search, UserPlus, Users } from '@lucide/vue'
import { computed, reactive, ref, shallowRef } from 'vue'
import { ROLE_LABELS, type Role } from '@/features/auth/permissions'
import { useAuthStore } from '@/features/auth/stores/useAuthStore'
import { isApiError, normalizeApiError, type ApiError } from '@/shared/api'
import PageHeader from '@/shared/components/PageHeader.vue'
import BaseBadge from '@/shared/components/ui/BaseBadge.vue'
import BaseButton from '@/shared/components/ui/BaseButton.vue'
import BaseCard from '@/shared/components/ui/BaseCard.vue'
import EmptyState from '@/shared/components/ui/EmptyState.vue'
import ErrorState from '@/shared/components/ui/ErrorState.vue'
import FormField from '@/shared/components/ui/FormField.vue'
import InlineAlert from '@/shared/components/ui/InlineAlert.vue'
import PaginationBar from '@/shared/components/ui/PaginationBar.vue'
import SkeletonBlock from '@/shared/components/ui/SkeletonBlock.vue'
import { usePagedList } from '@/shared/composables/usePagedList'
import { formatDate } from '@/shared/utils/formatters'
import { adminApi, type AdminUser } from '../api/adminApi'

/** Usuários e perfis (somente Administrator). Ninguém altera o próprio perfil nem se desativa. */
const auth = useAuthStore()
const roles = Object.keys(ROLE_LABELS) as Role[]

const list = usePagedList<AdminUser, { search: string; role: string }>({
  filters: { search: '', role: '' },
  fetchPage: (params, signal) =>
    adminApi.listUsers({ page: params.page, pageSize: params.pageSize, search: params.search || undefined, role: params.role || undefined }, signal),
})
void list.load()

const search = ref('')
const actionError = shallowRef<ApiError | null>(null)
const notice = ref<string | null>(null)

function fail(error: unknown) {
  actionError.value = isApiError(error) ? error : normalizeApiError(error)
}

async function changeRole(user: AdminUser, role: string) {
  if (role === user.role) return
  actionError.value = null
  try {
    await adminApi.updateUser(user.id, { role })
    notice.value = `${user.email} agora é ${ROLE_LABELS[role as Role] ?? role}. As sessões abertas dele foram encerradas.`
    await list.refresh({ force: true })
  } catch (error) {
    fail(error)
    await list.refresh({ force: true })
  }
}

async function toggleActive(user: AdminUser) {
  if (user.isActive && !window.confirm(`Desativar ${user.email}? A sessão dele será encerrada.`)) return
  actionError.value = null
  try {
    await adminApi.updateUser(user.id, { isActive: !user.isActive })
    await list.refresh({ force: true })
  } catch (error) {
    fail(error)
  }
}

async function resetPassword(user: AdminUser) {
  const password = window.prompt(`Nova senha temporária para ${user.email} (mín. 10 caracteres, maiúscula, minúscula e número). A troca será exigida no próximo login.`)
  if (!password) return
  actionError.value = null
  try {
    await adminApi.resetPassword(user.id, password)
    notice.value = `Senha de ${user.email} redefinida. Informe-a por um canal seguro.`
    await list.refresh({ force: true })
  } catch (error) {
    fail(error)
  }
}

// ---- novo usuário ----------------------------------------------------------

const showForm = ref(false)
const form = reactive({ email: '', fullName: '', password: '', role: 'Customer' as Role, requirePasswordChange: true })
const formErrors = ref<Record<string, string | undefined>>({})
const formError = shallowRef<ApiError | null>(null)
const saving = ref(false)

async function createUser() {
  formError.value = null
  formErrors.value = {
    email: /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(form.email.trim()) ? undefined : 'E-mail inválido.',
    fullName: form.fullName.trim() ? undefined : 'Nome obrigatório.',
    password: form.password ? undefined : 'Senha obrigatória.',
  }
  if (Object.values(formErrors.value).some(Boolean)) return

  saving.value = true
  try {
    const created = await adminApi.createUser({ ...form, email: form.email.trim(), fullName: form.fullName.trim() })
    notice.value = `Usuário ${created.email} criado como ${ROLE_LABELS[created.role as Role] ?? created.role}.`
    Object.assign(form, { email: '', fullName: '', password: '', role: 'Customer', requirePasswordChange: true })
    showForm.value = false
    await list.refresh({ force: true })
  } catch (error) {
    const apiError = isApiError(error) ? error : normalizeApiError(error)
    formErrors.value = {
      email: apiError.fieldError('email') ?? (apiError.status === 409 ? apiError.detail ?? undefined : undefined),
      fullName: apiError.fieldError('fullName'),
      password: apiError.fieldError('password'),
      role: apiError.fieldError('role'),
    }
    formError.value = Object.values(formErrors.value).some(Boolean) ? null : apiError
  } finally {
    saving.value = false
  }
}

const isSelf = (user: AdminUser) => user.id === auth.user?.id
const roleFilter = computed({
  get: () => list.filters.role,
  set: (role: string) => void list.setFilters({ role }),
})
</script>

<template>
  <div class="space-y-6">
    <PageHeader title="Usuários" description="Perfis de acesso: Administrador, Tesouraria e Cliente.">
      <template #actions>
        <BaseButton variant="primary" :icon="UserPlus" @click="showForm = !showForm">Novo usuário</BaseButton>
      </template>
    </PageHeader>

    <InlineAlert v-if="notice" tone="positive">{{ notice }}</InlineAlert>
    <InlineAlert v-if="actionError" tone="critical" :title="actionError.title">{{ actionError.detail }}</InlineAlert>

    <BaseCard v-if="showForm" title="Novo usuário">
      <form class="space-y-4" novalidate @submit.prevent="createUser">
        <InlineAlert v-if="formError" tone="critical" :title="formError.title">{{ formError.detail }}</InlineAlert>
        <div class="grid grid-cols-1 gap-4 sm:grid-cols-2">
          <FormField id="user-email" v-slot="field" label="E-mail" :error="formErrors.email">
            <input :id="field.id" v-model="form.email" type="email" class="field-control" autocomplete="off"
              :aria-invalid="field.invalid" :aria-describedby="field.describedBy" />
          </FormField>
          <FormField id="user-name" v-slot="field" label="Nome completo" :error="formErrors.fullName">
            <input :id="field.id" v-model="form.fullName" class="field-control" maxlength="100"
              :aria-invalid="field.invalid" :aria-describedby="field.describedBy" />
          </FormField>
          <FormField id="user-password" v-slot="field" label="Senha inicial" hint="Mín. 10 caracteres, com maiúscula, minúscula e número." :error="formErrors.password">
            <input :id="field.id" v-model="form.password" type="password" class="field-control" autocomplete="new-password"
              :aria-invalid="field.invalid" :aria-describedby="field.describedBy" />
          </FormField>
          <FormField id="user-role" v-slot="field" label="Perfil" :error="formErrors.role">
            <select :id="field.id" v-model="form.role" class="field-control">
              <option v-for="role in roles" :key="role" :value="role">{{ ROLE_LABELS[role] }}</option>
            </select>
          </FormField>
        </div>
        <label class="flex items-center gap-2 text-[13px] text-ink-secondary">
          <input v-model="form.requirePasswordChange" type="checkbox" class="size-4 accent-brand-600" />
          Exigir troca de senha no primeiro login (recomendado)
        </label>
        <div class="flex justify-end gap-2 border-t border-line pt-4">
          <BaseButton @click="showForm = false">Cancelar</BaseButton>
          <BaseButton type="submit" variant="primary" :icon="Plus" :loading="saving">Criar usuário</BaseButton>
        </div>
      </form>
    </BaseCard>

    <BaseCard flush>
      <template #header>
        <form class="flex flex-1 flex-col gap-2 sm:flex-row" @submit.prevent="list.setFilters({ search: search.trim() })">
          <label class="sr-only" for="user-search">Buscar</label>
          <input id="user-search" v-model="search" class="field-control sm:max-w-xs" placeholder="Buscar por e-mail ou nome" />
          <BaseButton type="submit" :icon="Search">Buscar</BaseButton>
          <label class="sr-only" for="user-role-filter">Perfil</label>
          <select id="user-role-filter" v-model="roleFilter" class="field-control sm:max-w-[12rem]">
            <option value="">Todos os perfis</option>
            <option v-for="role in roles" :key="role" :value="role">{{ ROLE_LABELS[role] }}</option>
          </select>
        </form>
      </template>

      <div v-if="list.isInitialLoading" class="space-y-2 p-5"><SkeletonBlock class="h-10" /><SkeletonBlock class="h-10" /></div>
      <ErrorState v-else-if="list.error && !list.hasLoaded" compact :error="list.error" @retry="list.refresh({ force: true })" />
      <EmptyState v-else-if="list.isEmpty" :icon="Users" title="Nenhum usuário encontrado" />
      <div v-else class="overflow-x-auto">
        <table class="w-full text-sm">
          <thead class="border-b border-line text-left text-xs text-ink-muted">
            <tr>
              <th class="px-5 py-2.5 font-medium sm:px-6">Usuário</th>
              <th class="px-3 py-2.5 font-medium">Perfil</th>
              <th class="px-3 py-2.5 font-medium">Situação</th>
              <th class="px-3 py-2.5 font-medium">Criado</th>
              <th class="px-5 py-2.5 sm:px-6"><span class="sr-only">Ações</span></th>
            </tr>
          </thead>
          <tbody class="divide-y divide-line">
            <tr v-for="user in list.items" :key="user.id">
              <td class="px-5 py-3 sm:px-6">
                <span class="block font-medium text-ink">{{ user.fullName }}</span>
                <span class="block text-xs text-ink-muted">{{ user.email }}<template v-if="isSelf(user)"> · você</template></span>
              </td>
              <td class="px-3 py-3">
                <select
                  :value="user.role"
                  class="field-control h-8 w-auto py-0 text-[13px]"
                  :disabled="isSelf(user)"
                  :title="isSelf(user) ? 'Você não pode alterar o próprio perfil.' : undefined"
                  :aria-label="`Perfil de ${user.email}`"
                  @change="changeRole(user, ($event.target as HTMLSelectElement).value)"
                >
                  <option v-for="role in roles" :key="role" :value="role">{{ ROLE_LABELS[role] }}</option>
                </select>
              </td>
              <td class="px-3 py-3">
                <span class="flex flex-wrap gap-1">
                  <BaseBadge :tone="user.isActive ? 'positive' : 'neutral'" dot>{{ user.isActive ? 'Ativo' : 'Inativo' }}</BaseBadge>
                  <BaseBadge v-if="user.isLockedOut" tone="warning">Bloqueado</BaseBadge>
                  <BaseBadge v-if="user.mustChangePassword" tone="info">Troca de senha pendente</BaseBadge>
                </span>
              </td>
              <td class="px-3 py-3 text-ink-muted">{{ formatDate(user.createdAt) }}</td>
              <td class="px-5 py-3 text-right whitespace-nowrap sm:px-6">
                <BaseButton size="sm" variant="ghost" :icon="KeyRound" @click="resetPassword(user)">Redefinir senha</BaseButton>
                <BaseButton v-if="!isSelf(user)" size="sm" variant="ghost" @click="toggleActive(user)">
                  {{ user.isActive ? 'Desativar' : 'Ativar' }}
                </BaseButton>
              </td>
            </tr>
          </tbody>
        </table>
      </div>
      <PaginationBar
        v-if="!list.isEmpty && list.hasLoaded"
        id="users-pagination"
        item-label="usuários"
        :page="list.page"
        :page-size="list.pageSize"
        :total-items="list.totalItems"
        :total-pages="list.totalPages"
        :has-previous-page="list.hasPreviousPage"
        :has-next-page="list.hasNextPage"
        :range-start="list.range.start"
        :range-end="list.range.end"
        :loading="list.isLoading"
        @update:page="list.setPage"
        @update:page-size="list.setPageSize"
      />
    </BaseCard>
  </div>
</template>
