import type { LessonLockReasonName, MasteryStatusName } from '@/shared/types'

/** Mustaqil o'quvchining kuratori (2026-11-18). Server: `SelfStudyCuratorsController`. */

export interface SelfStudyCuratorDto {
  id: number
  curatorId: number
  curatorName: string
  backupCuratorId: number | null
  backupCuratorName: string | null
  assignedAt: string
  assignedByName: string | null
  endedAt: string | null
}

export interface StudentCuratorInfoDto {
  current: SelfStudyCuratorDto | null
  /** Yangisi birinchi; joriy biriktirish ham shu ro'yxatda. */
  history: SelfStudyCuratorDto[]
}

export interface CuratorCandidateDto {
  id: number
  fullName: string
  /** `Assistant` (kurator) yoki `Teacher`. */
  role: string
  /** Hozir asosiy kurator bo'lib turgan o'quvchilar soni. */
  activeStudents: number
}

export interface MySelfStudyStudentDto {
  studentId: number
  fullName: string
  isBackup: boolean
  assignedAt: string
  subscriptionEndsAt: string | null
  subscriptionActive: boolean
  pendingSubmissions: number
  /** Oxirgi o'quv faolligi (video, vazifa, test, darslik, chat). Hech qachon — `null`. */
  lastActivityAt?: string | null
  /** Necha kundan beri faolsiz (hech boshlamagan — `null`). */
  idleDays?: number | null
  /** Eslatmadagi oxirgi tugma javobi: `Hard` | `Busy` | `Snooze` | `ComingToday` | `OptOut`. */
  lastNudgeResponse?: string | null
  /** O'quvchi avtomatik eslatmalarni o'chirgan. */
  nudgesOff?: boolean
}

export interface UnassignedSelfStudyStudentDto {
  studentId: number
  fullName: string
  subscriptionEndsAt: string
}

export interface MyCuratorDto {
  curatorId: number
  fullName: string
}

export const CURATOR_ROLE_LABELS: Record<string, string> = {
  Assistant: 'Kurator',
  Teacher: 'Ustoz',
}

/* ───────────── «O'zlashtirdi» qoidasi (2026-11-18) ───────────── */


export type MasteryEventKindName = 'Mastered' | 'NeedsWork' | 'AdminUnlocked' | 'AdminUnlockRevoked'

export interface BoardAssignmentDto {
  assignmentId: number
  title: string
  maxScore: number
  submissionId: number | null
  submissionStatus: 'Submitted' | 'Graded' | null
  score: number | null
  submittedAt: string | null
  attemptNumber: number | null
}

export interface LessonBoardRowDto {
  lessonId: number
  index: number
  moduleName: string
  lessonName: string
  unlocked: boolean
  lockReason: LessonLockReasonName | null
  /** O'quvchi video, vazifa va testni bajargan (kurator qaroridan qat'i nazar). */
  workDone: boolean
  hasVideo: boolean
  videoWatched: boolean
  hasAssignment: boolean
  assignment: BoardAssignmentDto | null
  hasTest: boolean
  testTaken: boolean
  testPercent: number | null
  mastery: MasteryStatusName | null
  masteryNote: string | null
  decidedByName: string | null
  decidedAt: string | null
  unlockedOverride: boolean
  overrideReason: string | null
  /** 2026-11-25: oldingi dars testi 100% — kurator qarorisiz VAQTINCHA ochilgan. */
  provisional?: boolean
}

export interface StudentLessonBoardDto {
  studentId: number
  studentName: string
  courseName: string | null
  subscriptionActive: boolean
  canDecide: boolean
  canOverride: boolean
  lessons: LessonBoardRowDto[]
}

export interface MasteryEventDto {
  id: number
  lessonId: number
  lessonName: string
  kind: MasteryEventKindName
  note: string | null
  actorName: string
  at: string
}

export interface MyLessonMasteryDto {
  lessonId: number
  status: MasteryStatusName
  note: string | null
  decidedAt: string
}

export const MASTERY_EVENT_LABELS: Record<MasteryEventKindName, string> = {
  Mastered: 'O‘zlashtirdi',
  NeedsWork: 'Qayta ishlasin',
  AdminUnlocked: 'Admin ochdi',
  AdminUnlockRevoked: 'Admin ochishni bekor qildi',
}
