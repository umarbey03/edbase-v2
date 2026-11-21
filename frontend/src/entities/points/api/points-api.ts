import { http } from '@/shared/api'

import type { LessonLettersDto, MyLettersDto, MyStreakDto } from '../model/streak'
import type { MyPointsDto } from '../model/types'

/** `GET /api/v1/progress/points?period=YYYY-MM` — "Mening ballim". */
export function fetchMyPoints(period?: string, options?: { signal?: AbortSignal }): Promise<MyPointsDto> {
  return http.get<MyPointsDto>('/api/v1/progress/points', {
    query: { period },
    signal: options?.signal,
  })
}

/** `GET /api/v1/progress/streak` — Mustaqil kunlik seriya (2026-11-21). */
export function fetchMyStreak(options?: { signal?: AbortSignal }): Promise<MyStreakDto> {
  return http.get<MyStreakDto>('/api/v1/progress/streak', { signal: options?.signal })
}

/** `GET /api/v1/progress/letters` — profil harflar jadvali (2026-11-21). */
export function fetchMyLetters(options?: { signal?: AbortSignal }): Promise<MyLettersDto> {
  return http.get<MyLettersDto>('/api/v1/progress/letters', { signal: options?.signal })
}

/** Kurs quruvchi: darslar va ularning harflari. */
export function fetchCourseLetters(courseId: number, options?: { signal?: AbortSignal }): Promise<LessonLettersDto[]> {
  return http.get<LessonLettersDto[]>(`/api/v1/courses/${courseId}/letters`, { signal: options?.signal })
}

export function saveLessonLetters(courseId: number, lessonId: number, letters: string | null): Promise<LessonLettersDto> {
  return http.put<LessonLettersDto>(`/api/v1/courses/${courseId}/lessons/${lessonId}/letters`, { letters })
}
