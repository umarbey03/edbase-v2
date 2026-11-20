import type { SubmissionDto } from '@/shared/types'

/**
 * KURATOR ISH STOLI (2026-11-20) — server `CuratorWorkspaceService.cs`
 * bilan AYNI shakllar.
 */

export interface AwaitingLessonDto {
  lessonId: number
  index: number
  lessonName: string
}

export interface WorkspaceStudentDto {
  studentId: number
  fullName: string
  isSelfStudy: boolean
  groupName: string | null
  /** Xodim shu o'quvchining suhbatdoshimi (yozishmaga yoza oladimi). */
  canChat: boolean
  /** Mustaqil o'quvchining zaxira kuratori — yozishmani faqat o'qiydi. */
  isBackup: boolean
  unreadCount: number
  /** Oxirgi javobdan keyin dars sahifasidan yozilgan savollar. */
  unansweredQuestions: number
  pendingSubmissions: number
  awaitingDecision: AwaitingLessonDto | null
  waitingSince: string | null
  lastActivityAt: string | null
  needsAction: boolean
}

export interface WorkspaceStudentHeaderDto {
  studentId: number
  fullName: string
  phone: string | null
  isSelfStudy: boolean
  groupName: string | null
  subscriptionEndsAt: string | null
  subscriptionActive: boolean
  canChat: boolean
  isBackup: boolean
  primaryCuratorId: number | null
  primaryCuratorName: string | null
  masteredLessons: number
  totalLessons: number
  awaitingDecision: AwaitingLessonDto | null
}

export interface StaffStudentSubmissionDto {
  submission: SubmissionDto
  assignmentTitle: string
  maxScore: number
  lessonId: number | null
  lessonName: string | null
  /** Shu xodim baholay oladimi (baholash qoidasi bilan AYNI). */
  canGrade: boolean
}

export interface WorkspaceTestResultDto {
  testId: number
  title: string
  lessonName: string | null
  score: number | null
  maxScore: number | null
  submittedAt: string
}

/** Chap ro'yxat filtri. */
export type WorkspaceFilter = 'all' | 'chat' | 'submissions' | 'decision'

export const WORKSPACE_FILTERS: { key: WorkspaceFilter, label: string }[] = [
  { key: 'all', label: 'Hammasi' },
  { key: 'chat', label: 'Javob' },
  { key: 'submissions', label: 'Vazifa' },
  { key: 'decision', label: 'Dars' },
]

export function matchesFilter(row: WorkspaceStudentDto, filter: WorkspaceFilter): boolean {
  switch (filter) {
    case 'chat':
      return row.unreadCount > 0 || row.unansweredQuestions > 0 || (row.canChat && row.waitingSince !== null && row.pendingSubmissions === 0)
    case 'submissions':
      return row.pendingSubmissions > 0
    case 'decision':
      return row.awaitingDecision !== null
    default:
      return true
  }
}

/** "2 daq", "3 soat", "kecha" — ro'yxat qatoridagi qisqa vaqt. */
export function shortAgo(iso: string | null, now = Date.now()): string {
  if (iso === null) return ''
  const minutes = Math.max(0, Math.floor((now - new Date(iso).getTime()) / 60_000))
  if (minutes < 1) return 'hozir'
  if (minutes < 60) return `${minutes} daq`
  const hours = Math.floor(minutes / 60)
  if (hours < 24) return `${hours} soat`
  const days = Math.floor(hours / 24)
  return days === 1 ? 'kecha' : `${days} kun`
}
