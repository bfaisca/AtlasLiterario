/**
 * Agregação dos mapas e estatísticas — implementação pura (sem I/O) das regras de
 * `docs/business-rules.md` §3, usada pelos mocks. O backend é a fonte oficial; esta
 * implementação existe para que a interface funcione e seja testada sem a API.
 *
 * Regras-chave:
 * - Conjunto base R = itens com status `Read` (os mapas aceitam, só para visualização, outro
 *   status ou `All`; estatísticas e Passaportes seguem sempre `Read`).
 * - Autores: C_a(b) = união das CIDADANIAS de todos os autores de b (um livro conta 1x por país).
 * - Obras: C_o(b) = países de origem de b.
 * - Livro sem país conhecido → `unknownBookCount` (desconhecido ≠ ausência).
 * - Total de livros lidos = |R|, nunca a soma dos bookCount.
 */
import type {
  BookDetailsDto,
  Continent,
  ContinentProgressDto,
  CountryAuthorDto,
  CountryBookDto,
  CountryDetailDto,
  CountryDto,
  CountryMapDatum,
  DashboardDto,
  LibraryItemDto,
  MapDataDto,
  MapStatusFilter,
  MapType,
  PassportMapStatsDto,
  PassportProgressDto,
  PassportsDto,
  ProfileBookDto,
  ProfileCountryDetailDto,
  TimelinePointDto,
  TopCountryDto,
} from '@/types/api'

export type CountryLookup = ReadonlyMap<string, CountryDto>

export const PASSPORT_CONTINENTS: readonly Continent[] = [
  'Africa',
  'Asia',
  'Europe',
  'NorthAmerica',
  'SouthAmerica',
  'Oceania',
]

export function readItems(items: readonly LibraryItemDto[]): LibraryItemDto[] {
  return items.filter((item) => item.status === 'Read')
}

/** Conjunto de livros de um mapa: um status de leitura ou `All` (padrão: `Read`). */
export function itemsForStatus(
  items: readonly LibraryItemDto[],
  status: MapStatusFilter = 'Read',
): LibraryItemDto[] {
  return status === 'All' ? [...items] : items.filter((item) => item.status === status)
}

/** Países de um livro no mapa indicado (conjunto, sem repetição). */
export function countriesOfBook(book: BookDetailsDto, mapType: MapType): Set<string> {
  if (mapType === 'Authors') {
    return new Set(book.authors.flatMap((a) => a.nationalities.map((n) => n.countryIso)))
  }
  return new Set(book.originCountries.map((o) => o.countryIso))
}

/** Autores que fazem o livro contar no país (Authors: só os com cidadania lá; Origins: todos). */
function authorsOfBookInCountry(book: BookDetailsDto, mapType: MapType, iso: string): string[] {
  if (mapType === 'Authors') {
    return book.authors
      .filter((a) => a.nationalities.some((n) => n.countryIso === iso))
      .map((a) => a.id)
  }
  return book.authors.map((a) => a.id)
}

function lookupCountry(countries: CountryLookup, iso: string): CountryDto {
  const country = countries.get(iso)
  if (!country) throw new Error(`País desconhecido na tabela de países: ${iso}`)
  return country
}

function compareByCountThenName(a: CountryMapDatum, b: CountryMapDatum): number {
  return b.bookCount - a.bookCount || a.name.localeCompare(b.name, 'pt-BR')
}

export function computeMapData(
  items: readonly LibraryItemDto[],
  mapType: MapType,
  countries: CountryLookup,
  status: MapStatusFilter = 'Read',
): MapDataDto {
  const read = itemsForStatus(items, status)
  const byCountry = new Map<string, { books: Set<string>; authors: Set<string> }>()
  let unknownBookCount = 0

  for (const item of read) {
    const isos = countriesOfBook(item.book, mapType)
    if (isos.size === 0) {
      unknownBookCount++
      continue
    }
    for (const iso of isos) {
      let entry = byCountry.get(iso)
      if (!entry) {
        entry = { books: new Set(), authors: new Set() }
        byCountry.set(iso, entry)
      }
      entry.books.add(item.book.id)
      for (const authorId of authorsOfBookInCountry(item.book, mapType, iso)) {
        entry.authors.add(authorId)
      }
    }
  }

  const data: CountryMapDatum[] = [...byCountry.entries()]
    .map(([iso, entry]) => {
      const country = lookupCountry(countries, iso)
      return {
        iso,
        name: country.namePt,
        continent: country.continent,
        bookCount: entry.books.size,
        authorCount: entry.authors.size,
      }
    })
    .sort(compareByCountThenName)

  return {
    mapType,
    status,
    totalReadBooks: read.length,
    exploredCountries: data.length,
    maxBookCount: data.reduce((max, d) => Math.max(max, d.bookCount), 0),
    unknownBookCount,
    countries: data,
  }
}

