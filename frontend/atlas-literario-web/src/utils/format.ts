const dateFormatter = new Intl.DateTimeFormat('pt-BR', {
  day: '2-digit',
  month: 'short',
  year: 'numeric',
  timeZone: 'UTC',
})

const monthFormatter = new Intl.DateTimeFormat('pt-BR', {
  month: 'short',
  year: 'numeric',
  timeZone: 'UTC',
})

/** "2025-03-14" → "14 de mar. de 2025" (sem deslocamento de fuso). */
export function formatDate(value: string | null | undefined): string {
  if (!value) return ''
  const [year, month, day] = value.slice(0, 10).split('-').map(Number)
  if (!year || !month || !day) return value
  return dateFormatter.format(new Date(Date.UTC(year, month - 1, day)))
}

/** "2025-03" → "mar. de 2025" */
export function formatMonth(value: string): string {
  const [year, month] = value.split('-').map(Number)
  if (!year || !month) return value
  return monthFormatter.format(new Date(Date.UTC(year, month - 1, 1)))
}

export function formatNumber(value: number): string {
  return value.toLocaleString('pt-BR')
}

/** Data de hoje (UTC) no formato "YYYY-MM-DD". */
export function todayIsoDate(now: Date = new Date()): string {
  return now.toISOString().slice(0, 10)
}
