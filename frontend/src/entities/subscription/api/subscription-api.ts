import { http } from '@/shared/api'
import type { PaymentMethodName } from '@/shared/types'

import type {
  GroupConversionDto,
  MySubscriptionDto,
  SaveSubscriptionPlanRequest,
  SubscriptionPeriodDto,
  SubscriptionPlanDto,
  SubscriptionRefundModeName,
} from '../model/types'

const BASE = '/api/v1/subscriptions'

/** O'quvchining o'zi — obuna holati va tarixi. */
export function fetchMySubscription(options?: { signal?: AbortSignal }): Promise<MySubscriptionDto> {
  return http.get<MySubscriptionDto>(`${BASE}/me`, { signal: options?.signal })
}

export function fetchSubscriptionPlans(options?: { signal?: AbortSignal }): Promise<SubscriptionPlanDto[]> {
  return http.get<SubscriptionPlanDto[]>(`${BASE}/plans`, { signal: options?.signal })
}

export function createSubscriptionPlan(request: SaveSubscriptionPlanRequest): Promise<SubscriptionPlanDto> {
  return http.post<SubscriptionPlanDto>(`${BASE}/plans`, request)
}

export function updateSubscriptionPlan(id: number, request: SaveSubscriptionPlanRequest): Promise<SubscriptionPlanDto> {
  return http.put<SubscriptionPlanDto>(`${BASE}/plans/${id}`, request)
}

export function fetchStudentSubscriptions(
  studentId: number,
  options?: { signal?: AbortSignal },
): Promise<SubscriptionPeriodDto[]> {
  return http.get<SubscriptionPeriodDto[]>(`${BASE}/students/${studentId}`, { signal: options?.signal })
}

/** Obuna berish yoki uzaytirish (to'lov qo'lda qabul qilingan). */
export function grantSubscription(request: {
  studentId: number
  planId: number
  method: PaymentMethodName
  note?: string | null
}): Promise<SubscriptionPeriodDto> {
  return http.post<SubscriptionPeriodDto>(BASE, request)
}

/**
 * Obunani bekor qilish. `refundMode` — pul qayerga (2026-10-29): qaytarilmaydi,
 * balansga yoki naqd. Summa davr summasidan oshmaydi (server tekshiradi).
 */
export function cancelSubscription(
  id: number,
  request: { reason: string | null; refundMode: SubscriptionRefundModeName; refundAmount: number | null },
): Promise<SubscriptionPeriodDto> {
  return http.post<SubscriptionPeriodDto>(`${BASE}/${id}/cancel`, request)
}

/** Guruhga qo'shilsa obunadan qancha kun va pul balansga o'tadi — hech narsa o'zgartirmaydi. */
export function fetchGroupConversion(
  studentId: number,
  options?: { signal?: AbortSignal },
): Promise<GroupConversionDto> {
  return http.get<GroupConversionDto>(`${BASE}/students/${studentId}/group-conversion`, { signal: options?.signal })
}
