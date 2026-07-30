import type { SubmitTestRequest, TakeQuestionDto } from '@/shared/types'

/**
 * Savol -> tanlangan variant ID'lari.
 *
 * `Map` emas, oddiy obyekt: Vue reaktivligi `Map` bilan ham ishlaydi, lekin
 * `readonly` nusxa yasash (`{ ...current }`) bu yerda arzon va o'zgarish
 * har doim YANGI qiymat sifatida uzatiladi — shablon `v-model` siz ham
 * to'g'ri yangilanadi.
 */
export type AnswerMap = Readonly<Record<number, readonly number[]>>

export function selectedFor(answers: AnswerMap, questionId: number): readonly number[] {
  return answers[questionId] ?? []
}

export function isSelected(answers: AnswerMap, questionId: number, optionId: number): boolean {
  return selectedFor(answers, questionId).includes(optionId)
}

/**
 * Variantni belgilaydi/olib tashlaydi.
 *
 * ★ IKKI XIL SAVOL — IKKI XIL XATTI-HARAKAT:
 *  • `multipleAnswers = false` — bitta tanlov (radio): yangi variant eskisini
 *    ALMASHTIRADI;
 *  • `multipleAnswers = true`  — checkbox: tanlovlar TO'PLANADI va qayta
 *    bosilganda olib tashlanadi.
 *
 * NEGA MUHIM: eski tizim ko'p to'g'ri javobni `dict[question] = option`
 * ko'rinishida saqlardi va faqat OXIRGI tanlov hisoblanardi — o'quvchi
 * hamma to'g'ri variantni belgilab ham 0 ball olardi. Endi server ham
 * (`TestAnswer` da `(attempt, savol, variant)` unikal), Domain ham
 * (`TestQuestion.Score` to'plamlarni solishtiradi) to'plam bilan ishlaydi;
 * UI ham shunday bo'lishi shart, aks holda xato UI tomonda takrorlanardi.
 *
 * BITTA JAVOBLI SAVOLDA "BEKOR QILISH" YO'Q: radio elementi allaqachon
 * belgilangan variant qayta bosilganda `change` hodisasini BERMAYDI (HTML
 * spetsifikatsiyasi), ya'ni "qayta bosib tozalash" brauzerda ishonchli
 * ishlamasdi. Amalda bu zarar qilmaydi — noto'g'ri variant ham, javobsiz
 * savol ham 0 ball beradi.
 */
export function toggleOption(
  answers: AnswerMap,
  question: TakeQuestionDto,
  optionId: number,
): AnswerMap {
  if (!question.multipleAnswers) return { ...answers, [question.id]: [optionId] }

  const current = selectedFor(answers, question.id)
  const next = current.includes(optionId)
    ? current.filter((id) => id !== optionId)
    : [...current, optionId]

  return { ...answers, [question.id]: next }
}

/** Nechta savolga javob belgilangan (jarayon ko'rsatkichi uchun). */
export function answeredCount(
  answers: AnswerMap,
  questions: readonly TakeQuestionDto[],
): number {
  return questions.filter((question) => selectedFor(answers, question.id).length > 0).length
}

/**
 * Topshirish tanasi.
 *
 * FAQAT javob belgilangan savollar yuboriladi: server yuborilmagan savolni
 * javobsiz (0 ball) deb hisoblaydi (`TestAttempt.SubmitAnswers`), ya'ni bo'sh
 * ro'yxatlarni uzatishning ma'nosi yo'q. Tartib savollar tartibida qoladi —
 * so'rovni logdan o'qish osonroq bo'lsin.
 */
export function toSubmitRequest(
  answers: AnswerMap,
  questions: readonly TakeQuestionDto[],
): SubmitTestRequest {
  const filled = questions
    .map((question) => ({ questionId: question.id, optionIds: [...selectedFor(answers, question.id)] }))
    .filter((answer) => answer.optionIds.length > 0)

  return { answers: filled }
}
