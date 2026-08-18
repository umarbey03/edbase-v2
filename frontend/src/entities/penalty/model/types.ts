import { lookup } from '@/shared/lib/lookup'

/**
 * JARIMALAR — yorliq va rang xaritalari (2026-08-18).
 *
 * ★ NAQSH `entities/group/model/types.ts` DAN: backend enum NOMINI satr
 * sifatida yuboradi, UI uni o'zbekcha yorliqqa aylantiradi. `lookup`
 * ishlatiladi — backend yangi qiymat qo'shsa UI qulamaydi.
 */
export type PenaltyTone = 'neutral' | 'success' | 'warning' | 'danger' | 'accent'

const KIND_LABELS: Record<string, string> = {
  LateStart: 'Kech boshlagan',
  MissedLesson: 'O‘tilmagan dars',
  Manual: 'Qo‘lda',
}

const KIND_TONES: Record<string, PenaltyTone> = {
  LateStart: 'warning',
  MissedLesson: 'danger',
  Manual: 'neutral',
}

const STATUS_LABELS: Record<string, string> = {
  Pending: 'Kutilmoqda',
  Approved: 'Tasdiqlangan',
  Cancelled: 'Bekor qilingan',
}

const STATUS_TONES: Record<string, PenaltyTone> = {
  Pending: 'warning',
  Approved: 'danger',
  Cancelled: 'neutral',
}

const ROLE_LABELS: Record<string, string> = {
  Teacher: 'Ustoz',
  Assistant: 'Kurator',
}

export function penaltyKindLabel(value: string): string {
  return lookup(KIND_LABELS, value, value)
}

export function penaltyKindTone(value: string): PenaltyTone {
  return lookup(KIND_TONES, value, 'neutral')
}

export function penaltyStatusLabel(value: string): string {
  return lookup(STATUS_LABELS, value, value)
}

/**
 * ★ TASDIQLANGAN — QIZIL, "muvaffaqiyat" YASHIL EMAS: bu holat xodim
 * uchun PUL YO'QOTISHNI bildiradi. Yashil nishon "hammasi joyida"
 * degan noto'g'ri xabar berardi.
 */
export function penaltyStatusTone(value: string): PenaltyTone {
  return lookup(STATUS_TONES, value, 'neutral')
}

export function staffRoleLabel(value: string): string {
  return lookup(ROLE_LABELS, value, value)
}

export const PENALTY_KIND_OPTIONS = [
  { value: 'LateStart', label: KIND_LABELS.LateStart! },
  { value: 'MissedLesson', label: KIND_LABELS.MissedLesson! },
  { value: 'Manual', label: KIND_LABELS.Manual! },
] as const

export const PENALTY_STATUS_OPTIONS = [
  { value: 'Pending', label: STATUS_LABELS.Pending! },
  { value: 'Approved', label: STATUS_LABELS.Approved! },
  { value: 'Cancelled', label: STATUS_LABELS.Cancelled! },
] as const
