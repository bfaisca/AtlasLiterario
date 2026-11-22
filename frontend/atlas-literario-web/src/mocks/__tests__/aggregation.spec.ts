import { describe, expect, it } from 'vitest'
import type {
  AuthorSummaryDto,
  BookDetailsDto,
  GeoRelationDto,
  LibraryItemDto,
  ReadingStatus,
} from '@/types/api'
import {
  computeCountryDetail,
  computeDashboard,
  computeMapData,
  computePassports,
  computeTimeline,
} from '../aggregation'
import { countryIndex, MOCK_COUNTRIES } from '../countries'
import { createMockLibrary } from '../data'

const countries = countryIndex()

function geo(iso: string): GeoRelationDto {
  return {
    countryIso: iso,
    countryName: iso,
    source: 'Wikidata',
    confidence: 0.9,
    isVerified: false,
  }
}

function author(id: string, ...citizenships: string[]): AuthorSummaryDto {
  return { id, name: `Autor ${id}`, nationalities: citizenships.map(geo) }
}

let seq = 0
function libraryItem(
  options: {
    authors?: AuthorSummaryDto[]
    origins?: string[]
    status?: ReadingStatus
    finishedDate?: string | null
  } = {},
): LibraryItemDto {
  seq++
  const book: BookDetailsDto = {
    id: `book-${seq}`,
    title: `Livro ${seq}`,
    subtitle: null,
    isbn13: null,
    isbn10: null,
    publisher: null,
    publicationYear: 2000,
    description: null,
    coverUrl: null,
    originalLanguage: null,
    genres: [],
    authors: options.authors ?? [],
    originCountries: (options.origins ?? []).map(geo),
    geoEnrichmentStatus: 'Completed',
    externalIds: [],
  }
  const status = options.status ?? 'Read'
  return {
    id: `item-${seq}`,
    status,
    rating: null,
    review: null,
    startDate: null,
    finishedDate: status === 'Read' ? (options.finishedDate ?? '2025-01-15') : null,
    createdAt: '2025-01-01T00:00:00Z',
    updatedAt: '2025-01-20T00:00:00Z',
    book,
  }
}

describe('computeMapData — autores', () => {
  it('dois autores do mesmo país contam o livro uma única vez nesse país', () => {
    const items = [libraryItem({ authors: [author('gaiman', 'GBR'), author('pratchett', 'GBR')] })]
    const map = computeMapData(items, 'Authors', countries)

    expect(map.countries).toEqual([
      expect.objectContaining({ iso: 'GBR', bookCount: 1, authorCount: 2 }),
    ])
    expect(map.exploredCountries).toBe(1)
    expect(map.totalReadBooks).toBe(1)
  })

  it('autor com duas cidadanias faz o livro contar em ambos os países', () => {
    const items = [libraryItem({ authors: [author('llosa', 'PER', 'ESP')] })]
    const map = computeMapData(items, 'Authors', countries)

    expect(map.countries.map((c) => c.iso).sort()).toEqual(['ESP', 'PER'])
    expect(map.countries.every((c) => c.bookCount === 1 && c.authorCount === 1)).toBe(true)
    expect(map.exploredCountries).toBe(2)
    // soma dos bookCount (2) excede o total de livros lidos (1)
    expect(map.totalReadBooks).toBe(1)
  })

  it('somente livros com status Read participam', () => {
    const items = [
      libraryItem({ authors: [author('a1', 'BRA')], status: 'Read' }),
      libraryItem({ authors: [author('a2', 'JPN')], status: 'Reading' }),
      libraryItem({ authors: [author('a3', 'COL')], status: 'WantToRead' }),
      libraryItem({ authors: [author('a4', 'RUS')], status: 'Abandoned' }),
    ]
    const map = computeMapData(items, 'Authors', countries)

    expect(map.countries.map((c) => c.iso)).toEqual(['BRA'])
    expect(map.totalReadBooks).toBe(1)
  })

  it('livros sem cidadania conhecida vão para unknownBookCount e não marcam países', () => {
    const items = [
      libraryItem({ authors: [author('anon')] }),
      libraryItem({ authors: [] }),
      libraryItem({ authors: [author('machado', 'BRA')] }),
    ]
    const map = computeMapData(items, 'Authors', countries)

    expect(map.unknownBookCount).toBe(2)
    expect(map.exploredCountries).toBe(1)
    expect(map.totalReadBooks).toBe(3)
  })

  it('o mesmo autor em vários livros conta uma vez no authorCount', () => {
    const kafka = author('kafka', 'AUT', 'CZE')
    const items = [libraryItem({ authors: [kafka] }), libraryItem({ authors: [kafka] })]
    const map = computeMapData(items, 'Authors', countries)

    expect(map.countries).toHaveLength(2)
    for (const datum of map.countries) {
      expect(datum.bookCount).toBe(2)
      expect(datum.authorCount).toBe(1)
    }
    expect(map.maxBookCount).toBe(2)
  })
})

