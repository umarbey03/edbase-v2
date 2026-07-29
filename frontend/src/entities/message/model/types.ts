import { isSameDay, toDate } from '@/shared/lib/datetime'
import type { ChatMessageDto } from '@/shared/types'

export type ChatMessage = ChatMessageDto

/** SPEC 6.3 — server 500 belgida kesadi, klient ham shu chegarani qo'llaydi. */
export const MAX_MESSAGE_LENGTH = 500

/** SPEC 6.2 — server rate-limit: 1 xabar / 2 sekund. Klient ham shuni takrorlaydi. */
export const SEND_COOLDOWN_MS = 2000

/**
 * DOM'da bir vaqtda saqlanadigan xabarlarning MAKSIMUM soni.
 *
 * Nega kerak: 200 ta o'quvchi soatiga minglab xabar yozadi. Har bir xabar ~5 ta DOM
 * tuguni. 3000 xabar = 15 000 tugun -> scroll ham, layout ham qotib qoladi.
 * Eng eskilarini tashlab yuborish (tarix serverdan qayta olinadi) — eng arzon yechim.
 */
export const MAX_RENDERED_MESSAGES = 200

/** Ketma-ket xabarlarni bitta guruhga birlashtirish oynasi. */
const GROUP_WINDOW_MS = 5 * 60 * 1000

/** Oldingi xabar bilan bitta "guruh"ga tushadimi (avatar/ism takrorlanmaydi). */
export function isGroupedWith(previous: ChatMessage | undefined, current: ChatMessage): boolean {
  if (previous === undefined) return false
  if (previous.senderId !== current.senderId) return false

  const previousAt = toDate(previous.sentAt)
  const currentAt = toDate(current.sentAt)
  if (Number.isNaN(previousAt.getTime()) || Number.isNaN(currentAt.getTime())) return false
  if (!isSameDay(previousAt, currentAt)) return false

  return currentAt.getTime() - previousAt.getTime() <= GROUP_WINDOW_MS
}

/** Yangi kun boshlanganini bildiradi (kun ajratgichi chizish uchun). */
export function startsNewDay(previous: ChatMessage | undefined, current: ChatMessage): boolean {
  if (previous === undefined) return true
  const previousAt = toDate(previous.sentAt)
  const currentAt = toDate(current.sentAt)
  if (Number.isNaN(currentAt.getTime())) return false
  if (Number.isNaN(previousAt.getTime())) return true
  return !isSameDay(previousAt, currentAt)
}

/** Xabarni yuborishdan oldin normallashtirish. */
export function normalizeBody(raw: string): string {
  return raw.replace(/\s+$/u, '').trimStart().slice(0, MAX_MESSAGE_LENGTH)
}
