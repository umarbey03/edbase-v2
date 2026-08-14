import { http } from '@/shared/api'
import type {
  AuthResponse,
  PhoneCodeRequest,
  PhoneCodeResponse,
  PhoneVerifyRequest,
  UserDto,
} from '@/shared/types'

/**
 * `POST /api/v1/auth/phone/request-code` — anonim, rate-limit qo'llanadi.
 *
 * ══════════════════════════════════════════════════════════════════════
 * ⚠️ `login()` (email + parol) OLIB TASHLANDI — 2026-08-13, loyiha
 *    egasining qarori. Endpointning O'ZI ham yo'q.
 *
 * 🔴 JAVOB HAR DOIM 200 VA HAR DOIM BIR XIL — raqam bazada bor yoki
 *    yo'qligidan qat'i nazar. Interfeys HECH QACHON "bunday raqam
 *    topilmadi" deb ko'rsatmasin: server bu ma'lumotni ATAYLAB bermaydi
 *    (hisob sanashga qarshi). Uni mijozda "o'ylab topish" — masalan
 *    javob vaqtiga qarab taxmin qilish — himoyani bekor qilardi.
 *
 * ★ ISTISNOLAR: 429 (kvota, `Retry-After` bilan) va 503 (Telegram
 *   sozlanmagan). Ikkalasi ham RAQAMGA bog'liq emas, ya'ni hech nima
 *   oshkor qilmaydi.
 * ══════════════════════════════════════════════════════════════════════
 */
export function requestPhoneCode(payload: PhoneCodeRequest): Promise<PhoneCodeResponse> {
  return http.post<PhoneCodeResponse>('/api/v1/auth/phone/request-code', payload, { auth: false })
}

/**
 * `POST /api/v1/auth/phone/verify` — kodni tasdiqlab, sessiya ochadi.
 *
 * Javob AYNI `AuthResponse` — ya'ni tokenlar Telegram Mini App oqimi
 * bilan bir xil joyda va bir xil yo'l bilan saqlanadi.
 */
export function verifyPhoneCode(payload: PhoneVerifyRequest): Promise<AuthResponse> {
  return http.post<AuthResponse>('/api/v1/auth/phone/verify', payload, { auth: false })
}

/**
 * `POST /api/v1/telegram/mini-app/auth` — anonim, rate-limit qo'llanadi.
 *
 * ★ NEGA AYNAN SHU YERDA, `login()` NING YONIDA: bu IKKINCHI auth yo'li emas,
 * o'sha yagona yo'lning boshqa "eshigi" — javob ham AYNI `AuthResponse`,
 * tokenlar ham AYNI joyda saqlanadi va `/api/v1/auth/refresh` ikkalasi uchun
 * ham bir xil ishlaydi. Alohida modulga ajratilsa, birida tuzatilgan xato
 * ikkinchisida ochiq qolishi mumkin edi.
 *
 * 🔴 XAVFSIZLIK: tanada FAQAT `initData` bo'ladi. `telegramId`, telefon raqam
 * yoki boshqa shaxsni aniqlovchi maydon YUBORILMAYDI — foydalanuvchi kimligini
 * SERVER `initData` imzosidan aniqlaydi. Eski tizimning kritik zaifligi
 * (audit X-1) aynan shu yerda edi: telefon raqam frontenddan kelardi va uni
 * qo'lda o'zgartirib, boshqa odamning akkauntiga kirish mumkin edi.
 */
export function loginWithTelegram(initData: string): Promise<AuthResponse> {
  return http.post<AuthResponse>('/api/v1/telegram/mini-app/auth', { initData }, { auth: false })
}

/** SPEC 5: `POST /api/v1/auth/logout` — 204. */
export function logout(): Promise<void> {
  return http.post<void>('/api/v1/auth/logout')
}

/** SPEC 5: `GET /api/v1/auth/me` — `UserDto`. */
export function fetchMe(options?: { signal?: AbortSignal }): Promise<UserDto> {
  return http.get<UserDto>('/api/v1/auth/me', { signal: options?.signal })
}
