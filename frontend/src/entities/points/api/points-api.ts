import { http } from '@/shared/api'

import type { MyPointsDto } from '../model/types'

/** `GET /api/v1/progress/points?period=YYYY-MM` — "Mening ballim". */
export function fetchMyPoints(period?: string, options?: { signal?: AbortSignal }): Promise<MyPointsDto> {
  return http.get<MyPointsDto>('/api/v1/progress/points', {
    query: { period },
    signal: options?.signal,
  })
}
