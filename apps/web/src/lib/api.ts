import type { AuthResponse, SetupStatus, User } from './types'

export class ApiError extends Error {
  status: number
  body: unknown

  constructor(status: number, message: string, body?: unknown) {
    super(message)
    this.status = status
    this.body = body
  }
}

let csrfToken = ''

export function setCsrfToken(token: string) {
  csrfToken = token
}

async function ensureCsrf() {
  if (csrfToken) return csrfToken
  const response = await fetch('/api/v1/auth/csrf', { credentials: 'include' })
  const data = (await response.json()) as { token: string }
  csrfToken = data.token
  return csrfToken
}

export async function api<T>(path: string, options: RequestInit = {}, retried = false): Promise<T> {
  const method = (options.method ?? 'GET').toUpperCase()
  const headers = new Headers(options.headers)
  if (method !== 'GET' && method !== 'HEAD') {
    headers.set('X-CSRF-TOKEN', await ensureCsrf())
    if (options.body && !headers.has('Content-Type')) headers.set('Content-Type', 'application/json')
  }

  const response = await fetch(path, { ...options, headers, credentials: 'include' })
  if (response.status === 204) return undefined as T
  const text = await response.text()
  const data = text ? JSON.parse(text) : null
  if (!response.ok) {
    if (response.status === 400 && data?.title === 'Invalid CSRF token' && !retried) {
      csrfToken = ''
      return api<T>(path, options, true)
    }
    throw new ApiError(response.status, data?.detail ?? data?.title ?? response.statusText, data)
  }
  return data as T
}

export const authApi = {
  setup: () => api<SetupStatus>('/api/v1/auth/setup-status'),
  me: () => api<User>('/api/v1/auth/me'),
  register: (body: { email: string; password: string; displayName: string }) =>
    api<AuthResponse>('/api/v1/auth/register', { method: 'POST', body: JSON.stringify(body) }).then(storeAuth),
  login: (body: { email: string; password: string }) =>
    api<AuthResponse>('/api/v1/auth/login', { method: 'POST', body: JSON.stringify(body) }).then(storeAuth),
  logout: () => api<void>('/api/v1/auth/logout', { method: 'POST' }),
}

async function storeAuth(response: AuthResponse) {
  // The token minted during login is anonymous. Fetch one after the session cookie is stored.
  setCsrfToken('')
  await ensureCsrf()
  return response
}
