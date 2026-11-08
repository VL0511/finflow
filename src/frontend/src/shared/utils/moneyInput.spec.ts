import { describe, expect, it } from 'vitest'
import { amountToDigits, digitsToAmount, digitsToDecimalString, extractDigits, formatMoneyDigits, MAX_MONEY_DIGITS } from './moneyInput'

// Intl usa espaço não separável entre símbolo e número em alguns locales.
const norm = (s: string) => s.replace(/\s/g, ' ')

describe('máscara monetária', () => {
  it('exemplos da especificação em BRL', () => {
    expect(norm(formatMoneyDigits(amountToDigits(1000, 2), 'BRL', 2))).toBe('R$ 1.000,00')
    expect(norm(formatMoneyDigits(amountToDigits(1500.5, 2), 'BRL', 2))).toBe('R$ 1.500,50')
    expect(norm(formatMoneyDigits(amountToDigits(25.99, 2), 'BRL', 2))).toBe('R$ 25,99')
  })

  it('separadores conforme a moeda', () => {
    expect(norm(formatMoneyDigits('150050', 'USD', 2))).toBe('$1,500.50')
    expect(norm(formatMoneyDigits('150050', 'EUR', 2))).toBe('1.500,50 €')
  })

  it('digitação entra pela direita (estilo caixa eletrônico)', () => {
    expect(digitsToAmount('1', 2)).toBe(0.01)
    expect(digitsToAmount('15', 2)).toBe(0.15)
    expect(digitsToAmount('1500', 2)).toBe(15)
    expect(digitsToAmount('150050', 2)).toBe(1500.5)
  })

  it('remoção: apagar o último dígito', () => {
    const digits = extractDigits('R$ 1.500,50')
    expect(digitsToAmount(digits.slice(0, -1), 2)).toBe(150.05)
    expect(digitsToAmount('', 2)).toBeNull()
  })

  it('colar valores formatados de qualquer jeito extrai só os dígitos', () => {
    expect(extractDigits('R$ 1.234,56')).toBe('123456')
    expect(extractDigits('$1,234.56')).toBe('123456')
    expect(extractDigits('0001,00')).toBe('100')
    expect(extractDigits('abc')).toBe('')
  })

  it('texto decimal exato, sem erro de ponto flutuante', () => {
    expect(digitsToDecimalString('1', 2)).toBe('0.01')
    expect(digitsToDecimalString('30', 2)).toBe('0.30')
    expect(digitsToDecimalString('1000', 0)).toBe('1000')
    // 0.1 + 0.2 em float daria 0.30000000000000004; aqui o valor vem do texto.
    expect(digitsToAmount('30', 2)).toBe(0.3)
    // Ida e volta exata para valores válidos (0.07 * 100 em float seria 7.000000000000001).
    for (const value of [0.07, 19.99, 1500.5, 1234567.89, 0.3])
      expect(digitsToAmount(amountToDigits(value, 2), 2)).toBe(value)
  })

  it('moeda sem casas decimais (ex.: JPY)', () => {
    expect(digitsToAmount('1500', 0)).toBe(1500)
    expect(norm(formatMoneyDigits('1500', 'JPY', 0))).toBe('JP¥ 1.500')
  })

  it('limita o tamanho para manter o valor exato', () => {
    expect(extractDigits('9'.repeat(30))).toHaveLength(MAX_MONEY_DIGITS)
    expect(Number.isSafeInteger(Number(extractDigits('9'.repeat(30))))).toBe(true)
  })

  it('valores inválidos não viram dígitos', () => {
    expect(amountToDigits(null, 2)).toBe('')
    expect(amountToDigits(-5, 2)).toBe('')
    expect(amountToDigits(Number.NaN, 2)).toBe('')
  })
})
