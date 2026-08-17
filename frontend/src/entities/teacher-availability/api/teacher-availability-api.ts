import { http } from '@/shared/api'
import type { TeacherAvailabilityTodayDto } from '@/shared/types'

const BASE = '/api/v1/teacher-availability'

/**
 * USTOZ KUNLIK TASDIQLASH + O'RINBOSAR (2026-08-17) — o'quv bo'limi paneli.
 * Suhbat mantig'i (savol/javob, o'rinbosar qidirish) BUTUNLAY Telegram bot
 * orqali; bu faqat BUGUNGI holatni o'qiydi.
 */
export function fetchTeacherAvailabilityToday(
  options?: { signal?: AbortSignal },
): Promise<TeacherAvailabilityTodayDto[]> {
  return http.get<TeacherAvailabilityTodayDto[]>(`${BASE}/today`, { signal: options?.signal })
}
