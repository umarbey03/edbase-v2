import { lookup } from '@/shared/lib/lookup'
import type {
  CourseDto,
  CourseLessonDto,
  CourseModuleDto,
  LessonAssetDto,
  LessonAssetKindName,
  LessonKindName,
  LessonLockReasonName,
} from '@/shared/types'

/** `BaseBadge` `tone` prop'i bilan mos qism to'plam. */
export type CourseTone = 'accent' | 'neutral' | 'success' | 'warning' | 'danger'

const LOCK_REASON_LABELS: Record<LessonLockReasonName, string> = {
  PreviousIncomplete: 'Oldingi dars tugatilmagan',
  TeacherPace: 'Ustoz hali bu darsga yetmagan',
  NotInCourse: 'Dars o‘quvchining kursida yo‘q',
  /*
    🔴 MATN ATAYLAB "oldingi darsni tugat" MAZMUNIDA EMAS: bu dars
    o'quvchining aybi bilan yopilmagan — guruh kursni shu qismdan
    boshlamagan (`Group.VideoStartLessonId`) va dars uning o'quv rejasiga
    UMUMAN kirmaydi. Server xabari ham aynan shunday ajratilgan
    (`GatingService`: "Guruhingiz kursni bu qismdan boshlamagan…").
  */
  BeforeGroupStart: 'Guruh bu qismdan boshlamaydi',
}

export function lessonLockReasonLabel(value: string): string {
  return lookup(LOCK_REASON_LABELS, value, 'Yopiq')
}

/** Kurs ro'yxatidagi "3 modul · 24 dars" satri. */
export function courseContentSummary(course: CourseDto): string {
  if (course.moduleCount === 0) return 'Kontent kiritilmagan'
  return `${course.moduleCount} modul · ${course.lessonCount} dars`
}

/**
 * Kursni o'chirish MUMKINMI — guruh biriktirilgan bo'lsa server 409 beradi.
 *
 * NEGA frontendda ham tekshiriladi: server qoidasi TAKRORLANMAYDI, shunchaki
 * tugma oldindan o'chiriladi. O'quvchi ishi bor-yo'qligini frontend bilmaydi,
 * shuning uchun bu YAKUNIY javob emas — 409 baribir ushlanadi.
 */
export function courseLooksDeletable(course: CourseDto): boolean {
  return course.groupCount === 0
}

/** Modul sarlavhasi ostidagi "5 dars" satri. */
export function moduleLessonSummary(module: CourseModuleDto): string {
  const count = module.lessons?.length ?? 0
  if (count === 0) return 'Dars yo‘q'
  return `${count} dars`
}

/** Dars qatoridagi "45 daq" — kiritilmagan bo'lsa chiziqcha. */
export function lessonDurationLabel(lesson: CourseLessonDto): string {
  return lesson.durationMin === null ? '—' : `${lesson.durationMin} daq`
}

/* ==========================================================================
   WAVE 2 · DARS TURI VA MEDIASI
   ========================================================================== */

const LESSON_KIND_LABELS: Record<LessonKindName, string> = {
  Normal: 'Odatiy',
  Exam: 'Imtihon',
}

export function lessonKindLabel(kind: string): string {
  return lookup(LESSON_KIND_LABELS, kind, 'Odatiy')
}

/**
 * Dars turi tanlagichi (segment tugma).
 *
 * `hint` — foydalanuvchi tanlashdan OLDIN oqibatini bilishi uchun: tur faqat
 * "nomlanish" emas, u darsga QANDAY fayl biriktirilishini belgilaydi.
 */
export const LESSON_KIND_OPTIONS: ReadonlyArray<{
  value: LessonKindName
  label: string
  hint: string
}> = [
  { value: 'Normal', label: 'Odatiy', hint: 'Video darslar (bir nechta qism)' },
  { value: 'Exam', label: 'Imtihon', hint: 'Rasmlar (savol varaqlari)' },
]

