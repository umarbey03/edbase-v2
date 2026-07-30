import { lookup } from '@/shared/lib/lookup'
import type {
  AttemptStatusName,
  AvailableTestDto,
  MyResultDto,
  TestDto,
  TestKindName,
} from '@/shared/types'

export type TestTone = 'accent' | 'success' | 'warning' | 'danger' | 'neutral'

const TEST_KIND_LABELS: Record<TestKindName, string> = {
  Lesson: 'Dars testi',
  Competition: 'Musobaqa',
}

const ATTEMPT_LABELS: Record<AttemptStatusName, string> = {
  InProgress: 'Boshlangan',
  Submitted: 'Topshirilgan',
}

export function testKindLabel(value: string): string {
  return lookup(TEST_KIND_LABELS, value, value)
}

export function attemptStatusLabel(value: string): string {
  return lookup(ATTEMPT_LABELS, value, value)
}

/** Test holati: urinish bo'lmasa `myStatus` NULL keladi — "yangi" deb ko'rsatamiz. */
export function testStatusLabel(test: AvailableTestDto): string {
  if (test.myStatus === null) return test.canStart ? 'Yangi' : 'Yopiq'
  return lookup(ATTEMPT_LABELS, test.myStatus, test.myStatus)
}

export function testStatusTone(test: AvailableTestDto): TestTone {
  if (test.myStatus === 'Submitted') return 'success'
  if (test.myStatus === 'InProgress') return 'warning'
  return test.canStart ? 'accent' : 'neutral'
}

/** Sarlavhasiz test uchun o'rinbosar nom. Ro'yxat ham, yechish ekrani ham ishlatadi. */
export function testTitle(test: { id: number; title: string | null }): string {
  return test.title !== null && test.title.length > 0 ? test.title : `Test #${test.id}`
}

/**
 * O'quvchi nima uchun testni boshlay olmasligi (`null` — boshlasa bo'ladi).
 *
 * ★ QARORNI SERVER QABUL QILADI (`canStart`), bu funksiya faqat SABABNI
 * tanlaydi — qoida klientda TAKRORLANMAYDI. Sabab aytilmasa "nega tugma
 * yo'q?" savoli qo'llab-quvvatlashga tushadi (`assignmentState` da ham
 * aynan shu yondashuv).
 *
 * `canStart` server hisobi: savol bor, hali topshirilmagan va muddat
 * (tolerantlik bilan) o'tmagan.
 */
export function testBlockedReason(test: AvailableTestDto): string | null {
  if (test.canStart) return null

  if (test.myStatus === 'Submitted') return 'Bu testni allaqachon topshirgansiz.'
  if (test.questionCount === 0) return 'Testga hali savol qo‘shilmagan.'
  if (test.dueAt !== null) return 'Test topshirish muddati tugagan.'
  return 'Hozircha bu testni boshlab bo‘lmaydi.'
}

/** `12 / 20` — ball satri. Ball hali yo'q bo'lsa chiziqcha. */
export function scoreLabel(score: number | null, maxScore: number | null): string {
  if (score === null) return '—'
  const max = maxScore ?? 0
  return max > 0 ? `${score} / ${max}` : String(score)
}

/** Foiz — SERVER hisoblagan qiymat (Domain `TestAttempt.Percent`), qayta hisoblanmaydi. */
export function percentLabel(percent: number | null): string {
  return percent === null ? '—' : `${percent}%`
}

/**
 * Natija ohangi.
 *
 * DIQQAT: platformada "o'tish bali" degan qoida YO'Q (backendda ham yo'q),
 * shuning uchun bu faqat VIZUAL ishora — baho yoki hukm emas.
 */
export function resultTone(result: MyResultDto): TestTone {
  if (result.closedByTimeout) return 'danger'
  const percent = result.percent
  if (percent === null) return 'neutral'
  if (percent >= 80) return 'success'
  if (percent >= 60) return 'accent'
  return 'warning'
}

