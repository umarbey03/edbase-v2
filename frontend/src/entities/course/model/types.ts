import { lookup } from '@/shared/lib/lookup'
import type { CourseDto, CourseLessonDto, CourseModuleDto, LessonLockReasonName } from '@/shared/types'

/** `BaseBadge` `tone` prop'i bilan mos qism to'plam. */
export type CourseTone = 'accent' | 'neutral' | 'success' | 'warning' | 'danger'

const LOCK_REASON_LABELS: Record<LessonLockReasonName, string> = {
  PreviousIncomplete: 'Oldingi dars tugatilmagan',
  TeacherPace: 'Ustoz hali bu darsga yetmagan',
  NotInCourse: 'Dars o‘quvchining kursida yo‘q',
}

export function lessonLockReasonLabel(value: string): string {
  return lookup(LOCK_REASON_LABELS, value, 'Yopiq')
}

/** Kurs ro'yxatidagi "3 modul · 24 dars" satri. */
export function courseContentSummary(course: CourseDto): string {
  if (course.moduleCount === 0) return 'Kontent kiritilmagan'
  return `${course.moduleCount} modul · ${course.lessonCount} dars`
}

/**
 * Kursni o'chirish MUMKINMI — guruh biriktirilgan bo'lsa server 409 beradi.
 *
 * NEGA frontendda ham tekshiriladi: server qoidasi TAKRORLANMAYDI, shunchaki
 * tugma oldindan o'chiriladi. O'quvchi ishi bor-yo'qligini frontend bilmaydi,
 * shuning uchun bu YAKUNIY javob emas — 409 baribir ushlanadi.
 */
export function courseLooksDeletable(course: CourseDto): boolean {
  return course.groupCount === 0
}

/** Modul sarlavhasi ostidagi "5 dars" satri. */
export function moduleLessonSummary(module: CourseModuleDto): string {
  const count = module.lessons?.length ?? 0
  if (count === 0) return 'Dars yo‘q'
  return `${count} dars`
}

/** Dars qatoridagi "45 daq" — kiritilmagan bo'lsa chiziqcha. */
export function lessonDurationLabel(lesson: CourseLessonDto): string {
  return lesson.durationMin === null ? '—' : `${lesson.durationMin} daq`
}