/**
 * 🔴 DOMAIN INVARIANTI: `Normal` -> faqat `Video`, `Exam` -> faqat `Image`.
 *
 * Qoida SERVERDA (`ModuleLesson.AllowedAssetKind`) va u yakuniy; bu funksiya
 * faqat UI matnini va `accept` ro'yxatini tanlash uchun. Ikkisi ajralib
 * ketmasligi uchun boshqa hech qanday joyda "video/rasm" shoxi yozilmaydi.
 */
export function allowedAssetKind(kind: LessonKindName): LessonAssetKindName {
  return kind === 'Exam' ? 'Image' : 'Video'
}

/**
 * Fayl tanlagichning `accept` atributi — SERVER ro'yxatining nusxasi
 * (`MediaSignatures`).
 *
 * ★ `accept` HIMOYA EMAS, faqat qulaylik: foydalanuvchi oynada "barcha
 * fayllar"ni tanlab istalgan narsani ko'rsata oladi va server baribir turni
 * MAZMUNDAN (sehrli baytlardan) aniqlaydi.
 *
 * ⚠️ RASM ro'yxati brifdagi uchtadan KENGROQ (`gif`, `heic` ham bor) va bu
 * ataylab: server `MediaCategories.Image` ni qabul qiladi, ya'ni GIF va HEIC
 * ham o'tadi. Ayniqsa HEIC muhim — iPhone'da surat STANDART shu formatda
 * saqlanadi, `accept` uni chetlab o'tsa xodim varaq suratini oynada UMUMAN
 * tanlay olmasdi.
 */
export const LESSON_VIDEO_ACCEPT = 'video/mp4,video/webm,video/quicktime'
export const LESSON_IMAGE_ACCEPT =
  'image/jpeg,image/png,image/webp,image/gif,image/heic,image/heif'

export function assetAcceptFor(kind: LessonKindName): string {
  return kind === 'Exam' ? LESSON_IMAGE_ACCEPT : LESSON_VIDEO_ACCEPT
}

/** Bitta darsga biriktiriladigan fayl soni (server: `MaxAssetsPerLesson`). */
export const MAX_LESSON_ASSETS = 50

/**
 * Media qatorining nomi. `title` bo'sh bo'lsa TARTIB raqamidan yasaladi —
 * "nomsiz" qator ro'yxatda bir-biridan ajralmasdi.
 */
export function assetTitleLabel(asset: LessonAssetDto, index: number): string {
  const title = (asset.title ?? '').trim()
  if (title.length > 0) return title
  return asset.kind === 'Image' ? `${index + 1}-rasm` : `${index + 1}-qism`
}

/**
 * Davomiylik: `1:05:03` yoki `12:34`. `null` — chiziqcha.
 *
 * ⚠️ Qiymat KLIENTDAN kelgan (server media dekoderi yo'q) — ko'rsatishdan
 * boshqa maqsadda ishlatilmaydi.
 */
export function assetDurationLabel(durationSec: number | null): string {
  if (durationSec === null || !Number.isFinite(durationSec) || durationSec <= 0) return '—'
  const total = Math.round(durationSec)
  const hours = Math.floor(total / 3600)
  const minutes = Math.floor((total % 3600) / 60)
  const seconds = total % 60
  const mm = hours > 0 ? String(minutes).padStart(2, '0') : String(minutes)
  return hours > 0
    ? `${hours}:${mm}:${String(seconds).padStart(2, '0')}`
    : `${mm}:${String(seconds).padStart(2, '0')}`
}

/** Dars qatoridagi "3 video" / "2 rasm" nishoni matni (bo'sh bo'lsa `null`). */
export function lessonAssetSummary(lesson: CourseLessonDto): string | null {
  const count = lesson.assets?.length ?? 0
  if (count === 0) return null
  return lesson.kind === 'Exam' ? `${count} rasm` : `${count} video`
}
