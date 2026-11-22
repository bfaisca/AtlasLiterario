import type { LocationQueryValue } from 'vue-router'
import type { MapStatusFilter } from '@/types/api'
import { pluralize } from '@/utils/labels'

/** Filtro de status do mapa quando `?status=` é omitido (= só livros lidos). */
export const DEFAULT_MAP_STATUS: MapStatusFilter = 'Read'

/** Ordem e rótulos do controle segmentado do mapa. */
export const MAP_STATUS_OPTIONS: ReadonlyArray<{ value: MapStatusFilter; label: string }> = [
  { value: 'All', label: 'Todos' },
  { value: 'WantToRead', label: 'Quero ler' },
  { value: 'Reading', label: 'Lendo' },
  { value: 'Read', label: 'Lido' },
  { value: 'Abandoned', label: 'Abandonado' },
]

const BOOK_NOUNS: Record<MapStatusFilter, [singular: string, plural: string]> = {
  Read: ['livro lido', 'livros lidos'],
  All: ['livro', 'livros'],
  WantToRead: ['livro que quero ler', 'livros que quero ler'],
  Reading: ['livro em leitura', 'livros em leitura'],
  Abandoned: ['livro abandonado', 'livros abandonados'],
}

/** "13 livros lidos", "1 livro em leitura", "4 livros que quero ler"… */
export function booksCountLabel(count: number, status: MapStatusFilter): string {
  const [singular, plural] = BOOK_NOUNS[status]
  return pluralize(count, singular, plural)
}

/** `?status=` → filtro válido; ausente, inválido ou igual ao padrão resulta em `Read`. */
export function parseMapStatusQuery(
  value: LocationQueryValue | LocationQueryValue[] | undefined,
): MapStatusFilter {
  const raw = Array.isArray(value) ? value[0] : value
  return MAP_STATUS_OPTIONS.find((o) => o.value === raw)?.value ?? DEFAULT_MAP_STATUS
}

/** Valor para a URL: `undefined` (omitido) quando é o padrão. */
export function mapStatusToQuery(status: MapStatusFilter): MapStatusFilter | undefined {
  return status === DEFAULT_MAP_STATUS ? undefined : status
}

/** Parâmetro para a API: só envia `status` quando difere do padrão do backend. */
export function mapStatusToApiParam(status: MapStatusFilter): MapStatusFilter | undefined {
  return mapStatusToQuery(status)
}
