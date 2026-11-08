import { isAxiosError, isCancel } from 'axios'
import { ApiError } from './ApiError'
import type { FieldErrors, ProblemDetails } from './types'

/** Títulos usados quando a resposta não traz um (corpo vazio, HTML, texto puro). */
const fallbackTitles: Record<number, string> = {
  400: 'Dados inválidos.',
  401: 'Não autenticado.',
  403: 'Acesso negado.',
  404: 'Recurso não encontrado.',
  409: 'Conflito.',
  422: 'Regra de negócio violada.',
  429: 'Muitas requisições. Tente novamente em instantes.',
  500: 'Erro interno no servidor.',
  502: 'Serviço indisponível.',
  503: 'Serviço indisponível.',
  504: 'O servidor demorou demais para responder.',
}

function fallbackTitle(status: number): string {
  return fallbackTitles[status] ?? (status >= 500 ? 'Erro no servidor.' : 'Falha na requisição.')
}

type JsonObject = Record<string, unknown>

function isJsonObject(value: unknown): value is JsonObject {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function lowerFirst(text: string): string {
  return text.charAt(0).toLowerCase() + text.slice(1)
}

function readString(value: unknown): string | null {
  return typeof value === 'string' && value.trim() !== '' ? value : null
}

/**
 * Lê o corpo da resposta de erro como objeto JSON.
 * O Axios já faz o parse de `application/json` e `application/problem+json`; se o corpo
 * chegar como string (parse silenciosamente falho, responseType 'text'), tentamos de novo.
 * Corpo vazio, HTML ou texto puro → null (o erro é montado só com o status).
 */
function parseBody(data: unknown): JsonObject | null {
  if (isJsonObject(data)) return data
  if (typeof data !== 'string' || !data.trimStart().startsWith('{')) return null
  try {
    const parsed: unknown = JSON.parse(data)
    return isJsonObject(parsed) ? parsed : null
  } catch {
    return null
  }
}

/** Converte as chaves de primeiro nível para camelCase (`Title` → `title`, `TraceId` → `traceId`). */
function toProblemDetails(body: JsonObject): ProblemDetails {
  return Object.fromEntries(Object.entries(body).map(([key, value]) => [lowerFirst(key), value]))
}

/**
 * Normaliza o nome do campo vindo do ASP.NET para o nome usado no frontend:
 * - FluentValidation:     `SourceAccount`       → `sourceAccount`
 * - Propriedades aninhadas: `Items[0].Amount`   → `items[0].amount`
 * - Binding do System.Text.Json: `$.amount`     → `amount`
 */
export function normalizeFieldName(name: string): string {
  return name
    .replace(/^\$\.?/, '')
    .split('.')
    .map(lowerFirst)
    .join('.')
}

function toFieldErrors(errors: unknown): FieldErrors {
  if (!isJsonObject(errors)) return {}

  const result: FieldErrors = {}
  for (const [name, value] of Object.entries(errors)) {
    const messages = (Array.isArray(value) ? value : [value]).filter(
      (message): message is string => typeof message === 'string' && message !== '',
    )
    if (messages.length === 0) continue

    const field = normalizeFieldName(name)
    result[field] = [...(result[field] ?? []), ...messages]
  }
  return result
}

function fromResponse(status: number, data: unknown, cause: unknown): ApiError {
  const body = parseBody(data)
  const problem = body ? toProblemDetails(body) : null

  return new ApiError({
    kind: 'http',
    status,
    title: readString(problem?.title) ?? fallbackTitle(status),
    detail: readString(problem?.detail),
    fieldErrors: toFieldErrors(problem?.errors),
    traceId: readString(problem?.traceId),
    cause,
  })
}

/** Converte qualquer falha de requisição em ApiError. Idempotente. */
export function normalizeApiError(error: unknown): ApiError {
  if (error instanceof ApiError) return error

  if (isCancel(error)) {
    return new ApiError({ kind: 'canceled', status: 0, title: 'Requisição cancelada.', cause: error })
  }

  if (!isAxiosError(error)) {
    return new ApiError({
      kind: 'unknown',
      status: 0,
      title: 'Erro inesperado.',
      detail: error instanceof Error ? error.message : null,
      cause: error,
    })
  }

  if (error.response) return fromResponse(error.response.status, error.response.data, error)

  if (error.code === 'ECONNABORTED' || error.code === 'ETIMEDOUT') {
    return new ApiError({
      kind: 'timeout',
      status: 0,
      title: 'A requisição excedeu o tempo limite.',
      cause: error,
    })
  }

  return new ApiError({
    kind: 'network',
    status: 0,
    title: 'Não foi possível conectar ao servidor.',
    detail: 'Verifique sua conexão ou se a API está em execução.',
    cause: error,
  })
}
