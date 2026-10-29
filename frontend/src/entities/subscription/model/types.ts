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
  /** Tarifga interaktiv darslik kiradimi (Mustaqil+). */
  includesInteractiveBook: boolean
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
  includesInteractiveBook: boolean
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
  /** `null` — o'quvchi balansidan to'langan (2026-10-29), kassaga pul tushmagan. */
  method: PaymentMethodName | null
  note: string | null
  createdAt: string
  cancelledAt: string | null
  cancelReason: string | null
  planId: number | null
  /** Sotilgan paytdagi tarifda interaktiv darslik bormidi (muzlatilgan). */
  includesInteractiveBook: boolean
  /**
   * Hozirgi ishlatilmagan qiymati (pro-rata, pastga yaxlitlangan) — bekor
   * qilishda "qaytariladigan summa" taklifi. Faqat xodim ro'yxatida to'ldiriladi.
   */
  unusedValue: number
}

export interface MySubscriptionDto {
  hasSubscription: boolean
  isActive: boolean
  courseId: number | null
  courseName: string | null
  endsAt: string | null
  daysLeft: number | null
  history: SubscriptionPeriodDto[]
  /** Hozirgi tarif nomi («Mustaqil», «Mustaqil+»). */
  planName?: string | null
}

/** Davr holati: bekor qilingan, kelajakda, faol yoki tugagan. */
export type PeriodState = 'cancelled' | 'upcoming' | 'active' | 'ended'

/**
 * Obunani bekor qilganda pul bilan nima qilinadi (2026-10-29, Q-5):
 * qaytarilmaydi, balansga o'tadi yoki naqd qaytariladi. Har ikkala
 * qaytarish ham moliya jurnaliga yoziladi.
 */
export type SubscriptionRefundModeName = 'None' | 'ToBalance' | 'Cash'

export const REFUND_MODE_LABELS: Record<SubscriptionRefundModeName, string> = {
  None: 'Pul qaytarilmaydi',
  ToBalance: 'Balansga o‘tkazish',
  Cash: 'Naqd qaytarildi',
}

/**
 * Guruhga qo'shishdan oldin: obunada necha kun va qancha pul qolgan
 * (2026-10-29, Q-2) — xodim "balansga o'tkazib qo'shilsinmi?" ni tasdiqlaydi.
 */
export interface GroupConversionDto {
  hasLiveSubscription: boolean
  daysLeft: number
  amount: number
}

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
