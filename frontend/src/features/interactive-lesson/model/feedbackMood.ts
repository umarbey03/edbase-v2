import type { MascotMood } from '@/features/mascot'

/**
 * Natija → Nuri kayfiyati va qisqa gap (2026-10-29, rejaning 4.5-bandi).
 * Sof funksiya — test qilinadi, komponentda mantiq yo'q.
 *
 * 🔴 Gaplar neytral: hech qanday diniy atama yo'q (loyiha qoidasi).
 */
export interface Feedback {
  mood: MascotMood
  text: string
}

export function feedbackMood(right: number, total: number): Feedback {
  if (total <= 0) return { mood: 'encouraging', text: 'Davom etamiz!' }

  const ratio = right / total
  if (ratio >= 1) return { mood: 'celebrate', text: 'Zo‘r! Hammasi to‘g‘ri.' }
  if (ratio >= 0.7) return { mood: 'happy', text: 'Yaxshi! Bitta-ikkita xatoni qayta ko‘ring.' }
  if (ratio >= 0.4) return { mood: 'encouraging', text: 'Yana bir marta tinglab ko‘ring.' }
  return { mood: 'concerned', text: 'Shoshilmang — avval audioni tinglang.' }
}

export function unitDoneFeedback(lessonNumber: number | null): Feedback {
  return {
    mood: 'celebrate',
    text: lessonNumber !== null ? `${lessonNumber}-dars tugadi! Zo‘r ish!` : 'Dars tugadi! Zo‘r ish!',
  }
}
