import type { SessionStatusName, SessionTypeName } from '@/shared/types'

/* ==========================================================================
   R24 · DARS BAHOSI — dars kesimidagi varaq va uni qo'yish/o'chirish.

   ★ TURLAR `shared/types/api.ts` DA EMAS, SHU YERDA e'lon qilingan —
   `entities/attendance/model/types.ts` bilan AYNI sabab: o'sha fayl ustida
   parallel tarmoqlar ishlaydi va to'qnashuv bo'lardi. Maydon nomlari
   server DTO'sidan aynan ko'chirildi
   (`GET /api/v1/live-sessions/{id}/grades`).

   🔴 `SubmissionDto` BILAN ARALASHTIRILMAYDI: u TOPSHIRILGAN ISHNING
   bahosi (vazifaga bog'langan), bu esa DARSNING bahosi — topshirilgan ish
   umuman bo'lmasligi mumkin.
   ========================================================================== */

/**
 * Varaqdagi bitta o'quvchi.
 *
 * ★ `score: null` — BAHO YO'Q (hech kim baholamagan). Bu `0` DAN BOSHQA
 * holat: 0 — "bajarmadi" degan HAQIQIY baho va reytingga to'liq kiradi,
 * `null` esa reytingda umuman hisobga olinmaydi. Jadvalda ular ataylab
 * ajratiladi (`·` va `0`).
 */
export interface LessonGradeRowDto {
  studentId: number
  studentName: string | null
  /** `null` — baholanmagan. */
  score: number | null
  /** `null` — standart shkala (`SessionLessonGradesDto.defaultMaxScore`). */
  maxScore: number | null
  /** Foiz (0..100), bir xona aniqlikda. SERVER hisoblaydi. */
  percent: number | null
  comment: string | null
  gradedById: number | null
  gradedByName: string | null
  gradedAt: string | null
}

/** `GET /api/v1/live-sessions/{id}/grades`. */
export interface SessionLessonGradesDto {
  sessionId: number
  groupId: number
  groupName: string | null
  title: string | null
  type: SessionTypeName
  status: SessionStatusName
  scheduledStart: string
  scheduledEnd: string
  /**
   * Shkala ko'rsatilmaganda ishlatiladigan maxraj (odatda 5).
   *
   * ★ SERVERDAN KELADI, frontendda QOTIRILMAGAN: Domain doimiysi
   * (`LessonGrade.DefaultMaxScore`) ikki joyda ayri-ayri bo'lib qolsa,
   * oynadagi tugmalar bilan serverning tekshiruvi mos kelmasdi.
   */
  defaultMaxScore: number
  /** `false` bo'lsa baholash tugmalari ko'rsatilmaydi (server baribir tekshiradi). */
  canEdit: boolean
  rows: LessonGradeRowDto[] | null
}

/* ==========================================================================
   O'QUVCHINING O'Z BAHOLARI — `GET /api/v1/progress/lesson-grades`

   ★ NIMA UCHUN ALOHIDA TIP, `SessionLessonGradesDto` EMAS: xodim varag'ining
   birligi — DARS (qatorlar = o'quvchilar), o'quvchi ekraniniki esa —
   O'QUVCHI (qatorlar = darslar). Server ham aynan shu sababdan ikkita DTO
   qaytaradi; bu yerda "boshqa o'quvchi" tushunchasi UMUMAN yo'q, ya'ni
   maxfiylik tipda ham ko'rinib turadi.
   ========================================================================== */

/** Bitta darsdagi baho — o'quvchining ko'zi bilan. */
export interface MyLessonGradeDto {
  sessionId: number
  groupId: number
  title: string | null
  type: SessionTypeName
  scheduledStart: string
  score: number
  /** Amaldagi maxraj (server standartni ham SHU maydonda qaytaradi). */
  maxScore: number
  /** Foiz (0..100), bir xona aniqlikda. SERVER hisoblaydi. */
  percent: number
  comment: string | null
  gradedByName: string | null
  gradedAt: string
}

/**
 * `GET /api/v1/progress/lesson-grades` — FAQAT O'ZINING baholari.
 *
 * 🔴 Serverga `studentId` YUBORILMAYDI va yuborib ham bo'lmaydi: u tokendan
 * olinadi. Ya'ni "boshqa o'quvchining bahosini so'rash" degan so'rov shakli
 * mavjud emas.
 */
export interface MyLessonGradesDto {
  groupIds: number[] | null
  from: string | null
  to: string | null
  defaultMaxScore: number
  gradedCount: number
  /** `null` — hali birorta baho yo'q (0% EMAS). */
  averagePercent: number | null
  items: MyLessonGradeDto[] | null
}

/**
 * `PUT /api/v1/live-sessions/{id}/grades/{studentId}` tanasi.
 *
 * ★ TO'LIQ ALMASHTIRISH: `comment` yuborilmasa avvalgi izoh SERVERDA
 * O'CHADI. Shuning uchun maydonlar `?` bilan BELGILANMAGAN — "yuborishni
 * unutish" kompilyatsiya xatosiga aylanadi, ishlatishda yo'qolgan
 * ma'lumotga emas.
 */
export interface UpsertLessonGradeRequest {
  score: number
  /** `null` — standart shkala. */
  maxScore: number | null
  /** `null` — izohni ataylab bo'shatish. */
  comment: string | null
}

/** Server shartnomasi nusxasi: uzunroq izoh 400 bilan qaytadi. */
export const LESSON_GRADE_COMMENT_MAX = 500

/**
 * Matritsa katagidagi MATN.
 *
 * `null` — baho yo'q (`·`), aks holda ballning o'zi. Maxraj katakda
 * KO'RSATILMAYDI (ustun tor) — u sarlavhada va oynada turadi.
 */
export function lessonGradeText(score: number | null): string {
  return score === null ? '·' : String(score)
}

/**
 * Katak rangi FOIZ bo'yicha — `GradesTab` dagi vazifa matritsasi bilan
 * AYNI chegaralarda (80 / 60), ya'ni ikki jadval bir xil o'qiladi.
 */
export function lessonGradeClass(percent: number | null): string {
  if (percent === null) return 'text-dim'
  if (percent >= 80) return 'text-green-400'
  if (percent >= 60) return 'text-brand-500'
  return 'text-rose-400'
}

/**
 * Oynadagi tez tanlash tugmalari.
 *
 * Standart shkalada (5) — butun sonlar, aks holda bo'sh: 100 ballik
 * imtihonda "1 2 3 4 5" tugmalari faqat chalg'itardi va ustoz baribir
 * maydonga yozardi.
 */
export function lessonGradeChoices(maxScore: number): number[] {
  if (!Number.isInteger(maxScore) || maxScore < 1 || maxScore > 10) return []
  return Array.from({ length: maxScore + 1 }, (_, index) => index)
}
