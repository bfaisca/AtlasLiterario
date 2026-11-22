import { reactive, ref } from 'vue'
import { getErrorMessage, toApiError } from '@/api/errors'

/**
 * Estado de erros de formulário: validação local por campo + mensagens vindas do `ApiError`
 * (ProblemDetails.errors, com chaves camelCase) + mensagem geral amigável.
 */
export function useFormErrors<K extends string>(fields: readonly K[]) {
  const fieldErrors = reactive(Object.fromEntries(fields.map((f) => [f, null]))) as Record<
    K,
    string | null
  >
  const formError = ref<string | null>(null)

  function reset() {
    for (const field of fields) fieldErrors[field] = null
    formError.value = null
  }

  /** Aplica resultados de validadores locais. Retorna `true` se tudo for válido. */
  function validate(results: Partial<Record<K, string | null>>): boolean {
    reset()
    let valid = true
    for (const field of fields) {
      const message = results[field] ?? null
      fieldErrors[field] = message
      if (message) valid = false
    }
    return valid
  }

  /** Distribui os erros da API nos campos conhecidos; o restante vira mensagem geral. */
  function applyApiError(error: unknown) {
    const apiError = toApiError(error)
    let mappedAny = false
    for (const field of fields) {
      const message = apiError.fieldError(field)
      if (message) {
        fieldErrors[field] = message
        mappedAny = true
      }
    }
    formError.value = mappedAny && apiError.isValidationError ? null : getErrorMessage(apiError)
  }

  return { fieldErrors, formError, reset, validate, applyApiError }
}
