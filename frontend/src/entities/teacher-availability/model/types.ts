import { lookup } from '@/shared/lib/lookup'

/**
 * USTOZ KUNLIK TASDIQLASH — yorliq va rang xaritalari.
 *
 * ★ NAQSH `entities/group/model/types.ts` DAN: backend enum NOMINI satr
 * sifatida yuboradi, UI esa uni O'ZBEKCHA yorliqqa aylantiradi. `lookup`
 * ishlatiladi — backend yangi qiymat qo'shsa UI qulamaydi, xom nomni
 * ko'rsatadi.
 */
export type AvailabilityTone = 'neutral' | 'success' | 'warning' | 'danger' | 'accent'

const STATUS_LABELS: Record<string, string> = {
  Pending: 'Javob kutilmoqda',
  Confirmed: 'Tasdiqladi',
  SelectingSessions: 'Dars tanlamoqda',
  AwaitingReason: 'Sabab yozmoqda',
  AwaitingDays: 'Kun sonini kiritmoqda',
  Declined: 'O‘ta olmaydi',
}

const STATUS_TONES: Record<string, AvailabilityTone> = {
  Pending: 'neutral',
  Confirmed: 'success',
  SelectingSessions: 'warning',
  AwaitingReason: 'warning',
  AwaitingDays: 'warning',
  Declined: 'danger',
}

const COVERAGE_LABELS: Record<string, string> = {
  Open: 'O‘rinbosar qidirilmoqda',
  Resolved: 'O‘rinbosar topildi',
  Cancelled: 'Bekor qilindi',
}

const COVERAGE_TONES: Record<string, AvailabilityTone> = {
  Open: 'warning',
  Resolved: 'success',
  Cancelled: 'neutral',
}

const OFFER_LABELS: Record<string, string> = {
  Sent: 'Javob kutilmoqda',
  Accepted: 'Rozi bo‘ldi',
  Declined: 'Rad etdi',
  Withdrawn: 'Bekor qilindi',
}

const OFFER_TONES: Record<string, AvailabilityTone> = {
  Sent: 'neutral',
  Accepted: 'success',
  Declined: 'danger',
  Withdrawn: 'neutral',
}

export function checkinStatusLabel(value: string): string {
  return lookup(STATUS_LABELS, value, value)
}

export function checkinStatusTone(value: string): AvailabilityTone {
  return lookup(STATUS_TONES, value, 'neutral')
}

/** `null` — qamrov so'rovi umuman ochilmagan. */
export function coverageLabel(value: string | null): string {
  if (value === null) return '—'
  return lookup(COVERAGE_LABELS, value, value)
}

export function coverageTone(value: string | null): AvailabilityTone {
  if (value === null) return 'neutral'
  return lookup(COVERAGE_TONES, value, 'neutral')
}

export function offerStatusLabel(value: string): string {
  return lookup(OFFER_LABELS, value, value)
}

export function offerStatusTone(value: string): AvailabilityTone {
  return lookup(OFFER_TONES, value, 'neutral')
}

/** Filtr uchun holat variantlari ("Barchasi" chaqiruvchida qo'shiladi). */
export const CHECKIN_STATUS_OPTIONS = [
  { value: 'Pending', label: STATUS_LABELS.Pending! },
  { value: 'Confirmed', label: STATUS_LABELS.Confirmed! },
  { value: 'Declined', label: STATUS_LABELS.Declined! },
  { value: 'SelectingSessions', label: STATUS_LABELS.SelectingSessions! },
  { value: 'AwaitingReason', label: STATUS_LABELS.AwaitingReason! },
  { value: 'AwaitingDays', label: STATUS_LABELS.AwaitingDays! },
] as const

// ---------------------------------------------------------------- sana oralig'i

/**
 * `YYYY-MM-DD` — MAHALLIY vaqt bo'yicha.
 *
 * 🔴 `toISOString()` ISHLATILMAYDI: u UTC ga o'giradi va UTC+5 da kechqurun
 * 05:00 dan keyin sana BIR KUN orqaga ketardi (`entities/payment` dagi
 * `todayIsoDate` bilan AYNI sabab va AYNI yechim).
 */
export function isoDate(date: Date): string {
  const year = date.getFullYear()
  const month = String(date.getMonth() + 1).padStart(2, '0')
  const day = String(date.getDate()).padStart(2, '0')
  return `${year}-${month}-${day}`
}

export function todayIso(): string {
  return isoDate(new Date())
}

/** `days` kun oldingi sana (0 — bugun). */
export function daysAgoIso(days: number): string {
  const date = new Date()
  date.setDate(date.getDate() - days)
  return isoDate(date)
}

/** Joriy oyning birinchi kuni. */
export function monthStartIso(): string {
  const now = new Date()
  return isoDate(new Date(now.getFullYear(), now.getMonth(), 1))
}

/**
 * Tez tanlov tugmalari.
 *
 * ★ KODBAZADA BIRINCHI MARTA: mavjud sana-oraliq ekranlari (moliya, dars
 * yozuvlari) faqat ikkita `<input type="date">` beradi. Bu panelda esa
 * "bugun" eng ko'p kerak bo'ladigan ko'rinish va uni har kuni qo'lda
 * kiritish ma'nosiz ish edi.
 */
export interface RangePreset {
  key: string
  label: string
  from: () => string
  to: () => string
}

export const RANGE_PRESETS: readonly RangePreset[] = [
  { key: 'today', label: 'Bugun', from: todayIso, to: todayIso },
  { key: 'week', label: 'Oxirgi 7 kun', from: () => daysAgoIso(6), to: todayIso },
  { key: 'month', label: 'Oxirgi 30 kun', from: () => daysAgoIso(29), to: todayIso },
  { key: 'thisMonth', label: 'Bu oy', from: monthStartIso, to: todayIso },
]

/** Sana to'liq va to'g'ri kiritilganmi (`YYYY-MM-DD`). */
export function isValidIsoDate(value: string): boolean {
  if (!/^\d{4}-\d{2}-\d{2}$/.test(value)) return false
  const date = new Date(`${value}T00:00:00`)
  return !Number.isNaN(date.getTime()) && isoDate(date) === value
}

/** Oraliq xatosi bo'lsa foydalanuvchiga ko'rsatiladigan matn, aks holda `null`. */
export function rangeError(from: string, to: string): string | null {
  if (from.length === 0 && to.length === 0) return null

  if (from.length > 0 && !isValidIsoDate(from)) return 'Boshlanish sanasi to‘liq kiritilmagan.'
  if (to.length > 0 && !isValidIsoDate(to)) return 'Tugash sanasi to‘liq kiritilmagan.'

  if (from.length > 0 && to.length > 0 && from > to)
    return 'Boshlanish sanasi tugash sanasidan keyin bo‘lishi mumkin emas.'

  return null
}
