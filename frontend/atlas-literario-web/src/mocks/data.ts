import type {
  AuthorCountryDto,
  AuthorCountryRelationship,
  AuthorDetailsDto,
  AuthorSummaryDto,
  BookDetailsDto,
  GeoEnrichmentStatus,
  GeoRelationDto,
  GeoSource,
  LibraryItemDto,
  ReadingStatus,
  UserProfileDto,
} from '@/types/api'
import { countryIndex } from './countries'

/**
 * Biblioteca simulada. Os vínculos geográficos imitam o que o backend obteria do Wikidata
 * (P27 / P495), incluindo mapeamentos históricos com confiança reduzida (Kafka, Dostoiévski)
 * e casos desconhecidos — nunca inventados como "ausência".
 */

let countriesCache: ReturnType<typeof countryIndex> | null = null

function geo(iso: string, source: GeoSource = 'Wikidata', confidence = 0.9): GeoRelationDto {
  countriesCache ??= countryIndex()
  const country = countriesCache.get(iso)
  if (!country) throw new Error(`País ${iso} ausente em MOCK_COUNTRIES`)
  return {
    countryIso: iso,
    countryName: country.namePt,
    source,
    confidence,
    isVerified: source === 'Manual',
  }
}

function rel(
  relationship: AuthorCountryRelationship,
  iso: string,
  source: GeoSource = 'Wikidata',
  confidence = 0.9,
): AuthorCountryDto {
  return { ...geo(iso, source, confidence), relationship }
}

let idCounter = 0
const uuid = (prefix: string) =>
  `00000000-0000-4000-8000-${prefix}${String(++idCounter).padStart(12 - prefix.length, '0')}`

// ---------------------------------------------------------------- Autores

function author(
  name: string,
  birthDate: string | null,
  countries: AuthorCountryDto[],
  wikidata?: string,
): AuthorDetailsDto {
  return {
    id: uuid('a'),
    name,
    birthDate,
    countries,
    externalIds: wikidata ? [{ provider: 'Wikidata', externalId: wikidata }] : [],
  }
}

export function toAuthorSummary(details: AuthorDetailsDto): AuthorSummaryDto {
  return {
    id: details.id,
    name: details.name,
    nationalities: details.countries
      .filter((c) => c.relationship === 'Citizenship')
      .map(({ relationship: _relationship, ...geoRelation }) => geoRelation),
  }
}

// ---------------------------------------------------------------- Livros (catálogo)

interface BookSeed {
  title: string
  subtitle?: string
  authors: AuthorDetailsDto[]
  origins: GeoRelationDto[]
  year: number
  language: string
  genres: string[]
  description: string
  status?: GeoEnrichmentStatus
}

function book(seed: BookSeed): BookDetailsDto {
  const id = uuid('b')
  return {
    id,
    title: seed.title,
    subtitle: seed.subtitle ?? null,
    isbn13: null,
    isbn10: null,
    publisher: null,
    publicationYear: seed.year,
    description: seed.description,
    coverUrl: null,
    originalLanguage: seed.language,
    genres: seed.genres,
    authors: seed.authors.map(toAuthorSummary),
    originCountries: seed.origins,
    geoEnrichmentStatus:
      seed.status ??
      (seed.origins.length && seed.authors.every((a) => a.countries.length)
        ? 'Completed'
        : 'Partial'),
    externalIds: [{ provider: 'Local', externalId: id }],
  }
}

/**
 * Catálogo simulado, construído sob demanda (sem efeitos colaterais no carregamento do módulo,
 * para que nada disto entre no bundle de produção quando VITE_USE_MOCKS=false).
 */
