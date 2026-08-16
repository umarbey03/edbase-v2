import { http } from '@/shared/api'
import type { GroupBroadcastDto, PagedResult, SendGroupBroadcastRequest } from '@/shared/types'

const BASE = '/api/v1/broadcasts'

/**
 * GURUHLARGA XABAR YUBORISH — "Xabarlar" paneli (2026-08-16).
 *
 * ★ RUXSAT: server bu yo'lni FAQAT `Academic,Admin` bilan ochgan
 * (`GroupBroadcastsController.ManageRoles`) — ustoz/kurator bu ekranni
 * ko'rmaydi, guruh chatiga o'z tomonidan yozadi.
 */

export interface GroupBroadcastListParams {
  page?: number
  pageSize?: number
}

export function fetchGroupBroadcasts(
  params: GroupBroadcastListParams = {},
  options?: { signal?: AbortSignal },
): Promise<PagedResult<GroupBroadcastDto>> {
  return http.get<PagedResult<GroupBroadcastDto>>(BASE, {
    query: { Page: params.page, PageSize: params.pageSize },
    signal: options?.signal,
  })
}

export function sendGroupBroadcast(body: SendGroupBroadcastRequest): Promise<GroupBroadcastDto> {
  return http.post<GroupBroadcastDto>(BASE, body)
}
