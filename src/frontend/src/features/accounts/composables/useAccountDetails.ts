import { computed, reactive, ref, watch, type Ref } from 'vue'
import { useRequestState } from '@/shared/api'
import { useAccountsStore } from '../stores/useAccountsStore'

/**
 * Detalhes de uma conta (GET /accounts/{id}) + consulta de saldo sob demanda
 * (GET /accounts/{id}/balance). Se a conta já estiver no store, ela aparece de
 * imediato enquanto a versão atualizada é buscada.
 */
export function useAccountDetails(id: Ref<string>) {
  const store = useAccountsStore()

  const detailState = useRequestState()
  const balanceState = useRequestState()
  const balanceCheckedAt = ref<string | null>(null)

  const account = computed(() => store.findById(id.value) ?? null)
  const notFound = computed(() => detailState.error.value?.status === 404)

  function load() {
    return detailState.run(async () => {
      await store.fetchAccount(id.value)
      balanceCheckedAt.value = new Date().toISOString()
    })
  }

  function refreshBalance() {
    return balanceState.run(async () => {
      await store.fetchBalance(id.value)
      balanceCheckedAt.value = new Date().toISOString()
    })
  }

  watch(id, () => void load(), { immediate: true })

  return {
    account,
    // reactive(): o template lê detailState.isLoading sem .value.
    detailState: reactive(detailState),
    balanceState: reactive(balanceState),
    balanceCheckedAt,
    notFound,
    load,
    refreshBalance,
  }
}
