import { monthNameCapitalized } from '@/shared/lib/datetime'
import type {
  GroupPayrollModeName,
  PayrollApprovalStatusName,
  PayrollBasisName,
  PayrollRuleDto,
  PayrollRuleKindName,
  UserRoleName,
} from '@/shared/types'

/* ==================================================================== rol === */

const ROLE_LABELS: Partial<Record<UserRoleName, string>> = {
  Teacher: 'Ustoz',
  Assistant: 'Kurator',
}

/** Qoida faqat Ustoz/Kurator uchun — boshqa rol shu yerga umuman kelmaydi. */
export function payrollRoleLabel(role: UserRoleName): string {
  return ROLE_LABELS[role] ?? role
}

export const PAYROLL_ROLE_OPTIONS: ReadonlyArray<{ value: UserRoleName; label: string }> = [
  { value: 'Teacher', label: ROLE_LABELS.Teacher! },
  { value: 'Assistant', label: ROLE_LABELS.Assistant! },
]

/* =============================================================== qoida turi === */

/**
 * ★ TARTIB MUHIM: ro'yxat DARS qamrovidan DAVR qamroviga qarab ketadi —
 * admin formani ochganda eng ko'p ishlatiladigan "har dars uchun" turlari
 * yuqorida bo'ladi.
 */
const RULE_KIND_LABELS: Record<PayrollRuleKindName, string> = {
  PerStudentAcademicHour: 'O‘quvchi × akademik soat (HolliHop)',
  PerSession: 'Har dars uchun',
  PerAcademicHour: 'Akademik soat uchun',
  PerAttendedStudent: 'Darsdagi har o‘quvchi uchun',
  TieredByAttendance: 'O‘quvchi soniga bosqichli',
  FixedMonthly: 'Fiksirlangan oylik (oklad)',
  PercentOfRevenue: 'Tushumdan foiz',
  MonthlyPerActiveStudent: 'Oylik: har faol o‘quvchi uchun',
}

/** Har turning bir gapli izohi — formada tanlov ostida ko'rsatiladi. */
const RULE_KIND_HINTS: Record<PayrollRuleKindName, string> = {
  PerStudentAcademicHour:
    'Stavka × darsdagi o‘quvchi soni × akademik soat. HolliHop’dagi «ставка за ученика за ак. час» bilan bir xil.',
  PerSession: 'Yakunlangan har bir dars uchun belgilangan summa.',
  PerAcademicHour: 'Dars davomiyligi akademik soatga bo‘linadi va stavkaga ko‘paytiriladi.',
  PerAttendedStudent: 'Darsdagi har bir o‘quvchi uchun qo‘shimcha summa.',
  TieredByAttendance: 'O‘quvchi soniga qarab suzuvchi summa: 0 ta uchun X, 1 ta uchun Y…',
  FixedMonthly: 'Dars soniga bog‘liq emas — oyga bir marta qo‘shiladi.',
  PercentOfRevenue: 'Hisoblangan o‘quv haqidan foiz. Reja qo‘yilsa, reja bajarilgach foiz oshadi.',
  MonthlyPerActiveStudent: 'Har faol o‘quvchi uchun oylik summa, koeffitsient bilan.',
}

export function payrollRuleKindLabel(kind: PayrollRuleKindName): string {
  return RULE_KIND_LABELS[kind]
}

export function payrollRuleKindHint(kind: PayrollRuleKindName): string {
  return RULE_KIND_HINTS[kind]
}

export const PAYROLL_RULE_KIND_OPTIONS: ReadonlyArray<{
  value: PayrollRuleKindName
  label: string
}> = (
  [
    // ★ BIRINCHI: markazning HolliHop'dagi asosiy formulasi — admin
    //   ko'chirishda aynan shuni qidiradi.
    'PerStudentAcademicHour',
    'PerSession',
    'PerAcademicHour',
    'PerAttendedStudent',
    'TieredByAttendance',
    'FixedMonthly',
    'PercentOfRevenue',
    'MonthlyPerActiveStudent',
  ] as const
).map((value) => ({ value, label: RULE_KIND_LABELS[value] }))

/** Har dars uchunmi (aks holda oyga bir marta). Backenddagi `IsSessionScoped` bilan AYNI. */
export function isSessionScopedKind(kind: PayrollRuleKindName): boolean {
  return (
    kind === 'PerSession' ||
    kind === 'PerAcademicHour' ||
    kind === 'PerAttendedStudent' ||
    kind === 'TieredByAttendance' ||
    kind === 'PerStudentAcademicHour'
  )
}

/** Dars davomiyligi akademik soatga bo'linadimi — `academicHourMinutes` shu turlarda ma'noli. Backenddagi `UsesAcademicHour` bilan AYNI. */
export function usesAcademicHour(kind: PayrollRuleKindName): boolean {
  return kind === 'PerAcademicHour' || kind === 'PerStudentAcademicHour'
}

