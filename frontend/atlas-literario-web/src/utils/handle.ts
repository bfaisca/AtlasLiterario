/** Apelido público (api-contracts §8.1): `^[a-z0-9_]{3,30}$` após `Trim` + minúsculas. */
export const HANDLE_PATTERN = /^[a-z0-9_]{3,30}$/
export const HANDLE_MIN_LENGTH = 3
export const HANDLE_MAX_LENGTH = 30
/** Nomes reservados: o backend os rejeita com `profile.handle_invalid`. */
export const RESERVED_HANDLES: readonly string[] = [
  'admin',
  'atlas',
  'api',
  'me',
  'profiles',
  'support',
  'moderator',
]

export function normalizeHandle(value: string): string {
  return value.trim().replace(/^@/, '').toLowerCase()
}

/** `true` quando o texto (já normalizado) é um apelido aceito. */
export function isValidHandle(value: string): boolean {
  return HANDLE_PATTERN.test(value) && !RESERVED_HANDLES.includes(value)
}

/** Validação de cliente; vazio é válido (o apelido é opcional, exceto com visibilidade pública). */
export function validateHandle(value: string): string | null {
  const handle = normalizeHandle(value)
  if (!handle) return null
  if (!HANDLE_PATTERN.test(handle)) {
    return 'Use de 3 a 30 caracteres: letras minúsculas, números e sublinhado (_).'
  }
  if (RESERVED_HANDLES.includes(handle)) return 'Este apelido é reservado. Escolha outro.'
  return null
}
