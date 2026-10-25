import { inject } from 'vue'
import type { InjectionKey } from 'vue'

/**
 * O'quvchi karkasining amallari — ichki sahifalar uchun (2026-10-23).
 *
 * Profil sahifasi "Tahrirlash" va "Chiqish" ni karkasning O'Z oynasi va
 * chiqish oqimi orqali bajaradi: chiqish mantig'i (Mini App'da ilovani
 * yopish va h.k.) faqat `StudentShell` da yashaydi va ikki nusxa bo'lmaydi.
 */
export interface StudentShellActions {
  openProfileEdit: () => void
  logout: () => Promise<void>
}

export const STUDENT_SHELL_ACTIONS: InjectionKey<StudentShellActions> = Symbol('student-shell-actions')

export function useStudentShellActions(): StudentShellActions {
  const actions = inject(STUDENT_SHELL_ACTIONS, null)
  if (actions === null) throw new Error('useStudentShellActions StudentShell ichida chaqirilishi kerak')
  return actions
}
