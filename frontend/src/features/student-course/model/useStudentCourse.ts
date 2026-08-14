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
 * ★ TUZATILDI (2026-08-13, R9): bu yerda ilgari "server dars `completed`
 *   ekanini BERMAYDI" deb yozilgan edi va progress shu sababli OCHILGAN
 *   darslar bo'yicha hisoblanardi. IZOH NOTO'G'RI EDI — `CourseLessonDto`
 *   WAVE 2 dan beri `completed` ni yuboradi (`CourseService.MapLesson` uni
 *   gating daraxtidan oladi), faqat frontend TIPIDA maydon yo'q edi.
 *   Endi progress AYNAN eski ilovadagidek "N/M dars tugatilgan" ni o'lchaydi.
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

/**
 * Dars O'QUVCHINING O'QUV REJASIGA kiradimi — ya'ni progress MAXRAJIGA
 * qo'shiladimi.
 *
 * 🔴 XATO TUZATILDI (2026-08-13): ilgari progress BARCHA darslar bo'yicha
 * hisoblanardi. `Group.VideoStartLessonId` bilan kursning O'RTASIDAN
 * boshlagan guruhda undan oldingi darslar `BeforeGroupStart` sababi bilan
 * qulflanadi va HECH QACHON ochilmaydi — ular maxrajda qolsa progress
 * abadiy, masalan 40% da qotib turardi va o'quvchi kursni tugatsa ham
 * 100% ni ko'rmasdi.
 *
 * Bu — frontend qarori emas, BACKEND SHARTNOMASI: `GatingDtos.cs` dagi
 * `LessonLockReason.BeforeGroupStart` izohi so'zma-so'z shunday deydi —
 * *"KURS PROGRESSI MAXRAJIGA KIRMAYDI"*.
 *
 * ★ SATR QIYMATI SERVERDAN TEKSHIRILDI: enum `LessonLockReason` da
 *   `BeforeGroupStart = 3` deb yozilgan (`GatingDtos.cs:38`) va
 *   `Program.cs` dagi `JsonStringEnumConverter` uni JSON'ga AYNAN shu nom
 *   bilan chiqaradi. Yozilishi `LessonLockReasonName` tipida ham bor, ya'ni
 *   xato yozsak `tsc` ushlaydi.
 *
 * ★ QULFLANGAN, LEKIN BOSHQA SABABLI dars maxrajda QOLADI: `TeacherPace`
 *   va `PreviousIncomplete` — vaqtinchalik holatlar, o'quvchi ularga yetib
 *   boradi. Ularni ham chiqarib tashlasak maxraj o'quvchi bilan birga
 *   o'sib, progress DOIM ~100% bo'lib turardi.
 */
export function countsTowardProgress(lesson: CourseLessonDto): boolean {
  return lesson.lockReason !== 'BeforeGroupStart'
}

export interface StudentCourse {
  tree: ComputedRef<CourseTreeDto | null>
  modules: ComputedRef<CourseModuleDto[]>
  /** Kursga biriktirilmagan (server bo'sh ro'yxat qaytardi). */
  hasNoCourse: ComputedRef<boolean>
  /**
   * Daraxtdagi BARCHA darslar soni — "kurs bo'shmi?" tekshiruvi uchun.
   * ★ Progress maxraji BU EMAS, `plannedCount` (pastga qarang).
   */
  lessonCount: ComputedRef<number>
  /** Progress MAXRAJI: `BeforeGroupStart` chiqarilgan darslar soni. */
  plannedCount: ComputedRef<number>
  /** Progress SURATI: tugatilgan darslar soni (maxraj bilan bir xil to'plamdan). */
  completedCount: ComputedRef<number>
  /** 0..100 — `completedCount / plannedCount`. */
  progressPercent: ComputedRef<number>
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

  /*
    Progress SURATI ham, MAXRAJI ham AYNI to'plamdan olinadi (`countsTowardProgress`
    filtrlagan). Ikkalasini turli to'plamdan olish klassik xato bo'lardi:
    `BeforeGroupStart` dars hech qachon `completed` bo'lmaydi, ya'ni uni faqat
    maxrajdan chiqarsak ham yetarli — LEKIN bitta ro'yxat ustida ishlash
    kelajakda server qoidasi o'zgarsa ham ikkovini bir joyda ushlab turadi.
  */
  const plannedLessons = computed(() => allLessons.value.filter(countsTowardProgress))
  const plannedCount = computed(() => plannedLessons.value.length)
  const completedCount = computed(
    () => plannedLessons.value.filter((lesson) => lesson.completed).length,
  )
  const progressPercent = computed(() =>
    plannedCount.value === 0 ? 0 : Math.round((completedCount.value / plannedCount.value) * 100),
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
    plannedCount,
    completedCount,
    progressPercent,
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
