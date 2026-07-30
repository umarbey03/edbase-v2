import { http } from '@/shared/api'
import type {
  ConversationDto,
  DirectMessageDto,
  MarkReadResultDto,
  MessagePageDto,
  SendDirectMessageRequest,
} from '@/shared/types'

const BASE = '/api/v1/messages'

/**
 * `GET /api/v1/messages/conversations` — suhbatlar ro'yxati.
 *
 * Kim kim bilan yozishishi SERVERDA hal qilinadi: o'quvchi faqat o'z
 * kuratorini ko'radi, kurator esa o'z guruhlaridagi o'quvchilarni.
 * Frontend ro'yxatni qo'shimcha filtrlamaydi.
 */
export function fetchConversations(options?: {
  signal?: AbortSignal
}): Promise<ConversationDto[]> {
  return http.get<ConversationDto[]>(`${BASE}/conversations`, { signal: options?.signal })
}

/**
 * `GET /api/v1/messages/conversations/{peerId}/messages` — KURSORLI sahifalash.
 *
 * `beforeId` — shu Id'dan ESKIROQ xabarlar. Ofset ishlatilmaydi: chat oqimi
 * o'sib turadi va yangi xabar kelganda ofsetli oyna siljib, ko'rilgan
 * xabarlar qayta chiqardi.
 */
export function fetchThread(
  peerId: number,
  params: { beforeId?: number; take?: number } = {},
  options?: { signal?: AbortSignal },
): Promise<MessagePageDto> {
  return http.get<MessagePageDto>(`${BASE}/conversations/${peerId}/messages`, {
    query: { beforeId: params.beforeId, take: params.take },
    signal: options?.signal,
  })
}

export function sendDirectMessage(
  peerId: number,
  body: SendDirectMessageRequest,
): Promise<DirectMessageDto> {
  return http.post<DirectMessageDto>(`${BASE}/conversations/${peerId}/messages`, body)
}

/** `O'qildi` belgilash — idempotent (takrorda `markedCount: 0`). */
export function markConversationRead(peerId: number): Promise<MarkReadResultDto> {
  return http.post<MarkReadResultDto>(`${BASE}/conversations/${peerId}/read`)
}
