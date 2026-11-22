import axios from 'axios'
import type { ProblemDetails } from '@/types/api'

export interface ApiErrorInit {
  status: number
  title: string
  detail?: string | null
  code?: string | null
  errors?: Record<string, string[]>
  type?: string | null
}

/**
 * Erro normalizado de qualquer chamada à API (HTTP real ou mock).
 * `status = 0` indica falha de rede/timeout (sem resposta do servidor).
 */
export class ApiError extends Error {
  readonly status: number
  readonly title: string
  readonly detail: string | null
  readonly code: string | null
  readonly type: string | null
  /** Erros de validação por campo, com chaves em camelCase (ex.: `email`, `newPassword`). */
  readonly errors: Record<string, string[]>

  constructor(init: ApiErrorInit) {
    super(init.detail || init.title)
    this.name = 'ApiError'
    this.status = init.status
    this.title = init.title
    this.detail = init.detail ?? null
    this.code = init.code ?? null
    this.type = init.type ?? null
    this.errors = normalizeErrorKeys(init.errors ?? {})
  }

  get isNetworkError(): boolean {
    return this.status === 0
  }

  get isValidationError(): boolean {
    return this.status === 400 && Object.keys(this.errors).length > 0
  }

  /** Primeira mensagem de validação de um campo (chave camelCase). */
  fieldError(field: string): string | undefined {
    return this.errors[field]?.[0]
  }
}

/** "Email" → "email", "$.newPassword" → "newPassword", "Book.Title" → "book.title" */
export function normalizeFieldKey(key: string): string {
  return key
    .replace(/^\$\./, '')
    .split('.')
    .map((part) => (part ? part.charAt(0).toLowerCase() + part.slice(1) : part))
    .join('.')
}

function normalizeErrorKeys(errors: Record<string, string[]>): Record<string, string[]> {
  const result: Record<string, string[]> = {}
  for (const [key, messages] of Object.entries(errors)) {
    const normalized = normalizeFieldKey(key)
    result[normalized] = [...(result[normalized] ?? []), ...messages]
  }
  return result
}

export function isProblemDetails(value: unknown): value is ProblemDetails {
  return (
    typeof value === 'object' &&
    value !== null &&
    ('title' in value || 'status' in value || 'errors' in value)
  )
}

const STATUS_TITLES: Record<number, string> = {
  400: 'Requisição inválida',
  401: 'Não autenticado',
  403: 'Acesso negado',
  404: 'Não encontrado',
  409: 'Conflito',
  429: 'Muitas requisições',
  500: 'Erro interno do servidor',
  501: 'Não implementado',
  502: 'Fonte externa indisponível',
  503: 'Serviço indisponível',
}

/** Converte qualquer erro (Axios, ProblemDetails, Error) em `ApiError`. */
export function toApiError(error: unknown): ApiError {
  if (error instanceof ApiError) return error

  if (axios.isAxiosError(error)) {
    const response = error.response
    if (!response) {
      const timeout = error.code === 'ECONNABORTED' || error.code === 'ETIMEDOUT'
      return new ApiError({
        status: 0,
        title: timeout ? 'Tempo de resposta esgotado' : 'Falha de conexão',
        code: timeout ? 'network.timeout' : 'network.unavailable',
      })
    }
    const data: unknown = response.data
    if (isProblemDetails(data)) {
      return new ApiError({
        status: data.status ?? response.status,
        title: data.title ?? STATUS_TITLES[response.status] ?? 'Erro',
        detail: data.detail,
        code: data.code,
        errors: data.errors,
        type: data.type,
      })
    }
    return new ApiError({
      status: response.status,
      title: STATUS_TITLES[response.status] ?? `Erro HTTP ${response.status}`,
    })
  }

  return new ApiError({
    status: 0,
    title: 'Erro inesperado',
    detail: error instanceof Error ? error.message : null,
    code: 'client.unexpected',
  })
}

