import { http } from '@/shared/api'
import type { ClassroomDto } from '@/shared/types'

/**
 * "MENING GURUHIM" OYNASI (2026-08-17) — `/api/v1/students/me/classroom`.
 *
 * 🔴 FAQAT O'QUVCHI: server rolni tekshiradi, bu yerda qo'shimcha
 * tekshiruv yo'q (marshrut o'quvchi karkasida joylashgani yetarli).
 */
export function fetchClassroom(options?: { signal?: AbortSignal }): Promise<ClassroomDto> {
  return http.get<ClassroomDto>('/api/v1/students/me/classroom', { signal: options?.signal })
}
