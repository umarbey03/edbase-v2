import { useQuery } from '@tanstack/vue-query'
import { computed } from 'vue'
import type { ComputedRef, Ref } from 'vue'

import { fetchMyAssignments } from '@/entities/assignment'
import { fetchMyPoints } from '@/entities/points'
import { fetchAttendanceSummary } from '@/entities/progress'
import { useMySubscription } from '@/entities/subscription'
import { sessionState } from '@/features/student-schedule/model/useStudentSchedule'
import type { StudentSchedule } from '@/features/student-schedule/model/useStudentSchedule'

import { decideMood } from './mood'
import type { MoodResult } from './mood'
import { buildMoodSignals } from './signals'

export interface StudentMood {
  /** `null` — asosiy ma'lumot hali kelmagan (karta skelet ko'rsatadi). */
  result: ComputedRef<MoodResult | null>
  /** Jonli dars ID si — «Darsga qo'shilish» tugmasi uchun. */
  liveSessionId: ComputedRef<number | null>
}

/**
 * O'quvchi kayfiyati — bosh sahifadagi maskot kartasi uchun.
 *
 * `schedule` — chaqiruvchi sahifaning O'Z jadvali (bosh sahifada u
 * allaqachon bor), ikkinchi nusxa ochilmaydi.
 *
 * ★ `queryKey` LAR BOSH SAHIFA, `MyPointsCard` va salomlashuv ekraniniki
 *   bilan AYNAN BIR XIL — ya'ni bu composable yangi so'rov yubormaydi,
 *   keshdagini qayta o'qiydi (vazifalar ro'yxati ham salomlashuvda
 *   allaqachon yuklangan bo'ladi).
 *
 * ★ KUTISH QOIDASI: davomat va vazifalar kelguncha natija `null`. Ball
 *   javobi KUTILMAYDI — u faqat qo'shimcha signal (o'rin, faollik
 *   sanasi), kelgach kayfiyat o'zi yangilanadi. Xato bo'lsa ham kutish
 *   tugaydi: signal `null` bo'lib qoladi va qoida uni e'tiborsiz qoldiradi.
 */
export function useStudentMood(now: Ref<Date>, schedule: StudentSchedule): StudentMood {
  const subscription = useMySubscription()

  const attendanceQuery = useQuery({
    queryKey: ['progress', 'attendance'],
    queryFn: ({ signal }) => fetchAttendanceSummary({}, { signal }),
  })

  const assignmentsQuery = useQuery({
    queryKey: ['assignments', 'mine'],
    queryFn: ({ signal }) => fetchMyAssignments({ signal }),
  })

  const pointsQuery = useQuery({
    queryKey: ['progress', 'points', 'current'],
    queryFn: ({ signal }) => fetchMyPoints(undefined, { signal }),
  })

  const liveSessionId = computed(() => {
    if (subscription.isSelfStudy.value) return null
    const next = schedule.nextAny.value
    if (next === null) return null
    return sessionState(next, now.value) === 'live' ? next.id : null
  })

  const result = computed<MoodResult | null>(() => {
    if (attendanceQuery.isPending.value || assignmentsQuery.isPending.value) return null

    const signals = buildMoodSignals({
      isSelfStudy: subscription.isSelfStudy.value,
      liveNow: liveSessionId.value !== null,
      attendance: attendanceQuery.data.value ?? null,
      assignments: assignmentsQuery.data.value ?? null,
      points: pointsQuery.data.value ?? null,
      now: now.value,
    })
    return decideMood(signals)
  })

  return { result, liveSessionId }
}