function buildCatalog() {
  const authors = {
    garciaMarquez: author(
      'Gabriel García Márquez',
      '1927-03-06',
      [rel('Citizenship', 'COL'), rel('BirthPlace', 'COL'), rel('Residence', 'MEX')],
      'Q5878',
    ),
    // Áustria-Hungria / Tchecoslováquia → mapeamento histórico (confidence 0.6, não verificado)
    kafka: author(
      'Franz Kafka',
      '1883-07-03',
      [rel('Citizenship', 'AUT', 'Wikidata', 0.6), rel('Citizenship', 'CZE', 'Wikidata', 0.6)],
      'Q905',
    ),
    murakami: author('Haruki Murakami', '1949-01-12', [rel('Citizenship', 'JPN')], 'Q134774'),
    machado: author(
      'Machado de Assis',
      '1839-06-21',
      [rel('Citizenship', 'BRA', 'Manual', 1), rel('BirthPlace', 'BRA')],
      'Q311145',
    ),
    saramago: author('José Saramago', '1922-11-16', [rel('Citizenship', 'PRT')], 'Q36507'),
    dostoevsky: author(
      'Fiódor Dostoiévski',
      '1821-11-11',
      [rel('Citizenship', 'RUS', 'Wikidata', 0.6)],
      'Q991',
    ),
    adichie: author(
      'Chimamanda Ngozi Adichie',
      '1977-09-15',
      [rel('Citizenship', 'NGA')],
      'Q22814',
    ),
    gaiman: author('Neil Gaiman', '1960-11-10', [rel('Citizenship', 'GBR')], 'Q210059'),
    pratchett: author('Terry Pratchett', '1948-04-28', [rel('Citizenship', 'GBR')], 'Q46248'),
    borges: author('Jorge Luis Borges', '1899-08-24', [rel('Citizenship', 'ARG')], 'Q909'),
    // Nasceu na Ucrânia: BirthPlace é informativo e NÃO entra no mapa de nacionalidades
    lispector: author(
      'Clarice Lispector',
      '1920-12-10',
      [rel('Citizenship', 'BRA'), rel('BirthPlace', 'UKR')],
      'Q234591',
    ),
    rulfo: author('Juan Rulfo', '1917-05-16', [rel('Citizenship', 'MEX')], 'Q312614'),
    // Dupla cidadania: o livro conta nos dois países
    vargasLlosa: author(
      'Mario Vargas Llosa',
      '1936-03-28',
      [rel('Citizenship', 'PER'), rel('Citizenship', 'ESP')],
      'Q39803',
    ),
  } satisfies Record<string, AuthorDetailsDto>
  const A = authors

  const books = {
    cemAnos: book({
      title: 'Cem Anos de Solidão',
      authors: [A.garciaMarquez],
      origins: [geo('COL')],
      year: 1967,
      language: 'es',
      genres: ['Romance', 'Realismo mágico'],
      description: 'A saga de sete gerações da família Buendía na cidade fictícia de Macondo.',
    }),
    processo: book({
      title: 'O Processo',
      authors: [A.kafka],
      origins: [], // origem não encontrada no Wikidata → desconhecida
      year: 1925,
      language: 'de',
      genres: ['Romance', 'Ficção filosófica'],
      description: 'Josef K. é detido sem saber de que crime é acusado.',
      status: 'Partial',
    }),
    metamorfose: book({
      title: 'A Metamorfose',
      authors: [A.kafka],
      origins: [geo('AUT', 'Wikidata', 0.6)],
      year: 1915,
      language: 'de',
      genres: ['Novela'],
      description: 'Gregor Samsa acorda certa manhã transformado em um inseto monstruoso.',
    }),
    norwegianWood: book({
      title: 'Norwegian Wood',
      authors: [A.murakami],
      origins: [geo('JPN')],
      year: 1987,
      language: 'ja',
      genres: ['Romance'],
      description: 'Toru Watanabe relembra a juventude em Tóquio no fim dos anos 1960.',
    }),
    domCasmurro: book({
      title: 'Dom Casmurro',
      authors: [A.machado],
      origins: [geo('BRA', 'Manual', 1)],
      year: 1899,
      language: 'pt',
      genres: ['Romance', 'Clássico'],
      description: 'Bentinho revisita a vida e a dúvida que o consome: Capitu o traiu?',
    }),
    bras: book({
      title: 'Memórias Póstumas de Brás Cubas',
      authors: [A.machado],
      origins: [geo('BRA')],
      year: 1881,
      language: 'pt',
      genres: ['Romance', 'Clássico'],
      description: 'Um defunto autor narra, com ironia, a própria vida.',
    }),
    cegueira: book({
      title: 'Ensaio sobre a Cegueira',
      authors: [A.saramago],
      origins: [geo('PRT')],
      year: 1995,
      language: 'pt',
      genres: ['Romance'],
      description: 'Uma epidemia de cegueira branca expõe o melhor e o pior da humanidade.',
    }),
    crimeCastigo: book({
      title: 'Crime e Castigo',
      authors: [A.dostoevsky],
      origins: [geo('RUS', 'Wikidata', 0.6)],
      year: 1866,
      language: 'ru',
      genres: ['Romance', 'Clássico'],
      description: 'Raskólnikov comete um assassinato e enfrenta a própria consciência.',
    }),
    americanah: book({
      title: 'Americanah',
      authors: [A.adichie],
      origins: [geo('NGA')],
      year: 2013,
      language: 'en',
      genres: ['Romance'],
      description: 'Ifemelu deixa a Nigéria rumo aos Estados Unidos e descobre o que é raça.',
    }),
    belasMaldicoes: book({
      title: 'Belas Maldições',
      authors: [A.gaiman, A.pratchett], // dois autores do mesmo país → GBR conta 1 livro
      origins: [geo('GBR')],
      year: 1990,
      language: 'en',
      genres: ['Fantasia', 'Humor'],
      description: 'Um anjo e um demônio tentam impedir o Apocalipse.',
    }),
    ficcoes: book({
      title: 'Ficções',
      authors: [A.borges],
      origins: [geo('ARG')],
      year: 1944,
      language: 'es',
      genres: ['Contos'],
      description: 'Labirintos, bibliotecas infinitas e espelhos.',
    }),
    horaEstrela: book({
      title: 'A Hora da Estrela',
      authors: [A.lispector],
      origins: [geo('BRA')],
      year: 1977,
      language: 'pt',
      genres: ['Novela'],
      description: 'A história de Macabéa, nordestina no Rio de Janeiro.',
    }),
    pedroParamo: book({
      title: 'Pedro Páramo',
      authors: [A.rulfo],
      origins: [geo('MEX')],
      year: 1955,
      language: 'es',
      genres: ['Romance'],
      description: 'Juan Preciado vai a Comala em busca do pai e encontra um povoado de mortos.',
    }),
    cidadeCachorros: book({
      title: 'A Cidade e os Cachorros',
      authors: [A.vargasLlosa],
      origins: [geo('PER')],
      year: 1963,
      language: 'es',
      genres: ['Romance'],
      description: 'Cadetes de um colégio militar em Lima sob um código de violência.',
    }),
    milEUmaNoites: book({
      title: 'As Mil e Uma Noites',
      authors: [], // autoria desconhecida
      origins: [], // origem não determinada nas fontes
      year: 900,
      language: 'ar',
      genres: ['Contos', 'Clássico'],
      description: 'Xerazade conta histórias noite após noite para adiar a própria morte.',
      status: 'Partial',
    }),
    // Fora da biblioteca: aparece apenas na pesquisa simulada
    grandeSertao: book({
      title: 'Grande Sertão: Veredas',
      authors: [
        author('João Guimarães Rosa', '1908-06-27', [rel('Citizenship', 'BRA')], 'Q312513'),
      ],
      origins: [geo('BRA')],
      year: 1956,
      language: 'pt',
      genres: ['Romance'],
      description: 'Riobaldo narra sua vida de jagunço e o pacto que talvez tenha feito.',
    }),
  } satisfies Record<string, BookDetailsDto>

  return { authors, books }
}

