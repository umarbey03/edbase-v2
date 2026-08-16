import { lookup } from '@/shared/lib/lookup'
import type { SessionStatusName, SessionTypeName } from '@/shared/types'

/* ==========================================================================
   DAVOMAT — dars kesimidagi varaq va uni qo'lda tuzatish.

   ★ TURLAR `shared/types/api.ts` DA EMAS, SHU YERDA e'lon qilingan: o'sha
   fayl ustida ayni damda boshqa agent ishlayapti va to'qnashuv bo'lardi.
   Maydon nomlari JONLI javobdan aynan ko'chirildi
   (`GET /api/v1/live-sessions/{id}/attendance`).
   ========================================================================== */

/** Server `AttendanceStatus` enum'ini SATR sifatida yuboradi. */
export type AttendanceStatusName = 'Present' | 'Late' | 'Partial' | 'Absent'

/**
 * Varaqdagi bitta o'quvchi.
 *
 * ★ `status: null` — YOZUV YO'Q (xonaga kirmagan va hech kim belgilamagan).
 * Hisobotda u "kelmagan" sanaladi, LEKIN jadvalda ataylab "belgilanmagan"
 * (`·`) ko'rsatiladi: "Absent" deb chizish ustoz qaror qabul qilgandek
 * ko'rinardi.
 *
 * ★ `status: "Absent"` va `durationSeconds > 0` ZIDDIYAT EMAS: birinchisi —
 * ustozning QARORI, ikkinchisi — platformaning O'LCHOVI. Modal ikkalasini
 * yonma-yon ko'rsatadi (eski ilovadagidek).
 */
export interface AttendanceRowDto {
  studentId: number
  studentName: string | null
  /** `null` — belgilanmagan. */
  status: AttendanceStatusName | null
  /** `true` — holatni ODAM qo'ygan; dars yakunlanganda qayta hisoblanmaydi. */
  isManual: boolean
  reason: string | null
  firstJoinAt: string | null
  leftAt: string | null
  /** Platforma o'lchovi. Qo'lda tuzatishda O'ZGARMAYDI. */
  durationSeconds: number
  editedById: number | null
  editedByName: string | null
  editedAt: string | null
  /**
   * "Sababli" (2026-08-16) — FAQAT Academic/Admin qo'yadi. Bosqichma-
   * bosqich to'lov hisoblash shu darsda BU o'quvchidan pul yechmaydi.
   * Oddiy (belgisiz) "Qatnashmagan" — BARIBIR to'lanadi, farqi shunda.
   */
  isExcused: boolean
  excuseReason: string | null
  /**
   * ★ 2026-08-16: shu darsning STIKER narxi (tarif/darslar soni). `null` —
   * hali hisoblanmagan (dars yakunlanmagan yoki tarif sozlanmagan).
   * `lessonChargedAmount` dan farqi: bu — "narxi shuncha edi", pastdagisi
   * — "haqiqatda shuncha yechildi" (chegirmadan keyin, sababli/bepul
   * bo'lsa 0).
   */
  lessonAmount: number | null
  lessonChargedAmount: number | null
}

/** `GET /api/v1/live-sessions/{id}/attendance`. */
export interface SessionAttendanceDto {
  sessionId: number
  groupId: number
  groupName: string | null
  title: string | null
  type: SessionTypeName
  status: SessionStatusName
  scheduledStart: string
  scheduledEnd: string
  /** `false` bo'lsa tuzatish tugmalari ko'rsatilmaydi (server baribir tekshiradi). */
  canEdit: boolean
  rows: AttendanceRowDto[] | null
  /** Butun dars "bepul" deb belgilanganmi — shunday bo'lsa hech kimdan pul yechilmaydi. */
  isFreeLesson: boolean
  freeLessonReason: string | null
  /** Bepul darsda ustoz/kurator HAM haq olmaydimi. Faqat `isFreeLesson` da ma'noli. */
  payrollExcluded: boolean
}

