import { http } from '@/shared/api'

import type {
  LessonGradeRowDto,
  MyLessonGradesDto,
  SessionLessonGradesDto,
  UpsertLessonGradeRequest,
} from '../model/types'

const BASE = '/api/v1/live-sessions'

/**
 * ★ IKKINCHI BAZA ATAYLAB SHU FAYLDA: o'quvchi yo'li `/progress` ostida
 * (u "mening progressim" oilasiga tegishli), lekin MA'LUMOT ayni shu
 * entity'niki. Uni `entities/progress` ga ko'chirish tiplarni ikki
 * entity o'rtasida bo'lishishga majbur qilardi.
 */
const MY_BASE = '/api/v1/progress'

/**
 * `GET /api/v1/live-sessions/{id}/grades` — bitta darsning baho varag'i.
 *
 * Qatorlar: guruhning FAOL a'zolari + shu darsda bahosi bor har qanday
 * o'quvchi (arxivlangani ham). Tartib serverda — ism, so'ng `studentId`;
 * frontend qayta saralamaydi (davomat varag'i bilan AYNI tartib, ya'ni
 * ikki tabdagi qatorlar bir xil turadi).
 *
 * Ruxsat SERVERDA: ustoz/kurator faqat o'z guruhini (bog'langan kurator
 * guruhi orqali ham), darsning `hostId` doim ko'radi, o'quvchi — 403.
 */
export function fetchSessionGrades(
  sessionId: number,
  options?: { signal?: AbortSignal },
): Promise<SessionLessonGradesDto> {
  return http.get<SessionLessonGradesDto>(`${BASE}/${sessionId}/grades`, {
    signal: options?.signal,
  })
}

/**
 * `GET /api/v1/progress/lesson-grades` — O'QUVCHINING O'Z dars baholari.
 *
 * 🔴 `studentId` PARAMETRI YO'Q va bo'lmaydi ham: server uni tokendan
 * oladi. Boshqa o'quvchining bahosini so'rash uchun so'rov SHAKLI mavjud
 * emas — bu maxfiylikni "filtrni unutmaslik" ga bog'lab qo'ymaydi.
 *
 * `from`/`to` — MAHALLIY sana (`YYYY-MM-DD`), ikkalasi ham KIRADI.
 * Reyting ekrani ularni tanlangan OYdan yasaydi: varaqadagi ro'yxat
 * jadvaldagi `lessonPercent` bilan AYNI oyni ko'rsatishi shart.
 *
 * Xatolar: 400 — `from > to`; 403 — begona `groupId` yoki profil faol emas.
 */
export function fetchMyLessonGrades(
  params: { groupId?: number; from?: string; to?: string } = {},
  options?: { signal?: AbortSignal },
): Promise<MyLessonGradesDto> {
  return http.get<MyLessonGradesDto>(`${MY_BASE}/lesson-grades`, {
    query: { groupId: params.groupId, from: params.from, to: params.to },
    signal: options?.signal,
  })
}

/**
 * `PUT /api/v1/live-sessions/{id}/grades/{studentId}` — baho qo'yish yoki
 * qayta yozish (upsert).
 *
 * ★ TO'LIQ ALMASHTIRISH: chaqiruvchi mavjud `comment` ni yuklab, uni
 * QAYTARIB yuborishi shart — aks holda izoh jimgina o'chadi.
 *
 * Xatolar: 400 — `score` yo'q, manfiy, maxrajdan katta yoki `comment`
 * 500 belgidan uzun (sabab `problem.errors` da); 403 — begona guruh;
 * 404 — dars yo'q yoki o'quvchi bu guruhda emas; 409 — dars BEKOR
 * QILINGAN yoki qator bir vaqtda ikki joydan o'zgardi.
 */
export function upsertLessonGrade(
  sessionId: number,
  studentId: number,
  body: UpsertLessonGradeRequest,
): Promise<LessonGradeRowDto> {
  return http.put<LessonGradeRowDto>(`${BASE}/${sessionId}/grades/${studentId}`, body)
}

/**
 * `DELETE /api/v1/live-sessions/{id}/grades/{studentId}` — bahoni butunlay
 * olib tashlaydi.
 *
 * ★ "0 QO'YISH" BILAN ARALASHTIRILMAYDI: 0 — reytingga to'liq kiradigan
 * haqiqiy baho, o'chirilgan baho esa umuman hisobga olinmaydi. Bu yo'lsiz
 * adashib qo'yilgan bahoni tuzatishning yagona usuli o'quvchiga 0 yozib
 * qo'yish bo'lardi.
 *
 * IDEMPOTENT: bahosi yo'q katak uchun ham 204.
 */
export function deleteLessonGrade(sessionId: number, studentId: number): Promise<void> {
  return http.delete<void>(`${BASE}/${sessionId}/grades/${studentId}`)
}
