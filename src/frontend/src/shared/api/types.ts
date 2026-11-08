/**
 * Contratos de erro HTTP do ASP.NET Core (RFC 9457).
 * A API os envia como `application/problem+json` via GlobalExceptionHandler / AddProblemDetails.
 */

/** Microsoft.AspNetCore.Mvc.ProblemDetails, já com chaves em camelCase. */
export interface ProblemDetails {
  type?: string | null
  title?: string | null
  status?: number | null
  detail?: string | null
  instance?: string | null
  /** Extensão adicionada por AddProblemDetails() em todas as respostas de erro. */
  traceId?: string | null
  /** Demais extensões (ProblemDetails.Extensions). */
  [extension: string]: unknown
}

/** Microsoft.AspNetCore.Mvc.ValidationProblemDetails (HTTP 400). */
export interface ValidationProblemDetails extends ProblemDetails {
  errors: Record<string, string[]>
}

/** Mensagens de validação por campo, com nomes em camelCase (ex.: `sourceAccount`). */
export type FieldErrors = Record<string, string[]>

/**
 * Origem do erro:
 * - `http`: o servidor respondeu com status fora de 2xx;
 * - `network`: sem resposta (API fora do ar, CORS, DNS, offline);
 * - `timeout`: a requisição excedeu o tempo limite;
 * - `canceled`: cancelada pelo próprio cliente (AbortController);
 * - `unknown`: falha que não veio do Axios (bug no código, por exemplo).
 */
export type ApiErrorKind = 'http' | 'network' | 'timeout' | 'canceled' | 'unknown'
