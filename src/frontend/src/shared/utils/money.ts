/**
 * Converte o texto digitado em valor monetário, aceitando os formatos comuns:
 * "1500", "1500.5", "1500,50", "1.500,50", "1,500.50", "1.500.000".
 * Devolve `null` se não for um número válido, tiver mais de 2 casas decimais
 * ou um agrupamento de milhar malformado ("10..5", "1.50.0").
 *
 * Um único "." ou "," é tratado como separador decimal — por isso "1.500" é
 * recusado (3 casas) em vez de ser adivinhado como mil e quinhentos.
 */
export function parseMoneyInput(raw: string): number | null {
  let text = raw.trim().replace(/\s|R\$|US\$|€/g, '')
  if (text === '') return null

  const negative = text.startsWith('-')
  if (negative) text = text.slice(1)

  const commas = text.split(',').length - 1
  const dots = text.split('.').length - 1

  let decimalSeparator: ',' | '.' | null = null
  if (commas > 0 && dots > 0) decimalSeparator = text.lastIndexOf(',') > text.lastIndexOf('.') ? ',' : '.'
  else if (commas === 1) decimalSeparator = ','
  else if (dots === 1) decimalSeparator = '.'

  let integerPart = text
  let fraction = ''
  if (decimalSeparator) {
    const index = text.lastIndexOf(decimalSeparator)
    integerPart = text.slice(0, index)
    fraction = text.slice(index + 1)
    if (!/^\d{1,2}$/.test(fraction)) return null
  }

  const thousandsSeparator = decimalSeparator === ',' ? '.' : decimalSeparator === '.' ? ',' : commas > 0 ? ',' : '.'
  if (integerPart.includes(thousandsSeparator)) {
    const grouped = new RegExp(`^\\d{1,3}(\\${thousandsSeparator}\\d{3})+$`)
    if (!grouped.test(integerPart)) return null
    integerPart = integerPart.split(thousandsSeparator).join('')
  }

  if (!/^\d+$/.test(integerPart)) return null

  const value = Number(fraction ? `${integerPart}.${fraction}` : integerPart)
  if (!Number.isFinite(value)) return null
  return negative ? -value : value
}
