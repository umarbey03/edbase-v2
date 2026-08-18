import { lookup } from '@/shared/lib/lookup'
import type { LiveSessionDto, SessionStatusName, SessionTypeName } from '@/shared/types'

export type LiveSession = LiveSessionDto

const STATUS_LABELS: Record<SessionStatusName, string> = {
  Scheduled: 'Rejalashtirilgan',
  Live: 'Jonli efirda',
  Ended: 'Yakunlangan',
  Cancelled: 'Bekor qilingan',
}

export type StatusTone = 'neutral' | 'live' | 'warning' | 'danger'

const STATUS_TONES: Record<SessionStatusName, StatusTone> = {
  Scheduled: 'neutral',
  Live: 'live',
  Ended: 'neutral',
  Cancelled: 'danger',
}

const TYPE_LABELS: Record<SessionTypeName, string> = {
  Teacher: 'Ustoz darsi',
  Assistant: 'Kurator darsi',
}

export function sessionStatusLabel(status: string): string {
  return lookup(STATUS_LABELS, status, status)
}

export function sessionStatusTone(status: string): StatusTone {
  return lookup(STATUS_TONES, status, 'neutral')
}

export function sessionTypeLabel(type: string): string {
  return lookup(TYPE_LABELS, type, type)
}

/**
 * Jadval ustuni uchun QISQA nom — eski ustoz panelidagi "Tur" ustuni.
 *
 * ★ CHEKINISH: eski ilova bu yerda "Yordamchi" deb yozardi, lekin O'ZI ham
 * boshqa joyda ("Kurator chati", kuratorlik bo'limi) "Kurator" deb atardi.
 * v2 butun ilovada BITTA so'z ishlatadi — "Kurator" (menyu, rollar, DM) —
 * shuning uchun bu yerda ham shu tanlandi: bir rolni ikki nom bilan atash
 * eski ilovaning chalkashligi edi, uni ko'chirish foydalanuvchiga yordam
 * bermaydi.
 */
export function sessionTypeShortLabel(type: string): string {
  return type === 'Teacher' ? 'Ustoz' : 'Kurator'
}

export function sessionTitle(session: LiveSession): string {
  const title = session.title?.trim()
  return title !== undefined && title.length > 0 ? title : session.groupName
}

/**
 * DARS KECHIKIB BOSHLANGANMI va necha daqiqaga (2026-08-18).
 *
 * Loyiha egasi: *"jonli dars kech boshlangan bo'lsa minus nechadir minut
 * qilib ko'rsatsa ham zo'r bo'lardi (qizildami) — jarima hisoblashga va
 * ustozlarga isbotlab berishga ham oson bo'lardi"*.
 *
 * ★ SERVER O'ZGARTIRILMADI: `scheduledStart` ham, `actualStart` ham
 * javobda ALLAQACHON bor — kechikish ular ayirmasidan chiqadi. Buni
 * serverga qo'shish AYNI ma'lumotni ikkinchi marta uzatish bo'lardi.
 *
 * ★ FAQAT MUSBAT QIYMAT: dars vaqtidan OLDIN boshlangan bo'lsa (ustoz
 * 5 daqiqa oldin ochishi mumkin) `null` qaytadi — "erta boshlandi"
 * jarima emas va uni ko'rsatishning ma'nosi yo'q.
 *
 * ★ 1 DAQIQALIK CHIDAM: soatlar bir necha soniyaga farq qilishi odatiy.
 * Chegara bo'lmasa har dars "1 daqiqa kechikdi" bo'lib ko'rinardi va
 * ko'rsatkich ishonchini yo'qotardi.
 */
const LATE_TOLERANCE_MINUTES = 1

export function lateStartMinutes(session: {
  scheduledStart: string
  actualStart: string | null
}): number | null {
  if (session.actualStart === null) return null

  const scheduled = new Date(session.scheduledStart).getTime()
  const actual = new Date(session.actualStart).getTime()

  if (Number.isNaN(scheduled) || Number.isNaN(actual)) return null

  const minutes = Math.floor((actual - scheduled) / 60_000)

  return minutes > LATE_TOLERANCE_MINUTES ? minutes : null
}

/** `-7 daq` ko'rinishi (soatdan oshsa `-1 soat 5 daq`). */
export function lateStartLabel(minutes: number): string {
  if (minutes < 60) return `−${minutes} daq`

  const hours = Math.floor(minutes / 60)
  const rest = minutes % 60

  return rest === 0 ? `−${hours} soat` : `−${hours} soat ${rest} daq`
}

