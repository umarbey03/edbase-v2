/**
 * O'quvchi profili (BLOK E) va foydalanuvchi filtrlari (BLOK F) uchun
 * KO'RINISH yordamchilari. Shakl `shared/types/api.ts` da, ruxsat serverda —
 * bu yerda faqat matn va formatlash.
 */

/** Telegram uzish sababining MAKSIMAL uzunligi (`TelegramUnlinkAudit.MaxReasonLength`). */
export const UNLINK_REASON_MAX = 500

/**
 * `t.me` havolasi.
 *
 * 🔴 USERNAME — IDENTIFIKATOR EMAS (13-bo'lim, 35-tuzoq): Telegram'da
 * bo'shatilgan nom boshqa odamga o'tadi, shuning uchun backendda unikal
 * indeks ATAYLAB yo'q va nom har muloqotda qayta yoziladi. UI unga FAQAT
 * havola sifatida murojaat qiladi; "shu odam" degan xulosa `telegramId`
 * bo'yicha chiqariladi.
 */
export function telegramLink(username: string): string {
  return `https://t.me/${username}`
}

/** `@nomi` — ko'rsatish shakli (server `@` belgisini YUBORMAYDI). */
export function telegramHandle(username: string): string {
  return `@${username}`
}

/**
 * Telegram bo'yicha filtrning UCH holati (BLOK F).
 *
 * Qiymatlar SATR: `<select v-model>` bo'sh satrni "filtr yo'q" deb bera
 * oladi, `boolean | null` esa `<option :value>` da `null` bilan chalkashardi
 * (mavjud "Barcha holatlar" filtri ham aynan shu shaklda).
 */
export type TelegramFilterValue = '' | 'true' | 'false'

export const TELEGRAM_FILTER_OPTIONS: ReadonlyArray<{
  value: TelegramFilterValue
  label: string
}> = [
  { value: '', label: 'Telegram: barchasi' },
  { value: 'true', label: 'Telegram ulangan' },
  { value: 'false', label: 'Telegram ulanmagan' },
]

/** `'true' | 'false' | ''` -> so'rov parametri (`undefined` — filtr qo'llanmaydi). */
export function telegramFilterToParam(value: TelegramFilterValue): boolean | undefined {
  if (value === '') return undefined
  return value === 'true'
}

/**
 * `93` -> `93%`, `93.42` -> `93,4%`.
 *
 * Foizni SERVER hisoblaydi (`ProfileAttendanceDto.percent`) — bu yerda faqat
 * yaxlitlanadi: `93.4200000001` kartochkani cho'zib yuborardi. Kasr ajratgichi
 * vergul — `shared/lib/money.ts` dagi summalar bilan bir xil.
 */
export function percentLabel(value: number): string {
  if (!Number.isFinite(value)) return '—'
  const rounded = Math.round(value * 10) / 10
  return `${String(rounded).replace('.', ',')}%`
}

/** `BaseBadge` ohangi: davomat foizi VIZUAL ishora (o'tish bali qoidasi YO'Q). */
export function attendanceTone(percent: number): 'success' | 'warning' | 'danger' | 'neutral' {
  if (!Number.isFinite(percent)) return 'neutral'
  if (percent >= 85) return 'success'
  if (percent >= 60) return 'warning'
  return 'danger'
}
