import type { EnrollmentApplicationStatusName } from '@/shared/types'

/**
 * Ariza izohining eng katta uzunligi — backenddagi
 * `EnrollmentApplication.MaxNoteLength` bilan AYNI.
 *
 * ★ NIMA UCHUN MIJOZDA HAM BOR: server chegaradan oshgan matnni RAD
 * ETADI, foydalanuvchi esa uzun izohni yozib bo'lgandan keyin xato
 * ko'rardi. Maydondagi `maxlength` uni yozayotgan paytda to'xtatadi.
 *
 * ⚠️ IKKI JOYDA TURGAN SON — MOS BO'LISHI SHART. Serverdagi qiymat
 * KATTALASHTIRILSA bu yerdagi ham o'zgartirilsin, aks holda foydalanuvchi
 * ruxsat etilganidan kamroq yoza olardi (xavfsiz tomon, lekin noto'g'ri).
 */
export const APPLICATION_NOTE_MAX = 500

/**
 * Holat nomlari.
 *
 * ★ TARTIB — ISH OQIMI BO'YICHA, alifbo bo'yicha emas: filtr ro'yxatida
 * operator ularni aynan shu ketma-ketlikda ko'radi.
 */
export const APPLICATION_STATUS_OPTIONS: readonly {
  value: EnrollmentApplicationStatusName
  label: string
}[] = [
  { value: 'New', label: 'Yangi' },
  { value: 'Contacted', label: 'Bog‘lanildi' },
  { value: 'Enrolled', label: 'Qabul qilindi' },
  { value: 'Rejected', label: 'Rad etildi' },
]

const LABELS: Record<EnrollmentApplicationStatusName, string> = {
  New: 'Yangi',
  Contacted: 'Bog‘lanildi',
  Enrolled: 'Qabul qilindi',
  Rejected: 'Rad etildi',
}

/**
 * Rang.
 *
 * ★ `New` — `accent` (brend rangi): u "hozir e'tibor kerak" degani.
 *   `Enrolled` — `success`, `Rejected` — `neutral` (qizil EMAS: rad etilgan
 *   ariza xato emas, oddiy natija; qizil ro'yxatni bekorga bezovta
 *   ko'rinishga keltirardi).
 */
const TONES: Record<EnrollmentApplicationStatusName, 'accent' | 'warning' | 'success' | 'neutral'> = {
  New: 'accent',
  Contacted: 'warning',
  Enrolled: 'success',
  Rejected: 'neutral',
}

export function applicationStatusLabel(status: EnrollmentApplicationStatusName): string {
  return LABELS[status] ?? status
}

export function applicationStatusTone(
  status: EnrollmentApplicationStatusName,
): 'accent' | 'warning' | 'success' | 'neutral' {
  return TONES[status] ?? 'neutral'
}
