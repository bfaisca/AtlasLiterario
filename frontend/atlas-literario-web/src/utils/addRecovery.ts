import type { BookSearchParams, BookSearchResultDto } from '@/types/api'

/**
 * Recuperação de `POST /library` sem resposta (timeout / falha de rede): o servidor pode ter
 * concluído a ação mesmo assim. Antes de exibir erro, re-pesquisa o livro e verifica se ele
 * agora consta como `inLibrary` com `libraryItemId`.
 */

/** `created` = adicionado agora; `recovered` = criado apesar do timeout; `duplicate` = já existia. */
export type AddOutcome = 'created' | 'recovered' | 'duplicate'

/** Esperas entre as verificações: o backend pode ainda estar terminando a ação abortada. */
export const RECOVERY_DELAYS_MS: readonly number[] = [0, 2500, 6000]

const normalize = (value: string | null | undefined) =>
  (value ?? '')
    .normalize('NFD')
    .replace(/\p{Diacritic}/gu, '')
    .trim()
    .toLowerCase()

/** O resultado importado passa a ser local (`local:{id}`): casa por chave, ISBN ou título+autor. */
export function isSameBook(candidate: BookSearchResultDto, target: BookSearchResultDto): boolean {
  if (candidate.resultKey === target.resultKey) return true
  if (target.bookId && candidate.bookId === target.bookId) return true
  if (target.isbn13 && candidate.isbn13 === target.isbn13) return true
  return (
    normalize(candidate.title) === normalize(target.title) &&
    normalize(candidate.authors[0]) === normalize(target.authors[0])
  )
}

export interface RecoveryOptions {
  delaysMs?: readonly number[]
  sleep?: (ms: number) => Promise<void>
}

const defaultSleep = (ms: number) => new Promise<void>((resolve) => setTimeout(resolve, ms))

/** `libraryItemId` do livro na biblioteca, ou `null` se (ainda) não foi criado. */
export async function findLibraryItemId(
  target: BookSearchResultDto,
  search: (params: BookSearchParams) => Promise<BookSearchResultDto[]>,
  { delaysMs = RECOVERY_DELAYS_MS, sleep = defaultSleep }: RecoveryOptions = {},
): Promise<string | null> {
  for (const wait of delaysMs) {
    if (wait > 0) await sleep(wait)
    try {
      const results = await search({ q: target.title, type: 'title', limit: 20 })
      const match = results.find((r) => r.inLibrary && r.libraryItemId && isSameBook(r, target))
      if (match?.libraryItemId) return match.libraryItemId
    } catch {
      // a verificação também pode falhar (rede); tenta de novo na próxima espera
    }
  }
  return null
}
