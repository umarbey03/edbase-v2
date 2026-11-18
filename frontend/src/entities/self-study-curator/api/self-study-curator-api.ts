import { http } from '@/shared/api'
import type { MasteryStatusName } from '@/shared/types'

import type {
  CuratorCandidateDto,
  MasteryEventDto,
  MyLessonMasteryDto,
  StudentLessonBoardDto,
  MyCuratorDto,
  MySelfStudyStudentDto,
  StudentCuratorInfoDto,
  UnassignedSelfStudyStudentDto,
} from '../model/types'

const BASE = '/api/v1/self-study/curators'

type Opts = { signal?: AbortSignal }

export function fetchCuratorCandidates(options?: Opts): Promise<CuratorCandidateDto[]> {
  return http.get<CuratorCandidateDto[]>(`${BASE}/candidates`, { signal: options?.signal })
}

export function fetchUnassignedSelfStudyStudents(options?: Opts): Promise<UnassignedSelfStudyStudentDto[]> {
  return http.get<UnassignedSelfStudyStudentDto[]>(`${BASE}/unassigned`, { signal: options?.signal })
}

export function fetchStudentCurator(studentId: number, options?: Opts): Promise<StudentCuratorInfoDto> {
  return http.get<StudentCuratorInfoDto>(`${BASE}/students/${studentId}`, { signal: options?.signal })
}

export function assignStudentCurator(
  studentId: number,
  request: { curatorId: number, backupCuratorId: number | null },
): Promise<StudentCuratorInfoDto> {
  return http.put<StudentCuratorInfoDto>(`${BASE}/students/${studentId}`, request)
}

export function unassignStudentCurator(studentId: number): Promise<StudentCuratorInfoDto> {
  return http.delete<StudentCuratorInfoDto>(`${BASE}/students/${studentId}`)
}

export function fetchMySelfStudyStudents(options?: Opts): Promise<MySelfStudyStudentDto[]> {
  return http.get<MySelfStudyStudentDto[]>(`${BASE}/my-students`, { signal: options?.signal })
}

/** O'quvchi: o'z kuratori; kurator yo'q bo'lsa server 204 qaytaradi → `null`. */
export async function fetchMyCurator(options?: Opts): Promise<MyCuratorDto | null> {
  const result = await http.get<MyCuratorDto | undefined>(`${BASE}/mine`, { signal: options?.signal })
  return result ?? null
}

/* ───────────── «O'zlashtirdi» qoidasi (2026-11-18) ───────────── */

const STUDENTS = '/api/v1/self-study/students'

export function fetchStudentLessonBoard(studentId: number, options?: Opts): Promise<StudentLessonBoardDto> {
  return http.get<StudentLessonBoardDto>(`${STUDENTS}/${studentId}/lessons`, { signal: options?.signal })
}

export function decideLessonMastery(
  studentId: number,
  lessonId: number,
  request: { status: MasteryStatusName, note: string | null },
): Promise<StudentLessonBoardDto> {
  return http.put<StudentLessonBoardDto>(`${STUDENTS}/${studentId}/lessons/${lessonId}/mastery`, request)
}

export function adminUnlockLesson(
  studentId: number,
  lessonId: number,
  request: { unlocked: boolean, reason: string | null },
): Promise<StudentLessonBoardDto> {
  return http.put<StudentLessonBoardDto>(`${STUDENTS}/${studentId}/lessons/${lessonId}/unlock`, request)
}

export function fetchMasteryEvents(studentId: number, options?: Opts): Promise<MasteryEventDto[]> {
  return http.get<MasteryEventDto[]>(`${STUDENTS}/${studentId}/mastery-events`, { signal: options?.signal })
}

/** O'quvchi: o'z darslari bo'yicha kurator qarorlari. */
export function fetchMyLessonMastery(options?: Opts): Promise<MyLessonMasteryDto[]> {
  return http.get<MyLessonMasteryDto[]>('/api/v1/self-study/me/mastery', { signal: options?.signal })
}
