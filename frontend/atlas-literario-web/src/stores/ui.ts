import { defineStore } from 'pinia'
import { ref } from 'vue'
import { getErrorMessage } from '@/api/errors'

export type ToastKind = 'success' | 'error' | 'info' | 'warning'

export interface Toast {
  id: number
  kind: ToastKind
  message: string
}

const DEFAULT_TIMEOUT_MS = 4500

export const useUiStore = defineStore('ui', () => {
  const toasts = ref<Toast[]>([])
  let nextId = 1

  function dismiss(id: number) {
    toasts.value = toasts.value.filter((t) => t.id !== id)
  }

  function notify(kind: ToastKind, message: string, timeoutMs = DEFAULT_TIMEOUT_MS) {
    const id = nextId++
    toasts.value = [...toasts.value, { id, kind, message }].slice(-4)
    if (timeoutMs > 0) setTimeout(() => dismiss(id), timeoutMs)
    return id
  }

  return {
    toasts,
    dismiss,
    notify,
    success: (message: string) => notify('success', message),
    info: (message: string) => notify('info', message),
    warning: (message: string) => notify('warning', message, 9000),
    error: (message: string) => notify('error', message, 7000),
    /** Exibe a mensagem amigável de um erro da API. */
    notifyError: (error: unknown) => notify('error', getErrorMessage(error), 7000),
  }
})
