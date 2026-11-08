import { api } from '@/shared/api'
import type { PagedResult } from '@/shared/api/pagination'

/** Espelha CurrencyResponse. */
export interface Currency {
  code: string
  name: string
  /** Casas decimais da moeda (0 a 2). */
  decimalPlaces: number
  isActive: boolean
}

/** Espelha ExchangeRateResponse. 1 unidade de fromCurrency = rate unidades de toCurrency. */
export interface ExchangeRate {
  id: string
  fromCurrency: string
  toCurrency: string
  rate: number
  isActive: boolean
  createdAt: string
  createdByUserId: string
  createdByEmail: string | null
  supersededAt: string | null
}

/** Espelha ExchangeQuote (GET /api/exchange-rates/quote). */
export interface ExchangeQuote {
  fromCurrency: string
  toCurrency: string
  amount: number
  convertedAmount: number
  rate: number
  rateId: string | null
  isConversion: boolean
}

export const currenciesApi = {
  /** Ativas para todos; `includeInactive` só tem efeito para Administrator. */
  list(includeInactive = false, signal?: AbortSignal): Promise<Currency[]> {
    return api.get<Currency[]>('/currencies', { params: { includeInactive }, signal })
  },
  create(request: { code: string; name: string; decimalPlaces: number }): Promise<Currency> {
    return api.post<Currency>('/currencies', request)
  },
  update(code: string, request: { name?: string; isActive?: boolean }): Promise<Currency> {
    return api.patch<Currency>(`/currencies/${encodeURIComponent(code)}`, request)
  },
}

export const exchangeRatesApi = {
  listActive(signal?: AbortSignal): Promise<ExchangeRate[]> {
    return api.get<ExchangeRate[]>('/exchange-rates', { signal })
  },
  history(params: { from?: string; to?: string; page: number; pageSize: number }, signal?: AbortSignal): Promise<PagedResult<ExchangeRate>> {
    return api.get<PagedResult<ExchangeRate>>('/exchange-rates/history', { params, signal })
  },
  /** Nova versão vigente da direção (a anterior é encerrada, nunca editada). */
  set(request: { fromCurrency: string; toCurrency: string; rate: number }): Promise<ExchangeRate> {
    return api.post<ExchangeRate>('/exchange-rates', request)
  },
  deactivate(from: string, to: string): Promise<void> {
    return api.delete<void>(`/exchange-rates/${encodeURIComponent(from)}/${encodeURIComponent(to)}`)
  },
  quote(from: string, to: string, amount: number, signal?: AbortSignal): Promise<ExchangeQuote> {
    return api.get<ExchangeQuote>('/exchange-rates/quote', { params: { from, to, amount }, signal })
  },
}