/** Darsga kirish mumkinmi: jonli, yoki boshlanishiga 15 daqiqadan kam qolgan. */
const EARLY_JOIN_WINDOW_MS = 15 * 60 * 1000

export function isJoinable(session: LiveSession, now: Date = new Date()): boolean {
  if (session.status === 'Live') return true
  if (session.status !== 'Scheduled') return false
  const start = new Date(session.scheduledStart).getTime()
  if (Number.isNaN(start)) return false
  return start - now.getTime() <= EARLY_JOIN_WINDOW_MS
}

/* ==========================================================================
   ESKI USTOZ PANELIDAGI "DARSNI BOSHLASH" HOLATI (`startState`/`startBtn`).

   NEGA `entities/session` DA: bir xil qoidani UCH ekran o'qiydi — ustoz bosh
   sahifasi, guruh ichidagi "keyingi dars" banneri va guruh kalendari. Uchtasi
   uch xil `features/` bo'lagida yashaydi va FSD'da bir-biridan import qila
   olmaydi, shuning uchun umumiy bilim eng pastki mos qatlamda turadi —
   `isJoinable` va 15 daqiqalik oyna ham shu yerda.
   ========================================================================== */

/**
 * Holatni hisoblash uchun YETARLI minimum. `LiveSessionDto` ham,
 * `ScheduledSessionDto` ham shu shaklga mos — shuning uchun ikkala ro'yxat
 * bitta funksiyadan foydalanadi.
 */
export interface SessionTiming {
  status: SessionStatusName
  scheduledStart: string
  scheduledEnd: string
}

/** Eski `LEAD_MIN` — dars boshlanishiga necha daqiqa qolganda tugma ochiladi. */
export const START_LEAD_MINUTES = 15

export type SessionStartState =
  | { kind: 'live' }
  | { kind: 'ready' }
  /** Hali erta. `text` — eski ilovadagi "2 soat 15 daq" ko'rinishi. */
  | { kind: 'wait'; text: string }
  | { kind: 'ended' }
  | { kind: 'cancelled' }

function remainingLabel(minutes: number): string {
  return minutes >= 60 ? `${Math.floor(minutes / 60)} soat ${minutes % 60} daq` : `${minutes} daq`
}

/** Eski `startState()` ning aynan nusxasi. */
export function sessionStartState(
  session: SessionTiming,
  now: Date = new Date(),
): SessionStartState {
  if (session.status === 'Live') return { kind: 'live' }
  if (session.status === 'Ended') return { kind: 'ended' }
  if (session.status === 'Cancelled') return { kind: 'cancelled' }

  const start = new Date(session.scheduledStart).getTime()
  if (Number.isNaN(start)) return { kind: 'ready' }

  if (now.getTime() >= start - EARLY_JOIN_WINDOW_MS) return { kind: 'ready' }

  const minutes = Math.ceil((start - EARLY_JOIN_WINDOW_MS - now.getTime()) / 60_000)
  return { kind: 'wait', text: remainingLabel(minutes) }
}

/**
 * Eski jadvaldagi QISQA holat nishoni ("jonli", "o'tilgan", "bekor",
 * "o'tilmagan", "rejada").
 *
 * `sessionStatusLabel` dan FARQ QILADI: u serverdagi holatni to'liq nom bilan
 * ataydi, bu esa eski `renderDashboard()` yozuvlarini takrorlaydi va
 * QO'SHIMCHA holat biladi — vaqti o'tib ketgan, lekin boshlanmagan dars
 * ("o'tilmagan"). Server bunday holatni saqlamaydi, u faqat vaqtdan
 * hisoblanadi.
 */
export function sessionStateBadge(
  session: SessionTiming,
  now: Date = new Date(),
): { label: string; tone: StatusTone | 'success' | 'accent' } {
  if (session.status === 'Live') return { label: 'jonli', tone: 'live' }
  if (session.status === 'Ended') return { label: 'o‘tilgan', tone: 'success' }
  if (session.status === 'Cancelled') return { label: 'bekor', tone: 'danger' }

  const end = new Date(session.scheduledEnd).getTime()
  if (!Number.isNaN(end) && end < now.getTime()) return { label: 'o‘tilmagan', tone: 'danger' }
  return { label: 'rejada', tone: 'accent' }
}
