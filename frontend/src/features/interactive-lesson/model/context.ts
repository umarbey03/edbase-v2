import type { InjectionKey, Ref } from 'vue'
import { inject } from 'vue'

import type {
  CheckItemBody,
  CheckItemResult,
  FirstScore,
  MiniTestAnswer,
  MiniTestDto,
  MiniTestResult,
  Persona,
  UnitProgress,
  VocabWord,
} from '@/entities/interactive-unit'

import type { AudioPlayer } from './useAudioPlayer'

/**
 * ════════════════════════════════════════════════════════════════════════
 *  DARSLIK KONTEKSTI — bloklar va mashqlar uchun bitta "port" (2026-10-29)
 * ════════════════════════════════════════════════════════════════════════
 *
 * ★ NEGA ADAPTER: blok/mashq komponentlari serverni bilmaydi — ular faqat
 *   `check`, `blockDone`, `audioUrl` kabi amallarni chaqiradi. Haqiqiy
 *   sahifa adapterni API bilan to'ldiradi (`createApiAdapter`), dizayn
 *   tekshiruvi va kelajakdagi ustoz rejimi esa boshqa adapter beradi.
 *   Kalit (to'g'ri javob) baribir faqat serverdan keladi.
 */
export interface LessonAdapter {
  check(itemKey: string, body: CheckItemBody): Promise<CheckItemResult>
  blockDone(blockId: string): Promise<UnitProgress>
  reveal(blockId: string, n: number): Promise<string | null>
  startTest(): Promise<MiniTestDto>
  submitTest(answers: MiniTestAnswer[]): Promise<MiniTestResult>
  /** Audio belgisidan `<audio src>` manzili (chipta bilan). */
  audioUrl(handle: string): Promise<string>
}

export interface LessonContext {
  adapter: LessonAdapter
  audio: AudioPlayer
  /** Birinchi urinish natijalari (server progressidan + yangi tekshiruvlar). */
  firstScores: Ref<Record<string, FirstScore>>
  setFirst(itemKey: string, score: FirstScore): void
  personas: Map<string, Persona>
  words: Map<string, VocabWord>
}

export const LESSON_CONTEXT: InjectionKey<LessonContext> = Symbol('interactive-lesson')

export function useLessonContext(): LessonContext {
  const ctx = inject(LESSON_CONTEXT)
  if (ctx === undefined) throw new Error('Darslik konteksti topilmadi (InteractiveLessonView ichida ishlating).')
  return ctx
}

/** Ko'rsatma boshidagi arabcha qism ("اِسْتَمِعْ — yana tinglang") ajratiladi. */
export function splitInstruction(text: string | null): { ar: string | null; uz: string } {
  const value = text ?? ''
  const match = /^([؀-ۿ\sً-ٟ]+)\s*—\s*(.*)$/u.exec(value)
  return match !== null && match[1] !== undefined
    ? { ar: match[1].trim(), uz: match[2] ?? '' }
    : { ar: null, uz: value }
}

export function hasArabic(text: string | null | undefined): boolean {
  return /[؀-ۿ]/u.test(text ?? '')
}
