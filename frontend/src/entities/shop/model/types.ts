import type { PlanKindName } from '@/entities/subscription'

/**
 * ONLAYN SOTUV (2026-10-29) — `ShopController` shartnomasi.
 *
 * O'quvchi tarif tanlaydi → buyurtma kodi oladi → Payme/Click'da to'laydi →
 * xodim to'lovni tasdiqlaydi. Narx SERVERDA (buyurtmada muzlatilgan).
 */
export type PurchaseOrderStatusName = 'Pending' | 'Confirmed' | 'Rejected' | 'Cancelled'

export interface ShopPlanDto {
  id: number
  name: string
  kind: PlanKindName
  courseName: string
  months: number
  price: number
  /** Taklif matnining qatorlari. */
  features: string[]
  /** Tarifga interaktiv darslik kiradi (Mustaqil+). */
  includesInteractiveBook: boolean
}

export interface ShopOffersDto {
  plans: ShopPlanDto[]
  paymentInstructions: string | null
  paymeUrl: string | null
  clickUrl: string | null
  /** Milliy to'lovi uchun Rahmat havolasi (2026-10-31). */
  rahmatUrl: string | null
}

export interface ShopOrderDto {
  id: number
  code: string
  planId: number
  planName: string
  kind: PlanKindName
  courseName: string
  amount: number
  months: number
  status: PurchaseOrderStatusName
  createdAt: string
  decidedAt: string | null
  decisionNote: string | null
  /** O'quvchi «To'ladim» ni bosgan vaqt (tasdiq emas — signal). */
  paidClaimedAt: string | null
}

export interface ShopMeDto {
  inGroup: boolean
  hasActiveSubscription: boolean
  orders: ShopOrderDto[]
  /** O'quvchi balansi (2026-10-29) — yetarli bo'lsa «Balansdan to'lash» taklif qilinadi. */
  balance: number
  /** Hozir yurib turgan davrning tarifi — kartada «Joriy tarif». */
  currentPlanId: number | null
  currentPlanEndsAt: string | null
}

export interface StaffOrderDto {
  id: number
  code: string
  studentId: number
  studentName: string
  studentPhone: string | null
  studentTelegramUsername: string | null
  planName: string
  kind: PlanKindName
  courseName: string
  amount: number
  months: number
  status: PurchaseOrderStatusName
  createdAt: string
  decidedAt: string | null
  decidedByName: string | null
  decisionNote: string | null
  /** O'quvchi «To'ladim» ni bosgan vaqt (tasdiq emas — signal). */
  paidClaimedAt: string | null
}

const STATUS_LABELS: Record<PurchaseOrderStatusName, string> = {
  Pending: 'To‘lov kutilmoqda',
  Confirmed: 'Tasdiqlangan',
  Rejected: 'Rad etilgan',
  Cancelled: 'Bekor qilingan',
}

export function orderStatusLabel(status: PurchaseOrderStatusName): string {
  return STATUS_LABELS[status]
}

const STATUS_TONES = {
  Pending: 'warning',
  Confirmed: 'success',
  Rejected: 'danger',
  Cancelled: 'neutral',
} as const satisfies Record<PurchaseOrderStatusName, string>

export function orderStatusTone(status: PurchaseOrderStatusName): (typeof STATUS_TONES)[PurchaseOrderStatusName] {
  return STATUS_TONES[status]
}
