/**
 * Thin fetch wrapper for the ERP API.
 * - Same-origin cookies (session is an HttpOnly cookie the script never sees).
 * - Antiforgery: a request token from /api/auth/csrf is sent as X-XSRF-TOKEN on unsafe requests;
 *   a stale token is refreshed and the request retried once.
 * - Errors are RFC 7807 problem documents, surfaced as ApiError with a stable `code`.
 */

export type FieldErrors = Record<string, string[]>

export class ApiError extends Error {
  readonly status: number
  readonly code: string
  readonly title: string
  readonly fieldErrors: FieldErrors

  constructor(status: number, code: string, title: string, message: string, fieldErrors: FieldErrors = {}) {
    super(message)
    this.status = status
    this.code = code
    this.title = title
    this.fieldErrors = fieldErrors
  }
}

type Query = Record<string, string | number | boolean | null | undefined | readonly (string | number)[]>

interface RequestOptions {
  method?: "GET" | "POST" | "PUT" | "DELETE"
  body?: unknown
  query?: Query
  signal?: AbortSignal
}

let csrfToken: string | null = null
let unauthorizedHandler: (() => void) | null = null

/** Called when an authenticated request comes back 401 (session expired or revoked). */
export function onUnauthorized(handler: () => void) {
  unauthorizedHandler = handler
}

/** The token is bound to the signed-in identity; refresh after login and logout. */
export async function refreshCsrf(): Promise<string> {
  const res = await fetch("/api/auth/csrf", { credentials: "same-origin", headers: { Accept: "application/json" } })
  if (!res.ok) throw new ApiError(res.status, "auth.csrf_unavailable", "Security token unavailable", "Could not reach the server.")
  csrfToken = ((await res.json()) as { token: string }).token
  return csrfToken
}

export function buildQuery(query?: Query): string {
  if (!query) return ""
  const params = new URLSearchParams()
  for (const [key, value] of Object.entries(query)) {
    if (value === undefined || value === null || value === "") continue
    if (Array.isArray(value)) value.forEach((v) => params.append(key, String(v)))
    else params.set(key, String(value))
  }
  const s = params.toString()
  return s ? `?${s}` : ""
}

export async function api<T>(path: string, options: RequestOptions = {}): Promise<T> {
  const { method = "GET", body, query, signal } = options
  const send = async (): Promise<Response> => {
    const headers: Record<string, string> = { Accept: "application/json" }
    let payload: BodyInit | undefined
    if (method !== "GET") headers["X-XSRF-TOKEN"] = csrfToken ?? (await refreshCsrf())
    if (body instanceof FormData) payload = body
    else if (body !== undefined) {
      headers["Content-Type"] = "application/json"
      payload = JSON.stringify(body)
    }
    return fetch(path + buildQuery(query), { method, headers, body: payload, credentials: "same-origin", signal })
  }

  let res = await send()
  if (res.status === 400 && method !== "GET") {
    const problem = await res.clone().json().catch(() => null)
    if (problem?.code === "auth.csrf_invalid") {
      await refreshCsrf()
      res = await send()
    }
  }

  if (res.status === 401 && path !== "/api/auth/login" && path !== "/api/auth/me") unauthorizedHandler?.()
  if (!res.ok) throw await toApiError(res)
  if (res.status === 204) return undefined as T
  const type = res.headers.get("content-type") ?? ""
  return (type.includes("json") ? await res.json() : undefined) as T
}

async function toApiError(res: Response): Promise<ApiError> {
  const problem = await res.json().catch(() => null)
  if (res.status === 429)
    return new ApiError(429, "rate_limited", "Too many attempts", "Too many attempts. Wait a minute and try again.")
  if (!problem)
    return new ApiError(res.status, "http_" + res.status, res.statusText, `The server responded with ${res.status}.`)
  return new ApiError(
    res.status,
    problem.code ?? "http_" + res.status,
    problem.title ?? "Request failed",
    problem.detail ?? problem.title ?? "Request failed.",
    problem.errors ?? {},
  )
}

/** Human message for any thrown value. */
export function errorMessage(error: unknown): string {
  if (error instanceof ApiError) {
    const fields = Object.values(error.fieldErrors).flat()
    return fields.length > 0 && error.code === "validation_failed" ? fields[0] : error.message
  }
  if (error instanceof Error) return error.message
  return "Something went wrong."
}
