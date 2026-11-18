export {
  adminUnlockLesson,
  assignStudentCurator,
  decideLessonMastery,
  fetchMasteryEvents,
  fetchMyLessonMastery,
  fetchStudentLessonBoard,
  fetchCuratorCandidates,
  fetchMyCurator,
  fetchMySelfStudyStudents,
  fetchStudentCurator,
  fetchUnassignedSelfStudyStudents,
  unassignStudentCurator,
} from './api/self-study-curator-api'
export { CURATOR_ROLE_LABELS, MASTERY_EVENT_LABELS } from './model/types'
export type {
  BoardAssignmentDto,
  CuratorCandidateDto,
  LessonBoardRowDto,
  MasteryEventDto,
  MasteryEventKindName,
  MyLessonMasteryDto,
  StudentLessonBoardDto,
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
  board: (studentId: number) => ['self-study', 'board', studentId] as const,
  events: (studentId: number) => ['self-study', 'events', studentId] as const,
  myMastery: ['self-study', 'my-mastery'] as const,
}
