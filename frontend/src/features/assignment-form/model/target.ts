import type { AssignmentDto } from '@/shared/types'

/**
 * Vazifa NISHONI.
 *
 * Serverda bu "YOKI guruh, YOKI kurs darsi" (`CK_Assignments_GroupXorLesson`
 * va `Assignment.Validate()`), lekin formada foydalanuvchi avval TURNI
 * tanlaydi, keyin qiymatni — shuning uchun `kind` alohida saqlanadi.
 * Tanlanmagan qiymat `null` bo'ladi va forma yuborilmaydi.
 *
 * Tur `<script setup>` dan eksport qilib bo'lmagani uchun alohida modulda
 * (`icon-names.ts` bilan bir xil sabab).
 */
export type AssignmentTargetKind = 'group' | 'lesson'

export interface AssignmentTarget {
  kind: AssignmentTargetKind
  groupId: number | null
  lessonId: number | null
}

/** Standart nishon — guruh vazifasi (ustoz eng ko'p shuni beradi). */
export function emptyTarget(): AssignmentTarget {
  return { kind: 'group', groupId: null, lessonId: null }
}

/** Nishon tanlanganmi (forma yuborishga tayyormi). */
export function isTargetChosen(target: AssignmentTarget): boolean {
  return target.kind === 'group' ? target.groupId !== null : target.lessonId !== null
}

/**
 * Mavjud vazifaning nishoni — TAHRIRLASHDA faqat ko'rsatish uchun.
 * Server nishonni o'zgartirmaydi, shuning uchun forma uni tahrirlamaydi ham.
 */
export function targetLabel(assignment: AssignmentDto): string {
  if (assignment.groupName !== null && assignment.groupName.length > 0) {
    return `Guruh: ${assignment.groupName}`
  }
  if (assignment.moduleLessonName !== null && assignment.moduleLessonName.length > 0) {
    return `Kurs darsi: ${assignment.moduleLessonName}`
  }
  return assignment.moduleLessonId !== null ? 'Kurs darsi' : 'Guruh vazifasi'
}