function compareFinishedDesc(a: LibraryItemDto, b: LibraryItemDto): number {
  const fa = a.finishedDate ?? ''
  const fb = b.finishedDate ?? ''
  if (fa === fb) return b.updatedAt.localeCompare(a.updatedAt)
  if (!fa) return 1
  if (!fb) return -1
  return fb.localeCompare(fa)
}

export function toCountryBook(item: LibraryItemDto): CountryBookDto {
  return {
    libraryItemId: item.id,
    bookId: item.book.id,
    title: item.book.title,
    coverUrl: item.book.coverUrl,
    authors: item.book.authors.map((a) => a.name),
    finishedDate: item.finishedDate,
    rating: item.rating,
  }
}

/** Autores com cidadania em `iso` nos livros do conjunto (ordem alfabética sem acento). */
function toCountryAuthors(
  books: readonly LibraryItemDto[],
  mapType: MapType,
  iso: string,
  countryName: string,
): CountryAuthorDto[] {
  if (mapType !== 'Authors') return []
  const byId = new Map<string, CountryAuthorDto>()
  // `books` já vem em finishedDate desc, então os livros de cada autor herdam a ordem.
  for (const item of books) {
    for (const author of item.book.authors) {
      if (!author.nationalities.some((n) => n.countryIso === iso)) continue
      const entry = byId.get(author.id)
      if (entry) {
        if (!entry.books.some((b) => b.libraryItemId === item.id))
          entry.books.push(toCountryBook(item))
      } else {
        byId.set(author.id, {
          authorId: author.id,
          name: author.name,
          countryIso: iso,
          countryName,
          books: [toCountryBook(item)],
        })
      }
    }
  }
  return [...byId.values()].sort(
    (a, b) =>
      a.name.localeCompare(b.name, 'pt-BR', { sensitivity: 'base' }) ||
      a.authorId.localeCompare(b.authorId),
  )
}

export function computeCountryDetail(
  items: readonly LibraryItemDto[],
  mapType: MapType,
  iso: string,
  countries: CountryLookup,
  status: MapStatusFilter = 'Read',
): CountryDetailDto {
  const country = lookupCountry(countries, iso)
  const books = itemsForStatus(items, status)
    .filter((item) => countriesOfBook(item.book, mapType).has(iso))
    .sort(compareFinishedDesc)
  const authors = new Set(books.flatMap((i) => authorsOfBookInCountry(i.book, mapType, iso)))
  return {
    mapType,
    status,
    iso,
    name: country.namePt,
    continent: country.continent,
    bookCount: books.length,
    authorCount: authors.size,
    books: books.map(toCountryBook),
    authors: toCountryAuthors(books, mapType, iso, country.namePt),
  }
}

function round1(value: number): number {
  return Math.round(value * 10) / 10
}

function progress(explored: number, totalCountries: number): PassportProgressDto {
  return {
    exploredCountries: explored,
    totalCountries,
    percentage: totalCountries > 0 ? round1((explored / totalCountries) * 100) : 0,
  }
}

function toTopCountry(datum: CountryMapDatum, mapType: MapType): TopCountryDto {
  return { iso: datum.iso, name: datum.name, bookCount: datum.bookCount, mapType }
}

function unionIsos(...maps: MapDataDto[]): Set<string> {
  return new Set(maps.flatMap((m) => m.countries.map((c) => c.iso)))
}

export function computeDashboard(
  items: readonly LibraryItemDto[],
  countries: CountryLookup,
  totalCountries: number,
): DashboardDto {
  const authors = computeMapData(items, 'Authors', countries)
  const origins = computeMapData(items, 'Origins', countries)
  const distinct = unionIsos(authors, origins)
  const continents = new Set([...distinct].map((iso) => lookupCountry(countries, iso).continent))
  const read = readItems(items)

  return {
    totalReadBooks: read.length,
    totalAuthors: new Set(read.flatMap((i) => i.book.authors.map((a) => a.id))).size,
    countriesByAuthors: authors.exploredCountries,
    countriesByOrigins: origins.exploredCountries,
    distinctCountries: distinct.size,
    continentsExplored: continents.size,
    readingNow: items.filter((i) => i.status === 'Reading').length,
    wantToRead: items.filter((i) => i.status === 'WantToRead').length,
    recentlyFinished: [...read].sort(compareFinishedDesc).slice(0, 6).map(toCountryBook),
    topCountries: [
      ...authors.countries.slice(0, 5).map((d) => toTopCountry(d, 'Authors')),
      ...origins.countries.slice(0, 5).map((d) => toTopCountry(d, 'Origins')),
    ],
    passportProgress: progress(distinct.size, totalCountries),
  }
}

