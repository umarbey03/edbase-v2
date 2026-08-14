import { lookup } from '@/shared/lib/lookup'
import type { UserDto, UserRoleName } from '@/shared/types'

export type User = UserDto

/** `BaseBadge` ning `tone` prop'i bilan mos keladigan qism to'plam. */
export type RoleTone = 'teacher' | 'assistant' | 'student' | 'neutral'

const ROLE_LABELS: Record<UserRoleName, string> = {
  Student: 'O‘quvchi',
  Teacher: 'Ustoz',
  Assistant: 'Kurator',
  Academic: 'Akademik',
  Admin: 'Administrator',
}

const ROLE_TONES: Record<UserRoleName, RoleTone> = {
  Student: 'student',
  Teacher: 'teacher',
  Assistant: 'assistant',
  Academic: 'neutral',
  Admin: 'neutral',
}

export function roleLabel(role: string): string {
  return lookup(ROLE_LABELS, role, role)
}

export function roleTone(role: string): RoleTone {
  return lookup(ROLE_TONES, role, 'neutral')
}

/** Ustoz yoki kurator — jonli darsda "host" bo'lishi mumkin bo'lganlar. */
export function isStaffRole(role: string): boolean {
  return role === 'Teacher' || role === 'Assistant'
}

/** Rol tanlash ro'yxati (CRM formasi va filtri uchun) — tartib ierarxiya bo'yicha. */
export const ROLE_OPTIONS: ReadonlyArray<{ value: UserRoleName; label: string }> = [
  { value: 'Student', label: ROLE_LABELS.Student },
  { value: 'Teacher', label: ROLE_LABELS.Teacher },
  { value: 'Assistant', label: ROLE_LABELS.Assistant },
  { value: 'Academic', label: ROLE_LABELS.Academic },
  { value: 'Admin', label: ROLE_LABELS.Admin },
]

/** O'quv bo'limi/administrator — boshqaruv panelini ko'radiganlar. */
export function isManagerRole(role: string): boolean {
  return role === 'Academic' || role === 'Admin'
}

/**
 * FAQAT administrator.
 *
 * `isManagerRole` dan ataylab ajratilgan: pul kiritish va yechish loyiha
 * egasining qaroriga ko'ra faqat admin panelida ("bunisi faqat admin
 * panelda"). O'quv bo'limi moliyani KO'RADI, lekin o'zgartira olmaydi.
 * ⚠️ Bu — KO'RINISH darvozasi; serverdagi tekshiruvni almashtirmaydi.
 */
export function isAdminRole(role: string): boolean {
  return role === 'Admin'
}

/**
 * O'quvchining KONTAKT ma'lumoti (email, telefon, Telegram) shu rolga
 * yuboriladimi — talab R27.
 *
 * ⚠️ BU XAVFSIZLIK TEKSHIRUVI EMAS va bo'la olmaydi: kesish SERVERDA
 * (`StudentAudience`), maydonlar javobga `null` bo'lib tushadi. Bu yerdagi
 * funksiya faqat MATNNI to'g'ri tanlash uchun: bo'sh telefonni ko'rgan
 * interfeys "Telefon kiritilmagan" deb yozib qo'ysa, ustozga YOLG'ON
 * aytgan bo'lardi (raqam bor, shunchaki berilmagan). Render qilish
 * qarorining o'zi baribir `null` tekshiruviga tayanadi.
 *
 * ★ KURATOR ATAYLAB ISTISNO: qo'ng'iroq — uning asosiy amali (dars
 * qoldirgan o'quvchini u qidiradi). Sabab serverdagi `StudentAudience`
 * izohida to'liq yozilgan.
 */
export function canSeeStudentContact(role: string): boolean {
  return role !== 'Teacher'
}

/** Rol bo'yicha saralash og'irligi: ustoz -> kurator -> qolganlar. */
export function roleWeight(role: string): number {
  if (role === 'Teacher') return 0
  if (role === 'Assistant') return 1
  if (role === 'Academic' || role === 'Admin') return 2
  return 3
}
