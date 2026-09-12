import { http } from '@/shared/api'
import { apiUrl } from '@/shared/config/env'
import type { BookDto, BookUploadFields, MediaAccessTicketDto, UpdateBookRequest } from '@/shared/types'

/**
 * KUTUBXONA (2026-09-09) — `/api/v1/books`.
 *
 * Fayl oqimi dars fayllari bilan AYNI naqshda: yuklash `multipart`
 * (`uploadWithProgress`), o'qish — `?ticket=` bilan (brauzerdagi PDF
 * o'quvchi `Authorization` sarlavhasini yubora olmaydi).
 */
const BASE = '/api/v1/books'

export function fetchBooks(
  params: { includeInactive?: boolean } = {},
  options?: { signal?: AbortSignal },
): Promise<BookDto[]> {
  return http.get<BookDto[]>(BASE, {
    query: { includeInactive: params.includeInactive === true ? true : undefined },
    signal: options?.signal,
  })
}

/** `POST /api/v1/books` yo'li — `uploadWithProgress` uchun. */
export const BOOK_UPLOAD_PATH = BASE

export function buildBookForm(file: File, fields: BookUploadFields = {}): FormData {
  const form = new FormData()
  form.append('file', file)

  const title = (fields.title ?? '').trim()
  if (title.length > 0) form.append('title', title)
  if (fields.pageCount != null) form.append('pageCount', String(fields.pageCount))

  return form
}

export function updateBook(id: number, body: UpdateBookRequest): Promise<BookDto> {
  return http.put<BookDto>(`${BASE}/${id}`, body)
}

export function deleteBook(id: number): Promise<void> {
  return http.delete<void>(`${BASE}/${id}`)
}

/** Qisqa muddatli (15 daqiqa) chipta — PDF o'quvchi uchun. */
export function fetchBookTicket(id: number): Promise<MediaAccessTicketDto> {
  return http.get<MediaAccessTicketDto>(`${BASE}/${id}/ticket`)
}

export function bookFileUrl(id: number, token: string): string {
  return `${apiUrl(`${BASE}/${id}/file`)}?ticket=${encodeURIComponent(token)}`
}

/**
 * Chipta olib, to'liq manzil qaytaradi — PDF o'quvchi shu manzilni
 * BIR MARTA yuklaydi (`disableRange`), ya'ni chipta muddati o'qish
 * davomida ahamiyatsiz.
 */
export async function resolveBookUrl(id: number): Promise<string> {
  const ticket = await fetchBookTicket(id)
  return bookFileUrl(id, ticket.token)
}
