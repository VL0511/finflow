import { describe, expect, it } from 'vitest'
import { parseMoneyInput } from './money'

describe('parseMoneyInput', () => {
  it.each([
    ['1500', 1500],
    ['1500.5', 1500.5],
    ['1500,50', 1500.5],
    ['1.500,50', 1500.5],
    ['1,500.50', 1500.5],
    ['1.500.000', 1500000],
    ['R$ 10,00', 10],
    ['0', 0],
    ['-10', -10],
  ])('"%s" → %d', (input, expected) => {
    expect(parseMoneyInput(input)).toBe(expected)
  })

  it.each(['', 'abc', '10,999', '1.5.5,1,2', '10..5', '1.50.0', '1.500', ',5', '-'])('rejeita "%s"', (input) => {
    expect(parseMoneyInput(input)).toBeNull()
  })
})