/** Mensagens amigáveis (pt-BR) por código estável da API. */
const CODE_MESSAGES: Record<string, string> = {
  // Geral
  'validation.failed': 'Verifique os campos destacados.',
  'feature.not_implemented': 'Este recurso ainda não está disponível.',
  'external.unavailable':
    'As fontes bibliográficas externas estão indisponíveis no momento. Tente novamente em instantes.',
  // Autenticação
  'auth.unauthorized': 'Sua sessão expirou. Entre novamente.',
  'auth.invalid_credentials': 'E-mail ou senha incorretos.',
  'auth.email_taken': 'Já existe uma conta com este e-mail.',
  'auth.invalid_refresh_token': 'Sua sessão expirou. Entre novamente.',
  'auth.invalid_reset_token': 'O link de redefinição é inválido ou expirou. Solicite um novo.',
  'auth.invalid_password': 'A senha atual está incorreta.',
  'auth.user_not_found': 'Usuário não encontrado.',
  // Usuário
  'user.invalid_name': 'Informe um nome válido (até 100 caracteres).',
  'user.invalid_email': 'Informe um e-mail válido.',
  'user.invalid_map_visibility': 'Opção de visibilidade inválida.',
  // Biblioteca
  'library.duplicate': 'Este livro já está na sua biblioteca.',
  'library.invalid_rating': 'A avaliação deve ser de 1 a 5 estrelas.',
  'library.invalid_dates': 'A data de início deve ser anterior ou igual à data de conclusão.',
  'library.invalid_status': 'Status de leitura inválido.',
  'library.review_too_long': 'A resenha deve ter no máximo 10.000 caracteres.',
  // Livros e autores
  'book.invalid_isbn13': 'ISBN-13 inválido. Confira os 13 dígitos.',
  'book.invalid_isbn10': 'ISBN-10 inválido. Confira os 10 caracteres.',
  'book.duplicate_isbn': 'Já existe um livro cadastrado com este ISBN-13.',
  'book.not_in_library':
    'Adicione este livro à sua biblioteca antes de corrigir seus dados geográficos.',
  'author.not_in_library': 'Este autor não pertence a nenhum livro da sua biblioteca.',
  'geo.moderator_required': 'Somente moderadores podem alterar países já informados.',
  'cover.invalid_type': 'Formato de imagem não aceito. Use JPEG, PNG ou WebP.',
  'cover.too_large':
    'A imagem é grande demais (máximo de 2 MB). Escolha outra ou reduza o tamanho.',
  'cover.not_found': 'Este livro não tem capa enviada.',
  'author.invalid_name': 'Informe um nome de autor válido.',
  // Compartilhamento
  'share.private_profile':
    'Seu perfil está privado. Altere a visibilidade do mapa para gerar um link público.',
  // Cliente
  'network.unavailable': 'Não foi possível conectar ao servidor. Verifique sua conexão.',
  'network.timeout': 'O servidor demorou demais para responder. Tente novamente.',
}

const STATUS_MESSAGES: Record<number, string> = {
  401: 'Sua sessão expirou. Entre novamente.',
  403: 'Você não tem permissão para esta ação.',
  404: 'O recurso solicitado não foi encontrado.',
  429: 'Muitas tentativas em sequência. Aguarde um pouco.',
  500: 'Ocorreu um erro no servidor. Tente novamente mais tarde.',
  502: CODE_MESSAGES['external.unavailable']!,
  501: CODE_MESSAGES['feature.not_implemented']!,
  503: 'Serviço temporariamente indisponível.',
}

/** Mensagem para exibir ao usuário a partir de qualquer erro. */
export function getErrorMessage(error: unknown): string {
  const apiError = toApiError(error)
  if (apiError.code && CODE_MESSAGES[apiError.code]) return CODE_MESSAGES[apiError.code]!
  if (apiError.isValidationError) return CODE_MESSAGES['validation.failed']!
  if (apiError.detail) return apiError.detail
  return STATUS_MESSAGES[apiError.status] ?? apiError.title ?? 'Algo deu errado.'
}
