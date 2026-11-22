import type { ReadingStatus, UpdateLibraryItemRequest } from '@/types/api'
import { todayIsoDate } from './format'

/** Rascunho do formulário de leitura (strings vazias = não informado). */
export interface ReadingDraft {
  status: ReadingStatus
  rating: number | null
  startDate: string
  finishedDate: string
  review: string
}

export type ReadingField = 'status' | 'rating' | 'startDate' | 'finishedDate' | 'review'
export type ReadingErrors = Partial<Record<ReadingField, string>>

export const REVIEW_MAX_LENGTH = 10_000

export function emptyReadingDraft(status: ReadingStatus = 'WantToRead'): ReadingDraft {
  return { status, rating: null, startDate: '', finishedDate: '', review: '' }
}

export function draftFromItem(item: {
  status: ReadingStatus
  rating: number | null
  startDate: string | null
  finishedDate: string | null
  review: string | null
}): ReadingDraft {
  return {
    status: item.status,
    rating: item.rating,
    startDate: item.startDate ?? '',
    finishedDate: item.finishedDate ?? '',
    review: item.review ?? '',
  }
}

/**
 * Validação no cliente (o backend continua sendo a fonte da verdade):
 * início ≤ conclusão, datas não futuras, nota 1–5, resenha ≤ 10.000.
 * A data de conclusão só vale para "Lido"; sem ela, o backend usa a data de hoje.
 */
export function validateReading(
  draft: ReadingDraft,
  today: string = todayIsoDate(),
): ReadingErrors {
  const errors: ReadingErrors = {}
  const finished = draft.status === 'Read' ? draft.finishedDate : ''
  if (
    draft.rating !== null &&
    (!Number.isInteger(draft.rating) || draft.rating < 1 || draft.rating > 5)
  ) {
    errors.rating = 'A avaliação deve ser de 1 a 5 estrelas.'
  }
  if (draft.startDate && draft.startDate > today) {
    errors.startDate = 'A data de início não pode estar no futuro.'
  }
  if (finished && finished > today) {
    errors.finishedDate = 'A data de conclusão não pode estar no futuro.'
  }
  if (draft.startDate && finished && draft.startDate > finished) {
    errors.finishedDate = 'A conclusão deve ser igual ou posterior ao início.'
  }
  if (draft.review.length > REVIEW_MAX_LENGTH) {
    errors.review = 'A resenha deve ter no máximo 10.000 caracteres.'
  }
  return errors
}

export function toReadingRequest(draft: ReadingDraft): UpdateLibraryItemRequest {
  return {
    status: draft.status,
    rating: draft.rating,
    review: draft.review.trim() || null,
    startDate: draft.startDate || null,
    finishedDate: draft.status === 'Read' ? draft.finishedDate || null : null,
  }
}
