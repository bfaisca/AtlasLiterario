import { describe, expect, it } from 'vitest'
import type { BookDetailsDto } from '@/types/api'
import { lacksAuthorCountry, lacksOriginCountry, matchesGeoMissing } from '../libraryGeo'

const geo = { countryIso: 'BRA', countryName: 'Brasil', source: 'Wikidata', confidence: 1 } as const
function item(origin: boolean, authors: Array<boolean>) {
  return {
    book: {
      originCountries: origin ? [{ ...geo, isVerified: false }] : [],
      authors: authors.map((hasNationality, i) => ({
        id: String(i),
        name: `A${i}`,
        nationalities: hasNationality ? [{ ...geo, isVerified: false }] : [],
      })),
    } as unknown as BookDetailsDto,
  }
}

describe('lacunas geográficas da biblioteca', () => {
  it('detecta obra e autor sem país', () => {
    expect(lacksOriginCountry(item(false, [true]))).toBe(true)
    expect(lacksOriginCountry(item(true, [true]))).toBe(false)
    expect(lacksAuthorCountry(item(true, [false]))).toBe(true)
    expect(lacksAuthorCountry(item(true, [false, true]))).toBe(false) // basta um autor com país
    expect(lacksAuthorCountry(item(true, []))).toBe(true) // sem autores cadastrados
  })

  it('combina como o contrato (any / origin / author / both)', () => {
    const complete = item(true, [true])
    const onlyOrigin = item(false, [true])
    const onlyAuthor = item(true, [false])
    const both = item(false, [false])
    const check = (filter: 'any' | 'origin' | 'author' | 'both') =>
      [complete, onlyOrigin, onlyAuthor, both].map((i) => matchesGeoMissing(i, filter))
    expect(check('any')).toEqual([false, true, true, true])
    expect(check('origin')).toEqual([false, true, false, true])
    expect(check('author')).toEqual([false, false, true, true])
    expect(check('both')).toEqual([false, false, false, true])
  })
})
