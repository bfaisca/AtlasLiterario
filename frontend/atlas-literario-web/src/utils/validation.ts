/** Validações de formulário (espelham as regras do backend, que continua sendo a fonte da verdade). */

const EMAIL_PATTERN = /^[^\s@]+@[^\s@]+\.[^\s@]+$/

export function validateRequired(value: string, label = 'Este campo'): string | null {
  return value.trim() ? null : `${label} é obrigatório.`
}

export function validateEmail(value: string): string | null {
  if (!value.trim()) return 'Informe seu e-mail.'
  return EMAIL_PATTERN.test(value.trim()) ? null : 'Informe um e-mail válido.'
}

/** Senha ≥ 8 caracteres, com ao menos uma letra e um número (api-contracts §2). */
export function validatePassword(value: string): string | null {
  if (!value) return 'Informe uma senha.'
  if (value.length < 8) return 'A senha deve ter pelo menos 8 caracteres.'
  if (!/\p{L}/u.test(value) || !/\d/.test(value)) return 'A senha deve conter letras e números.'
  return null
}

export function validatePasswordConfirmation(
  password: string,
  confirmation: string,
): string | null {
  if (!confirmation) return 'Confirme a senha.'
  return password === confirmation ? null : 'As senhas não coincidem.'
}

export function validateName(value: string): string | null {
  const trimmed = value.trim()
  if (!trimmed) return 'Informe seu nome.'
  if (trimmed.length > 100) return 'O nome deve ter no máximo 100 caracteres.'
  return null
}
