import type { ApiErrorKind, FieldErrors } from './types'

export interface ApiErrorInit {
  kind: ApiErrorKind
  /** Status HTTP; 0 quando não houve resposta do servidor. */
  status: number
  title: string
  detail?: string | null
  fieldErrors?: FieldErrors
  traceId?: string | null
  cause?: unknown
}

/**
 * Único formato de erro que sai da camada HTTP.
 * Stores e componentes nunca precisam conhecer AxiosError nem ProblemDetails.
 */
export class ApiError extends Error {
  readonly kind: ApiErrorKind
  readonly status: number
  readonly title: string
  readonly detail: string | null
  readonly fieldErrors: FieldErrors
  readonly traceId: string | null

  constructor(init: ApiErrorInit) {
    super(init.detail ?? init.title, { cause: init.cause })
    this.name = 'ApiError'
    this.kind = init.kind
    this.status = init.status
    this.title = init.title
    this.detail = init.detail ?? null
    this.fieldErrors = init.fieldErrors ?? {}
    this.traceId = init.traceId ?? null
  }

  get isValidationError(): boolean {
    return Object.keys(this.fieldErrors).length > 0
  }

  /** Primeira mensagem de validação de um campo (nome em camelCase). */
  fieldError(field: string): string | undefined {
    return this.fieldErrors[field]?.[0]
  }
}

export function isApiError(error: unknown): error is ApiError {
  return error instanceof ApiError
}
