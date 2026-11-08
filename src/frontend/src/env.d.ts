/// <reference types="vite/client" />

interface ImportMetaEnv {
  /** URL base da API. Padrão: `/api` (proxy do Vite → http://localhost:5017). */
  readonly VITE_API_BASE_URL?: string
}

interface ImportMeta {
  readonly env: ImportMetaEnv
}
