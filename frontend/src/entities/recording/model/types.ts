import { lookup } from '@/shared/lib/lookup'
import type {
  RecordingCompositionStatusName,
  RecordingDto,
  RecordingListItemDto,
  RecordingPipelineName,
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
   YOZUV QUVURI VA TUNGI MONTAJ (`RecordingPipeline`,
   `RecordingCompositionStatus`)
   ==========================================================================

   Ikki yangi maydon IKKI XIL savolga javob beradi va ular aralashtirilmasin:

     • `pipeline`          — yozuv QANDAY olingan (jonli kodlash / tungi montaj);
     • `compositionStatus` — tungi montaj QAYERDA turibdi (faqat yangi quvurda).

   🔴 ENG MUHIM MATN QOIDASI. `Queued` va `Running` — XATO EMAS. Dars tugagan,
   fayl hali yo'q, lekin u ERTALAB paydo bo'ladi. Agar bu holat "xato" yoki
   hatto neytral "tayyor emas" bo'lib ko'rinsa, xodim yozuvni yo'qolgan deb
   hisoblab qo'llab-quvvatlashga murojaat qiladi — butun bu ish AYNAN o'sha
   shikoyatni tugatish uchun qilingan. Shuning uchun matnlar QACHON tayyor
   bo'lishini aytadi, "muvaffaqiyatsiz" degan ma'no bermaydi. */

/**
 * Quvur nishonining matni.
 *
 * ★ `RoomComposite` uchun BO'SH SATR VA BU ATAYLAB: u — standart yo'l va
 * 33 ta guruhning hammasi shunday. Har kartochkaga "Standart" deb yozib
 * qo'yilsa nishon shovqinga aylanardi va HAQIQIY farqni (tungi montaj)
 * ko'rsatmasdi. Bo'sh satr = nishon umuman chizilmaydi.
 */
const PIPELINE_LABELS: Record<RecordingPipelineName, string> = {
  RoomComposite: '',
  TrackComposition: 'Tungi montaj',
}

/**
 * Nishon ohangi. `accent` — bu "diqqat" emas, "boshqacha": qator xato ham,
 * ogohlantirish ham emas, shunchaki BOSHQA usulda olingan.
 */
const PIPELINE_TONES: Record<RecordingPipelineName, RecordingTone> = {
  RoomComposite: 'neutral',
  TrackComposition: 'accent',
}

/** Nishon matni. Bo'sh satr — nishon chizilmaydi (sabab yuqorida). */
export function recordingPipelineLabel(pipeline: string | null): string {
  if (pipeline === null) return ''
  return lookup(PIPELINE_LABELS, pipeline, '')
}

export function recordingPipelineTone(pipeline: string | null): RecordingTone {
  if (pipeline === null) return 'neutral'
  return lookup(PIPELINE_TONES, pipeline, 'neutral')
}

/**
 * Nishon UMUMAN chiziladimi. `pipeline` serverda hech qachon `null` emas,
 * lekin noma'lum qiymat kelsa ham (backend yangi quvur qo'shsa) yorliqsiz
 * bo'sh rozetka chizilmasin.
 */
export function hasPipelineBadge(recording: Recording): boolean {
  return recordingPipelineLabel(recording.pipeline).length > 0
}

/** SPEC 7.1-jadvali — matnlar AYNAN o'sha yerdagidek. */
const COMPOSITION_LABELS: Record<RecordingCompositionStatusName, string> = {
  Collecting: 'Yozilmoqda',
  Queued: 'Tungi montaj navbatida',
  Running: 'Montaj qilinmoqda',
  Completed: 'Tayyor',
  Failed: 'Xato',
}

const COMPOSITION_TONES: Record<RecordingCompositionStatusName, RecordingTone> = {
  // Dars ketyapti — efirdagi qizil nuqta bilan bir xil ma'no (`Active` dek).
  Collecting: 'live',
  /*
    🔴 `Queued` VA `Running` — `accent`, `warning` EMAS VA `danger` EMAS.
    Sariq/qizil ohang "biror narsa noto'g'ri" degan ma'no beradi, bu yerda
    esa hammasi rejadagidek: navbat — bu KUTISH, xato emas.
  */
  Queued: 'accent',
  Running: 'accent',
  Completed: 'success',
  Failed: 'danger',
}

export function recordingCompositionLabel(status: string | null): string {
  if (status === null) return ''
  return lookup(COMPOSITION_LABELS, status, status)
}

export function recordingCompositionTone(status: string | null): RecordingTone {
  if (status === null) return 'neutral'
  return lookup(COMPOSITION_TONES, status, 'neutral')
}

/**
 * "Fayl qani?" degan savolga BIR GAPLIK javob — nishon yonidagi izoh uchun.
 *
 * ⚠️ `Failed` uchun ATAYLAB BO'SH: xato sababini server `error` maydonida
 * o'zbekcha yozib beradi va kartochka uni allaqachon ko'rsatadi. Bu yerda
 * o'zimizdan "xato yuz berdi" deb yozsak, xodim IKKI xil tushuntirish
 * ko'rardi va aniqrog'i (serverniki) ikkinchi darajaga tushardi.
 *
 * `Collecting` ham bo'sh: dars hozir ketyapti va "Yozilmoqda" nishonining
 * o'zi yetarli — bu holat eski quvurda ham bor va u haqda hech kim
 * so'ramagan.
 */
const COMPOSITION_NOTES: Record<RecordingCompositionStatusName, string> = {
  Collecting: '',
  Queued: 'Dars yozib olindi. Video kechasi montaj qilinadi — ertalab tayyor bo‘ladi.',
  Running: 'Video hozir montaj qilinmoqda. Tayyor bo‘lgach o‘zi ochiladi.',
  Completed: '',
  Failed: '',
}

export function recordingCompositionNote(status: string | null): string {
  if (status === null) return ''
  return lookup(COMPOSITION_NOTES, status, '')
}

/**
 * KARTOCHKADA KO'RSATILADIGAN holat yorlig'i.
 *
 * 🔴 SPEC 7.1: `compositionStatus` bor va u `Completed` EMAS bo'lsa, xom
 * `status` O'RNIGA montaj holati ko'rsatiladi. Sabab: yangi quvurda dars
 * tugagach ham `status` `'Active'` bo'lib qolaveradi (fayl ertalab
 * yakunlanadi), ya'ni olti soat oldin tugagan dars ro'yxatda "Yozilmoqda"
 * bo'lib turardi — bu shunchaki YOLG'ON.
 *
 * `Completed` da esa xom `status` ishlatiladi: montaj tugagan bo'lsa ham
 * fayl yuklanishi/tekshirilishi qolgan bo'lishi mumkin va o'sha payt
 * "Tayyor" deb yozish "Ko'rish" tugmasi ishlamasligi bilan zid kelardi
 * (tugma `isPlayable` ga bog'liq, u esa `status` dan chiqadi).
 */
export function recordingDisplayStatusLabel(recording: Recording): string {
  const composition = recording.compositionStatus
  if (composition !== null && composition !== 'Completed') {
    return recordingCompositionLabel(composition)
  }
  return recordingStatusLabel(recording.status)
}

export function recordingDisplayStatusTone(recording: Recording): RecordingTone {
  const composition = recording.compositionStatus
  if (composition !== null && composition !== 'Completed') {
    return recordingCompositionTone(composition)
  }
  return recordingStatusTone(recording.status)
}

/**
 * Yozuv tungi montajni KUTYAPTIMI (ya'ni "fayl hali yo'q, lekin bo'ladi").
 *
 * Bu "xato" dan ATAYLAB ajratilgan: ikkalasida ham fayl yo'q, lekin
 * birinchisida qilinadigan ish — ertaga qarash, ikkinchisida — sababni
 * o'qish.
 */
export function isAwaitingComposition(recording: Recording): boolean {
  const composition = recording.compositionStatus
  return composition === 'Queued' || composition === 'Running'
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