/* ==========================================================================
   TUZISH QOIDALARI — SERVER CHEGARALARINING NUSXASI.

   Bu qiymatlar QOIDA EMAS, faqat OLDINDAN OGOHLANTIRISH: yakuniy hakam
   Domain (`Test.Validate`, `TestQuestion.Validate`). Klient tekshiruvi
   xodim 20 ta variant yozib, so'ng 409 olishining oldini oladi.
   ========================================================================== */

/** Server: `Test.MaxTitleLength`. */
export const TEST_TITLE_MAX = 200

/** Server: `TestQuestion.MaxBodyLength`. */
export const QUESTION_BODY_MAX = 2000

/** Server: `TestOption.MaxBodyLength`. */
export const OPTION_BODY_MAX = 1000

/** Server: `TestQuestion.MinOptions`. */
export const MIN_OPTIONS = 2

/**
 * Test TUZILMASI qulflanganmi.
 *
 * Server: `TestService.EnsureNoAttemptsAsync` — o'quvchilar yechishni
 * boshlagan bo'lsa savol qo'shish/o'zgartirish/o'chirish va testni o'chirish
 * TAQIQLANADI (409): qo'yilgan ballar ma'nosini yo'qotardi.
 *
 * ★ DIQQAT: `attemptCount` faqat TOPSHIRILGAN urinishlarni sanaydi
 * (`Status == Submitted`), server esa BOSHLANGANLARINI ham hisobga oladi.
 * Ya'ni bu tekshiruv "aniq qulflangan" ni biladi, "aniq ochiq" ni EMAS —
 * shuning uchun 409 baribir ushlanib, foydalanuvchiga ko'rsatiladi.
 */
export function testStructureLocked(test: TestDto): boolean {
  return test.attemptCount > 0
}

/**
 * Testni e'lon qilishga tayyorligini tekshiradi (`null` — tayyor).
 *
 * Server `Test.Publish()` da har bir savolni QAYTA tekshiradi; bu yerda
 * faqat eng ko'p uchraydigan holat — bo'sh test — oldindan to'siladi.
 */
export function publishBlockedReason(test: TestDto): string | null {
  return test.questionCount === 0 ? 'Avval savol qo‘shing — bo‘sh test e’lon qilinmaydi.' : null
}

/** Savol formasidagi bitta variant qatori. */
export interface QuestionOptionDraft {
  body: string
  isCorrect: boolean
}

/**
 * Variantlar ro'yxatining ochiq muammosi (`null` — muammo topilmadi).
 *
 * Domain talabi: kamida 2 ta variant, matn bo'sh emas, kamida bittasi
 * to'g'ri. "Kamida bittasi" — bu KO'P TO'G'RI JAVOBGA ham ruxsat: bir
 * nechtasi belgilansa savol avtomatik "ko'p javobli" bo'ladi va o'quvchida
 * checkbox ko'rinadi (`TakeQuestionDto.multipleAnswers`).
 */
export function validateOptions(options: readonly QuestionOptionDraft[]): string | null {
  const filled = options.filter((option) => option.body.trim().length > 0)

  if (filled.length < MIN_OPTIONS) return `Kamida ${MIN_OPTIONS} ta variant matni kerak.`
  if (filled.some((option) => option.body.trim().length > OPTION_BODY_MAX)) {
    return `Variant matni ${OPTION_BODY_MAX} belgidan oshmasin.`
  }
  if (!filled.some((option) => option.isCorrect)) {
    return 'Kamida bitta to‘g‘ri variant belgilanishi kerak.'
  }
  return null
}

/** Nechta to'g'ri variant belgilangan — formadagi "ko'p javobli" nishoni uchun. */
export function correctCount(options: readonly QuestionOptionDraft[]): number {
  return options.filter((option) => option.isCorrect && option.body.trim().length > 0).length
}
