import type { AccountDailyCashFlow } from '@/features/accounts/types/account'
import type { DailyCashFlow } from '../types/dashboard'

export function toLocalIsoDate(date: Date): string {
  const y = date.getFullYear()
  const m = String(date.getMonth() + 1).padStart(2, '0')
  const d = String(date.getDate()).padStart(2, '0')
  return `${y}-${m}-${d}`
}

/** Período e fuso usados para pedir GET /accounts/{id}/cash-flow: `days` dias até hoje, no fuso local. */
export function cashFlowRange(days: number, today: Date = new Date()) {
  const end = new Date(today)
  end.setHours(12, 0, 0, 0)
  const start = new Date(end)
  start.setDate(end.getDate() - (days - 1))
  return {
    from: toLocalIsoDate(start),
    to: toLocalIsoDate(end),
    // getTimezoneOffset é invertido (Brasília = +180); a API espera -180.
    utcOffsetMinutes: -end.getTimezoneOffset(),
  }
}

/**
 * Converte a resposta da API (só dias com movimento, já agregados no banco) numa série
 * contínua de `days` dias terminando em `today` — dias sem movimento viram zero.
 */
export function toDailySeries(
  movements: readonly AccountDailyCashFlow[],
  options: { days: number; today?: Date },
): DailyCashFlow[] {
  const byDate = new Map(movements.map((m) => [m.date, m]))
  const today = new Date(options.today ?? new Date())
  today.setHours(12, 0, 0, 0)

  const series: DailyCashFlow[] = []
  for (let offset = options.days - 1; offset >= 0; offset--) {
    const date = new Date(today)
    date.setDate(today.getDate() - offset)
    const key = toLocalIsoDate(date)
    const movement = byDate.get(key)
    series.push({ date: key, inflow: movement?.inflow ?? 0, outflow: movement?.outflow ?? 0 })
  }
  return series
}
