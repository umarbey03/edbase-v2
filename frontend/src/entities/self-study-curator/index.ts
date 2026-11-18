export {
  assignStudentCurator,
  fetchCuratorCandidates,
  fetchMyCurator,
  fetchMySelfStudyStudents,
  fetchStudentCurator,
  fetchUnassignedSelfStudyStudents,
  unassignStudentCurator,
} from './api/self-study-curator-api'
export { CURATOR_ROLE_LABELS } from './model/types'
export type {
  CuratorCandidateDto,
  MyCuratorDto,
  MySelfStudyStudentDto,
  SelfStudyCuratorDto,
  StudentCuratorInfoDto,
  UnassignedSelfStudyStudentDto,
} from './model/types'

export const SELF_STUDY_CURATOR_KEYS = {
  all: ['self-study-curators'] as const,
  student: (studentId: number) => ['self-study-curators', 'student', studentId] as const,
  candidates: ['self-study-curators', 'candidates'] as const,
  unassigned: ['self-study-curators', 'unassigned'] as const,
  mine: ['self-study-curators', 'my-students'] as const,
  myCurator: ['self-study-curators', 'mine'] as const,
}
