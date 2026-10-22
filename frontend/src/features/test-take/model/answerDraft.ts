import type { AnswerMap } from './answers'

/**
 * Test javoblari qoralamasi — `sessionStorage`da, urinish (`attemptId`)
 * bo'yicha.
 *
 * NEGA KERAK: javoblar faqat xotirada edi. Sahifa yangilansa (mobil
 * brauzer fon tabini o'zi qayta yuklaydi) barcha tanlovlar yo'qolar, server
 * taymeri esa davom etardi.
 *
 * `sessionStorage` — faqat shu tab va shu sessiya: boshqa qurilma yoki
 * boshqa foydalanuvchiga o'tmaydi. Saqlash yopiq bo'lishi mumkin (private
 * rejim, kvota) — shuning uchun har murojaat `try/catch` ichida va xato
 * jim o'tkaziladi: qoralama — qulaylik, majburiyat emas.
 */
const PREFIX = 'zn:test-draft:'

export function loadDraft(attemptId: number): AnswerMap {
  try {
    const raw = window.sessionStorage.getItem(PREFIX + attemptId)
    if (raw === null) return {}
    const parsed: unknown = JSON.parse(raw)
    return isAnswerMap(parsed) ? parsed : {}
  } catch {
    return {}
  }
}

export function saveDraft(attemptId: number, answers: AnswerMap): void {
  try {
    window.sessionStorage.setItem(PREFIX + attemptId, JSON.stringify(answers))
  } catch {
    // saqlash yopiq — qoralamasiz davom etamiz
  }
}

export function clearDraft(attemptId: number): void {
  try {
    window.sessionStorage.removeItem(PREFIX + attemptId)
  } catch {
    // saqlash yopiq — o'chiradigan narsa ham yo'q
  }
}

function isAnswerMap(value: unknown): value is AnswerMap {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) return false
  return Object.values(value).every(
    (ids) => Array.isArray(ids) && ids.every((id) => typeof id === 'number'),
  )
}
