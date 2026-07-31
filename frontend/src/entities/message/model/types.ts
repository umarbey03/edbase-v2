import { isSameDay, toDate } from '@/shared/lib/datetime'
import type { ChatMessageDto } from '@/shared/types'

export type ChatMessage = ChatMessageDto

/** SPEC 6.3 — server 500 belgida kesadi, klient ham shu chegarani qo'llaydi. */
export const MAX_MESSAGE_LENGTH = 500

/**
 * SPEC 6.2 — server tezlik chegarasi. Klient AYNAN shu qoidani takrorlaydi:
 * `LiveClassHub.ChatRateWindow` / `ChatRateMaxMessages`.
 *
 * ★ NIMA UCHUN "2 soniyada 1 ta" DAN VOZ KECHILDI: eski qoida odam tabiiy
 * yozadigan ketma-ket ikki qatorning ikkinchisini bloklardi — kompozitor
 * 2 soniya "o'lik" bo'lib turardi va Enter hech narsa qilmasdi. Aynan shu
 * foydalanuvchi "kechikish" deb shikoyat qilgan holatlardan biri edi.
 * O'rtacha tezlik o'zgarmadi (10 soniyada 5 ta = 2 soniyada 1 ta), lekin
 * qisqa "portlash" endi mumkin.
 */
export const CHAT_RATE_WINDOW_MS = 10_000

/** Bitta oynada yuborish mumkin bo'lgan xabarlar soni. */
export const CHAT_RATE_MAX_MESSAGES = 5

/**
 * Xabarning BARQAROR kaliti — takrorlarni filtrlash va ro'yxat `key` i uchun.
 *
 * ★ NIMA UCHUN `id` YETMAYDI (topilgan ILDIZ NOSOZLIK): hub xabarni avval
 * tarqatadi, bazaga esa fon navbati yozadi. Ya'ni tarqatilayotgan payt baza
 * raqami hali yo'q va broadcast'da `id = 0` keladi — HAR xabarda bir xil.
 * `id` bo'yicha dedupe qilinganda birinchi xabardan keyingi hammasi
 * "allaqachon ko'rilgan" deb jimgina tashlanardi va chat ekranda qotib
 * qolardi. Sim bo'ylab esa hamma xabar kelib turardi — shuning uchun
 * yuklama testi ham, backend testlari ham buni ko'rmagan.
 *
 * ★ NIMA UCHUN KALITGA `senderId` QO'SHILADI: `clientId` ni KLIENT yasaydi.
 * Prefikssiz, boshqa foydalanuvchining kalitini ataylab takrorlab, uning
 * xabarini HAMMANING ekranidan yashirish mumkin bo'lardi. Yuboruvchi bilan
 * birga kalitlanganda bu yo'l yopiladi.
 */
export function messageKey(message: ChatMessage): string {
  const clientId = message.clientId
  return typeof clientId === 'string' && clientId.length > 0
    ? `c:${message.senderId}:${clientId}`
    : `s:${message.id}`
}

/**
 * Yangi xabar uchun kalit yasaydi.
 *
 * `crypto.randomUUID` HTTPS (yoki `localhost`) da mavjud; boshqa holatda
 * zaxira yo'l ishlaydi — kalit XAVFSIZLIK vositasi emas, u faqat noyob
 * bo'lishi kifoya. Server ham uni tekshiradi va shubhali bo'lsa o'zinikiga
 * almashtiradi (`LiveClassHub.NormalizeClientId`).
 */
export function newClientId(): string {
  const api = globalThis.crypto
  if (typeof api?.randomUUID === 'function') return api.randomUUID()
  return `k-${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 10)}`
}

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
