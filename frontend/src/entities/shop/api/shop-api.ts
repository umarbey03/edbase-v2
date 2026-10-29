import { http } from '@/shared/api'

import type {
  PurchaseOrderStatusName,
  ShopMeDto,
  ShopOffersDto,
  ShopOrderDto,
  StaffOrderDto,
} from '../model/types'

const BASE = '/api/v1/shop'

/** Saytdagi tariflar — login talab qilinmaydi. */
export function fetchShopOffers(options?: { signal?: AbortSignal }): Promise<ShopOffersDto> {
  return http.get<ShopOffersDto>(`${BASE}/offers`, { signal: options?.signal })
}

export function fetchShopMe(options?: { signal?: AbortSignal }): Promise<ShopMeDto> {
  return http.get<ShopMeDto>(`${BASE}/me`, { signal: options?.signal })
}

export function createShopOrder(planId: number): Promise<ShopOrderDto> {
  return http.post<ShopOrderDto>(`${BASE}/orders`, { planId })
}

export function cancelShopOrder(id: number): Promise<ShopOrderDto> {
  return http.post<ShopOrderDto>(`${BASE}/orders/${id}/cancel`, {})
}

/** «To'ladim» — xodimlarga tekshirish eslatmasi boradi. */
export function claimShopOrderPaid(id: number): Promise<ShopOrderDto> {
  return http.post<ShopOrderDto>(`${BASE}/orders/${id}/paid`, {})
}

export function fetchStaffOrders(
  status: PurchaseOrderStatusName | null,
  options?: { signal?: AbortSignal },
): Promise<StaffOrderDto[]> {
  const query = status === null ? '' : `?status=${status}`
  return http.get<StaffOrderDto[]>(`${BASE}/orders${query}`, { signal: options?.signal })
}

export function confirmShopOrder(id: number, note: string | null): Promise<StaffOrderDto> {
  return http.post<StaffOrderDto>(`${BASE}/orders/${id}/confirm`, { note })
}

export function rejectShopOrder(id: number, note: string | null): Promise<StaffOrderDto> {
  return http.post<StaffOrderDto>(`${BASE}/orders/${id}/reject`, { note })
}
