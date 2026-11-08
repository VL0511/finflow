import type { CashFlowPeriod, CashFlowPoint, DailyCashFlow } from '../types/dashboard'

const monthAbbr = new Intl.DateTimeFormat('pt-BR', { month: 'short' })
/** "09 out" — mais curto que o "09 de out." do Intl, cabe no eixo. */
const dayShort = { format: (date: Date) => `${String(date.getDate()).padStart(2, '0')} ${monthAbbr.format(date)}` }
const dayLong = new Intl.DateTimeFormat('pt-BR', { weekday: 'long', day: '2-digit', month: 'long' })
const monthShort = new Intl.DateTimeFormat('pt-BR', { month: 'short' })
const monthLong = new Intl.DateTimeFormat('pt-BR', { month: 'long', year: 'numeric' })

const clean = (text: string) => text.replace(/\./g, '')

/** "2026-11-05" → Date ao meio-dia local (evita deslocamento por fuso). */
function parseLocalDate(isoDate: string): Date {
  const [y, m, d] = isoDate.split('-').map(Number)
  return new Date(y!, m! - 1, d!, 12)
}

export function sumFlows(days: DailyCashFlow[]) {
  return days.reduce(
    (acc, day) => ({ inflow: acc.inflow + day.inflow, outflow: acc.outflow + day.outflow }),
    { inflow: 0, outflow: 0 },
  )
}

function toDailyPoints(days: DailyCashFlow[]): CashFlowPoint[] {
  return days.map((day) => {
    const date = parseLocalDate(day.date)
    return {
      key: day.date,
      label: clean(dayShort.format(date)),
      fullLabel: dayLong.format(date),
      inflow: day.inflow,
      outflow: day.outflow,
    }
  })
}

/** Agrega por mês civil; o mês corrente aparece como parcial. */
function toMonthlyPoints(days: DailyCashFlow[], months: number): CashFlowPoint[] {
  const buckets = new Map<string, { date: Date; inflow: number; outflow: number }>()

  for (const day of days) {
    const key = day.date.slice(0, 7)
    const bucket = buckets.get(key) ?? { date: parseLocalDate(day.date), inflow: 0, outflow: 0 }
    bucket.inflow += day.inflow
    bucket.outflow += day.outflow
    buckets.set(key, bucket)
  }

  const keys = [...buckets.keys()].sort().slice(-months)
  const lastKey = keys[keys.length - 1]

  return keys.map((key) => {
    const bucket = buckets.get(key)!
    const isCurrent = key === lastKey
    return {
      key,
      label: clean(monthShort.format(bucket.date)),
      fullLabel: `${monthLong.format(bucket.date)}${isCurrent ? ' (até hoje)' : ''}`,
      inflow: bucket.inflow,
      outflow: bucket.outflow,
    }
  })
}

export function buildCashFlowPoints(days: DailyCashFlow[], period: CashFlowPeriod): CashFlowPoint[] {
  switch (period) {
    case '7d':
      return toDailyPoints(days.slice(-7))
    case '30d':
      return toDailyPoints(days.slice(-30))
    case '12m':
      return toMonthlyPoints(days, 12)
  }
}
