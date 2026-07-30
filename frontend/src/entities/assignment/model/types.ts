import { lookup } from '@/shared/lib/lookup'
import type { StudentAssignmentDto, SubmissionStatusName } from '@/shared/types'

export type AssignmentTone = 'accent' | 'success' | 'warning' | 'danger' | 'neutral'

const SUBMISSION_STATUS_LABELS: Record<SubmissionStatusName, string> = {
  Submitted: 'Topshirilgan',
  Graded: 'Baholangan',
}

const SUBMISSION_STATUS_TONES: Record<SubmissionStatusName, AssignmentTone> = {
  Submitted: 'warning',
  Graded: 'success',
}

export function submissionStatusLabel(value: string): string {
  return lookup(SUBMISSION_STATUS_LABELS, value, value)
}

export function submissionStatusTone(value: string): AssignmentTone {
  return lookup(SUBMISSION_STATUS_TONES, value, 'neutral')
}

/**
 * O'quvchi vazifasining bir qatorlik holati.
 *
 * `canSubmit=false` ning SABABI foydalanuvchiga aytilishi shart: aks holda
 * "nega tugma yo'q?" degan savol qo'llab-quvvatlashga tushadi. Sabablar
 * ustuvorligi: allaqachon baholangan -> topshirilgan -> dars qulflangan ->
 * muddat o'tgan.
 */
export interface AssignmentState {
  label: string
  tone: AssignmentTone
  /** Nima uchun topshirib bo'lmasligi (`null` — topshirsa bo'ladi). */
  blockedReason: string | null
}

export function assignmentState(item: StudentAssignmentDto): AssignmentState {
  const submission = item.mySubmission

  /*
    ★ "TOPSHIRSA BO'LADIMI" — SERVER QARORI (`canSubmit`), klient uni QAYTA
    HISOBLAMAYDI.

    Ilgari bu yerda `isOverdue` mustaqil to'siq sifatida ishlatilardi va bu
    serverga ZID edi: `AssignmentService.SubmitAsync` muddati o'tgan javobni
    RAD ETMAYDI, faqat `IsLate = true` deb belgilaydi (o'quvchi kech bo'lsa
    ham ishini topshirishi kerak — bu Domain izohida aniq yozilgan). Natijada
    kechikkan o'quvchida "Topshirish" tugmasi umuman chiqmasdi.

    Endi shoxlar faqat YORLIQ va SABAB MATNINI tanlaydi; to'sish yoki
    to'smaslikni esa `item.canSubmit` hal qiladi.
  */
  const blocked = !item.canSubmit

  if (submission !== null && submission.status === 'Graded') {
    return {
      label: `Baholandi: ${submission.score ?? 0} / ${item.maxScore}`,
      tone: 'success',
      blockedReason: blocked ? 'Ish baholangan.' : null,
    }
  }

  if (submission !== null) {
    return {
      label: 'Tekshiruvda',
      tone: 'warning',
      blockedReason: blocked ? 'Javobingiz yuborilgan, ustoz tekshirmoqda.' : null,
    }
  }

  if (!item.lessonUnlocked) {
    return {
      label: 'Dars qulflangan',
      tone: 'neutral',
      blockedReason: blocked ? 'Bu darsga hali ruxsat ochilmagan — oldingi darsni yakunlang.' : null,
    }
  }

  if (item.isOverdue) {
    return {
      label: 'Muddati o‘tgan',
      tone: 'danger',
      // Muddat o'tgan bo'lsa ham topshirish MUMKIN — javob "kechikkan" deb
      // belgilanadi va ustoz buni ko'rib turadi.
      blockedReason: blocked ? 'Topshirish muddati tugagan.' : null,
    }
  }

  if (blocked) {
    return { label: 'Yopiq', tone: 'neutral', blockedReason: 'Hozircha topshirib bo‘lmaydi.' }
  }

  return { label: 'Topshirish kerak', tone: 'accent', blockedReason: null }
}

/**
 * `AnswerFormats` .NET `[Flags]` enum — JSON'da `"Text, Image"` ko'rinishida
 * keladi, shuning uchun vergul bo'yicha ajratib tarjima qilamiz.
 */
const FORMAT_LABELS: Record<string, string> = {
  None: 'Yo‘q',
  Text: 'Matn',
  Image: 'Rasm',
  Audio: 'Audio',
}

export function answerFormatsLabel(value: string): string {
  const parts = value
    .split(',')
    .map((part) => part.trim())
    .filter((part) => part.length > 0)
  if (parts.length === 0) return ''
  return parts.map((part) => lookup(FORMAT_LABELS, part, part)).join(', ')
}

export function assignmentTitle(title: string | null, id: number): string {
  return title !== null && title.length > 0 ? title : `Vazifa #${id}`
}

/* ==========================================================================
   JAVOB FORMATLARI — `[Flags]` enum bilan ikki tomonlama ishlash.

   Server bayroqlar birlashmasini SATR sifatida beradi ("Text, Image") va
   AYNAN shu shaklda qabul ham qiladi (`JsonStringEnumConverter` vergulli
   nomlarni tushunadi). Ya'ni UI'da tanlash "belgilash qutilari" bilan
   bo'ladi, so'ng ro'yxat yana o'sha satrga yig'iladi — API assimetrik
   bo'lmasin.
   ========================================================================== */

