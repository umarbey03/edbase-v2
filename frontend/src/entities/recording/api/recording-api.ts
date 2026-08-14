import { http } from '@/shared/api'
import type {
  RecordingDto,
  RecordingLinkDto,
  RecordingListItemDto,
  RecordingLiveStatusDto,
  RecordingSectionDto,
} from '@/shared/types'

import type { RecordingRange } from '../model/types'

const SESSIONS_BASE = '/api/v1/live-sessions'
const RECORDINGS_BASE = '/api/v1/recordings'

/**
 * `GET /api/v1/recordings?from&to` — barcha guruhlar bo'yicha yozuvlar.
 *
 * ★ `from` VA `to` MAJBURIY, GARCHI SWAGGER ULARNI IXTIYORIY DEB KO'RSATSA HAM.
 * Ikkalasisiz chaqirilganda server **500** qaytaradi (jonli tekshirilgan:
 * "The UTC time represented when the offset is applied must be between year 0
 * and 10,000"), faqat bittasi berilganda esa 400. Shuning uchun bu funksiya
 * oraliqni IXTIYORIY emas, TALAB qilib oladi — chaqiruvchi ularni tushirib
 * qoldira olmaydi.
 *
 * Server javobni rolga qarab o'zi cheklaydi (jonli tekshirilgan: ustoz faqat
 * o'zi olib boradigan, o'quvchi faqat o'z guruhi darslarini ko'radi), shuning
 * uchun bu yerda qo'shimcha filtr yo'q.
 */
export function fetchRecordings(
  range: RecordingRange,
  options?: { signal?: AbortSignal },
): Promise<RecordingListItemDto[]> {
  return http.get<RecordingListItemDto[]>(RECORDINGS_BASE, {
    query: { from: range.from, to: range.to },
    signal: options?.signal,
  })
}

/**
 * `GET /api/v1/live-sessions/{id}/recordings` — bitta darsning yozuvlari.
 *
 * Ruxsat DARSGA bog'liq (jonli tekshirilgan): darsni olib bormaydigan ustoz
 * ham, guruhda bo'lmagan o'quvchi ham `403` oladi.
 */
export function fetchSessionRecordings(
  sessionId: number,
  options?: { signal?: AbortSignal },
): Promise<RecordingDto[]> {
  return http.get<RecordingDto[]>(`${SESSIONS_BASE}/${sessionId}/recordings`, {
    signal: options?.signal,
  })
}

/**
 * `GET /api/v1/live-sessions/{id}/recording-status` — "hozir yozib
 * olinyaptimi".
 *
 * ══════════════════════════════════════════════════════════════════════
 * 🔴 JONLI XONADAGI "YOZUV KETMOQDA" INDIKATORINING YAGONA MANBAI.
 * ══════════════════════════════════════════════════════════════════════
 *
 * Ruxsat — DARSGA bog'liq, ROLGA emas: guruh a'zosi o'quvchi ham `200`
 * oladi (yuqoridagi `fetchSessionRecordings` dan AYNAN shu bilan farq
 * qiladi — u o'quvchiga faqat TAYYOR yozuvlarni ko'rsatadi va ketayotgan
 * yozuvni umuman bermaydi). Guruhda bo'lmagan odam `403` oladi.
 *
 * ⚠️ INDIKATORNI `group.recordEnabled` GA ULASH MUMKIN EMAS: u "yozilishi
 * KERAK" degani, "yozilYAPTI" degani emas. Egress yiqilgan darsda u
 * yashil turaverib, hech qachon bo'lmagan yozuv haqida ogohlantirardi —
 * ya'ni indikator ma'nosini yo'qotardi.
 */
export function fetchSessionRecordingStatus(
  sessionId: number,
  options?: { signal?: AbortSignal },
): Promise<RecordingLiveStatusDto> {
  return http.get<RecordingLiveStatusDto>(
    `${SESSIONS_BASE}/${sessionId}/recording-status`,
    { signal: options?.signal },
  )
}

