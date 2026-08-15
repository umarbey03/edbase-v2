import { http } from '@/shared/api'
import type { DownloadedFile } from '@/shared/api'
import { apiUrl } from '@/shared/config/env'
import type { LessonAssetUploadFields, MediaAccessTicketDto, PositionDto } from '@/shared/types'

/**
 * DARS MEDIASI API'si (video qismlari / imtihon rasmlari).
 *
 * ⚠️ YO'LLAR KURS DARAXTIDAN MUSTAQIL: `/api/v1/lessons/...`, ya'ni
 * `courses/{id}/modules/{id}/...` EMAS (server shunday qurgan — fayl so'rovi
 * `assetId` dan boshqa hech nimani bilmasligi kerak).
 *
 * ★ YUKLASH BU YERDA YO'Q va bu ataylab: `fetch` da yuklash PROGRESSI yo'q
 * (`XMLHttpRequest.upload.onprogress` kerak), 1 GB video uchun esa progress
 * va bekor qilish MAJBURIY. Shuning uchun yuklash oqimi
 * `features/lesson-media` da (`uploadWithProgress`), bu modul esa faqat
 * YO'LNI va `FormData` SHAKLINI beradi — maydon nomlari bitta joyda tursin.
 */
const LESSONS_BASE = '/api/v1/lessons'

/** `POST /api/v1/lessons/{lessonId}/assets` yo'li (`multipart/form-data`). */
export function lessonAssetUploadPath(lessonId: number): string {
  return `${LESSONS_BASE}/${lessonId}/assets`
}

/**
 * Yuklash uchun `FormData`.
 *
 * 🔴 FAYL MAYDONINING NOMI AYNAN `file` (server imzosi: `IFormFile file`).
 * `kind` YUBORILMAYDI — u dars turidan kelib chiqadi.
 *
 * `durationSec`/`width`/`height` — brauzer o'lchagan qiymatlar; server ularni
 * faqat KO'RSATISH uchun saqlaydi (13-bo'lim, 47-tuzoq). `null` bo'lsa
 * maydon umuman qo'shilmaydi: bo'sh satr yuborilsa ASP.NET `int?` ni
 * bog'lashda 400 berardi.
 */
export function buildLessonAssetForm(file: File, fields: LessonAssetUploadFields = {}): FormData {
  const form = new FormData()
  form.append('file', file)

  const title = (fields.title ?? '').trim()
  if (title.length > 0) form.append('title', title)

  if (fields.durationSec != null) form.append('durationSec', String(fields.durationSec))
  if (fields.width != null) form.append('width', String(fields.width))
  if (fields.height != null) form.append('height', String(fields.height))

  return form
}

/**
 * `DELETE /api/v1/lessons/assets/{assetId}` — 204.
 *
 * ⚠️ QAYTARIB BO'LMAYDI: baza yozuvi ham, ombordagi obyekt ham o'chadi.
 * Tasdiq so'rash — chaqiruvchining ishi (`useConfirm`, `tone: 'danger'`).
 */
export function deleteLessonAsset(assetId: number): Promise<void> {
  return http.delete<void>(`${LESSONS_BASE}/assets/${assetId}`)
}

/**
 * `POST /api/v1/lessons/{lessonId}/assets/reorder`.
 *
 * ★ TO'LIQ ro'yxat kutiladi — darsning BARCHA fayl Id'lari. Yetishmasa,
 * takrorlansa yoki begona Id bo'lsa 400 (`problem.errors.orderedIds[0]`) va
 * HECH NARSA yozilmaydi (`DAVOM_ETTIRISH.md` 6-bo'lim, 7-tuzoq).
 *
 * ★ METOD `POST` — loyihadagi qolgan uchta reorder bilan AYNI (integratsiyada
 * `PUT` dan o'tkazilgan).
 */
export function reorderLessonAssets(
  lessonId: number,
  orderedIds: number[],
): Promise<PositionDto[]> {
  return http.post<PositionDto[]>(`${LESSONS_BASE}/${lessonId}/assets/reorder`, { orderedIds })
}

/**
 * `GET /api/v1/lessons/assets/{assetId}` — faylni Blob sifatida oladi.
 *
 * ★ NEGA BLOB, ya'ni `<img src>` EMAS: endpoint `Authorization` sarlavhasini
 * talab qiladi, brauzer esa rasm/video so'rovlarida uni YUBORMAYDI.
 * `http.download` token bilan oladi va yo'lda 401 bo'lsa tokenni
 * yangilaydi. Naqsh
 * `fetchSubmissionFile` bilan AYNI.
 *
 * 🔴 FAQAT RASM UCHUN. Video (1 GB gacha) bu yo'ldan O'TMASLIGI kerak:
 * Blob butun faylni xotiraga soladi va `Range` (seek) ma'nosini yo'qotadi.
 * Video pleyeri qisqa muddatli asset tokeni bilan alohida vazifada quriladi.
 */
export function fetchLessonAssetFile(
  assetId: number,
  options?: { signal?: AbortSignal },
): Promise<DownloadedFile> {
  return http.download(`${LESSONS_BASE}/assets/${assetId}`, `dars-fayli-${assetId}`, {
    signal: options?.signal,
    headers: { Accept: '*/*' },
  })
}

/**
 * `GET /api/v1/lessons/assets/{assetId}/ticket` — VIDEO PLEYER uchun.
 *
 * ★ NEGA `fetchLessonAssetFile` (Blob) EMAS: video (1 GB gacha) Blob'ga
 * sig'maydi va `Range` (seek) ma'nosini yo'qotadi. Chipta esa to'g'ridan-
 * to'g'ri `<video src>` ga qo'yiladigan manzil yasaydi —
 * `useRecordingLink` bilan AYNI g'oya, faqat manba S3 presigned URL emas,
 * o'zimiz yasagan `?ticket=` so'rov parametri (sabab: server chiptasi
 * `assetId`ga bog'langan imzo, S3 emas).
 *
 * ⚠️ Chipta ~15 daqiqada o'ladi — pleyer `expiresAt`ni kuzatib, kerak
 * bo'lsa YANGISINI so'rashi kerak (`lessonAssetTicketUrl` bilan birga).
 */
export function fetchLessonAssetTicket(assetId: number): Promise<MediaAccessTicketDto> {
  return http.get<MediaAccessTicketDto>(`${LESSONS_BASE}/assets/${assetId}/ticket`)
}

/** Chiptadan `<video src>` ga qo'yiladigan to'liq manzilni yasaydi. */
export function lessonAssetTicketUrl(assetId: number, token: string): string {
  return `${apiUrl(`${LESSONS_BASE}/assets/${assetId}`)}?ticket=${encodeURIComponent(token)}`
}
