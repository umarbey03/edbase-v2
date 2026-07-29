/**
 * Sana/vaqt yordamchilari.
 *
 * `Intl` ning `uz-Latn` lokali barcha brauzerlarda to'liq emas, shuning uchun
 * oy nomlari qo'lda beriladi — natija hamma joyda bir xil bo'ladi.
 */

const MONTHS_UZ = [
  'yanvar',
  'fevral',
  'mart',
  'aprel',
  'may',
  'iyun',
  'iyul',
  'avgust',
  'sentabr',
  'oktabr',
  'noyabr',
  'dekabr',
] as const

const WEEKDAYS_UZ = [
  'Yakshanba',
  'Dushanba',
  'Seshanba',
  'Chorshanba',
  'Payshanba',
  'Juma',
  'Shanba',
] as const

function pad2(value: number): string {
  return value < 10 ? `0${value}` : String(value)
}

export function toDate(value: string | Date): Date {
  return value instanceof Date ? value : new Date(value)
}

/** `14:05` */
export function formatTime(value: string | Date): string {
  const date = toDate(value)
  if (Number.isNaN(date.getTime())) return ''
  return `${pad2(date.getHours())}:${pad2(date.getMinutes())}`
}

/** `12-mart 14:05` */
export function formatDateTime(value: string | Date): string {
  const date = toDate(value)
  if (Number.isNaN(date.getTime())) return ''
  return `${date.getDate()}-${MONTHS_UZ[date.getMonth()] ?? ''} ${formatTime(date)}`
}

/** Kunlar bir xilmi (mahalliy vaqt bo'yicha). */
export function isSameDay(a: Date, b: Date): boolean {
  return (
    a.getFullYear() === b.getFullYear() &&
    a.getMonth() === b.getMonth() &&
    a.getDate() === b.getDate()
  )
}

/** Chatdagi kun ajratgichi uchun: `Bugun`, `Kecha`, `12-mart, Seshanba`. */
export function formatDayLabel(value: string | Date): string {
  const date = toDate(value)
  if (Number.isNaN(date.getTime())) return ''

  const now = new Date()
  if (isSameDay(date, now)) return 'Bugun'

  const yesterday = new Date(now)
  yesterday.setDate(now.getDate() - 1)
  if (isSameDay(date, yesterday)) return 'Kecha'

  const day = `${date.getDate()}-${MONTHS_UZ[date.getMonth()] ?? ''}`
  const weekday = WEEKDAYS_UZ[date.getDay()] ?? ''
  const yearSuffix = date.getFullYear() === now.getFullYear() ? '' : `, ${date.getFullYear()}`
  return `${day}, ${weekday}${yearSuffix}`
}

/** `01:23:45` yoki `05:12` ko'rinishidagi orqaga sanoq. */
export function formatCountdown(msRemaining: number): string {
  const total = Math.max(0, Math.floor(msRemaining / 1000))
  const hours = Math.floor(total / 3600)
  const minutes = Math.floor((total % 3600) / 60)
  const seconds = total % 60
  return hours > 0
    ? `${pad2(hours)}:${pad2(minutes)}:${pad2(seconds)}`
    : `${pad2(minutes)}:${pad2(seconds)}`
}

/** Ikki vaqt orasidagi farq millisekundda (`null` bo'lsa 0). */
export function msBetween(from: string | Date | null, to: Date = new Date()): number {
  if (from === null) return 0
  const date = toDate(from)
  if (Number.isNaN(date.getTime())) return 0
  return date.getTime() - to.getTime()
}
