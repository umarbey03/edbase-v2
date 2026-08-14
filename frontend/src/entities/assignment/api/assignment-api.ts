import { http } from '@/shared/api'
import type { DownloadedFile } from '@/shared/api'
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
  /**
   * WAVE 2 · KURS DARSI bo'yicha filtr — dars drawer'i shu bilan darsning
   * vazifasi bor-yo'qligini aniqlaydi (`CourseLessonDto.hasAssignment` faqat
   * `true`/`false` beradi, vazifaning O'ZINI emas, va alohida
   * "darsning vazifasi" endpointi YO'Q).
   */
  moduleLessonId?: number
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
      ModuleLessonId: params.moduleLessonId,
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

/**
 * `GET /api/v1/submissions/files/{fileId}` — o'quvchi biriktirgan fayl.
 *
 * ★ NEGA BLOB, ya'ni `<img src>` yoki `<a href>` EMAS.
 * Endpoint `Authorization` sarlavhasini TALAB qiladi, brauzer esa rasm,
 * audio va navigatsiya so'rovlarida bu sarlavhani YUBORMAYDI — oddiy havola
 * doim 401 olardi. `http.download` faylni token bilan olib Blob qaytaradi,
 * chaqiruvchi esa undan `URL.createObjectURL()` bilan ko'rinish yasaydi
 * (va `URL.revokeObjectURL()` bilan bo'shatadi — bu MAJBURIY, aks holda
 * uzoq tekshirish seansida o'nlab megabayt xotirada qolib ketadi).
 *
 * `Accept: * / *` ataylab: javob rasm, audio yoki hujjat bo'lishi mumkin, va
 * `http.download` ning odatiy `text/csv` qiymati bu yerda noto'g'ri bo'lardi.
 *
 * Xatolar: 403 — ish begona o'quvchiniki; 404 — fayl yozuvi yo'q;
 * 503 — fayl ombori sozlanmagan yoki javob bermayapti.
 */
export function fetchSubmissionFile(
  fileId: number,
  options?: { signal?: AbortSignal },
): Promise<DownloadedFile> {
  return http.download(`/api/v1/submissions/files/${fileId}`, `fayl-${fileId}`, {
    signal: options?.signal,
    headers: { Accept: '*/*' },
  })
}

/** `POST /api/v1/submissions/{id}/grade` — e'tibor bering: yo'l `assignments` EMAS. */
export function gradeSubmission(
  submissionId: number,
  body: GradeSubmissionRequest,
): Promise<SubmissionDto> {
  return http.post<SubmissionDto>(`/api/v1/submissions/${submissionId}/grade`, body)
}

/* ==========================================================================
   WAVE 2 · VAZIFA SHARTI BIRIKTIRMALARI (rasm / audio / hujjat)

   `imageKey` (BITTA rasm, ombor kaliti) o'rniga keladi: shart bir nechta
   faylga ega bo'lishi mumkin va kalit UI'ga umuman chiqmaydi.

   ★ YUKLASHNING O'ZI bu yerda YO'Q — progress va bekor qilish uchun
   `XMLHttpRequest` kerak (`features/lesson-media/lib/upload-with-progress.ts`).
   Bu modul faqat YO'L va `FormData` shaklini beradi.
   ========================================================================== */

/** `POST /api/v1/assignments/{id}/attachments` yo'li (`multipart/form-data`). */
export function assignmentAttachmentUploadPath(assignmentId: number): string {
  return `${BASE}/${assignmentId}/attachments`
}

/**
 * Biriktirma uchun `FormData`.
 *
 * 🔴 MAYDON NOMI AYNAN `file` (server imzosi: `IFormFile file`). `kind`
 * YUBORILMAYDI — server turni fayl MAZMUNIDAN aniqlaydi.
 */
export function buildAssignmentAttachmentForm(
  file: File,
  durationSec: number | null = null,
): FormData {
  const form = new FormData()
  form.append('file', file)
  if (durationSec != null) form.append('durationSec', String(durationSec))
  return form
}

/** `DELETE /api/v1/assignments/attachments/{id}` — 204, qaytarib bo'lmaydi. */
export function deleteAssignmentAttachment(attachmentId: number): Promise<void> {
  return http.delete<void>(`${BASE}/attachments/${attachmentId}`)
}

/**
 * `GET /api/v1/assignments/attachments/{id}` — Blob.
 *
 * Naqsh `fetchSubmissionFile` bilan AYNI: endpoint `Authorization` talab
 * qiladi, brauzer esa `<img src>`/`<audio src>` da uni yubormaydi.
 */
export function fetchAssignmentAttachmentFile(
  attachmentId: number,
  options?: { signal?: AbortSignal },
): Promise<DownloadedFile> {
  return http.download(`${BASE}/attachments/${attachmentId}`, `biriktirma-${attachmentId}`, {
    signal: options?.signal,
    headers: { Accept: '*/*' },
  })
}

/* ==========================================================================
   R37 · USTOZNING TEKSHIRUV FAYLLARI (rasm / ovoz / PDF)

   Talab: *"student uchun ham teacher uchun ham vazifada fayl va rasm
   jo'natish mumkin bo'lsin"*. O'quvchi tomoni allaqachon ishlaydi
   (`submitAssignment`), bu esa TESKARI yo'nalish.

   🔴 `gradeSubmission` TEGILMADI va bu ONGLI: u JSON qabul qiladi va uni
   frontend ham, backend integratsion testlari ham shunday chaqiradi.
   `multipart` ga o'tkazilsa HAR BIR mavjud chaqiruv 415 olardi. Shuning
   uchun fayl ALOHIDA endpoint orqali ketadi va bahodan MUSTAQIL — uni
   baho qo'yishdan oldin ham, keyin ham biriktirish mumkin.
   ========================================================================== */

/** `POST /api/v1/submissions/{id}/feedback-files` yo'li (`multipart/form-data`). */
export function submissionFeedbackUploadPath(submissionId: number): string {
  return `/api/v1/submissions/${submissionId}/feedback-files`
}

/**
 * Tekshiruv fayli uchun `FormData`.
 *
 * 🔴 MAYDON NOMI AYNAN `file` (server imzosi: `IFormFile file`). Tur
 * YUBORILMAYDI — server uni fayl MAZMUNIDAN aniqlaydi.
 */
export function buildSubmissionFeedbackForm(file: File): FormData {
  const form = new FormData()
  form.append('file', file)
  return form
}

/** `DELETE /api/v1/submissions/feedback-files/{id}` — 204, qaytarib bo'lmaydi. */
export function deleteSubmissionFeedbackFile(fileId: number): Promise<void> {
  return http.delete<void>(`/api/v1/submissions/feedback-files/${fileId}`)
}

/**
 * `GET /api/v1/submissions/feedback-files/{id}` — Blob.
 *
 * ★ O'QUVCHI HAM OLADI (ruxsat — javobni KO'RISH huquqi): R37 ning mohiyati
 * aynan shu, ustoz qo'ygan tuzatish o'quvchiga yetib borishi kerak.
 */
export function fetchSubmissionFeedbackFile(
  fileId: number,
  options?: { signal?: AbortSignal },
): Promise<DownloadedFile> {
  return http.download(`/api/v1/submissions/feedback-files/${fileId}`, `fayl-${fileId}`, {
    signal: options?.signal,
    headers: { Accept: '*/*' },
  })
}
