import { http } from '@/shared/api'
import type { AttendanceSummaryDto } from '@/shared/types'

const BASE = '/api/v1/progress'

/**
 * `GET /api/v1/progress/attendance` — o'quvchining davomat xulosasi.
 *
 * Uch chelak qaytadi: `overall` (hammasi), `teacher` va `assistant`.
 * Bosh sahifadagi doira `overall` ni ko'rsatadi — eski ilovada ham shunday
 * edi. `groupId` berilmasa server o'quvchining barcha faol guruhlarini
 * qo'shib hisoblaydi.
 */
export function fetchAttendanceSummary(
  params: { groupId?: number; from?: string; to?: string } = {},
  options?: { signal?: AbortSignal },
): Promise<AttendanceSummaryDto> {
  return http.get<AttendanceSummaryDto>(`${BASE}/attendance`, {
    query: { groupId: params.groupId, from: params.from, to: params.to },
    signal: options?.signal,
  })
}
