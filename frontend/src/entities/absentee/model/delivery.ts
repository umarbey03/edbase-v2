import { lookup } from '@/shared/lib/lookup'

/**
 * YETKAZILISH HOLATI — yorliq va rang (2026-08-18).
 *
 * ★ `Sent` YASHIL EMAS, NEYTRAL-KO'K: u "Telegram qabul qildi" degani,
 * "o'quvchi o'qidi" EMAS. Yashil nishon kuratorga "ish tugadi" degan
 * noto'g'ri xabar berardi — o'qilganlik belgisi Telegram Bot API'da
 * umuman mavjud emas.
 *
 * ★ `NoTelegram` — XATO EMAS: o'quvchida Telegram ulanmagan, ya'ni
 * boshqa kanal (qo'ng'iroq) kerak. Uni qizil qilsak, texnik nosozlik
 * bilan chalkashardi.
 */
export type DeliveryTone = 'neutral' | 'success' | 'warning' | 'danger' | 'accent'

const LABELS: Record<string, string> = {
  Pending: 'Navbatda',
  Sent: 'Yuborildi',
  Failed: 'Yetkazilmadi',
  NoTelegram: 'Telegram yo‘q',
}

const TONES: Record<string, DeliveryTone> = {
  Pending: 'warning',
  Sent: 'accent',
  Failed: 'danger',
  NoTelegram: 'neutral',
}

export function deliveryLabel(value: string): string {
  return lookup(LABELS, value, value)
}

export function deliveryTone(value: string): DeliveryTone {
  return lookup(TONES, value, 'neutral')
}

/** Filtr uchun variantlar. */
export const DELIVERY_OPTIONS = [
  { value: 'Sent', label: LABELS.Sent! },
  { value: 'Pending', label: LABELS.Pending! },
  { value: 'Failed', label: LABELS.Failed! },
  { value: 'NoTelegram', label: LABELS.NoTelegram! },
] as const
