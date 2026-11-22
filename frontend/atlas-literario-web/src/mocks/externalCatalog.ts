import type { ExternalProvider, GeoSource } from '@/types/api'

/**
 * Resultados "externos" simulados (como se viessem do Google Books / Open Library).
 * Ao serem adicionados, o livro é importado com geografia `Pending`; "Buscar dados novamente"
 * (POST /books/{id}/enrich) aplica as nacionalidades/origens abaixo, imitando o Wikidata.
 * O ISBN 9780000000002 é fictício (dígito verificador válido) e serve só para testar a busca por ISBN.
 */
export interface ExternalSeed {
  provider: Exclude<ExternalProvider, 'Local'>
  externalId: string
  title: string
  subtitle?: string
  authors: Array<{ name: string; citizenships: string[] }>
  origins: string[]
  year: number
  publisher: string
  isbn13?: string
  language: string
  genres: string[]
  description: string
  geoSource?: GeoSource
}

export function externalCatalog(): ExternalSeed[] {
  return [
    // Cenários de lentidão (ver mockApi `library.add`): "lento" = a ação conclui mas a resposta
    // expira; "perdido" = a resposta expira e a ação NÃO é concluída (1ª tentativa).
    {
      provider: 'GoogleBooks',
      externalId: 'mock-gb-lento',
      title: 'O Livro Lento',
      authors: [{ name: 'Autora da Demora', citizenships: ['PRT'] }],
      origins: ['PRT'],
      year: 2020,
      publisher: 'Editora Exemplo',
      language: 'pt',
      genres: ['Romance'],
      description: 'Cenário de teste: a resposta do servidor expira, mas o livro é adicionado.',
    },
    {
      provider: 'GoogleBooks',
      externalId: 'mock-gb-perdido',
      title: 'O Livro Perdido',
      authors: [{ name: 'Autor da Falha', citizenships: ['PRT'] }],
      origins: ['PRT'],
      year: 2021,
      publisher: 'Editora Exemplo',
      language: 'pt',
      genres: ['Romance'],
      description: 'Cenário de teste: a 1ª tentativa expira sem adicionar; a 2ª funciona.',
    },
    {
      provider: 'GoogleBooks',
      externalId: 'mock-gb-alienista',
      title: 'O Alienista',
      authors: [{ name: 'Machado de Assis', citizenships: ['BRA'] }],
      origins: ['BRA'],
      year: 1882,
      publisher: 'Editora Exemplo',
      language: 'pt',
      genres: ['Novela', 'Clássico'],
      description:
        'Simão Bacamarte funda a Casa Verde em Itaguaí e passa a internar a cidade inteira.',
    },
    {
      provider: 'OpenLibrary',
      externalId: 'OL-mock-quincas-W',
      title: 'Quincas Borba',
      authors: [{ name: 'Machado de Assis', citizenships: ['BRA'] }],
      origins: ['BRA'],
      year: 1891,
      publisher: 'Editora Exemplo',
      language: 'pt',
      genres: ['Romance', 'Clássico'],
      description: 'Rubião herda a fortuna e a filosofia do Humanitismo de Quincas Borba.',
    },
    {
      provider: 'GoogleBooks',
      externalId: 'mock-gb-memorial',
      title: 'Memorial de Aires',
      authors: [{ name: 'Machado de Assis', citizenships: ['BRA'] }],
      origins: ['BRA'],
      year: 1908,
      publisher: 'Editora Exemplo',
      language: 'pt',
      genres: ['Romance'],
      description: 'O diário do Conselheiro Aires, diplomata aposentado no Rio de Janeiro.',
    },
    {
      provider: 'GoogleBooks',
      externalId: 'mock-gb-colera',
      title: 'O Amor nos Tempos do Cólera',
      authors: [{ name: 'Gabriel García Márquez', citizenships: ['COL'] }],
      origins: ['COL'],
      year: 1985,
      publisher: 'Editora Exemplo',
      language: 'es',
      genres: ['Romance'],
      description: 'Florentino Ariza espera mais de cinquenta anos por Fermina Daza.',
    },
    {
      provider: 'OpenLibrary',
      externalId: 'OL-mock-kafka-praia-W',
      title: 'Kafka à Beira-Mar',
      authors: [{ name: 'Haruki Murakami', citizenships: ['JPN'] }],
      origins: ['JPN'],
      year: 2002,
      publisher: 'Editora Exemplo',
      language: 'ja',
      genres: ['Romance', 'Realismo mágico'],
      description:
        'Um adolescente foge de casa e um idoso que conversa com gatos seguem rumos paralelos.',
    },
    {
      provider: 'GoogleBooks',
      externalId: 'mock-gb-torto-arado',
      title: 'Torto Arado',
      authors: [{ name: 'Itamar Vieira Junior', citizenships: ['BRA'] }],
      origins: ['BRA'],
      year: 2019,
      publisher: 'Editora Exemplo',
      isbn13: '9780000000002',
      language: 'pt',
      genres: ['Romance'],
      description: 'Duas irmãs no sertão da Bahia e um acidente com uma faca de prata.',
    },
    {
      provider: 'OpenLibrary',
      externalId: 'OL-mock-beloved-W',
      title: 'Amada',
      authors: [{ name: 'Toni Morrison', citizenships: ['USA'] }],
      origins: [], // origem não encontrada: continua desconhecida mesmo após o enriquecimento
      year: 1987,
      publisher: 'Editora Exemplo',
      language: 'en',
      genres: ['Romance'],
      description: 'Sethe, ex-escravizada em Ohio, é assombrada pelo passado.',
    },
  ]
}
