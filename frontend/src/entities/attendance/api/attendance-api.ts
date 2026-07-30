import { http } from '@/shared/api'

import type { AttendanceRowDto, SessionAttendanceDto, UpdateAttendanceRequest } from '../model/types'

const BASE = '/api/v1/live-sessions'

/**
 * `GET /api/v1/live-sessions/{id}/attendance` — bitta darsning davomat varag'i.
 *
 * Qatorlar: guruhning FAOL a'zolari + shu darsda yozuvi bor har qanday
 * o'quvchi (arxivlangani ham). Tartib serverda — ism, so'ng `studentId`;
 * frontend qayta saralamaydi.
 *
 * Ruxsat SERVERDA: ustoz/kurator faqat o'z guruhini (bog'langan kurator
 * guruhi orqali ham), darsning `hostId` doim ko'radi, o'quvchi — 403.
 */
export function fetchSessionAttendance(
  sessionId: number,
  options?: { signal?: AbortSignal },
): Promise<SessionAttendanceDto> {
  return http.get<SessionAttendanceDto>(`${BASE}/${sessionId}/attendance`, {
    signal: options?.signal,
  })
}

/**
 * `PUT /api/v1/live-sessions/{id}/attendance/{studentId}` — qo'lda tuzatish.
 *
 * ★ TO'LIQ ALMASHTIRISH: chaqiruvchi mavjud `reason` ni yuklab, uni
 * QAYTARIB yuborishi shart — aks holda sabab jimgina o'chadi.
 *
 * Qator hali bo'lmasa YARATILADI: xonaga umuman kirmagan o'quvchini
 * "kelgan" deb belgilash — asosiy stsenariy. `Scheduled` darsni ham
 * oldindan belgilash mumkin.
 *
 * Xatolar: 400 — `status` yo'q/noma'lum yoki `reason` 300 belgidan uzun
 * (sabab `problem.errors` da); 403 — begona guruh; 404 — dars yo'q yoki
 * o'quvchi bu guruhda emas; 409 — dars BEKOR QILINGAN yoki qator bir
 * vaqtda ikki joydan o'zgardi.
 */
export function updateAttendance(
  sessionId: number,
  studentId: number,
  body: UpdateAttendanceRequest,
): Promise<AttendanceRowDto> {
  return http.put<AttendanceRowDto>(`${BASE}/${sessionId}/attendance/${studentId}`, body)
}
