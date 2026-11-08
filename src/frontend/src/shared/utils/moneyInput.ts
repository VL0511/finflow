import { currencyLocale, type CurrencyCode } from './formatters'

/**
 * Lógica da máscara monetária, sem DOM (testável). O valor é mantido em UNIDADES MÍNIMAS
 * inteiras (centavos), nunca como float durante a digitação: cada dígito digitado entra pela
 * direita ("estilo caixa eletrônico"): 1 → 0,01; 15 → 0,15; 1500 → 15,00. Assim não há
 * ambiguidade de separador nem salto de cursor (o cursor fica sempre no fim).
 */

/** Limite de dígitos: mantém o valor inteiro exato em Number (< 2^53) e dentro de decimal(18,2). */
export const MAX_MONEY_DIGITS = 15

/** Dígitos de um texto qualquer (digitado ou colado), sem zeros à esquerda. */
export function extractDigits(text: string): string {
  return text.replace(/\D/g, '').replace(/^0+(?=\d)/, '').slice(0, MAX_MONEY_DIGITS)
}

/** "150050" com 2 casas → "1500.50" (texto decimal exato, sem passar por float). */
export function digitsToDecimalString(digits: string, decimals: number): string | null {
  if (digits === '') return null
  if (decimals === 0) return digits.replace(/^0+(?=\d)/, '')
  const padded = digits.padStart(decimals + 1, '0')
  const integer = padded.slice(0, -decimals).replace(/^0+(?=\d)/, '')
  return `${integer}.${padded.slice(-decimals)}`
}

/**
 * Valor numérico a enviar para a API. Number(texto decimal) produz o double mais próximo, que o
 * JSON serializa de volta no mesmo texto ("1500.5"); o backend recebe em decimal (C#).
 */
export function digitsToAmount(digits: string, decimals: number): number | null {
  const text = digitsToDecimalString(digits, decimals)
  return text === null ? null : Number(text)
}

/**
 * Valor → dígitos (para preencher o campo a partir de um número vindo da API ou da própria
 * máscara, que já têm as casas da moeda). toFixed evita resíduos como 0.07 * 100 = 7.000000000000001.
 */
export function amountToDigits(amount: number | null | undefined, decimals: number): string {
  if (amount === null || amount === undefined || !Number.isFinite(amount) || amount < 0) return ''
  return extractDigits(amount.toFixed(decimals))
}

/** Texto exibido no campo: "R$ 1.500,50", "$1,500.50", "1.500,50 €". Vazio quando não há valor. */
export function formatMoneyDigits(digits: string, currency: CurrencyCode, decimals: number): string {
  const text = digitsToDecimalString(digits, decimals)
  if (text === null) return ''
  return new Intl.NumberFormat(currencyLocale(currency), {
    style: 'currency',
    currency,
    minimumFractionDigits: decimals,
    maximumFractionDigits: decimals,
  }).format(Number(text))
}
