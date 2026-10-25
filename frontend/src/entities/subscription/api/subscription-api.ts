import { http } from '@/shared/api'
import type { PaymentMethodName } from '@/shared/types'

import type {
  MySubscriptionDto,
  SaveSubscriptionPlanRequest,
  SubscriptionPeriodDto,
  SubscriptionPlanDto,
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

export function cancelSubscription(id: number, reason: string | null): Promise<SubscriptionPeriodDto> {
  return http.post<SubscriptionPeriodDto>(`${BASE}/${id}/cancel`, { reason })
}
