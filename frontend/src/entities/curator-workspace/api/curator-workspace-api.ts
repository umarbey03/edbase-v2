import { http } from '@/shared/api'
import type { MessagePageDto } from '@/shared/types'

import type {
  StaffStudentSubmissionDto,
  WorkspaceStudentDto,
  WorkspaceStudentHeaderDto,
  WorkspaceTestResultDto,
} from '../model/types'

const BASE = '/api/v1/curator/workspace'

type Opts = { signal?: AbortSignal }

export function fetchWorkspaceStudents(options?: Opts): Promise<WorkspaceStudentDto[]> {
  return http.get<WorkspaceStudentDto[]>(`${BASE}/students`, { signal: options?.signal })
}

export function fetchWorkspaceStudent(studentId: number, options?: Opts): Promise<WorkspaceStudentHeaderDto> {
  return http.get<WorkspaceStudentHeaderDto>(`${BASE}/students/${studentId}`, { signal: options?.signal })
}

export function fetchWorkspaceSubmissions(studentId: number, options?: Opts): Promise<StaffStudentSubmissionDto[]> {
  return http.get<StaffStudentSubmissionDto[]>(`${BASE}/students/${studentId}/submissions`, { signal: options?.signal })
}

export function fetchWorkspaceTests(studentId: number, options?: Opts): Promise<WorkspaceTestResultDto[]> {
  return http.get<WorkspaceTestResultDto[]>(`${BASE}/students/${studentId}/tests`, { signal: options?.signal })
}

/** Zaxira kurator: asosiy kurator bilan yozishma — faqat o'qish. */
export function fetchWorkspaceBackupThread(studentId: number, options?: Opts): Promise<MessagePageDto> {
  return http.get<MessagePageDto>(`${BASE}/students/${studentId}/backup-thread`, {
    signal: options?.signal,
    query: { take: 100 },
  })
}
