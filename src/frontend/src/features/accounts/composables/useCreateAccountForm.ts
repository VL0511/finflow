import { computed, reactive, ref, shallowRef } from 'vue'
import { useCurrenciesStore } from '@/features/currencies/stores/useCurrenciesStore'
import { isApiError, normalizeApiError, type ApiError } from '@/shared/api'
import type { CurrencyCode } from '@/shared/utils/formatters'
import { useAccountsStore } from '../stores/useAccountsStore'
import type { Account, CreateAccountRequest } from '../types/account'

type Field = 'holderName' | 'initialBalance' | 'currency' | 'ownerUserId'

/**
 * Estado e envio do formulário de nova conta. O NÚMERO É GERADO PELO BACKEND e aparece na
 * conta criada. A validação local só dá retorno imediato; a palavra final é da API (400/422
 * são mapeados para os campos).
 */
export function useCreateAccountForm() {
  const store = useAccountsStore()
  const currencies = useCurrenciesStore()

  const values = reactive({
    holderName: '',
    /** Valor já normalizado pela máscara (MoneyInput); null = vazio (0). */
    initialBalance: null as number | null,
    currency: 'BRL' as CurrencyCode,
    isActive: true,
    /** Só para Administrator (vazio = conta institucional). */
    ownerUserId: '',
  })

  const errors = ref<Partial<Record<Field, string>>>({})
  const submitError = shallowRef<ApiError | null>(null)
  const isSubmitting = ref(false)

  const decimals = computed(() => currencies.decimalsFor(values.currency))

  function validate(): CreateAccountRequest | null {
    const next: Partial<Record<Field, string>> = {}
    const holderName = values.holderName.trim()

    if (!holderName) next.holderName = 'Titular obrigatório.'
    else if (holderName.length > 100) next.holderName = 'Máximo de 100 caracteres.'

    const balance = values.initialBalance ?? 0
    if (balance < 0) next.initialBalance = 'O saldo inicial não pode ser negativo.'

    if (!/^[A-Z]{3}$/.test(values.currency)) next.currency = 'Escolha a moeda.'

    errors.value = next
    if (Object.keys(next).length > 0) return null

    return {
      holderName,
      initialBalance: balance,
      currency: values.currency,
      isActive: values.isActive,
      ownerUserId: values.ownerUserId.trim() || null,
    }
  }

  function applyServerError(error: ApiError) {
    const fieldMessages: Partial<Record<Field, string>> = {}
    for (const field of ['holderName', 'initialBalance', 'currency', 'ownerUserId'] as const) {
      const message = error.fieldError(field)
      if (message) fieldMessages[field] = message
    }
    errors.value = fieldMessages
    // 422 (moeda desativada, dono inválido), rede etc.: alerta do formulário.
    submitError.value = Object.keys(fieldMessages).length > 0 ? null : error
  }

  /** Envia e devolve a conta criada (com o número gerado); `null` se a validação ou a API recusar. */
  async function submit(): Promise<Account | null> {
    if (isSubmitting.value) return null
    submitError.value = null

    const request = validate()
    if (!request) return null

    isSubmitting.value = true
    try {
      return await store.createAccount(request)
    } catch (error) {
      applyServerError(isApiError(error) ? error : normalizeApiError(error))
      return null
    } finally {
      isSubmitting.value = false
    }
  }

  const hasErrors = computed(() => Object.keys(errors.value).length > 0)

  return { values, errors, submitError, isSubmitting, hasErrors, decimals, submit }
}
