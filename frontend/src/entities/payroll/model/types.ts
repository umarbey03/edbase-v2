import { monthNameCapitalized } from '@/shared/lib/datetime'
import type { TeacherRateDto, UserRoleName } from '@/shared/types'

/* ==================================================================== rol === */

const ROLE_LABELS: Partial<Record<UserRoleName, string>> = {
  Teacher: 'Ustoz',
  Assistant: 'Kurator',
}

/** Stavka faqat Ustoz/Kurator uchun — boshqa rol shu yerga umuman kelmaydi. */
export function payrollRoleLabel(role: UserRoleName): string {
  return ROLE_LABELS[role] ?? role
}

export const PAYROLL_ROLE_OPTIONS: ReadonlyArray<{ value: UserRoleName; label: string }> = [
  { value: 'Teacher', label: ROLE_LABELS.Teacher! },
  { value: 'Assistant', label: ROLE_LABELS.Assistant! },
]

/** Stavkaning qamrovi — `entities/payment` dagi `tariffScopeLabel` bilan AYNI naqsh. */
export function rateScopeLabel(rate: TeacherRateDto): string {
  return rate.userId !== null ? (rate.userName ?? '—') : `${payrollRoleLabel(rate.role)} — standart`
}

/* ==================================================================== oy === */

/*
  ★ `entities/payment` dagi `currentPeriod`/`periodLabel`/`isValidPeriod` DAN
  ATAYLAB QAYTA YOZILGAN, ko'chirilmagan: FSD qoidasi — entity entity'dan
  import qilmaydi (izoh: `entities/payment/model/types.ts` dagi
  `collectionRateLabel` bilan AYNI sabab).
*/
const PERIOD_PATTERN = /^(\d{4})-(\d{2})$/

/** Markaz vaqtidagi joriy oy, `YYYY-MM`. */
export function currentPayrollPeriod(): string {
  const now = new Date()
  const month = now.getMonth() + 1
  return `${now.getFullYear()}-${month < 10 ? '0' : ''}${month}`
}

export function payrollPeriodLabel(period: string): string {
  const match = PERIOD_PATTERN.exec(period)
  if (match === null) return period
  const year = match[1] ?? ''
  const monthIndex = Number(match[2]) - 1
  const name = monthNameCapitalized(monthIndex)
  return name.length > 0 ? `${name.toLowerCase()} ${year}` : period
}

export function isValidPayrollPeriod(period: string): boolean {
  const match = PERIOD_PATTERN.exec(period)
  if (match === null) return false
  const month = Number(match[2])
  return month >= 1 && month <= 12
}

/** `<input type="date">` uchun bugungi sana, `YYYY-MM-DD` (MAHALLIY vaqtda). */
export function todayIsoDate(): string {
  const now = new Date()
  const month = now.getMonth() + 1
  const day = now.getDate()
  return `${now.getFullYear()}-${month < 10 ? '0' : ''}${month}-${day < 10 ? '0' : ''}${day}`
}
