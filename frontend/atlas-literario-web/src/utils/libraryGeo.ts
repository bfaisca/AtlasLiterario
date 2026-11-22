import type { LibraryGeoMissing, LibraryItemDto } from '@/types/api'

/** Opções do seletor "Dados geográficos" da biblioteca (`null` = sem filtro). */
export const GEO_MISSING_OPTIONS: ReadonlyArray<{
  value: LibraryGeoMissing | ''
  label: string
}> = [
  { value: '', label: 'Todos os livros' },
  { value: 'any', label: 'Falta algum país' },
  { value: 'origin', label: 'Sem país da obra' },
  { value: 'author', label: 'Sem país do autor' },
  { value: 'both', label: 'Sem nenhum dos dois' },
]

export const GEO_MISSING_VALUES: readonly LibraryGeoMissing[] = ['any', 'origin', 'author', 'both']

/** Obra sem país de origem conhecido (desconhecido, não "inexistente"). */
export function lacksOriginCountry(item: Pick<LibraryItemDto, 'book'>): boolean {
  return item.book.originCountries.length === 0
}

/** Nenhum autor com nacionalidade conhecida (inclui livro sem autores cadastrados). */
export function lacksAuthorCountry(item: Pick<LibraryItemDto, 'book'>): boolean {
  return item.book.authors.every((a) => a.nationalities.length === 0)
}

/** Mesma semântica de `GET /library?geoMissing=`, usada pelos mocks. */
export function matchesGeoMissing(
  item: Pick<LibraryItemDto, 'book'>,
  filter: LibraryGeoMissing,
): boolean {
  const origin = lacksOriginCountry(item)
  const author = lacksAuthorCountry(item)
  switch (filter) {
    case 'origin':
      return origin
    case 'author':
      return author
    case 'both':
      return origin && author
    default:
      return origin || author
  }
}
