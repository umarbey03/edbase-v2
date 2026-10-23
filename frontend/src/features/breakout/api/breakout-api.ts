import { http } from '@/shared/api'
import type { LiveKitJoinDto } from '@/shared/types'

/**
 * Ichki guruhlar (breakout) — `BreakoutsController`.
 *
 * Holat so'rovchiga mos keladi: ustoz hamma guruhni ko'radi, o'quvchi faqat
 * o'zinikini (`rooms` da bitta element yoki bo'sh).
 */
export interface BreakoutMemberDto {
  userId: number
  displayName: string
  online: boolean
}

export interface BreakoutRoomDto {
  number: number
  members: BreakoutMemberDto[]
  helpRequested: boolean
}

export interface BreakoutStateDto {
  active: boolean
  endsAt: string | null
  rooms: BreakoutRoomDto[]
  unassigned: BreakoutMemberDto[]
  /** O'quvchining guruhi; asosiy xonada bo'lsa `null`. */
  myRoom: number | null
  canManage: boolean
}

const base = (sessionId: number): string => `/api/v1/live-sessions/${sessionId}/breakouts`

export function fetchBreakouts(sessionId: number, signal?: AbortSignal): Promise<BreakoutStateDto> {
  return http.get<BreakoutStateDto>(base(sessionId), signal === undefined ? undefined : { signal })
}

/** `durationMinutes = null` — taymersiz. */
export function startBreakouts(sessionId: number, durationMinutes: number | null): Promise<BreakoutStateDto> {
  return http.post<BreakoutStateDto>(base(sessionId), { durationMinutes })
}

/** `room = 0` — asosiy xona; `rooms.length + 1` — yangi guruh. */
export function moveBreakoutStudent(sessionId: number, studentId: number, room: number): Promise<BreakoutStateDto> {
  return http.put<BreakoutStateDto>(`${base(sessionId)}/students/${studentId}`, { room })
}

export function closeBreakouts(sessionId: number): Promise<void> {
  return http.delete<void>(base(sessionId))
}

export function setBreakoutHelp(sessionId: number, raised: boolean): Promise<void> {
  return http.post<void>(`${base(sessionId)}/help`, { raised })
}

/** `monitor = true` — ustozning ko'rinmas tinglovchi ulanishi (kim gapirayotganini bilish). */
export function fetchBreakoutJoin(
  sessionId: number,
  room: number | null,
  monitor = false,
  signal?: AbortSignal,
): Promise<LiveKitJoinDto> {
  return http.post<LiveKitJoinDto>(
    `${base(sessionId)}/token`,
    { room, monitor },
    signal === undefined ? undefined : { signal },
  )
}
