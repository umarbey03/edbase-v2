import { lookup } from '@/shared/lib/lookup'
import type { ConversationDto } from '@/shared/types'

/**
 * Xabar uzunligi chegarasi — SERVER shartnomasi nusxasi (2000 belgi; server
 * uzunini kesib tashlaydi). Klientda ham cheklanadi, chunki jimgina kesilgan
 * xabar foydalanuvchi uchun ma'lumot yo'qolishi bo'lardi.
 */
export const DM_BODY_MAX = 2000

const ROLE_LABELS: Record<string, string> = {
  Assistant: 'Kurator',
  Teacher: 'Ustoz',
  Student: "O'quvchi",
  Academic: "O'quv bo'limi",
  Admin: 'Administrator',
}

export function peerRoleLabel(role: string): string {
  return lookup(ROLE_LABELS, role, role)
}

/**
 * Ro'yxatdagi ikkinchi qator: oxirgi xabar ko'rinishi.
 *
 * O'zim yozgan xabar oldiga "Siz:" qo'shiladi — Telegram'da ham shunday va
 * u holda o'quvchi javob kutayotganini bir qarashda tushunadi.
 */
export function conversationSubtitle(conversation: ConversationDto): string {
  const preview = conversation.lastMessagePreview ?? ''
  if (preview.length === 0) return 'Hali xabar yo‘q'
  return conversation.lastMessageMine === true ? `Siz: ${preview}` : preview
}

/* ==========================================================================
   "JAVOB KUTMOQDA" — eski ustoz panelidagi `waiting_hours`/`days_since`.

   ★ ESKI SERVER bu ikki maydonni TAYYOR berardi, v2 esa bermaydi
   (`ConversationDto` da faqat oxirgi xabar va uning egasi bor). Shuning
   uchun ular shu yerda, DTO ustidan hisoblanadi — ma'no o'zgarmagan:
   "oxirgi so'z o'quvchida bo'lsa, xodim javob qarzdor".

   NEGA ENTITY QATLAMIDA: bu bilimni IKKI joy o'qiydi — "Savollar" ekrani
   (`features/teacher-inbox`) va "Kuratorlik" ko'rsatkichlari
   (`features/teacher-curator`). FSD'da bir feature ikkinchisidan import
   qila olmaydi.
   ========================================================================== */

const HOUR_MS = 60 * 60 * 1000

/**
 * Xodim necha soatdan beri javob bermayapti.
 *
 * `null` — kutish YO'Q: yo hali xabar yozilmagan, yo oxirgi xabar xodimning
 * o'zidan (ya'ni to'p o'quvchida).
 */
export function waitingHours(conversation: ConversationDto, now: Date): number | null {
  if (conversation.lastMessageAt === null) return null
  if (conversation.lastMessageMine !== false) return null
  const sent = new Date(conversation.lastMessageAt).getTime()
  if (Number.isNaN(sent)) return null
  return Math.max(0, (now.getTime() - sent) / HOUR_MS)
}

/** Eski `waitLabel()`: `40 daq`, `6 soat`, `3 kun`. */
export function waitLabel(hours: number): string {
  if (hours < 1) return `${Math.max(1, Math.round(hours * 60))} daq`
  if (hours < 24) return `${Math.round(hours)} soat`
  return `${Math.round(hours / 24)} kun`
}

/**
 * Oxirgi xabardan beri necha kun o'tgan. Eski ilova buni "N kun aloqa yo'q"
 * ogohlantirishi uchun ishlatardi: kurator unutib qo'ygan o'quvchi ro'yxat
 * quyisida ko'rinmay qolardi.
 */
export function daysSinceLastMessage(
  conversation: ConversationDto,
  now: Date,
): number | null {
  if (conversation.lastMessageAt === null) return null
  const sent = new Date(conversation.lastMessageAt).getTime()
  if (Number.isNaN(sent)) return null
  return Math.floor((now.getTime() - sent) / (24 * HOUR_MS))
}

/**
 * Kutish qanchalik shoshilinch. Chegaralar eski ilovadan: 24 soatdan
 * oshgani QIZIL, 4 soatdan oshgani OLTIN, qolgani neytral.
 */
export function waitTone(hours: number): 'danger' | 'accent' | 'neutral' {
  if (hours >= 24) return 'danger'
  if (hours >= 4) return 'accent'
  return 'neutral'
}