export type AnswerFormatName = 'Text' | 'Image' | 'Audio'

/** Formani tanlash varianti (tartib: matn -> rasm -> audio). */
export const ANSWER_FORMAT_OPTIONS: ReadonlyArray<{
  value: AnswerFormatName
  label: string
  hint: string
}> = [
  { value: 'Text', label: 'Matn', hint: 'Yozma javob' },
  { value: 'Image', label: 'Rasm', hint: 'Daftar surati' },
  { value: 'Audio', label: 'Audio', hint: 'Talaffuz, qiroat' },
]

/**
 * `"Text, Image"` -> `['Text', 'Image']`.
 *
 * Natija ma'lum variantlar bo'yicha yig'iladi, ya'ni serverdan notanish nom
 * kelsa (`None` yoki kelajakda qo'shiladigan format) u shunchaki tushib
 * qoladi va UI qulamaydi. Tartib ham DOIM bir xil — serverdagi tartibga
 * bog'liq emas.
 */
export function parseAnswerFormats(value: string): AnswerFormatName[] {
  const parts = value.split(',').map((part) => part.trim())
  return ANSWER_FORMAT_OPTIONS.map((option) => option.value).filter((name) => parts.includes(name))
}

/**
 * `['Text', 'Image']` -> `"Text, Image"`.
 *
 * Bo'sh ro'yxat `"None"` beradi — server buni Domain qoidasi bilan rad etadi
 * ("Kamida bitta javob formati tanlanishi kerak", 409). Klient ham oldindan
 * to'sadi, lekin qoida SERVERDA qoladi.
 */
export function serializeAnswerFormats(formats: readonly AnswerFormatName[]): string {
  return formats.length === 0 ? 'None' : formats.join(', ')
}

export function allowsFormat(value: string, format: AnswerFormatName): boolean {
  return parseAnswerFormats(value).includes(format)
}

/* ==========================================================================
   ILOVA FAYLLARI — server chegaralarining nusxasi.

   Bu qiymatlar QOIDA EMAS, faqat OLDINDAN OGOHLANTIRISH: haqiqiy tekshiruv
   `SubmissionAttachmentReader` da, u turni fayl MAZMUNIDAN aniqlaydi. Klient
   tekshiruvi 10 MB faylni mobil internetda bir necha daqiqa yuklab, so'ng
   400 olishning oldini oladi.
   ========================================================================== */

/** Server: `Submission.MaxAttachments`. */
export const MAX_ATTACHMENTS = 5

/** Server: `SubmissionAttachmentReader.MaxImageBytes`. */
export const MAX_IMAGE_BYTES = 5 * 1024 * 1024

/** Server: `SubmissionAttachmentReader.MaxAudioBytes`. */
export const MAX_AUDIO_BYTES = 10 * 1024 * 1024

/**
 * Fayl tanlagichning `accept` atributi. Bo'sh satr — fayl umuman
 * kutilmaydi (faqat matnli vazifa).
 *
 * `accept` HIMOYA emas: foydalanuvchi oynada "barcha fayllar" ni tanlab
 * istalgan narsani ko'rsata oladi — shuning uchun server baribir turni
 * mazmundan tekshiradi.
 */
export function fileAcceptFor(formats: string): string {
  const accepted: string[] = []
  if (allowsFormat(formats, 'Image')) accepted.push('image/*')
  if (allowsFormat(formats, 'Audio')) accepted.push('audio/*')
  return accepted.join(',')
}

/**
 * Tanlangan fayllarning ochiq muammosi (`null` — muammo topilmadi).
 *
 * Fayl turi brauzer bergan MIME bo'yicha taxmin qilinadi: `audio/*` bo'lsa
 * ovoz, aks holda rasm. Bu taxmin, chunki iPhone'ning `.heic` fayli ko'pincha
 * bo'sh `type` bilan keladi — shuning uchun noaniq holatda rasm chegarasi
 * (qattiqroq) qo'llanadi va yakuniy qarorni server aytadi.
 */
export function validateAttachments(files: readonly File[], formats: string): string | null {
  if (files.length > MAX_ATTACHMENTS) {
    return `Bitta javobga ko‘pi bilan ${MAX_ATTACHMENTS} ta fayl ilova qilinadi.`
  }

  const imageAllowed = allowsFormat(formats, 'Image')
  const audioAllowed = allowsFormat(formats, 'Audio')

  for (const file of files) {
    const isAudio = file.type.startsWith('audio/')

    if (isAudio && !audioAllowed) return 'Bu vazifaga ovozli javob qabul qilinmaydi.'
    if (!isAudio && !imageAllowed) return 'Bu vazifaga rasm qabul qilinmaydi.'

    const limit = isAudio ? MAX_AUDIO_BYTES : MAX_IMAGE_BYTES
    if (file.size > limit) {
      const megabytes = limit / (1024 * 1024)
      const what = isAudio ? 'Ovoz' : 'Rasm'
      return `${what} hajmi ${megabytes} MB dan oshmasligi kerak: ${file.name}`
    }
  }

  return null
}
