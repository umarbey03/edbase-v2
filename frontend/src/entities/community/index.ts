import { http } from '@/shared/api'

/**
 * HAMJAMIYAT — Mustaqil o'quvchilarning umumiy chati (2026-11-21).
 * Server: `CommunityService.cs` (shakllar AYNI).
 */

export interface CommunityAccessDto {
  enabled: boolean
  canRead: boolean
  canWrite: boolean
  isModerator: boolean
  mutedUntil: string | null
  memberCount: number
  unread: number
}

export interface CommunityReplyDto {
  id: number
  authorName: string
  snippet: string
  deleted: boolean
}

export interface CommunityMessageDto {
  id: number
  authorId: number
  authorName: string
  authorKind: 'Student' | 'Staff'
  mine: boolean
  body: string
  deleted: boolean
  replyTo: CommunityReplyDto | null
  createdAt: string
}

export interface CommunityPageDto {
  items: CommunityMessageDto[]
  hasMore: boolean
}

export const COMMUNITY_BODY_MAX = 1000

export const COMMUNITY_KEYS = {
  access: ['community', 'access'] as const,
}

const BASE = '/api/v1/community'

type Opts = { signal?: AbortSignal }

export function fetchCommunityAccess(options?: Opts): Promise<CommunityAccessDto> {
  return http.get<CommunityAccessDto>(`${BASE}/access`, { signal: options?.signal })
}

export function fetchCommunityMessages(
  params: { beforeId?: number, afterId?: number, take?: number },
  options?: Opts,
): Promise<CommunityPageDto> {
  return http.get<CommunityPageDto>(`${BASE}/messages`, { query: params, signal: options?.signal })
}

export function sendCommunityMessage(body: string, replyToId: number | null): Promise<CommunityMessageDto> {
  return http.post<CommunityMessageDto>(`${BASE}/messages`, { body, replyToId })
}

export function deleteCommunityMessage(id: number): Promise<void> {
  return http.delete<void>(`${BASE}/messages/${id}`)
}

export function markCommunityRead(lastId: number): Promise<void> {
  return http.post<void>(`${BASE}/read`, undefined, { query: { lastId } })
}

export function muteCommunityMember(studentId: number, hours: number, reason: string | null): Promise<void> {
  return http.put<void>(`${BASE}/mutes/${studentId}`, { hours, reason })
}

export function unmuteCommunityMember(studentId: number): Promise<void> {
  return http.delete<void>(`${BASE}/mutes/${studentId}`)
}
