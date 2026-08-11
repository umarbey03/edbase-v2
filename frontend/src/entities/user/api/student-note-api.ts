import { http } from '@/shared/api'
import type {
  CreateStudentNoteRequest,
  StudentNoteDto,
  UpdateStudentNoteRequest,
} from '@/shared/types'

const BASE = '/api/v1/users'

/**
 * Izoh matnining MAKSIMAL uzunligi — server shartnomasi
 * (`StudentNote.MaxBodyLength = 2000`).
 *
 * ★ NEGA KLIENTDA HAM TEKSHIRILADI: uzun matnni Domain rad etadi va
 * `DomainException` middleware'da **409** ga aylanadi — ya'ni xato "holat
 * ziddiyati" bo'lib chiqib, maydon ostida ko'rsatilmasdi. Chegara shu yerda
 * bo'lganda xodim yozayotib ko'radi.
 */
export const NOTE_BODY_MAX = 2000

/**
 * Ichki izohlar CRUD'i (`/users/{id}/notes`).
 *
 * 🔴 RO'YXAT (`GET`) ATAYLAB YO'Q: drawer izohlarni PROFIL AGREGATIDAN oladi
 * (`UserProfileDto.notes`), ya'ni ochilishda ikkinchi so'rov yuborilmaydi.
 * Mutatsiyadan keyin agregat qayta o'qiladi — bitta manba, bitta haqiqat.
 * Endpoint serverda bor va alohida "izohlar sahifasi" kerak bo'lsa qo'shiladi.
 *
 * RUXSAT (serverda): `Student` -> **403** (bu xodimlarning o'zaro yozuvi) ·
 * ustoz/kurator faqat O'Z izohini tahrirlaydi/o'chiradi (begona izoh -> 403) ·
 * `Academic`/`Admin` hammasini.
 */
export function createStudentNote(
  studentId: number,
  body: CreateStudentNoteRequest,
): Promise<StudentNoteDto> {
  return http.post<StudentNoteDto>(`${BASE}/${studentId}/notes`, body)
}

/**
 * `PUT /users/{id}/notes/{noteId}` — FAQAT matn o'zgaradi.
 *
 * ★ "PUT = to'liq almashtirish" tuzog'i (6-bo'lim, 1-tuzoq) bu yerda xavf
 * TUG'DIRMAYDI: so'rov tanasida yagona maydon bor va guruh konteksti,
 * muallif, sanalar serverda o'zgarmaydi.
 */
export function updateStudentNote(
  studentId: number,
  noteId: number,
  body: UpdateStudentNoteRequest,
): Promise<StudentNoteDto> {
  return http.put<StudentNoteDto>(`${BASE}/${studentId}/notes/${noteId}`, body)
}

/** `DELETE /users/{id}/notes/{noteId}` -> 204. QATTIQ o'chirish (tiklanmaydi). */
export function deleteStudentNote(studentId: number, noteId: number): Promise<void> {
  return http.delete<void>(`${BASE}/${studentId}/notes/${noteId}`)
}