describe('computeMapData — origens', () => {
  it('é independente das nacionalidades dos autores', () => {
    const items = [libraryItem({ authors: [author('x', 'BRA')], origins: ['PRT'] })]
    expect(computeMapData(items, 'Origins', countries).countries.map((c) => c.iso)).toEqual(['PRT'])
    expect(computeMapData(items, 'Authors', countries).countries.map((c) => c.iso)).toEqual(['BRA'])
  })

  it('origem ausente é desconhecida (não usa a nacionalidade do autor como substituto)', () => {
    const items = [libraryItem({ authors: [author('kafka', 'AUT')], origins: [] })]
    const map = computeMapData(items, 'Origins', countries)
    expect(map.unknownBookCount).toBe(1)
    expect(map.countries).toEqual([])
    expect(map.maxBookCount).toBe(0)
  })

  it('authorCount informa os autores distintos dos livros do país', () => {
    const items = [
      libraryItem({ authors: [author('a', 'GBR'), author('b', 'USA')], origins: ['GBR'] }),
    ]
    expect(computeMapData(items, 'Origins', countries).countries[0]).toMatchObject({
      iso: 'GBR',
      bookCount: 1,
      authorCount: 2,
    })
  })
})

describe('indicadores', () => {
  it('países distintos = união dos dois mapas, cada país contado uma vez', () => {
    const items = [
      libraryItem({ authors: [author('a', 'BRA')], origins: ['BRA'] }),
      libraryItem({ authors: [author('b', 'AUT', 'CZE')], origins: ['AUT'] }),
      libraryItem({ authors: [author('c', 'JPN')], origins: [] }),
    ]
    const dashboard = computeDashboard(items, countries, 250)

    expect(dashboard.countriesByAuthors).toBe(4) // BRA AUT CZE JPN
    expect(dashboard.countriesByOrigins).toBe(2) // BRA AUT
    expect(dashboard.distinctCountries).toBe(4)
    expect(dashboard.continentsExplored).toBe(3) // América do Sul, Europa, Ásia
    expect(dashboard.passportProgress).toEqual({
      exploredCountries: 4,
      totalCountries: 250,
      percentage: 1.6,
    })
  })

  it('detalhe do país lista livros por finishedDate desc', () => {
    const items = [
      libraryItem({ origins: ['BRA'], finishedDate: '2025-01-10' }),
      libraryItem({ origins: ['BRA'], finishedDate: '2025-05-01' }),
      libraryItem({ origins: ['BRA'], status: 'Reading' }),
    ]
    const detail = computeCountryDetail(items, 'Origins', 'BRA', countries)
    expect(detail.bookCount).toBe(2)
    expect(detail.books.map((b) => b.finishedDate)).toEqual(['2025-05-01', '2025-01-10'])
  })

  it('evolução histórica é cumulativa e inclui meses sem leituras', () => {
    const items = [
      libraryItem({ authors: [author('a', 'BRA')], origins: ['BRA'], finishedDate: '2025-01-05' }),
      libraryItem({ authors: [author('b', 'BRA')], origins: ['PRT'], finishedDate: '2025-03-20' }),
    ]
    const timeline = computeTimeline(items)
    expect(timeline.map((p) => p.month)).toEqual(['2025-01', '2025-02', '2025-03'])
    expect(timeline[1]).toMatchObject({ booksFinished: 0, cumulativeBooks: 1 })
    expect(timeline[2]).toMatchObject({
      booksFinished: 1,
      cumulativeBooks: 2,
      cumulativeCountriesAuthors: 1,
      cumulativeCountriesOrigins: 2,
      cumulativeDistinctCountries: 2,
    })
  })

  it('passaportes listam todos os continentes exceto Antártida', () => {
    const passports = computePassports([libraryItem({ origins: ['NGA'] })], countries, 250)
    expect(passports.continents).toHaveLength(6)
    expect(passports.continents.find((c) => c.continent === 'Africa')?.exploredCountries).toBe(1)
    expect(passports.continents.some((c) => c.continent === 'Antarctica')).toBe(false)
  })
})

describe('biblioteca simulada', () => {
  const library = createMockLibrary()

  it('usa apenas países existentes na tabela de países', () => {
    const isos = new Set(MOCK_COUNTRIES.map((c) => c.iso))
    for (const item of library) {
      for (const origin of item.book.originCountries) expect(isos.has(origin.countryIso)).toBe(true)
      for (const a of item.book.authors)
        for (const n of a.nationalities) expect(isos.has(n.countryIso)).toBe(true)
    }
  })

  it('reflete os casos de negócio esperados', () => {
    const authors = computeMapData(library, 'Authors', countries)
    const byIso = new Map(authors.countries.map((c) => [c.iso, c]))

    expect(byIso.get('AUT')).toMatchObject({ bookCount: 2, authorCount: 1 }) // Kafka, 2 livros
    expect(byIso.get('CZE')).toMatchObject({ bookCount: 2, authorCount: 1 })
    expect(byIso.get('GBR')).toMatchObject({ bookCount: 1, authorCount: 2 }) // Gaiman + Pratchett
    expect(byIso.has('UKR')).toBe(false) // local de nascimento não conta
    expect(byIso.has('MEX')).toBe(false) // Pedro Páramo em leitura; residência de García Márquez não conta
    expect(authors.unknownBookCount).toBe(1) // As Mil e Uma Noites
  })
})