/**
 * `PUT /api/v1/live-sessions/{id}/free-lesson` tanasi (2026-08-16) —
 * FAQAT Academic/Admin.
 */
export interface SetFreeLessonRequest {
  isFree: boolean
  payrollExcluded: boolean
  reason?: string | null
}

/** `SetFreeLessonRequest` javobi. */
export interface FreeLessonStatusDto {
  sessionId: number
  isFreeLesson: boolean
  freeLessonReason: string | null
  payrollExcluded: boolean
}


/**
 * `PUT /api/v1/live-sessions/{id}/attendance/{studentId}` tanasi.
 *
 * ★ TO'LIQ ALMASHTIRISH: `reason` yuborilmasa avvalgi sabab SERVERDA
 * O'CHADI (jonli tekshirilgan). Shuning uchun `reason` bu yerda `?` bilan
 * BELGILANMAGAN — "yuborishni unutish" kompilyatsiya xatosiga aylanadi,
 * ishlatishda yo'qolgan ma'lumotga emas.
 */
export interface UpdateAttendanceRequest {
  status: AttendanceStatusName
  /** `null` — sababni ataylab bo'shatish. Maksimal uzunlik 300 belgi. */
  reason: string | null
}

/**
 * `PUT /api/v1/live-sessions/{id}/attendance/{studentId}/excuse` tanasi
 * (2026-08-16) — FAQAT Academic/Admin (server ham shu rollarga qulflagan).
 */
export interface SetExcusedRequest {
  excused: boolean
  reason?: string | null
}

/** Server shartnomasi nusxasi: uzunroq sabab 400 bilan qaytadi. */
export const ATTENDANCE_REASON_MAX = 300

/**
 * Tugmalar TARTIBI va NOMI eski ilovadagi `.seg` blokidan aynan
 * ("Qatnashgan · Kech · Qisman · Qatnashmagan").
 */
export const ATTENDANCE_CHOICES: readonly { value: AttendanceStatusName; label: string }[] = [
  { value: 'Present', label: 'Qatnashgan' },
  { value: 'Late', label: 'Kech' },
  { value: 'Partial', label: 'Qisman' },
  { value: 'Absent', label: 'Qatnashmagan' },
]

/** Eski `ST_UZ` — to'liq nomlar (modal tafsilotida). */
const STATUS_LABELS: Record<AttendanceStatusName, string> = {
  Present: 'Qatnashgan',
  Late: 'Kech qoldi',
  Partial: 'Qisman',
  Absent: 'Qatnashmagan',
}

export function attendanceStatusLabel(status: AttendanceStatusName | null): string {
  if (status === null) return 'Belgilanmagan'
  return lookup(STATUS_LABELS, status, status)
}

export type AttendanceTone = 'success' | 'warning' | 'danger' | 'neutral'

const STATUS_TONES: Record<AttendanceStatusName, AttendanceTone> = {
  Present: 'success',
  Late: 'warning',
  Partial: 'warning',
  Absent: 'danger',
}

export function attendanceStatusTone(status: AttendanceStatusName | null): AttendanceTone {
  if (status === null) return 'neutral'
  return lookup(STATUS_TONES, status, 'neutral')
}

/**
 * Matritsa katagidagi BELGI — eski `attSym()`:
 * `+` qatnashgan (yashil), `+` kech (sariq), `±` qisman (sariq),
 * `−` qatnashmagan (qizil), `·` belgilanmagan (xira).
 */
const STATUS_SYMBOLS: Record<AttendanceStatusName, string> = {
  Present: '+',
  Late: '+',
  Partial: '±',
  Absent: '−',
}

export function attendanceSymbol(status: AttendanceStatusName | null): string {
  if (status === null) return '·'
  return lookup(STATUS_SYMBOLS, status, '·')
}

/** `3600` -> `60 daqiqa`. Nol bo'lsa bo'sh satr (qator umuman ko'rsatilmaydi). */
export function durationLabel(seconds: number): string {
  const minutes = Math.round(seconds / 60)
  return minutes > 0 ? `${minutes} daqiqa` : ''
}
