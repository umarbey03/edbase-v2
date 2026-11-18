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
