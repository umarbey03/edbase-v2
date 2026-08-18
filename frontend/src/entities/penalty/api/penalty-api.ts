import { http } from '@/shared/api'
import type {
  CancelPenaltyRequest,
  CreateManualPenaltyRequest,
  PagedResult,
  PenaltyByUserDto,
  PenaltyListParams,
  PenaltyRowDto,
  PenaltySummaryDto,
} from '@/shared/types'

const BASE = '/api/v1/penalties'

/**
 * USTOZ/KURATOR JARIMALARI (2026-08-18).
 *
 * ⚠️ TASDIQLASH/BEKOR QILISH — FAQAT ADMIN (server 403 qaytaradi).
 * Ko'rish va qo'lda kiritish o'quv bo'limiga ham ochiq.
 */

function toQuery(params: PenaltyListParams): Record<string, string | number | undefined> {
  return {
    period: params.period,
    userId: params.userId,
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
