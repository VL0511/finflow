import { defineStore } from 'pinia'
import { computed, shallowRef } from 'vue'
import { useRequestState } from '@/shared/api'
import { currenciesApi, type Currency } from '../api/currenciesApi'

/** Moedas ativas (GET /api/currencies): opções dos formulários e casas decimais da máscara. */
export const useCurrenciesStore = defineStore('currencies', () => {
  const active = shallowRef<Currency[]>([])
  const state = useRequestState()

  function fetchActive(): Promise<void> {
    return state.run(async () => {
      active.value = await currenciesApi.list()
    })
  }

  function ensureActive(): Promise<void> {
    return state.hasLoaded.value ? Promise.resolve() : fetchActive()
  }

  const byCode = computed(() => new Map(active.value.map((c) => [c.code, c])))

  /** Casas decimais da moeda; 2 enquanto o cadastro não carregou ou para moeda desconhecida. */
  function decimalsFor(code: string | null | undefined): number {
    const currency = code ? byCode.value.get(code) : undefined
    return currency?.decimalPlaces ?? 2
  }

  return { active, state, fetchActive, ensureActive, byCode, decimalsFor }
})
