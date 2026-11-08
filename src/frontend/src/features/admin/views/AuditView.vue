<script setup lang="ts">
import { ScrollText, Search } from '@lucide/vue'
import { reactive, ref } from 'vue'
import PageHeader from '@/shared/components/PageHeader.vue'
import BaseBadge from '@/shared/components/ui/BaseBadge.vue'
import BaseButton from '@/shared/components/ui/BaseButton.vue'
import BaseCard from '@/shared/components/ui/BaseCard.vue'
import EmptyState from '@/shared/components/ui/EmptyState.vue'
import ErrorState from '@/shared/components/ui/ErrorState.vue'
import PaginationBar from '@/shared/components/ui/PaginationBar.vue'
import SkeletonBlock from '@/shared/components/ui/SkeletonBlock.vue'
import { usePagedList } from '@/shared/composables/usePagedList'
import { formatDateTime } from '@/shared/utils/formatters'
import { adminApi, type AuditLog, type AuditQuery } from '../api/adminApi'

/** Trilha de auditoria (somente leitura). Os registros não podem ser alterados nem removidos. */
const ACTIONS = [
  'TransferRequested', 'TransferCompleted', 'TransferFailed', 'TransferCancelled', 'AccountCreated',
  'ExchangeRateSet', 'ExchangeRateDeactivated', 'CurrencyCreated', 'CurrencyUpdated', 'CurrencyActivated', 'CurrencyDeactivated',
  'UserCreated', 'UserUpdated', 'UserActivated', 'UserDeactivated', 'UserRoleChanged', 'UserPasswordReset',
  'PasswordChanged', 'LoginSucceeded', 'LoginFailed',
]

const list = usePagedList<AuditLog, AuditQuery>({
  filters: {},
  pageSize: 20,
  fetchPage: (params, signal) => adminApi.auditLogs(params, signal),
})
void list.load()

const filters = reactive({ action: '', entityType: '', entityId: '' })
function apply() {
  void list.setFilters({
    action: filters.action || undefined,
    entityType: filters.entityType || undefined,
    entityId: filters.entityId.trim() || undefined,
  })
}

const expanded = ref<string | null>(null)
function pretty(details: string): string {
  try {
    return JSON.stringify(JSON.parse(details), null, 2)
  } catch {
    return details
  }
}
</script>

<template>
  <div class="space-y-6">
    <PageHeader title="Auditoria" description="Operações financeiras e administrativas, com responsável, data e resultado. Somente leitura." />

    <BaseCard flush>
      <template #header>
        <form class="grid flex-1 grid-cols-1 gap-2 sm:grid-cols-4" @submit.prevent="apply">
          <select v-model="filters.action" class="field-control" aria-label="Operação">
            <option value="">Todas as operações</option>
            <option v-for="action in ACTIONS" :key="action" :value="action">{{ action }}</option>
          </select>
          <select v-model="filters.entityType" class="field-control" aria-label="Entidade">
            <option value="">Todas as entidades</option>
            <option value="Transaction">Transação</option>
            <option value="Account">Conta</option>
            <option value="User">Usuário</option>
            <option value="Currency">Moeda</option>
            <option value="ExchangeRate">Taxa de câmbio</option>
          </select>
          <input v-model="filters.entityId" class="field-control" placeholder="ID da entidade" aria-label="ID da entidade" />
          <BaseButton type="submit" :icon="Search">Filtrar</BaseButton>
        </form>
      </template>

      <div v-if="list.isInitialLoading" class="space-y-2 p-5"><SkeletonBlock class="h-10" /><SkeletonBlock class="h-10" /></div>
      <ErrorState v-else-if="list.error && !list.hasLoaded" compact :error="list.error" @retry="list.refresh({ force: true })" />
      <EmptyState v-else-if="list.isEmpty" :icon="ScrollText" title="Nenhum registro" />
      <ul v-else class="divide-y divide-line">
        <li v-for="log in list.items" :key="log.id" class="px-5 py-3 sm:px-6">
          <button type="button" class="flex w-full flex-wrap items-center gap-x-3 gap-y-1 text-left" :aria-expanded="expanded === log.id"
            @click="expanded = expanded === log.id ? null : log.id">
            <span class="w-36 shrink-0 text-xs text-ink-muted tabular">{{ formatDateTime(log.occurredAt) }}</span>
            <span class="font-medium text-ink">{{ log.action }}</span>
            <BaseBadge :tone="log.result === 'Succeeded' ? 'positive' : 'critical'">{{ log.result }}</BaseBadge>
            <span class="text-xs text-ink-muted">{{ log.entityType }}<template v-if="log.entityId"> · {{ log.entityId }}</template></span>
            <span class="ml-auto text-xs text-ink-secondary">{{ log.userEmail ?? 'Sistema' }}</span>
          </button>
          <pre v-if="expanded === log.id" class="mt-2 overflow-x-auto rounded-lg bg-surface-muted p-3 text-xs text-ink-secondary">{{ pretty(log.details) }}</pre>
        </li>
      </ul>
      <PaginationBar
        v-if="!list.isEmpty && list.hasLoaded"
        id="audit-pagination"
        item-label="registros"
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
