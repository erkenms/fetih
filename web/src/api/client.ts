import createClient from 'openapi-fetch'
import type { components, paths } from './schema'

// Typed client for Comet.Web. Types come from `npm run api`; never write DTOs by hand.
export const api = createClient<paths>({ baseUrl: '', credentials: 'same-origin' })

export type Schemas = components['schemas']
export type Permission = Schemas['Permission']
export type Me = Schemas['MeResponse']

// A failed call. `code` is the backend's error code ("game_offline", "invalid_credentials"),
// which pages show through the common:errors.* texts.
export class ApiError extends Error {
  readonly status: number
  readonly code: string

  constructor(status: number, code: string) {
    super(code)
    this.status = status
    this.code = code
  }
}

function errorCode(status: number, body: unknown): string {
  if (body && typeof body === 'object' && 'error' in body && typeof body.error === 'string')
    return body.error
  if (status === 401) return 'unauthorized'
  if (status === 403) return 'forbidden'
  if (status === 404) return 'not_found'
  if (status === 429) return 'too_many_requests'
  return 'server_error'
}

// Turns an openapi-fetch result into its data, or throws ApiError.
export async function unwrap<T>(
  call: Promise<{ data?: T; error?: unknown; response: Response }>,
): Promise<T> {
  let result
  try {
    result = await call
  } catch {
    throw new ApiError(0, 'network')
  }
  const { data, error, response } = result
  if (!response.ok) throw new ApiError(response.status, errorCode(response.status, error))
  return data as T
}
