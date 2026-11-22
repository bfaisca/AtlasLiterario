import { describe, expect, it } from 'vitest'
import { parseCountryQuery, sortCountriesForList } from '../mapSelection'

describe('parseCountryQuery', () => {
  it('aceita ISO alpha-3 em qualquer caixa e normaliza para maiúsculas', () => {
    expect(parseCountryQuery('bra')).toBe('BRA')
    expect(parseCountryQuery(' FRA ')).toBe('FRA')
    expect(parseCountryQuery(['NOR', 'BRA'])).toBe('NOR')
  })
  it('rejeita valores ausentes ou malformados', () => {
    expect(parseCountryQuery(undefined)).toBeNull()
    expect(parseCountryQuery(null)).toBeNull()
    expect(parseCountryQuery('')).toBeNull()
    expect(parseCountryQuery('BR')).toBeNull()
    expect(parseCountryQuery('BRAS')).toBeNull()
    expect(parseCountryQuery('B1A')).toBeNull()
  })
})

describe('sortCountriesForList', () => {
  it('ordena por livros (desc) e desempata pelo nome', () => {
    const sorted = sortCountriesForList([
      { iso: 'ARG', name: 'Argentina', bookCount: 2 },
      { iso: 'BRA', name: 'Brasil', bookCount: 5 },
      { iso: 'AUT', name: 'Áustria', bookCount: 2 },
    ])
    expect(sorted.map((c) => c.iso)).toEqual(['BRA', 'ARG', 'AUT'])
  })
  it('não altera o array original', () => {
    const input = [
      { iso: 'B', name: 'B', bookCount: 1 },
      { iso: 'A', name: 'A', bookCount: 2 },
    ]
    sortCountriesForList(input)
    expect(input[0]!.iso).toBe('B')
  })
})
