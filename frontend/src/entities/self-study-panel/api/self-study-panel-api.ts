import { http } from '@/shared/api'
import type { DownloadedFile } from '@/shared/api'
import type { MessagePageDto, PagedResult } from '@/shared/types'

import type {
  BulkAssignResultDto,
  ChatAccessLogEntryDto,
  SelfStudyChatFilters,
  SelfStudyChatRowDto,
  SelfStudyCuratorRowDto,
  SelfStudyPanelSummaryDto,
  SelfStudyStudentFilters,
  SelfStudyStudentRowDto,
  SelfStudySubmissionFilters,
  SelfStudySubmissionRowDto,
} from '../model/types'

const BASE = '/api/v1/self-study/panel'

type Opts = { signal?: AbortSignal }

export function fetchSelfStudySummary(options?: Opts): Promise<SelfStudyPanelSummaryDto> {
  return http.get<SelfStudyPanelSummaryDto>(`${BASE}/summary`, { signal: options?.signal })
}

export function fetchSelfStudyStudents(
  filters: SelfStudyStudentFilters,
  options?: Opts,
): Promise<PagedResult<SelfStudyStudentRowDto>> {
  return http.get<PagedResult<SelfStudyStudentRowDto>>(`${BASE}/students`, {
    signal: options?.signal,
    query: {
      search: filters.search,
      state: filters.state,
      curatorId: filters.curatorId,
      unassignedOnly: filters.unassignedOnly === true ? true : undefined,
      page: filters.page,
      pageSize: filters.pageSize,
    },
  })
}

/** Bir nechta o'quvchini bitta kuratorga biriktirish — har o'quvchi alohida hal qilinadi. */
export function bulkAssignSelfStudyCurator(request: {
  studentIds: number[]
  curatorId: number
  backupCuratorId: number | null
}): Promise<BulkAssignResultDto> {
  return http.post<BulkAssignResultDto>(`${BASE}/students/assign`, request)
}

export function fetchSelfStudyCurators(options?: Opts): Promise<SelfStudyCuratorRowDto[]> {
  return http.get<SelfStudyCuratorRowDto[]>(`${BASE}/curators`, { signal: options?.signal })
}

export function fetchSelfStudySubmissions(
  filters: SelfStudySubmissionFilters,
  options?: Opts,
): Promise<PagedResult<SelfStudySubmissionRowDto>> {
  return http.get<PagedResult<SelfStudySubmissionRowDto>>(`${BASE}/submissions`, {
    signal: options?.signal,
    query: {
      status: filters.status,
      curatorId: filters.curatorId,
      search: filters.search,
      page: filters.page,
      pageSize: filters.pageSize,
    },
  })
}

export function fetchSelfStudyChats(
  filters: SelfStudyChatFilters,
  options?: Opts,
): Promise<PagedResult<SelfStudyChatRowDto>> {
  return http.get<PagedResult<SelfStudyChatRowDto>>(`${BASE}/chats`, {
    signal: options?.signal,
    query: {
      curatorId: filters.curatorId,
      waitingOnly: filters.waitingOnly === true ? true : undefined,
      search: filters.search,
      page: filters.page,
      pageSize: filters.pageSize,
    },
  })
}

/**
 * Yozishmani O'QISH (faqat o'qish). Birinchi sahifa (`beforeId` yo'q) server
 * tomonida auditga yoziladi; "o'qildi" bayroqlari o'zgarmaydi.
 */
export function fetchSelfStudyChatMessages(
  studentId: number,
  staffId: number,
  beforeId: number | null,
  options?: Opts,
): Promise<MessagePageDto> {
  return http.get<MessagePageDto>(`${BASE}/chats/${studentId}/${staffId}/messages`, {
    signal: options?.signal,
    query: { beforeId: beforeId ?? undefined, take: 50 },
  })
}

/** Biriktirma baytlari (rasm/ovoz/hujjat). Har ochish auditga yoziladi. */
export function fetchSelfStudyChatAttachment(attachmentId: number, options?: Opts): Promise<DownloadedFile> {
  return http.download(`${BASE}/chats/attachments/${attachmentId}`, `fayl-${attachmentId}`, {
    signal: options?.signal,
    headers: { Accept: '*/*' },
  })
}

export function fetchSelfStudyChatAccessLog(
  studentId: number,
  staffId: number,
  options?: Opts,
): Promise<ChatAccessLogEntryDto[]> {
  return http.get<ChatAccessLogEntryDto[]>(`${BASE}/chats/${studentId}/${staffId}/access-log`, {
    signal: options?.signal,
  })
}
