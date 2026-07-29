import { http } from '@/shared/api'
import type { LiveKitJoinDto, LiveSessionDto } from '@/shared/types'

const BASE = '/api/v1/live-sessions'

/** SPEC 5: `GET /api/v1/live-sessions` */
export function fetchLiveSessions(options?: { signal?: AbortSignal }): Promise<LiveSessionDto[]> {
  return http.get<LiveSessionDto[]>(BASE, { signal: options?.signal })
}

/** SPEC 5: `GET /api/v1/live-sessions/{id}` */
export function fetchLiveSession(
  id: number,
  options?: { signal?: AbortSignal },
): Promise<LiveSessionDto> {
  return http.get<LiveSessionDto>(`${BASE}/${id}`, { signal: options?.signal })
}

/** SPEC 5: `POST /api/v1/live-sessions/{id}/start` — Teacher/Assistant/Admin */
export function startLiveSession(id: number): Promise<LiveSessionDto> {
  return http.post<LiveSessionDto>(`${BASE}/${id}/start`)
}

/** SPEC 5: `POST /api/v1/live-sessions/{id}/end` — Teacher/Assistant/Admin */
export function endLiveSession(id: number): Promise<LiveSessionDto> {
  return http.post<LiveSessionDto>(`${BASE}/${id}/end`)
}

/**
 * SPEC 5: `POST /api/v1/live-sessions/{id}/token` -> `LiveKitJoinDto`.
 * Frontend LiveKit'ga aynan shu javob bilan ulanadi.
 */
export function fetchLiveKitJoin(id: number): Promise<LiveKitJoinDto> {
  return http.post<LiveKitJoinDto>(`${BASE}/${id}/token`)
}
