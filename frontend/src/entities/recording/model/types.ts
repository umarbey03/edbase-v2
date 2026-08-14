import { lookup } from '@/shared/lib/lookup'
import type {
  RecordingDto,
  RecordingListItemDto,
  RecordingStatusName,
  SessionReviewVerdictName,
} from '@/shared/types'

export type Recording = RecordingDto
export type RecordingListItem = RecordingListItemDto

/**
 * Holat nomlari — eski ilovada yozuv holati UMUMAN ko'rsatilmasdi (u yerda
 * yozuv yo tayyor edi, yo ro'yxatda yo'q edi). v2 backendi esa `Requested` va
 * `Failed` qatorlarini ham beradi, shuning uchun matnlar shu yerda YANGI
 * yozilgan. Har biri xodim NIMA QILISHINI aytadi, ichki atamani takrorlamaydi.
 */
const STATUS_LABELS: Record<RecordingStatusName, string> = {
  Requested: 'Navbatda',
  Starting: 'Boshlanmoqda',
  Active: 'Yozilmoqda',
  Completed: 'Tayyor',
  Failed: 'Xato',
}

export type RecordingTone = 'neutral' | 'accent' | 'success' | 'live' | 'danger'

const STATUS_TONES: Record<RecordingStatusName, RecordingTone> = {
  Requested: 'neutral',
  Starting: 'accent',
  // "Yozilmoqda" — efirdagi qizil nuqta bilan bir xil ma'no.
  Active: 'live',
  Completed: 'success',
  Failed: 'danger',
}

/**
 * Server noma'lum holat yuborsa (yangi enum qiymati) raqamning O'ZI qaytadi —
 * `lookup` shuni ko'rsatadi, bo'sh rozetka emas.
 */
export function recordingStatusLabel(status: string | null): string {
  if (status === null) return '—'
  return lookup(STATUS_LABELS, status, status)
}

export function recordingStatusTone(status: string | null): RecordingTone {
  if (status === null) return 'neutral'
  return lookup(STATUS_TONES, status, 'neutral')
}

/* ==========================================================================
   R29 — SIFAT NAZORATI NISHONI (eski ilovadan tiklandi)
   ========================================================================== */

/**
 * ★ BU MATNLAR ESKI ILOVADAN AYNAN OLINDI: "Ko'rilmagan / Tasdiqlandi /
 * Muammo bor". Ular `RecordingCard.vue` izohida TARIXIY yozuv sifatida
 * saqlanib qolgan edi — v2 da nishon olib tashlangan, chunki backendda
 * na maydon, na endpoint bor edi. Endi ikkalasi ham bor va matnlar
 * O'YLAB TOPILMADI: xodimlar aynan shu uch so'zga o'rgangan.
 *
 * ⚠️ UCH HOLAT, LEKIN TO'RT MANBA HOLATI:
 *   • tahlil YO'Q (`hasReview === false`)      -> "Ko'rilmagan"
 *   • tahlil bor, xulosasiz (`NotReviewed`)    -> "Ko'rilmagan"
 *   • `Approved`                               -> "Tasdiqlandi"
 *   • `HasIssue`                               -> "Muammo bor"
 * Birinchi ikkitasi FOYDALANUVCHI UCHUN bir xil ("hali xulosa yo'q"),
 * shuning uchun ular bitta yorliqni beradi.
 */
const REVIEW_LABELS: Record<SessionReviewVerdictName, string> = {
  NotReviewed: 'Ko‘rilmagan',
  Approved: 'Tasdiqlandi',
  HasIssue: 'Muammo bor',
}

const REVIEW_TONES: Record<SessionReviewVerdictName, RecordingTone> = {
  NotReviewed: 'neutral',
  Approved: 'success',
  HasIssue: 'danger',
}

/**
 * Nishon yorlig'i. `null` (tahlil yo'q) ham "Ko'rilmagan" beradi — sabab
 * yuqorida.
 */
export function reviewVerdictLabel(verdict: string | null): string {
  if (verdict === null) return REVIEW_LABELS.NotReviewed
  return lookup(REVIEW_LABELS, verdict, verdict)
}

export function reviewVerdictTone(verdict: string | null): RecordingTone {
  if (verdict === null) return 'neutral'
  return lookup(REVIEW_TONES, verdict, 'neutral')
}

/**
 * Nishon UMUMAN chiziladimi.
 *
 * 🔴 O'QUVCHIDA HECH QACHON: server unga `hasReview: false` va
 * `reviewStatus: null` beradi, ya'ni bu funksiya `false` qaytaradi.
 * Chegara SERVERDA — bu yerda faqat uning natijasi o'qiladi.
 */
export function hasQualityReview(recording: Recording): boolean {
  return recording.hasReview
}

/** Yozuv hali tugamagan — ro'yxat avtomatik yangilanib turishi kerak. */
export function isRecordingInProgress(recording: Recording): boolean {
  const status = recording.status
  return status === 'Requested' || status === 'Starting' || status === 'Active'
}

/**
 * `4820` -> `1 soat 20 daq`.
 *
 * ★ SHAKL ESKI ILOVADAN (`academic.html`, `_recDur()`, 6063-qator):
 * `h ? '{h} soat {mm} daq' : '{mm} daq'`. Farq faqat kirish birligida — eski
 * server DAQIQA berardi, v2 esa SONIYA (`durationSeconds`), shuning uchun
 * bu yerda avval daqiqaga o'giriladi.
 */
