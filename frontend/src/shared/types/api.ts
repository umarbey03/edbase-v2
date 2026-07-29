/**
 * SPEC 5-bo'limidagi DTO'larning AYNAN nusxasi.
 * Maydon nomlari o'zgartirilmaydi (backend camelCase JSON qaytaradi).
 * C# `long` -> TS `number` (2^53 gacha xavfsiz).
 */

/** SPEC 2: `UserRole` enum nomlari (JSON'da satr sifatida keladi). */
export type UserRoleName = 'Student' | 'Teacher' | 'Assistant' | 'Academic' | 'Admin'

/** SPEC 2: `SessionType` */
export type SessionTypeName = 'Teacher' | 'Assistant'

/** SPEC 2: `SessionStatus` */
export type SessionStatusName = 'Scheduled' | 'Live' | 'Ended' | 'Cancelled'

/** RFC 7807 — global middleware qaytaradigan xato formati (SPEC 5). */
export interface ProblemDetails {
  type?: string
  title?: string
  status?: number
  detail?: string
  traceId?: string
  /** ASP.NET validatsiya xatolari: { "Email": ["..."] } */
  errors?: Record<string, string[]>
}

/** `POST /api/v1/auth/login` tanasi */
export interface LoginRequest {
  email: string
  password: string
}

/**
 * `POST /api/v1/auth/refresh` tanasi.
 * SPEC'da alohida DTO ko'rsatilmagan (5-bo'limda faqat javob turi bor) —
 * amalda yagona mantiqiy shakl shu.
 */
export interface RefreshRequest {
  refreshToken: string
}

export interface UserDto {
  id: number
  fullName: string
  email: string
  role: UserRoleName
}

export interface AuthResponse {
  accessToken: string
  refreshToken: string
  user: UserDto
}

export interface LiveSessionDto {
  id: number
  groupId: number
  groupName: string
  title: string | null
  type: SessionTypeName
  status: SessionStatusName
  /** ISO-8601 (DateTimeOffset) */
  scheduledStart: string
  scheduledEnd: string
  actualStart: string | null
  endsAt: string | null
  isHost: boolean
}

/** Frontend LiveKit'ga AYNAN shu bilan ulanadi (SPEC 5). */
export interface LiveKitJoinDto {
  serverUrl: string
  token: string
  roomName: string
  isHost: boolean
  endsAt: string | null
}

export interface ChatMessageDto {
  id: number
  senderId: number
  senderName: string
  body: string
  sentAt: string
}
