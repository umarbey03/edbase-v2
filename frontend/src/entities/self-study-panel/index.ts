export {
  addCuratorToPool,
  fetchCuratorPool,
  removeCuratorFromPool,
  runCuratorPool,
  updateCuratorPoolMember,
  bulkAssignSelfStudyCurator,
  fetchSelfStudyChatAccessLog,
  fetchSelfStudyChatAttachment,
  fetchSelfStudyChatMessages,
  fetchSelfStudyChats,
  fetchSelfStudyCurators,
  fetchSelfStudyStudents,
  fetchSelfStudySubmissions,
  fetchSelfStudySummary,
} from './api/self-study-panel-api'
export { DEFAULT_CURATOR_CAPACITY, SUBSCRIPTION_STATE_LABELS, SUBSCRIPTION_STATE_TONES } from './model/types'
export type {
  AutoAssignResultDto,
  CuratorPoolDto,
  CuratorPoolMemberDto,
  BulkAssignFailureDto,
  BulkAssignResultDto,
  ChatAccessLogEntryDto,
  DirectMessageAccessKindName,
  SelfStudyChatFilters,
  SelfStudyChatRowDto,
  SelfStudyCuratorRowDto,
  SelfStudyPanelSummaryDto,
  SelfStudyStudentFilters,
  SelfStudyStudentRowDto,
  SelfStudySubmissionFilters,
  SelfStudySubmissionRowDto,
  SelfStudySubscriptionStateName,
  SubmissionStatusName,
} from './model/types'

export const SELF_STUDY_PANEL_KEYS = {
  all: ['self-study-panel'] as const,
  summary: ['self-study-panel', 'summary'] as const,
  students: (filters: object) => ['self-study-panel', 'students', filters] as const,
  curators: ['self-study-panel', 'curators'] as const,
  pool: ['self-study-panel', 'pool'] as const,
  submissions: (filters: object) => ['self-study-panel', 'submissions', filters] as const,
  chats: (filters: object) => ['self-study-panel', 'chats', filters] as const,
  chat: (studentId: number, staffId: number) => ['self-study-panel', 'chat', studentId, staffId] as const,
  chatAccess: (studentId: number, staffId: number) =>
    ['self-study-panel', 'chat-access', studentId, staffId] as const,
}
