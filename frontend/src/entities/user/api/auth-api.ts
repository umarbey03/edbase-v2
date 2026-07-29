import { http } from '@/shared/api'
import type { AuthResponse, LoginRequest, UserDto } from '@/shared/types'

/** SPEC 5: `POST /api/v1/auth/login` — anonim. */
export function login(payload: LoginRequest): Promise<AuthResponse> {
  return http.post<AuthResponse>('/api/v1/auth/login', payload, { auth: false })
}

/** SPEC 5: `POST /api/v1/auth/logout` — 204. */
export function logout(): Promise<void> {
  return http.post<void>('/api/v1/auth/logout')
}

/** SPEC 5: `GET /api/v1/auth/me` — `UserDto`. */
export function fetchMe(options?: { signal?: AbortSignal }): Promise<UserDto> {
  return http.get<UserDto>('/api/v1/auth/me', { signal: options?.signal })
}
