import { lookup } from '@/shared/lib/lookup'

/**
 * TO'KILISHLAR — yorliq va rang xaritalari.
 *
 * ★ ATAMALAR LOYIHA EGASINING SO'ZLARI BILAN (2026-08-17): *"to'kilgan,
 * muzlatilgan, to'xtatilgan, ko'chirilgan"*. Diqqat: mavjud a'zolik
 * nishonlarida (`entities/group`) `Paused` "To'xtatilgan" deb ataladi,
 * bu yerda esa "Muzlatilgan" — chunki bu paneldagi savol boshqa
 * ("nima bo'ldi?"), va loyiha egasi muzlatishni aynan shu so'z bilan
 * ajratdi.
 */
export type AttritionTone = 'neutral' | 'success' | 'warning' | 'danger' | 'accent'

const KIND_LABELS: Record<string, string> = {
  Joined: 'Qo‘shildi',
  Paused: 'Muzlatilgan',
  Resumed: 'Qaytdi',
  Stopped: 'Chiqarilgan',
  Moved: 'Ko‘chirilgan',
}

const KIND_TONES: Record<string, AttritionTone> = {
  Joined: 'success',
  Paused: 'warning',
  Resumed: 'accent',
  Stopped: 'danger',
  Moved: 'neutral',
}

export function eventKindLabel(value: string): string {
  return lookup(KIND_LABELS, value, value)
}

export function eventKindTone(value: string): AttritionTone {
  return lookup(KIND_TONES, value, 'neutral')
}

/**
 * Filtr variantlari.
 *
 * ★ TARTIB MA'NOLI: eng muhimi — "Chiqarilgan" (haqiqiy to'kilish) —
 * birinchi turadi, "Qo'shildi/Qaytdi" (yo'qotish EMAS) oxirida.
 */
export const EVENT_KIND_OPTIONS = [
  { value: 'Stopped', label: KIND_LABELS.Stopped! },
  { value: 'Paused', label: KIND_LABELS.Paused! },
  { value: 'Moved', label: KIND_LABELS.Moved! },
  { value: 'Joined', label: KIND_LABELS.Joined! },
  { value: 'Resumed', label: KIND_LABELS.Resumed! },
] as const

/** "Probniy" chegarasi — backend'dagi `GroupMembershipEvent.TrialLessonCount` bilan AYNI. */
export const TRIAL_LESSON_COUNT = 8

/**
 * O'tilgan dars soniga qarab qisqa yorliq.
 *
 * Loyiha egasi ta'rifi: *"probniy deganda har bir guruhni birinchi 8 darsi
 * tushuniladi. 8 darsdan to'kilmasdan o'qib ketgan o'quvchilar aktiv
 * hisoblanadi"*.
 */
export function trialLabel(isTrial: boolean): string {
  return isTrial ? 'Probniy' : 'Aktiv'
}

export function trialTone(isTrial: boolean): AttritionTone {
  return isTrial ? 'warning' : 'accent'
}