/** `amount` pul emas, FOIZ sifatida o'qiladimi. */
export function isPercentKind(kind: PayrollRuleKindName): boolean {
  return kind === 'PercentOfRevenue'
}

/**
 * Guruh/kurs/kategoriya bo'yicha maqsadlash ma'nolimi.
 *
 * 🔴 Oklad va oylik o'quvchi bonusi xodimning BUTUN oyiga tegishli —
 * ularni bitta guruhga bog'lash "qaysi guruh oyni to'laydi?" degan
 * javobsiz savol tug'dirardi (server ham buni rad etadi).
 */
export function supportsGroupTargeting(kind: PayrollRuleKindName): boolean {
  return kind !== 'FixedMonthly' && kind !== 'MonthlyPerActiveStudent'
}

/** O'quvchi soni natijaga ta'sir qiladimi — `basis` va min/max shu turlarda ma'noli. */
export function usesStudentCount(kind: PayrollRuleKindName): boolean {
  return (
    kind === 'PerAttendedStudent' ||
    kind === 'TieredByAttendance' ||
    kind === 'PerStudentAcademicHour'
  )
}

/* =============================================================== hisob asosi === */

const BASIS_LABELS: Record<PayrollBasisName, string> = {
  Attended: 'Faqat kelganlar',
  AttendedAndExcused: 'Kelganlar + sababli kelmaganlar',
  Enrolled: 'Ro‘yxatdagi barcha o‘quvchilar',
}

export function payrollBasisLabel(basis: PayrollBasisName): string {
  return BASIS_LABELS[basis]
}

export const PAYROLL_BASIS_OPTIONS: ReadonlyArray<{ value: PayrollBasisName; label: string }> = (
  ['Attended', 'AttendedAndExcused', 'Enrolled'] as const
).map((value) => ({ value, label: BASIS_LABELS[value] }))

/* ========================================================== guruh rejimi === */

const GROUP_MODE_LABELS: Record<GroupPayrollModeName, string> = {
  Auto: 'Avtomatik (qoidalar bo‘yicha)',
  IncludedInSalary: 'Oklad ichida — alohida haq yo‘q',
  FixedRule: 'Aniq qoida majburlangan',
}

export function groupPayrollModeLabel(mode: GroupPayrollModeName): string {
  return GROUP_MODE_LABELS[mode]
}

export const GROUP_PAYROLL_MODE_OPTIONS: ReadonlyArray<{
  value: GroupPayrollModeName
  label: string
}> = (['Auto', 'IncludedInSalary', 'FixedRule'] as const).map((value) => ({
  value,
  label: GROUP_MODE_LABELS[value],
}))

/* ============================================================ qoida qamrovi === */

/**
 * Qoida KIMGA/NIMAGA tegishli — bir qatorli ta'rif.
 * `entities/payment` dagi `tariffScopeLabel` bilan AYNI naqsh.
 */
export function ruleScopeLabel(rule: PayrollRuleDto): string {
  const parts: string[] = []

  parts.push(rule.userId !== null ? (rule.userName ?? '—') : `${payrollRoleLabel(rule.role)} — barchasi`)

  if (rule.groupName !== null) parts.push(`guruh: ${rule.groupName}`)
  if (rule.courseName !== null) parts.push(`kurs: ${rule.courseName}`)
  if (rule.categoryName !== null) parts.push(`kategoriya: ${rule.categoryName}`)
  if (rule.groupType !== null) parts.push(GROUP_TYPE_LABELS[rule.groupType] ?? rule.groupType)

  return parts.join(' · ')
}

const GROUP_TYPE_LABELS: Partial<Record<string, string>> = {
  Group: 'guruh darsi',
  Individual: 'yakka dars',
  Curator: 'kurator guruhi',
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

/* ============================================================ tasdiqlash/to'lov === */

const APPROVAL_STATUS_LABELS: Record<PayrollApprovalStatusName, string> = {
  Draft: 'Qoralama',
  Approved: 'Tasdiqlangan',
  Paid: 'To‘langan',
}

export function payrollApprovalStatusLabel(status: PayrollApprovalStatusName): string {
  return APPROVAL_STATUS_LABELS[status]
}

const APPROVAL_STATUS_TONES: Record<PayrollApprovalStatusName, 'neutral' | 'warning' | 'success'> = {
  Draft: 'neutral',
  Approved: 'warning',
  Paid: 'success',
}

export function payrollApprovalStatusTone(status: PayrollApprovalStatusName): 'neutral' | 'warning' | 'success' {
  return APPROVAL_STATUS_TONES[status]
}

/** `<input type="date">` uchun bugungi sana, `YYYY-MM-DD` (MAHALLIY vaqtda). */
export function todayIsoDate(): string {
  const now = new Date()
  const month = now.getMonth() + 1
  const day = now.getDate()
  return `${now.getFullYear()}-${month < 10 ? '0' : ''}${month}-${day < 10 ? '0' : ''}${day}`
}
