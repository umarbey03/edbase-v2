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

/** Rol bo'yicha saralash og'irligi: ustoz -> kurator -> qolganlar. */
export function roleWeight(role: string): number {
  if (role === 'Teacher') return 0
  if (role === 'Assistant') return 1
  if (role === 'Academic' || role === 'Admin') return 2
  return 3
}
