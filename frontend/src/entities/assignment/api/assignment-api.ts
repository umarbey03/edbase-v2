import { http } from '@/shared/api'
import type {
  AssignmentDto,
  CreateAssignmentRequest,
  GradeSubmissionRequest,
  PagedResult,
  ReopenSubmissionRequest,
  StudentAssignmentDto,
  StudentSubmissionDto,
  SubmissionDto,
  UpdateAssignmentRequest,
} from '@/shared/types'

const BASE = '/api/v1/assignments'

/** `GET /api/v1/assignments/mine` — faqat o'quvchi uchun. */
export function fetchMyAssignments(options?: {
  signal?: AbortSignal
}): Promise<StudentAssignmentDto[]> {
  return http.get<StudentAssignmentDto[]>(`${BASE}/mine`, { signal: options?.signal })
}

export interface AssignmentListParams {
  groupId?: number
  page?: number
  pageSize?: number
}

/** `GET /api/v1/assignments` — ustoz/kurator/o'quv bo'limi ko'radi. */
export function fetchAssignments(
  params: AssignmentListParams = {},
  options?: { signal?: AbortSignal },
): Promise<PagedResult<AssignmentDto>> {
  return http.get<PagedResult<AssignmentDto>>(BASE, {
    query: {
      GroupId: params.groupId,
      Page: params.page,
      PageSize: params.pageSize,
    },
    signal: options?.signal,
  })
}

/**
 * `POST /api/v1/assignments` — yangi vazifa.
 *
 * 403: ustoz KURS vazifasini (dars nishoni) bera olmaydi va begona guruhga
 * ham yoza olmaydi — qoida `AssignmentService.EnsureCanCreateAsync` da.
 * 409: Domain tekshiruvi (sarlavha bo'sh, ball <= 0, format tanlanmagan,
 * nishon YOKI guruh YOKI dars bo'lmasa).
 */
export function createAssignment(body: CreateAssignmentRequest): Promise<AssignmentDto> {
  return http.post<AssignmentDto>(BASE, body)
}

/**
 * `PUT /api/v1/assignments/{id}` — ★ TO'LIQ ALMASHTIRISH.
 *
 * Yuborilmagan maydon serverda `null` bo'lib yoziladi (`UpdateAssignmentRequest`
 * izohiga qarang), shuning uchun chaqiruvchi mavjud qiymatlarni yuklab,
 * HAMMASINI qaytarib yuborishi shart. Nishon (guruh/dars) bu yerda umuman
 * yo'q — server uni o'zgartirmaydi.
 */
export function updateAssignment(
  id: number,
  body: UpdateAssignmentRequest,
): Promise<AssignmentDto> {
  return http.put<AssignmentDto>(`${BASE}/${id}`, body)
}

/** O'quvchi javobi: matn va/yoki fayllar (kamida bittasi bo'lishi shart). */
export interface SubmitAssignmentInput {
  text: string | null
  files: readonly File[]
}

/**
 * `POST /api/v1/assignments/{id}/submit` — `multipart/form-data`.
 *
 * Maydon nomlari AYNAN server imzosidan olingan:
 * `[FromForm] string? text` va `[FromForm] IFormFileCollection? files`.
 * Bir nechta fayl BITTA `files` nomi bilan qo'shiladi — ASP.NET kolleksiyani
 * shunday yig'adi (`files[0]` shakli EMAS).
 *
 * `Content-Type` bu yerda ham, `http` qatlamida ham QO'LDA qo'yilmaydi:
 * boundary'ni brauzer o'zi hosil qiladi (`shared/api/http.ts` izohi).
 *
 * Xatolar: 400 — fayl turi/hajmi yoki bo'sh javob (`problem.errors`);
 * 409 — "javob allaqachon yuborilgan" yoki format ruxsat etilmagan;
 * 403 — dars qulflangan/vazifa begona; 503 — fayl ombori sozlanmagan.
 */
export function submitAssignment(
  assignmentId: number,
  input: SubmitAssignmentInput,
): Promise<StudentSubmissionDto> {
  const form = new FormData()

  // Bo'sh matn UMUMAN qo'shilmaydi: server `IsNullOrWhiteSpace` bilan
  // tekshiradi, ya'ni bo'sh satr yuborish "matnli javob bor" degani emas.
  if (input.text !== null && input.text.length > 0) form.append('text', input.text)

  for (const file of input.files) form.append('files', file)

  return http.post<StudentSubmissionDto>(`${BASE}/${assignmentId}/submit`, form)
}

/**
 * `POST /api/v1/submissions/{id}/reopen` — qayta topshirishga ruxsat.
 *
 * Ruxsat BIR MARTALIK: o'quvchi yangi javob yuborgach Domain uni o'zi yopadi.
 * Baho tozalanmaydi — u yangi javob kelganda bekor bo'ladi.
 */
export function reopenSubmission(
  submissionId: number,
  body: ReopenSubmissionRequest = {},
): Promise<SubmissionDto> {
  return http.post<SubmissionDto>(`/api/v1/submissions/${submissionId}/reopen`, body)
}

/** `GET /api/v1/assignments/{id}/submissions` — baholash navbati. */
export function fetchSubmissions(
  assignmentId: number,
  options?: { signal?: AbortSignal },
): Promise<SubmissionDto[]> {
  return http.get<SubmissionDto[]>(`${BASE}/${assignmentId}/submissions`, {
    signal: options?.signal,
  })
}

/** `POST /api/v1/submissions/{id}/grade` — e'tibor bering: yo'l `assignments` EMAS. */
export function gradeSubmission(
  submissionId: number,
  body: GradeSubmissionRequest,
): Promise<SubmissionDto> {
  return http.post<SubmissionDto>(`/api/v1/submissions/${submissionId}/grade`, body)
}
