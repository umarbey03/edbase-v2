import { http } from '@/shared/api'
import type { GroupCategoryDto, GroupCategoryWriteRequest } from '@/shared/types'

const BASE = '/api/v1/group-categories'

/**
 * GURUH KATEGORIYALARI (R21b) — o'quv yo'nalishlari lug'ati.
 *
 * ★ RUXSAT: server bu yo'lni `Teacher,Assistant,Academic,Admin` bilan
 * ochgan, o'zgartirishni esa `Academic,Admin` bilan. O'QUVCHI 403 oladi —
 * ya'ni bu funksiyalar o'quvchi ekranlaridan CHAQIRILMASLIGI kerak.
 */

export interface GroupCategoryListParams {
  /**
   * `true` — faqat faollar (tanlagichlar uchun). Berilmasa hammasi keladi
   * (boshqaruv ekrani arxivlanganlarni ham ko'rsatadi).
   */
  isActive?: boolean
}

/**
 * `GET /api/v1/group-categories`.
 *
 * ★ Javob — ODDIY MASSIV, `PagedResult` EMAS. Lug'at o'nlab qatordan iborat
 * va u tanlagichni to'liq to'ldirishi kerak; sahifalash bo'lsa har chaqiruv
 * joyi "pageSize nechta?" degan savolga o'zicha javob berardi.
 */
export function fetchGroupCategories(
  params: GroupCategoryListParams = {},
  options?: { signal?: AbortSignal },
): Promise<GroupCategoryDto[]> {
  return http.get<GroupCategoryDto[]>(BASE, {
    // Swagger'dagi query nomi BOSH HARF bilan (`IsActive`) — `fetchGroups`
    // dagi bilan AYNI kelishuv.
    query: { IsActive: params.isActive },
    signal: options?.signal,
  })
}

export function createGroupCategory(body: GroupCategoryWriteRequest): Promise<GroupCategoryDto> {
  return http.post<GroupCategoryDto>(BASE, body)
}

/**
 * `PUT /api/v1/group-categories/{id}` — TO'LIQ almashtirish (nom + faollik).
 * Tartib (`position`) bu yerda o'zgarmaydi.
 */
export function updateGroupCategory(
  id: number,
  body: GroupCategoryWriteRequest,
): Promise<GroupCategoryDto> {
  return http.put<GroupCategoryDto>(`${BASE}/${id}`, body)
}

/**
 * `DELETE /api/v1/group-categories/{id}`.
 *
 * 🔴 409: kategoriyaga guruh biriktirilgan. Server ataylab to'sadi — bazadagi
 * FK `SET NULL` bo'lgani uchun o'chirish jimgina muvaffaqiyatli tugab, o'nlab
 * guruh yorlig'ini yo'qotardi. Sabab `ProblemDetails.detail` da keladi va
 * u ARXIVLASHNI taklif qiladi.
 */
export function deleteGroupCategory(id: number): Promise<void> {
  return http.delete<void>(`${BASE}/${id}`)
}
