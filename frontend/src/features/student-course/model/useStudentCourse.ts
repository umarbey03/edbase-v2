import { useQuery } from '@tanstack/vue-query'
import { computed } from 'vue'
import type { ComputedRef } from 'vue'

import { fetchCourses, fetchCourseTree } from '@/entities/course'
import { toUserMessage } from '@/shared/api'
import { lookup } from '@/shared/lib/lookup'
import type { CourseLessonDto, CourseModuleDto, CourseTreeDto } from '@/shared/types'

/**
 * O'quvchining kursi.
 *
 * IKKI BOSQICH, chunki backend shunday ajratgan:
 *  1) `GET /api/v1/courses` — o'quvchiga FAQAT o'z kursini qaytaradi (guruh
 *     orqali). Guruhga kurs biriktirilmagan bo'lsa `items` bo'sh keladi,
 *     403 EMAS — ya'ni "kurs yo'q" holati oddiy bo'sh ro'yxat.
 *  2) `GET /api/v1/courses/{id}` — modul va darslar daraxti, gating natijasi
 *     (`unlocked` / `lockReason`) bilan.
 *
 * ★ NIMA YO'Q: server dars `completed` ekanini, video ko'rilganini yoki
 *   vazifa/test topshirilganini kurs daraxtida BERMAYDI (`CourseLessonDto` da
 *   bunday maydonlar yo'q; ichki `LessonGateDto` hech qaysi controller orqali
 *   ochilmagan). Shu sababli eski ilovadagi "N/M dars tugatilgan" o'rniga
 *   "ochilgan darslar" hisoblanadi — bu bizda BOR bo'lgan yagona halol o'lchov.
 */

/** Eski ilovadagi `LOCK_MSG` — qulflangan darsni bosganda chiqadigan matn. */
const LOCK_MESSAGES: Record<string, string> = {
  TeacherPace: "Ustoz bu darsga hali yetmagan — jonli darsda o'tilgach ochiladi",
  PreviousIncomplete: "Avvalgi darsni to'liq tugating (video, vazifa va test)",
  NotInCourse: "Guruhingiz kursning boshqa qismidan boshlagan",
}

/** Eski ilovadagi `LOCK_SHORT` — dars qatorining o'ng chekkasidagi qisqa yorliq. */
const LOCK_SHORT: Record<string, string> = {
  TeacherPace: 'Ustoz kutilmoqda',
  PreviousIncomplete: 'Avvalgi dars',
  NotInCourse: 'Boshqa qism',
}

export function lockMessage(reason: string | null): string {
  if (reason === null) return 'Bu dars hali ochilmagan'
  return lookup(LOCK_MESSAGES, reason, 'Bu dars hali ochilmagan')
}

export function lockShortLabel(reason: string | null): string {
  if (reason === null) return 'Yopiq'
  return lookup(LOCK_SHORT, reason, 'Yopiq')
}

export interface StudentCourse {
  tree: ComputedRef<CourseTreeDto | null>
  modules: ComputedRef<CourseModuleDto[]>
  /** Kursga biriktirilmagan (server bo'sh ro'yxat qaytardi). */
  hasNoCourse: ComputedRef<boolean>
  lessonCount: ComputedRef<number>
  unlockedCount: ComputedRef<number>
  unlockedPercent: ComputedRef<number>
  /** Keyingi qadam: birinchi OCHIQ dars (eski ilovadagi "Boshlash" tugmasi). */
  nextLessonId: ComputedRef<number | null>
  isPending: ComputedRef<boolean>
  isFetching: ComputedRef<boolean>
  error: ComputedRef<string | null>
  refetch: () => void
}

export function useStudentCourse(): StudentCourse {
  const listQuery = useQuery({
    queryKey: ['courses', 'student'],
    // O'quvchiga baribir bitta kurs keladi; `pageSize: 5` — server yangi
    // qoida qo'shsa ham birinchi sahifa yetarli bo'lsin.
    queryFn: ({ signal }) => fetchCourses({ page: 1, pageSize: 5 }, { signal }),
    staleTime: 5 * 60_000,
  })

  const courseId = computed(() => listQuery.data.value?.items?.[0]?.id ?? null)

  const treeQuery = useQuery({
    queryKey: computed(() => ['courses', 'tree', courseId.value]),
    queryFn: ({ signal }) => fetchCourseTree(courseId.value as number, { signal }),
    enabled: computed(() => courseId.value !== null),
    staleTime: 5 * 60_000,
  })

  const tree = computed(() => treeQuery.data.value ?? null)
  const modules = computed(() => (tree.value?.modules ?? []).filter((m) => (m.lessons ?? []).length > 0))

  const allLessons = computed<CourseLessonDto[]>(() =>
    modules.value.flatMap((module) => module.lessons ?? []),
  )

  const lessonCount = computed(() => allLessons.value.length)
  const unlockedCount = computed(() => allLessons.value.filter((lesson) => lesson.unlocked).length)
  const unlockedPercent = computed(() =>
    lessonCount.value === 0 ? 0 : Math.round((unlockedCount.value / lessonCount.value) * 100),
  )

  /*
    "Hozirgi" dars — OXIRGI ochiq dars, birinchisi emas: gating ketma-ket
    ishlaydi (`PreviousIncomplete`), ya'ni ochiq darslarning eng oxirgisi
    o'quvchi yetib kelgan nuqtadir.
  */
  const nextLessonId = computed(() => {
    const unlocked = allLessons.value.filter((lesson) => lesson.unlocked)
    return unlocked[unlocked.length - 1]?.id ?? null
  })

  return {
    tree,
    modules,
    hasNoCourse: computed(
      () => !listQuery.isPending.value && (listQuery.data.value?.items ?? []).length === 0,
    ),
    lessonCount,
    unlockedCount,
    unlockedPercent,
    nextLessonId,
    isPending: computed(
      () => listQuery.isPending.value || (courseId.value !== null && treeQuery.isPending.value),
    ),
    isFetching: computed(() => listQuery.isFetching.value || treeQuery.isFetching.value),
    error: computed(() => {
      const failure = listQuery.error.value ?? treeQuery.error.value
      return failure !== null && failure !== undefined ? toUserMessage(failure) : null
    }),
    refetch: () => {
      void listQuery.refetch()
      void treeQuery.refetch()
    },
  }
}
