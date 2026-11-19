/**
 * MUSTAQIL PANELI (2026-11-19) — server `SelfStudyPanelDtos.cs` bilan AYNI shakllar.
 * Enumlar SATR sifatida keladi (`JsonStringEnumConverter`).
 */

export type SelfStudySubscriptionStateName = 'Active' | 'Upcoming' | 'Expired'

export type SubmissionStatusName = 'Submitted' | 'Graded'

export type DirectMessageAccessKindName = 'Thread' | 'Attachment'

export const SUBSCRIPTION_STATE_LABELS: Record<SelfStudySubscriptionStateName, string> = {
  Active: 'Faol',
  Upcoming: 'Boshlanmagan',
  Expired: 'Tugagan',
}

export const SUBSCRIPTION_STATE_TONES: Record<SelfStudySubscriptionStateName, 'success' | 'accent' | 'danger'> = {
  Active: 'success',
  Upcoming: 'accent',
  Expired: 'danger',
}

export interface SelfStudyPanelSummaryDto {
  students: number
  activeSubscriptions: number
  /** Faol obunasi bor, lekin kuratori yo'qlar — darhol ish kerak. */
  unassigned: number
  pendingSubmissions: number
  waitingChats: number
}

export interface SelfStudyStudentRowDto {
  studentId: number
  fullName: string
  phone: string | null
  isActive: boolean
  planName: string | null
  state: SelfStudySubscriptionStateName
  subscriptionEndsAt: string | null
  curatorId: number | null
  curatorName: string | null
  backupCuratorId: number | null
  backupCuratorName: string | null
  curatorAssignedAt: string | null
  masteredLessons: number
  totalLessons: number
  pendingSubmissions: number
  lastMessageAt: string | null
  /** O'quvchi yozgan va javob olmagan eng eski xabar vaqti. */
  waitingSince: string | null
}

export interface SelfStudyStudentFilters {
  search?: string
  state?: SelfStudySubscriptionStateName
  curatorId?: number
  unassignedOnly?: boolean
  page: number
  pageSize: number
}

export interface BulkAssignFailureDto {
  studentId: number
  fullName: string | null
  error: string
}

export interface BulkAssignResultDto {
  assigned: number
  failed: BulkAssignFailureDto[]
}

export interface SelfStudyCuratorRowDto {
  id: number
  fullName: string
  role: string
  primaryStudents: number
  backupStudents: number
  pendingSubmissions: number
  waitingChats: number
  lastReplyAt: string | null
}

export interface SelfStudySubmissionRowDto {
  submissionId: number
  assignmentId: number
  assignmentTitle: string
  lessonName: string | null
  studentId: number
  studentName: string
  curatorName: string | null
  status: SubmissionStatusName
  score: number | null
  maxScore: number
  submittedAt: string
  gradedAt: string | null
  gradedByName: string | null
  isLate: boolean
  attemptNumber: number
}

export interface SelfStudySubmissionFilters {
  status?: SubmissionStatusName
  curatorId?: number
  search?: string
  page: number
  pageSize: number
}

export interface SelfStudyChatRowDto {
  studentId: number
  studentName: string
  staffId: number
  staffName: string
  /** Xodim hozir ham shu o'quvchining asosiy kuratorimi. */
  isCurrentCurator: boolean
  messageCount: number
  lastMessageAt: string
  lastMessagePreview: string
  lastFromStudent: boolean
  unreadByStaff: number
  waitingSince: string | null
}

export interface SelfStudyChatFilters {
  curatorId?: number
  waitingOnly?: boolean
  search?: string
  page: number
  pageSize: number
}

export interface ChatAccessLogEntryDto {
  viewerName: string
  kind: DirectMessageAccessKindName
  at: string
}
