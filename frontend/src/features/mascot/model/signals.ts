import type { MyPointsDto, PointKindName } from '@/entities/points'
import type { AttendanceSummaryDto, StudentAssignmentDto } from '@/shared/types'

import type { MoodSignals } from './mood'

/**
 * ════════════════════════════════════════════════════════════════════════
 * SERVER JAVOBLARI → KAYFIYAT SIGNALLARI (2026-10-29)
 * ════════════════════════════════════════════════════════════════════════
 *
 * ★ YANGI ENDPOINT YO'Q. Hamma signal bosh sahifa allaqachon so'raydigan
 *   (yoki keshda turgan) uch javobdan yig'iladi:
 *     • `GET /progress/attendance` — davomat, ketma-ketlik;
 *     • `GET /assignments/mine`   — kechikkan / kutayotgan / baholangan;
 *     • `GET /progress/points`    — oydagi ball, o'rin, oxirgi hodisalar.
 *   Har biri `null` bo'lishi mumkin (so'rov yiqildi yoki hali kelmadi) —
 *   unda tegishli signal ham `null` va qoida uni e'tiborsiz qoldiradi.
 *
 * ★ SOF FUNKSIYA — `now` tashqaridan keladi.
 */

export interface SignalSources {
  isSelfStudy: boolean
  liveNow: boolean
  attendance: AttendanceSummaryDto | null
  assignments: StudentAssignmentDto[] | null
  points: MyPointsDto | null
  now: Date
}

const DAY_MS = 86_400_000

/** Faollik EMAS hisoblanadigan ball hodisalari (kelmaganlik). */
const PASSIVE_KINDS: ReadonlySet<PointKindName> = new Set<PointKindName>(['Absent', 'Excused'])

/** Baho o'rtachasi uchun eng oxirgi shuncha baholangan ish olinadi. */
const RECENT_GRADES = 5

function time(iso: string | null | undefined): number | null {
  if (iso === null || iso === undefined) return null
  const t = new Date(iso).getTime()
  return Number.isNaN(t) ? null : t
}

function untouched(items: StudentAssignmentDto[]): StudentAssignmentDto[] {
  // `canSubmit` — server qarori (qulf, yopilgan, to'lov to'sig'i) —
  // `greeting-message.ts` dagi `untouched` bilan AYNI mulohaza.
  return items.filter((item) => item.canSubmit && item.mySubmission === null)
}

/**
 * Oxirgi faollikdan beri o'tgan kun.
 *
 * Manba: ball hodisalari (kelish, baho, video, vazifa, test) va vazifa
 * topshirish sanalari — eng kechi olinadi.
 *
 * ★ PASTKI CHEGARA: ball javobi FAQAT JORIY OYNI qamraydi. Agar oyda
 *   hech qanday faollik bo'lmasa, lekin o'quvchining tarixi bor bo'lsa,
 *   aniq sana noma'lum — biroq "kamida oy boshidan beri" ekani aniq.
 *   Oyning birinchi haftasida bu chegara kichik va hech nima demaydi.
 */
function daysSinceActivity(src: SignalSources, hasHistory: boolean): number | null {
  let latest: number | null = null
  const consider = (t: number | null): void => {
    if (t !== null && t <= src.now.getTime() && (latest === null || t > latest)) latest = t
  }

  for (const event of src.points?.recent ?? []) {
    if (!PASSIVE_KINDS.has(event.kind)) consider(time(event.occurredAt))
  }
  for (const item of src.assignments ?? []) consider(time(item.mySubmission?.submittedAt))

  if (latest !== null) return Math.floor((src.now.getTime() - latest) / DAY_MS)

  if (src.points !== null && hasHistory) {
    const monthStart = new Date(src.now.getFullYear(), src.now.getMonth(), 1).getTime()
    return Math.floor((src.now.getTime() - monthStart) / DAY_MS)
  }
  return null
}

export function buildMoodSignals(src: SignalSources): MoodSignals {
  // Mustaqil o'quvchida jonli dars va davomat yo'q — signal "noma'lum".
  const attendance = src.isSelfStudy ? null : src.attendance
  const lessonsTotal = attendance?.overall.total ?? null

  const assignments = src.assignments
  const pending = assignments === null ? null : untouched(assignments)
  const overdueCount = pending === null ? null : pending.filter((a) => a.isOverdue).length
  const dueSoonCount = pending === null
    ? null
    : pending.filter((a) => {
      if (a.isOverdue) return false
      const due = time(a.dueAt)
      return due !== null && due >= src.now.getTime() && due - src.now.getTime() <= DAY_MS
    }).length

  const graded = (assignments ?? [])
    .map((a) => a.mySubmission)
    .filter((s): s is NonNullable<typeof s> => s !== null && s.scorePercent !== null)
    .sort((a, b) => (time(b.submittedAt) ?? 0) - (time(a.submittedAt) ?? 0))
    .slice(0, RECENT_GRADES)
  const avgGradePercent = graded.length === 0
    ? null
    : graded.reduce((sum, s) => sum + (s.scorePercent ?? 0), 0) / graded.length

  const anySubmission = (assignments ?? []).some((a) => a.mySubmission !== null)
  const hasHistory = (lessonsTotal ?? 0) > 0
    || anySubmission
    || (src.points?.total ?? 0) !== 0
    || (src.points?.recent.length ?? 0) > 0

  return {
    isSelfStudy: src.isSelfStudy,
    liveNow: src.liveNow,
    hasHistory,
    lessonsTotal,
    attendancePercent: attendance === null || lessonsTotal === 0 ? null : attendance.overall.percent,
    // Ketma-ketlik: davomatdan; u bo'lmasa — ball javobidagi (guruh o'quvchisi).
    attendanceStreak: attendance?.streak ?? (src.isSelfStudy ? null : src.points?.streak ?? null),
    overdueCount,
    pendingCount: pending === null ? null : pending.length,
    dueSoonCount,
    gradedCount: graded.length,
    avgGradePercent,
    monthPoints: src.points?.total ?? null,
    rank: src.points?.rank ?? null,
    daysSinceActivity: daysSinceActivity(src, hasHistory),
  }
}
