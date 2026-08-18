import { http } from '@/shared/api'
import type { GlobalSearchResultDto } from '@/shared/types'

const BASE = '/api/v1/search'

/**
 * GLOBAL QIDIRUV (2026-08-18) — bitta so'rovda barcha turlar.
 *
 * ★ `signal` MAJBURIY EMAS, lekin CHAQIRUVCHI uni BERISHI kerak:
 * foydalanuvchi yozishda davom etganda eski so'rov bekor qilinmasa,
 * javoblar tartibsiz qaytib natijalar sakrab turardi. TanStack Query
 * `queryKey` ichida debounce qilingan matn bo'lsa buni o'zi hal qiladi.
 */
export function globalSearch(
  q: string,
  options?: { limit?: number; type?: string; signal?: AbortSignal },
): Promise<GlobalSearchResultDto> {
  return http.get<GlobalSearchResultDto>(BASE, {
    query: { q, limit: options?.limit, type: options?.type },
    signal: options?.signal,
  })
}
