import { http } from '@/shared/api'
import type {
  ConversationDto,
  DirectMessageDto,
  LessonQuestionDto,
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
 *
 * `moduleLessonId` — berilsa, FAQAT shu kurs darsidan yozilgan xabarlar
 * (Dars Dashboard mini-chat'i, `LessonChatPanel`). Berilmasa — butun
 * yozishma (mavjud xatti-harakat).
 */
export function fetchThread(
  peerId: number,
  params: { beforeId?: number; take?: number; moduleLessonId?: number } = {},
  options?: { signal?: AbortSignal },
): Promise<MessagePageDto> {
  return http.get<MessagePageDto>(`${BASE}/conversations/${peerId}/messages`, {
    query: { beforeId: params.beforeId, take: params.take, moduleLessonId: params.moduleLessonId },
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

/**
 * R40 · `GET /api/v1/messages/lesson-questions` — DARS savollari navbati.
 *
 * Xodim uchun: o'quvchilar aynan kurs darsi sahifasidan yozgan savollar.
 * Tartibni SERVER belgilaydi (javobsizlar tepada, ular ichida eng uzoq
 * kutgani birinchi) — klient uni qayta saralamaydi, aks holda "navbatda
 * kim birinchi" degan qaror ikki joyda bo'lardi.
 *
 * ★ Bu ALOHIDA yozishma emas: har qator `peerId` beradi va u AYNI
 * `fetchThread` / `sendDirectMessage` ga olib boradi.
 */
export function fetchLessonQuestions(
  params: { take?: number } = {},
  options?: { signal?: AbortSignal },
): Promise<LessonQuestionDto[]> {
  return http.get<LessonQuestionDto[]>(`${BASE}/lesson-questions`, {
    query: { take: params.take },
    signal: options?.signal,
  })
}
