/// <reference types="vite/client" />

interface ImportMetaEnv {
  /** Base da API REST. Padrão: `/api` (proxy do Vite em dev / nginx em produção). */
  readonly VITE_API_BASE_URL?: string
  /** `"true"` para usar a camada de mocks em memória em vez da API real. */
  readonly VITE_USE_MOCKS?: string
}

interface ImportMeta {
  readonly env: ImportMetaEnv
}
