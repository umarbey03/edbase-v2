/**
 * BALL TIZIMI (2026-10-23) — `ProgressController.Points` shartnomasi.
 *
 * Qoidalar serverda (`PointRules`): kelish +10, kelmaslik −5 (uzrli −3),
 * ketma-ket 4 darsga +10, 8 darsga +25, dars bahosi ×2, vazifa ×3,
 * test 10 gacha, dars videosi +3. Mijoz ularni QAYTA HISOBLAMAYDI —
 * faqat ko'rsatadi.
 */
export type PointKindName =
  | 'Present'
  | 'Absent'
  | 'Excused'
  | 'StreakBonus'
  | 'LessonGrade'
  | 'Assignment'
  | 'Test'
  | 'VideoWatched'

export interface PointsBreakdownDto {
  kind: PointKindName
  count: number
  points: number
}

export interface PointEventDto {
  kind: PointKindName
  delta: number
  occurredAt: string
  title: string | null
}

export interface MyPointsDto {
  period: string
  total: number
  /** Guruhdagi o'rin; ball yo'q bo'lsa `null`. */
  rank: number | null
  groupName: string | null
  streak: number
  lessonsToNextBonus: number
  nextBonus: number
  breakdown: PointsBreakdownDto[]
  recent: PointEventDto[]
}

const KIND_LABELS: Record<PointKindName, string> = {
  Present: 'Darsga keldi',
  Absent: 'Darsga kelmadi',
  Excused: 'Uzrli kelmadi',
  StreakBonus: 'Ketma-ketlik bonusi',
  LessonGrade: 'Dars bahosi',
  Assignment: 'Uy vazifasi',
  Test: 'Test',
  VideoWatched: 'Dars videosi',
}

export function pointKindLabel(kind: PointKindName): string {
  return KIND_LABELS[kind] ?? kind
}

/** `+10`, `-5`, `0`. */
export function formatDelta(delta: number): string {
  return delta > 0 ? `+${delta}` : String(delta)
}