let catalogCache: ReturnType<typeof buildCatalog> | null = null

export function getMockCatalog() {
  catalogCache ??= buildCatalog()
  return catalogCache
}

// ---------------------------------------------------------------- Biblioteca do usuário demo

interface ItemSeed {
  book: BookDetailsDto
  status: ReadingStatus
  rating?: number
  review?: string
  start?: string
  finished?: string
}

function item(seed: ItemSeed): LibraryItemDto {
  const created = `${seed.start ?? seed.finished ?? '2025-01-01'}T12:00:00Z`
  const updated = seed.finished ? `${seed.finished}T20:00:00Z` : created
  return {
    id: uuid('c'),
    status: seed.status,
    rating: seed.rating ?? null,
    review: seed.review ?? null,
    startDate: seed.start ?? null,
    finishedDate: seed.status === 'Read' ? (seed.finished ?? null) : null,
    createdAt: created,
    updatedAt: updated,
    book: seed.book,
  }
}

export function createMockLibrary(): LibraryItemDto[] {
  const B = getMockCatalog().books
  return [
    item({
      book: B.cemAnos,
      status: 'Read',
      rating: 5,
      review: 'Macondo ficou morando em mim.',
      start: '2024-12-20',
      finished: '2025-01-18',
    }),
    item({
      book: B.metamorfose,
      status: 'Read',
      rating: 4,
      start: '2025-02-01',
      finished: '2025-02-03',
    }),
    item({
      book: B.processo,
      status: 'Read',
      rating: 4,
      start: '2025-02-10',
      finished: '2025-03-02',
    }),
    item({
      book: B.norwegianWood,
      status: 'Read',
      rating: 4,
      start: '2025-03-05',
      finished: '2025-03-28',
    }),
    item({
      book: B.domCasmurro,
      status: 'Read',
      rating: 5,
      review: 'Capitu traiu? Releio e mudo de opinião a cada vez.',
      start: '2025-04-02',
      finished: '2025-04-20',
    }),
    item({
      book: B.cegueira,
      status: 'Read',
      rating: 5,
      start: '2025-05-01',
      finished: '2025-05-25',
    }),
    item({
      book: B.crimeCastigo,
      status: 'Read',
      rating: 4,
      start: '2025-06-01',
      finished: '2025-07-14',
    }),
    item({
      book: B.americanah,
      status: 'Read',
      rating: 5,
      start: '2025-08-03',
      finished: '2025-08-30',
    }),
    item({
      book: B.belasMaldicoes,
      status: 'Read',
      rating: 4,
      start: '2025-09-02',
      finished: '2025-09-19',
    }),
    item({
      book: B.cidadeCachorros,
      status: 'Read',
      rating: 3,
      start: '2025-10-01',
      finished: '2025-10-27',
    }),
    item({ book: B.milEUmaNoites, status: 'Read', start: '2025-11-01', finished: '2025-12-15' }),
    item({ book: B.bras, status: 'Reading', start: '2026-10-28' }),
    item({ book: B.pedroParamo, status: 'Reading', start: '2026-11-02' }),
    item({ book: B.ficcoes, status: 'WantToRead' }),
    item({ book: B.horaEstrela, status: 'Abandoned', start: '2026-01-10' }),
  ]
}

export const MOCK_DEMO_USER: UserProfileDto = {
  id: '00000000-0000-4000-8000-000000000001',
  name: 'Leitora Demo',
  email: 'leitora@atlas.dev',
  mapVisibility: 'Private',
  role: 'User',
  createdAt: '2024-12-01T10:00:00Z',
}

/** Moderadora de demonstração: pode alterar países já informados (contrato v1.7). */
export const MOCK_MODERATOR_USER: UserProfileDto = {
  id: '00000000-0000-4000-8000-000000000002',
  name: 'Moderadora Demo',
  email: 'moderadora@atlas.dev',
  mapVisibility: 'Private',
  role: 'Moderator',
  createdAt: '2024-11-01T10:00:00Z',
}

export const MOCK_DEMO_PASSWORD = 'atlas1234'
