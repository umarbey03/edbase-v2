import type { PaymentMethodName } from '@/shared/types'

/**
 * MUSTAQIL O'QISH OBUNASI (2026-10-23) — `SubscriptionsController` shartnomasi.
 *
 * Obuna — guruhsiz o'quvchiga bitta kursning video darslari, vazifa va
 * testlari. Guruh va obuna bir-birini taqiqlaydi (server qoidasi), ya'ni
 * obunasi bor o'quvchi har doim "mustaqil o'quvchi".
 */
/**
 * Tarif turi (2026-10-29, onlayn sotuv): mustaqil o'qish obunasi yoki
 * guruhdagi jonli kursning oylik to'lovi (pul balansga tushadi).
 */
export type PlanKindName = 'SelfStudy' | 'Group'

export const PLAN_KIND_LABELS: Record<PlanKindName, string> = {
  SelfStudy: 'Mustaqil o‘qish',
  Group: 'Guruhda jonli dars',
}

export interface SubscriptionPlanDto {
  id: number
  name: string
  courseId: number
  courseName: string
  months: number
  price: number
  isActive: boolean
  kind: PlanKindName
  /** Saytdagi taklif matni — har qatori alohida afzallik. */
  description: string | null
  /** Saytdagi tariflar ro'yxatida ko'rinadimi. */
  isPublic: boolean
}

export interface SaveSubscriptionPlanRequest {
  name: string
  courseId: number
  months: number
  price: number
  isActive: boolean
  kind: PlanKindName
  description: string | null
  isPublic: boolean
}

export interface SubscriptionPeriodDto {
  id: number
  studentId: number
  planName: string | null
  courseId: number
  courseName: string
  startsAt: string
  endsAt: string
  amount: number
  method: PaymentMethodName
  note: string | null
  createdAt: string
  cancelledAt: string | null
  cancelReason: string | null
}

export interface MySubscriptionDto {
  hasSubscription: boolean
  isActive: boolean
  courseId: number | null
  courseName: string | null
  endsAt: string | null
  daysLeft: number | null
  history: SubscriptionPeriodDto[]
}

/** Davr holati: bekor qilingan, kelajakda, faol yoki tugagan. */
export type PeriodState = 'cancelled' | 'upcoming' | 'active' | 'ended'

export function periodState(period: SubscriptionPeriodDto, now: number = Date.now()): PeriodState {
  if (period.cancelledAt !== null) return 'cancelled'
  if (new Date(period.startsAt).getTime() > now) return 'upcoming'
  if (new Date(period.endsAt).getTime() > now) return 'active'
  return 'ended'
}

const STATE_LABELS: Record<PeriodState, string> = {
  cancelled: 'Bekor qilingan',
  upcoming: 'Navbatda',
  active: 'Faol',
  ended: 'Tugagan',
}

export function periodStateLabel(state: PeriodState): string {
  return STATE_LABELS[state]
}
