export {
  fetchWorkspaceBackupThread,
  fetchWorkspaceStudent,
  fetchWorkspaceStudents,
  fetchWorkspaceSubmissions,
  fetchWorkspaceTests,
} from './api/curator-workspace-api'
export { WORKSPACE_FILTERS, matchesFilter, shortAgo } from './model/types'
export type {
  AwaitingLessonDto,
  StaffStudentSubmissionDto,
  WorkspaceFilter,
  WorkspaceStudentDto,
  WorkspaceStudentHeaderDto,
  WorkspaceTestResultDto,
} from './model/types'

export const CURATOR_WORKSPACE_KEYS = {
  all: ['workspace'] as const,
  students: ['workspace', 'students'] as const,
  student: (id: number) => ['workspace', 'student', id] as const,
  submissions: (id: number) => ['workspace', 'submissions', id] as const,
  tests: (id: number) => ['workspace', 'tests', id] as const,
}