function passportStats(map: MapDataDto): PassportMapStatsDto {
  const top = map.countries[0]
  return {
    exploredCountries: map.exploredCountries,
    topCountry: top ? toTopCountry(top, map.mapType) : null,
    booksByCountry: map.countries,
    unknownBookCount: map.unknownBookCount,
  }
}

/** Mês efetivo de conclusão ("YYYY-MM"): finishedDate, ou updatedAt por segurança. */
function finishedMonth(item: LibraryItemDto): string {
  return (item.finishedDate ?? item.updatedAt).slice(0, 7)
}

function nextMonth(month: string): string {
  const [y, m] = month.split('-').map(Number) as [number, number]
  const date = new Date(Date.UTC(y, m, 1)) // m (1-based) como índice 0-based = mês seguinte
  return `${date.getUTCFullYear()}-${String(date.getUTCMonth() + 1).padStart(2, '0')}`
}

export function computeTimeline(items: readonly LibraryItemDto[]): TimelinePointDto[] {
  const read = readItems(items)
  if (read.length === 0) return []

  const byMonth = new Map<string, LibraryItemDto[]>()
  for (const item of read) {
    const month = finishedMonth(item)
    byMonth.set(month, [...(byMonth.get(month) ?? []), item])
  }
  const months = [...byMonth.keys()].sort()
  const first = months[0]!
  const last = months[months.length - 1]!

  const authors = new Set<string>()
  const origins = new Set<string>()
  const timeline: TimelinePointDto[] = []
  let cumulativeBooks = 0

  for (let month = first; month <= last; month = nextMonth(month)) {
    const finished = byMonth.get(month) ?? []
    for (const item of finished) {
      countriesOfBook(item.book, 'Authors').forEach((iso) => authors.add(iso))
      countriesOfBook(item.book, 'Origins').forEach((iso) => origins.add(iso))
    }
    cumulativeBooks += finished.length
    timeline.push({
      month,
      booksFinished: finished.length,
      cumulativeBooks,
      cumulativeCountriesAuthors: authors.size,
      cumulativeCountriesOrigins: origins.size,
      cumulativeDistinctCountries: new Set([...authors, ...origins]).size,
    })
  }
  return timeline
}

export function computePassports(
  items: readonly LibraryItemDto[],
  countries: CountryLookup,
  totalCountries: number,
): PassportsDto {
  const authors = computeMapData(items, 'Authors', countries)
  const origins = computeMapData(items, 'Origins', countries)
  const distinct = unionIsos(authors, origins)
  const allCountries = [...countries.values()]

  const continents: ContinentProgressDto[] = PASSPORT_CONTINENTS.map((continent) => ({
    continent,
    exploredCountries: [...distinct].filter(
      (iso) => lookupCountry(countries, iso).continent === continent,
    ).length,
    totalCountries: allCountries.filter((c) => c.continent === continent).length,
  }))

  return {
    authors: passportStats(authors),
    origins: passportStats(origins),
    distinctCountries: distinct.size,
    continents,
    timeline: computeTimeline(items),
    progress: progress(distinct.size, totalCountries),
  }
}

/** Livro de um perfil público: sem `libraryItemId` (não expõe ids da biblioteca de terceiros) e com a resenha. */
export function toProfileBook(item: LibraryItemDto): ProfileBookDto {
  return {
    bookId: item.book.id,
    title: item.book.title,
    coverUrl: item.book.coverUrl,
    authors: item.book.authors.map((a) => a.name),
    finishedDate: item.finishedDate,
    rating: item.rating,
    review: item.review?.trim() ? item.review : null,
  }
}

/** Painel de país de um perfil público (api-contracts §8.1): sempre só `Read`, com resenhas. */
export function computeProfileCountryDetail(
  items: readonly LibraryItemDto[],
  mapType: MapType,
  iso: string,
  countries: CountryLookup,
): ProfileCountryDetailDto {
  const detail = computeCountryDetail(items, mapType, iso, countries, 'Read')
  const byItemId = new Map(items.map((item) => [item.id, item]))
  const convert = (book: CountryBookDto): ProfileBookDto =>
    toProfileBook(byItemId.get(book.libraryItemId)!)
  return {
    mapType: detail.mapType,
    iso: detail.iso,
    name: detail.name,
    continent: detail.continent,
    bookCount: detail.bookCount,
    authorCount: detail.authorCount,
    books: detail.books.map(convert),
    authors: detail.authors.map((author) => ({
      authorId: author.authorId,
      name: author.name,
      countryIso: author.countryIso,
      countryName: author.countryName,
      books: author.books.map(convert),
    })),
    status: 'Read',
  }
}
