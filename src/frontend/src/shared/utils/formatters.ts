/**
 * Código ISO 4217. Aberto (string): moedas novas cadastradas pelo Administrator funcionam sem
 * mudar o frontend.
 */
export type CurrencyCode = string

/**
 * Localidade usada para formatar cada moeda (separadores de milhar/decimal). Ponto único:
 * a máscara de entrada (MoneyInput) usa o mesmo mapa, então o que se digita é o que se vê.
 * Moedas sem entrada usam o padrão da aplicação (pt-BR).
 */
const CURRENCY_LOCALES: Record<string, string> = {
  BRL: 'pt-BR',
  USD: 'en-US',
  EUR: 'de-DE',
}

export const DEFAULT_LOCALE = 'pt-BR'

export function currencyLocale(currency: CurrencyCode): string {
  return CURRENCY_LOCALES[currency] ?? DEFAULT_LOCALE
}

const currencyFormatters = new Map<string, Intl.NumberFormat>()

function getCurrencyFormatter(currency: CurrencyCode, compact: boolean, fixedDigits?: number): Intl.NumberFormat {
  const key = `${currency}:${compact}:${fixedDigits ?? ''}`
  let formatter = currencyFormatters.get(key)

  if (!formatter) {
    formatter = new Intl.NumberFormat(currencyLocale(currency), {
      style: 'currency',
      currency,
      notation: compact ? 'compact' : 'standard',
      maximumFractionDigits: fixedDigits ?? (compact ? 1 : undefined),
      minimumFractionDigits: fixedDigits ?? (compact ? 0 : undefined),
    })
    currencyFormatters.set(key, formatter)
  }

  return formatter
}

/** Casas decimais conforme a moeda (Intl conhece o padrão ISO: BRL/USD/EUR = 2, JPY = 0). */
export function formatCurrency(value: number, currency: CurrencyCode = 'BRL'): string {
  return getCurrencyFormatter(currency, false).format(value)
}

/**
 * R$ 1,2 mi — para eixos e espaços curtos.
 * `fixedDigits` força casas decimais fixas, para que valores lado a lado
 * sejam comparáveis (R$ 2,10 mi vs. R$ 2,03 mi, e não "2,1 mi" vs. "2 mi").
 */
export function formatCompactCurrency(
  value: number,
  currency: CurrencyCode = 'BRL',
  fixedDigits?: number,
): string {
  return getCurrencyFormatter(currency, true, fixedDigits).format(value)
}

const percentFormatter = new Intl.NumberFormat('pt-BR', {
  style: 'percent',
  maximumFractionDigits: 1,
  minimumFractionDigits: 1,
})

/** Recebe fração (0.124 → 12,4%). */
export function formatPercent(fraction: number): string {
  return percentFormatter.format(fraction)
}

const dateTimeFormatter = new Intl.DateTimeFormat('pt-BR', {
  day: '2-digit',
  month: 'short',
  hour: '2-digit',
  minute: '2-digit',
})

const dateFormatter = new Intl.DateTimeFormat('pt-BR', { day: '2-digit', month: 'short', year: 'numeric' })

export function formatDateTime(iso: string): string {
  return dateTimeFormatter.format(new Date(iso)).replace('.', '')
}

export function formatDate(iso: string): string {
  return dateFormatter.format(new Date(iso)).replace('.', '')
}

const relativeFormatter = new Intl.RelativeTimeFormat('pt-BR', { numeric: 'auto' })

/** "há 5 minutos", "ontem"... cai para data absoluta após 7 dias. */
export function formatRelativeTime(iso: string, now: Date = new Date()): string {
  const diffSeconds = Math.round((new Date(iso).getTime() - now.getTime()) / 1000)
  const abs = Math.abs(diffSeconds)

  if (abs < 60) return 'agora'
  if (abs < 3600) return relativeFormatter.format(Math.round(diffSeconds / 60), 'minute')
  if (abs < 86400) return relativeFormatter.format(Math.round(diffSeconds / 3600), 'hour')
  if (abs < 86400 * 7) return relativeFormatter.format(Math.round(diffSeconds / 86400), 'day')
  return formatDate(iso)
}

/** "Ana Souza" → "AS" */
export function getInitials(name: string): string {
  const parts = name.trim().split(/\s+/).filter(Boolean)
  const first = parts[0]?.[0] ?? ''
  const last = parts.length > 1 ? (parts[parts.length - 1]?.[0] ?? '') : ''
  return (first + last).toUpperCase()
}

/** Variação relativa entre dois valores; null quando a base é zero. */
export function relativeChange(current: number, previous: number): number | null {
  if (previous === 0) return null
  return (current - previous) / Math.abs(previous)
}
