/** Remove hífens e espaços ("978-85-359-0277-1" → "9788535902771"). */
export function normalizeIsbn(value: string): string {
  return value.replace(/[\s-]/g, '').toUpperCase()
}

export function isValidIsbn13(value: string): boolean {
  const isbn = normalizeIsbn(value)
  if (!/^\d{13}$/.test(isbn)) return false
  const sum = [...isbn]
    .slice(0, 12)
    .reduce((acc, digit, index) => acc + Number(digit) * (index % 2 === 0 ? 1 : 3), 0)
  return (10 - (sum % 10)) % 10 === Number(isbn[12])
}

export function isValidIsbn10(value: string): boolean {
  const isbn = normalizeIsbn(value)
  if (!/^\d{9}[\dX]$/.test(isbn)) return false
  const sum = [...isbn].reduce(
    (acc, char, index) => acc + (char === 'X' ? 10 : Number(char)) * (10 - index),
    0,
  )
  return sum % 11 === 0
}

/** Termo parece um ISBN (10 ou 13 dígitos, com ou sem hífens)? */
export function looksLikeIsbn(value: string): boolean {
  return /^(\d{9}[\dX]|\d{13})$/.test(normalizeIsbn(value))
}
