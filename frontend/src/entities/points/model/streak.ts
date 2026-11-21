/**
 * Mustaqil o'quvchining kunlik seriyasi va profil harflar jadvali
 * (2026-11-21) — server `DailyStreakService` / `LetterProgressService`.
 */

export type StreakDayState = 'Active' | 'Frozen' | 'Missed' | 'Today'

export interface StreakDayDto {
  date: string
  state: StreakDayState
}

export interface MyStreakDto {
  /** Faqat Mustaqil o'quvchida `true`. */
  enabled: boolean
  current: number
  best: number
  doneToday: boolean
  freezeAvailable: boolean
  nextBonusDay: number
  nextBonusPoints: number
  days: StreakDayDto[]
  /** Haftalik maqsad (2026-11-21): shu haftadagi faol kunlar / maqsad. */
  weekActiveDays: number
  weekGoalDays: number
  weekGoalBonus: number
}

export type LetterState = 'Mastered' | 'Learning' | 'Locked'

export interface LetterProgressDto {
  letter: string
  state: LetterState
  lessonId: number | null
  lessonNumber: number | null
  lessonName: string | null
}

export interface MyLettersDto {
  mastered: number
  total: number
  letters: LetterProgressDto[]
}

export interface LessonLettersDto {
  lessonId: number
  moduleId: number
  number: number
  lessonName: string
  letters: string | null
}

/** Nishon (2026-11-21) — server `BadgeService`. */
export interface BadgeDto {
  code: string
  title: string
  description: string
  emoji: string
  earned: boolean
  progress: number
  target: number
  earnedAt: string | null
}

export interface MyBadgesDto {
  earned: number
  total: number
  badges: BadgeDto[]
}
