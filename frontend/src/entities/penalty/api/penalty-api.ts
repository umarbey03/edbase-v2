import { http } from '@/shared/api'
import type {
  CancelPenaltyRequest,
  CreateManualPenaltyRequest,
  PagedResult,
  PenaltyByUserDto,
  PenaltyCategoryDto,
  PenaltyListParams,
  PenaltyReportDto,
  PenaltyRowDto,
  PenaltySummaryDto,
  SavePenaltyCategoryRequest,
} from '@/shared/types'

const BASE = '/api/v1/penalties'
const CATEGORIES = '/api/v1/penalty-categories'

/**
 * USTOZ/KURATOR JARIMALARI (2026-08-18).
 *
 * ⚠️ TASDIQLASH/BEKOR QILISH — FAQAT ADMIN (server 403 qaytaradi).
 * Ko'rish va qo'lda kiritish o'quv bo'limiga ham ochiq.
 */

function toQuery(params: PenaltyListParams): Record<string, string | number | undefined> {
  return {
    period: params.period,
    occurredOn: params.occurredOn,
    userId: params.userId,
    categoryId: params.categoryId,
    kind: params.kind,
    status: params.status,
    search: params.search,
    page: params.page,
    pageSize: params.pageSize,
  }
}

export function fetchPenalties(
  params: PenaltyListParams = {},
  options?: { signal?: AbortSignal },
): Promise<PagedResult<PenaltyRowDto>> {
  return http.get<PagedResult<PenaltyRowDto>>(BASE, {
    query: toQuery(params),
    signal: options?.signal,
  })
}

/** Yig'ma — AYNI filtr, lekin sahifalashsiz (butun to'plamni sanaydi). */
export function fetchPenaltySummary(
  params: PenaltyListParams = {},
  options?: { signal?: AbortSignal },
): Promise<PenaltySummaryDto> {
  const { page: _page, pageSize: _pageSize, ...rest } = params

  return http.get<PenaltySummaryDto>(`${BASE}/summary`, {
    query: toQuery(rest),
    signal: options?.signal,
  })
}

export function fetchPenaltiesByUser(
  params: PenaltyListParams = {},
  options?: { signal?: AbortSignal },
): Promise<PenaltyByUserDto[]> {
  const { page: _page, pageSize: _pageSize, ...rest } = params

  return http.get<PenaltyByUserDto[]>(`${BASE}/by-user`, {
    query: toQuery(rest),
    signal: options?.signal,
  })
}

export function createManualPenalty(body: CreateManualPenaltyRequest): Promise<PenaltyRowDto> {
  return http.post<PenaltyRowDto>(BASE, body)
}

/** Tasdiqlash — oylikka manfiy tuzatma yaratiladi (FAQAT admin). */
export function approvePenalty(id: number): Promise<PenaltyRowDto> {
  return http.post<PenaltyRowDto>(`${BASE}/${id}/approve`)
}

export function cancelPenalty(id: number, body: CancelPenaltyRequest = {}): Promise<PenaltyRowDto> {
  return http.post<PenaltyRowDto>(`${BASE}/${id}/cancel`, body)
}

/**
 * OYLIK HISOBOT — butun oy, SAHIFALANMAGAN.
 *
 * ★ Jadvaldan guruhlanmaydi: jadval 20 tadan keladi va undan
 * hisoblangan "jami" faqat birinchi sahifani qamrardi.
 */
export function fetchPenaltyReport(
  period: string,
  options?: { signal?: AbortSignal },
): Promise<PenaltyReportDto> {
  return http.get<PenaltyReportDto>(`${BASE}/report`, {
    query: { period },
    signal: options?.signal,
  })
}

/* ===== Tariflar katalogi ===== */

/** @param activeOnly Jarima kiritish oynasi uchun `true` (arxivlanganlarsiz). */
export function fetchPenaltyCategories(
  activeOnly = false,
  options?: { signal?: AbortSignal },
): Promise<PenaltyCategoryDto[]> {
  return http.get<PenaltyCategoryDto[]>(CATEGORIES, {
    query: { activeOnly },
    signal: options?.signal,
  })
}

/** ⚠️ FAQAT ADMIN (server 403 qaytaradi). */
export function createPenaltyCategory(
  body: SavePenaltyCategoryRequest,
): Promise<PenaltyCategoryDto> {
  return http.post<PenaltyCategoryDto>(CATEGORIES, body)
}

export function updatePenaltyCategory(
  id: number,
  body: SavePenaltyCategoryRequest,
): Promise<PenaltyCategoryDto> {
  return http.put<PenaltyCategoryDto>(`${CATEGORIES}/${id}`, body)
}

/** Ishlatilgan tarif o'chirilmaydi — ARXIVLANADI (server hal qiladi). */
export function deletePenaltyCategory(id: number): Promise<void> {
  return http.delete<void>(`${CATEGORIES}/${id}`)
}
