import axios, { type AxiosRequestConfig } from 'axios'
import { normalizeApiError } from './normalizeApiError'

const DEFAULT_BASE_URL = '/api'

/** VITE_API_BASE_URL (sem barra final) ou `/api`, atendido pelo proxy do Vite em dev. */
export const API_BASE_URL = (import.meta.env.VITE_API_BASE_URL?.trim() || DEFAULT_BASE_URL).replace(/\/+$/, '')

/**
 * Instância única do Axios. As rotas são relativas à base:
 * `httpClient.get('/accounts')` → `GET /api/accounts`.
 */
export const httpClient = axios.create({
  baseURL: API_BASE_URL,
  timeout: 15_000,
  // Sessão em cookie HttpOnly emitido pela API: o JavaScript nunca vê o token.
  // withCredentials só importa se a API estiver em outra origem (VITE_API_BASE_URL).
  withCredentials: true,
  headers: {
    Accept: 'application/json, application/problem+json',
  },
})

type UnauthorizedHandler = () => void
let unauthorizedHandler: UnauthorizedHandler | null = null

/**
 * Registra quem trata sessão ausente/expirada (401) — o store de autenticação, no main.ts.
 * Evita dependência circular entre a camada HTTP e o roteador.
 */
export function onUnauthorized(handler: UnauthorizedHandler | null) {
  unauthorizedHandler = handler
}

/** Rotas cujo 401 é resposta esperada (credenciais inválidas, verificação de sessão). */
const AUTH_PROBES = ['/auth/login', '/auth/me']

/**
 * Tratamento centralizado de respostas não bem-sucedidas: toda rejeição
 * (status fora de 2xx, rede, timeout, cancelamento) vira ApiError.
 * 401 em qualquer outra chamada = sessão expirou: avisa o handler de autenticação.
 */
httpClient.interceptors.response.use(undefined, (error: unknown) => {
  const apiError = normalizeApiError(error)
  const url = (error as { config?: { url?: string } })?.config?.url ?? ''
  if (apiError.status === 401 && !AUTH_PROBES.some((probe) => url.startsWith(probe))) unauthorizedHandler?.()
  return Promise.reject(apiError)
})

/**
 * Atalhos tipados que devolvem só o corpo da resposta.
 * Os serviços de cada feature usam estes métodos; em caso de falha, rejeitam com ApiError.
 */
export const api = {
  get<T>(url: string, config?: AxiosRequestConfig): Promise<T> {
    return httpClient.get<T>(url, config).then((response) => response.data)
  },
  post<T>(url: string, body?: unknown, config?: AxiosRequestConfig): Promise<T> {
    return httpClient.post<T>(url, body, config).then((response) => response.data)
  },
  put<T>(url: string, body?: unknown, config?: AxiosRequestConfig): Promise<T> {
    return httpClient.put<T>(url, body, config).then((response) => response.data)
  },
  patch<T>(url: string, body?: unknown, config?: AxiosRequestConfig): Promise<T> {
    return httpClient.patch<T>(url, body, config).then((response) => response.data)
  },
  delete<T>(url: string, config?: AxiosRequestConfig): Promise<T> {
    return httpClient.delete<T>(url, config).then((response) => response.data)
  },
}