/**
 * `GET /api/v1/recordings/{id}/link` — ko'rish havolasi.
 *
 * ★ PRESIGNED S3 MANZIL (jonli tekshirilgan): javobda `X-Amz-Signature` va
 * `X-Amz-Expires=900` bor, ya'ni havola ~15 daqiqada eskiradi va `expiresAt`
 * da aniq vaqt keladi. `Authorization` sarlavhasi KERAK EMAS — shuning uchun
 * `http.download()` yoki blob shart emas, manzilni to'g'ridan-to'g'ri
 * `<video src>` ga berish mumkin (`URL.createObjectURL` ham, `revoke` ham yo'q).
 *
 * ⚠️ Bu chaqiruv qarzdor o'quvchida `403` beradi (`PaymentBlockService`,
 * qamrov `Video`) — server sababni `detail` da yozadi va `toUserMessage` uni
 * o'zgarishsiz ko'rsatadi. Ro'yxat endpointlari esa BLOKLANMAYDI (jonli
 * tekshirilgan), shuning uchun qarzdor yozuvlar borligini ko'radi, lekin
 * ochganda sababni o'qiydi.
 */
export function fetchRecordingLink(recordingId: number): Promise<RecordingLinkDto> {
  return http.get<RecordingLinkDto>(`${RECORDINGS_BASE}/${recordingId}/link`)
}

/**
 * `POST /api/v1/live-sessions/{id}/recordings/start`.
 *
 * Ruxsat (jonli tekshirilgan): ustoz/kurator (dars egasi) va o'quv bo'limi/admin
 * — ha; o'quvchi — `403` (tanasiz). Dars JONLI bo'lmasa `409`:
 * "Yozuvni faqat JONLI dars uchun boshlash mumkin. Avval darsni boshlang."
 *
 * ⚠️ Javob `200` bo'lsa ham yozuv MUVAFFAQIYATLI boshlangan degani emas:
 * egress rad etsa DTO `status: "Requested"`, `error: "..."` bilan qaytadi
 * (jonli ko'rilgan). Shuning uchun UI javobdagi `error` ni ham o'qiydi.
 */
export function startRecording(sessionId: number): Promise<RecordingDto> {
  return http.post<RecordingDto>(`${SESSIONS_BASE}/${sessionId}/recordings/start`)
}

/**
 * `POST /api/v1/live-sessions/{id}/recordings/stop`.
 * Faol yozuv bo'lmasa `409`: "Bu darsda faol yozuv yo'q."
 */
export function stopRecording(sessionId: number): Promise<RecordingDto> {
  return http.post<RecordingDto>(`${SESSIONS_BASE}/${sessionId}/recordings/stop`)
}

/**
 * `PATCH /api/v1/recordings/{id}/visibility` — yozuvni o'quvchilarga
 * ochadi yoki yashiradi (talab R5).
 *
 * ════════════════════════════════════════════════════════════════════════
 * 🔴 IKKI XATO JAVOBI IKKI XIL MA'NOGA EGA VA ULAR ARALASHTIRILMASIN:
 *
 *   • `403` — RUXSAT. Ikki sababi bor va ikkalasi ham server matnida
 *     ochiq yozilgan: (a) begona guruhning darsi; (b) yozuvni O'QUV
 *     BO'LIMI yopgan va uni faqat o'quv bo'limi qayta ocha oladi
 *     ("eng qattig'i yutadi" qoidasi — `IRecordingService.SetVisibilityAsync`).
 *   • `409` — HOLAT. Tayyor bo'lmagan yozuvni ochib bo'lmaydi
 *     (`SessionRecording.ShowToStudents`, `Test.Publish()` bilan AYNI naqsh).
 *
 * ⚠️ YASHIRISH HECH QACHON `403`/`409` BERMAYDI (o'z guruhida): u
 * roziligning zaxira chiqishi va har qanday holatda ishlashi shart.
 * ════════════════════════════════════════════════════════════════════════
 */
export function updateRecordingVisibility(
  recordingId: number,
  visible: boolean,
): Promise<RecordingDto> {
  return http.patch<RecordingDto>(`${RECORDINGS_BASE}/${recordingId}/visibility`, { visible })
}

/**
 * `GET /api/v1/recordings/section` — "yozuvlar bo'limi menga ochiqmi" (R5).
 *
 * ★ FAQAT O'QUVCHI INTERFEYSI UCHUN: "O'quv" ekranidagi kirish kartochkasi
 * shu javobga qarab chiziladi. Xodimga har doim `{ visible: true }`.
 *
 * ⚠️ BU RUXSAT SO'ROVI EMAS, MASLAHAT. Server ro'yxat va havola
 * yo'llarini bundan MUSTAQIL tekshiradi — klient `true` deb ishonsa ham
 * yopilgan yozuvni ocha olmaydi.
 */
export function fetchRecordingSection(
  options?: { signal?: AbortSignal },
): Promise<RecordingSectionDto> {
  return http.get<RecordingSectionDto>(`${RECORDINGS_BASE}/section`, {
    signal: options?.signal,
  })
}
