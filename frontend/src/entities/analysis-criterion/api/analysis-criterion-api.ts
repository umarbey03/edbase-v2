import { http } from '@/shared/api'
import type { AnalysisCriterionDto, SaveAnalysisCriterionRequest } from '@/shared/types'

const BASE = '/api/v1/analysis-criteria'

/**
 * `GET /api/v1/analysis-criteria` — dars tahlili mezonlari katalogi
 * (R29/R30 kengaytmasi), ko'rsatish tartibida.
 *
 * O'quv bo'limi "O'quv bo'limi sozlamalari" > "Mezonlar" bo'limida
 * boshqaradi; dars tahlili formasi (`SessionReviewModal`) shu ro'yxatdan
 * ball qo'yish uchun foydalanadi.
 */
export function fetchAnalysisCriteria(options?: {
  signal?: AbortSignal
}): Promise<AnalysisCriterionDto[]> {
  return http.get<AnalysisCriterionDto[]>(BASE, { signal: options?.signal })
}

export function createAnalysisCriterion(
  request: SaveAnalysisCriterionRequest,
): Promise<AnalysisCriterionDto> {
  return http.post<AnalysisCriterionDto>(BASE, request)
}

export function updateAnalysisCriterion(
  id: number,
  request: SaveAnalysisCriterionRequest,
): Promise<AnalysisCriterionDto> {
  return http.put<AnalysisCriterionDto>(`${BASE}/${id}`, request)
}

/** IDEMPOTENT: mezon bo'lmasa ham `204` (`deleteSessionReview` bilan AYNI naqsh). */
export function deleteAnalysisCriterion(id: number): Promise<void> {
  return http.delete<void>(`${BASE}/${id}`)
}
