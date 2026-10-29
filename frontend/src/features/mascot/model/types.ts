/**
 * Maskot kayfiyatlari — yagona ro'yxat (2026-10-29).
 *
 * ★ NOMLAR INGLIZCHA, chunki ular KOD kaliti (prop, CSS klass, test).
 *   Ekranga hech qachon chiqmaydi — o'quvchi faqat rasm va gapni ko'radi.
 */
export type MascotMood =
  /** Yaxshi ketyapti — qo'l silkitadi. */
  | 'happy'
  /** Katta yutuq (1-3-o'rin, bonus, a'lo baho) — ikki qo'l tepada, uchqunlar. */
  | 'celebrate'
  /** Yangi yoki neytral holat — musht ko'tarib dalda beradi. */
  | 'encouraging'
  /** Qoldirilgan dars yoki kechikkan vazifa — xavotirda, lekin ayblamaydi. */
  | 'concerned'
  /** Bir necha kun faollik yo'q — mudroq, olov xira ("seni sog'indim"). */
  | 'sleepy'
  /** Dars ayni hozir ketyapti yoki muddati yaqin ish — o'ylayapti. */
  | 'thinking'

export const MASCOT_MOODS: readonly MascotMood[] = [
  'happy',
  'celebrate',
  'encouraging',
  'concerned',
  'sleepy',
  'thinking',
]
