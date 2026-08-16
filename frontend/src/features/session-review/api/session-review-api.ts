import { http } from '@/shared/api'
import type {
  SaveSessionReviewRequest,
  SessionReviewDto,
  TeacherReviewOverviewDto,
} from '@/shared/types'

const BASE = '/api/v1/live-sessions'

function reviewUrl(sessionId: number): string {
  return `${BASE}/${sessionId}/review`
}

/**
 * `GET /api/v1/live-sessions/{id}/review` — dars sifati tahlili (R29 / R30).
 *
 * ════════════════════════════════════════════════════════════════════════
 * ⚠️ TAHLIL YO'Q BO'LSA `200` VA JSON `null` — 404 EMAS.
 *
 * Bu ATAYLAB shunday: "hali yozilmagan" — normal va eng ko'p uchraydigan
 * holat, xato emas. 404 bo'lsa u "dars topilmadi" bilan bir xil kodga
 * tushib qolardi va modal har ochilishida qizil ogohlantirish
 * ko'rsatardi. Shuning uchun qaytish turi `| null`.
 *
 * 🔴 O'QUVCHI BU MANZILGA UMUMAN KIRA OLMAYDI (`403`, tanasida tahlil
 * matnining zarrasi ham bo'lmaydi). Chegara SERVERDA — tugmani yashirish
 * emas. Bu funksiya o'quvchi ekranidan hech qachon chaqirilmasin, lekin
 * chaqirilsa ham hech narsa oshkor bo'lmaydi.
 *
 * ★ USTOZ FAQAT O'Z DARSINI ko'radi (R30): hamkasbining darsi uchun `403`.
 * ════════════════════════════════════════════════════════════════════════
 */
export function fetchSessionReview(
  sessionId: number,
  options?: { signal?: AbortSignal },
): Promise<SessionReviewDto | null> {
  return http.get<SessionReviewDto | null>(reviewUrl(sessionId), {
    signal: options?.signal,
  })
}

/**
 * `PUT /api/v1/live-sessions/{id}/review` — UPSERT (faqat o'quv bo'limi).
 *
 * ★ NEGA `PUT` VA UPSERT: bitta darsda BITTA tahlil bo'ladi (bazada unikal
 * indeks). `POST`/`PUT` ajratilsa klient yozishdan oldin "bormi?" deb
 * so'rashga majbur bo'lardi va ikki so'rov orasida hamkasbi yozib qo'ysa
 * `409` olardi.
 *
 * ⚠️ `409` — matn bo'sh yoki 4000 belgidan uzun (domain qoidasi).
 * ⚠️ `403` — ustoz yoki o'quvchi: tahlilni ular YOZMAYDI. Ustoz sifat
 *    nazoratining OBYEKTI, ya'ni "Muammo bor" ni o'zi "Tasdiqlandi" ga
 *    aylantira olmaydi.
 */
export function saveSessionReview(
  sessionId: number,
  request: SaveSessionReviewRequest,
): Promise<SessionReviewDto> {
  return http.put<SessionReviewDto>(reviewUrl(sessionId), request)
}

/**
 * `DELETE /api/v1/live-sessions/{id}/review` — faqat o'quv bo'limi.
 * IDEMPOTENT: tahlil bo'lmasa ham `204`.
 */
export function deleteSessionReview(sessionId: number): Promise<void> {
  return http.delete<void>(reviewUrl(sessionId))
}

/* ==========================================================================
   TAHLILLAR PANELI (2026-08-16) — "Dars yozuvlari" bo'limi, faqat
   Academic/Admin. Bitta darsning EMAS, XODIM (ustoz/kurator) bo'yicha
   ko'rinish: avval xulosa jadvali, so'ng bitta xodimning BARCHA tahlillari.
   ========================================================================== */

/** `GET /api/v1/session-reviews/teachers-overview` — xodim bo'yicha xulosa jadvali. */
export function fetchTeacherReviewsOverview(
  options?: { signal?: AbortSignal },
): Promise<TeacherReviewOverviewDto[]> {
  return http.get<TeacherReviewOverviewDto[]>('/api/v1/session-reviews/teachers-overview', {
    signal: options?.signal,
  })
}

/** `GET /api/v1/session-reviews?teacherId=` — bitta xodimning barcha tahlillari. */
export function fetchSessionReviewsByTeacher(
  teacherId: number,
  options?: { signal?: AbortSignal },
): Promise<SessionReviewDto[]> {
  return http.get<SessionReviewDto[]>('/api/v1/session-reviews', {
    query: { TeacherId: teacherId },
    signal: options?.signal,
  })
}