export function formatRecordingDuration(seconds: number | null): string {
  if (seconds === null || seconds < 0) return ''
  const minutes = Math.round(seconds / 60)
  const hours = Math.floor(minutes / 60)
  const rest = minutes % 60
  return hours > 0 ? `${hours} soat ${rest} daq` : `${rest} daq`
}

const BYTES_IN_MB = 1024 * 1024
const BYTES_IN_GB = BYTES_IN_MB * 1024

/**
 * Fayl hajmi. Eski ilovada bu ko'rsatkich YO'Q edi (server bermasdi), lekin
 * v2 `sizeBytes` beradi va u xodimga kerak: 80 daqiqalik dars ~1 GB bo'ladi va
 * mobil internetda ochishdan oldin buni bilish foydali.
 *
 * 1024 lik bo'luvchi ATAYLAB: fayl hajmi MinIO konsolida ham shu shaklda
 * ko'rinadi, ikki joyda ikki xil raqam chalkashlik tug'dirardi.
 */
export function formatRecordingSize(bytes: number | null): string {
  if (bytes === null || bytes <= 0) return ''
  if (bytes >= BYTES_IN_GB) return `${(bytes / BYTES_IN_GB).toFixed(1)} GB`
  return `${Math.max(1, Math.round(bytes / BYTES_IN_MB))} MB`
}

/**
 * Kartochka sarlavhasi.
 *
 * Eski ilova `r.title || (r.type === 'teacher' ? 'Asosiy dars' : 'Amaliyot darsi')`
 * yozardi. v2 ro'yxat qatorida dars TURI yo'q (`RecordingListItemDto` da faqat
 * `title`, `groupName`), shuning uchun zaxira qiymat — guruh nomi. O'ylab
 * topilgan "Asosiy dars" ni turini bilmasdan yozish noto'g'ri bo'lardi.
 */
export function recordingItemTitle(item: RecordingListItem): string {
  const title = item.title?.trim()
  if (title !== undefined && title.length > 0) return title
  const group = item.groupName?.trim()
  return group !== undefined && group.length > 0 ? group : 'Dars yozuvi'
}

/* ==========================================================================
   SANA ORALIG'I — `GET /api/v1/recordings` NING QAT'IY TALABI
   ========================================================================== */

/**
 * Server oraliqni 92 kun bilan cheklaydi (jonli tekshirilgan: 93 kun so'ralsa
 * `400` va `errors.toDate = ["Oraliq 92 kundan oshmasin."]`).
 */
export const RECORDINGS_MAX_RANGE_DAYS = 92

/** Sahifa ochilganda ko'rsatiladigan oyna — oxirgi 30 kun. */
const DEFAULT_RANGE_DAYS = 30

function pad2(value: number): string {
  return value < 10 ? `0${value}` : String(value)
}

/** `Date` -> `YYYY-MM-DD` MAHALLIY vaqtda (`toISOString` UTC beradi va kunni surardi). */
export function toDateInput(date: Date): string {
  return `${date.getFullYear()}-${pad2(date.getMonth() + 1)}-${pad2(date.getDate())}`
}

export interface RecordingRange {
  from: string
  to: string
}

/**
 * Boshlang'ich oraliq.
 *
 * ★ ORALIQ HAR DOIM YUBORILADI. `from`/`to` siz chaqirilgan `GET /recordings`
 * serverda **500** beradi ("The UTC time represented when the offset is applied
 * must be between year 0 and 10,000") — jonli tekshirilgan. Ya'ni "filtrsiz"
 * rejim umuman mavjud emas va UI uni taklif ham qilmaydi.
 */
export function defaultRecordingRange(now: Date = new Date()): RecordingRange {
  const from = new Date(now)
  from.setDate(from.getDate() - (DEFAULT_RANGE_DAYS - 1))
  return { from: toDateInput(from), to: toDateInput(now) }
}

const MS_IN_DAY = 24 * 60 * 60 * 1000

/**
 * Oraliqni serverga yuborishdan OLDIN tekshiradi.
 *
 * NEGA MIJOZDA HAM: server xatosi to'g'ri o'qiladi (`toUserMessage` 400 dagi
 * `errors` ni yig'adi), lekin `<input type="date">` da bitta raqamni
 * tahrirlaganda oraliq vaqtincha buzilgan bo'ladi (masalan `2026-05-01` dan
 * `2026-0` ga) va har bosishda serverga 400 so'rov ketardi. Xato matni AYNAN
 * serverdagidek yozilgan — foydalanuvchi ikki xil ta'rif ko'rmasin.
 */
export function validateRecordingRange(range: RecordingRange): string | null {
  const from = new Date(range.from)
  const to = new Date(range.to)
  if (Number.isNaN(from.getTime()) || Number.isNaN(to.getTime())) return 'Sanani to‘liq kiriting.'
  if (from.getTime() > to.getTime()) {
    return 'Boshlanish sanasi tugash sanasidan keyin bo‘lishi mumkin emas.'
  }
  // Server chegarani kunlar FARQI bo'yicha hisoblaydi: 2026-05-01..2026-08-01
  // (92 kun farq) rad etiladi, 2026-05-01..2026-07-31 (91) o'tadi.
  const days = Math.round((to.getTime() - from.getTime()) / MS_IN_DAY)
  if (days >= RECORDINGS_MAX_RANGE_DAYS) return 'Oraliq 92 kundan oshmasin.'
  return null
}
