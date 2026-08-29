import { http } from '@/shared/api'
import type {
  AuthResponse,
  PhoneCodeRequest,
  PhoneCodeResponse,
  PhoneVerifyRequest,
  TelegramLoginStartResponse,
  TelegramLoginStatusResponse,
  TelegramLoginVerifyRequest,
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

/*
  ══════════════════════════════════════════════════════════════════════════
  BOT ORQALI KIRISH — DEEP-LINK OQIMI (2026-08-28)

  ★ NEGA AYNAN SHU YERDA, telefon oqimining yonida: bu UCHINCHI auth yo'li
    EMAS, o'sha yagona yo'lning yana bir "eshigi" — javob ham AYNI
    `AuthResponse`, tokenlar ham AYNI joyda saqlanadi.

  ⚠️ TELEFON OQIMI OLIB TASHLANMADI — u ZAXIRA yo'l bo'lib qoladi (bot
     bloklangan, havola ochilmagan yoki Telegram boshqa qurilmada).
  ══════════════════════════════════════════════════════════════════════════
*/

/**
 * 1-QADAM: `POST /api/v1/auth/telegram/start` — chipta va bot havolasi.
 *
 * Tanasi YO'Q: foydalanuvchidan hech narsa so'ralmaydi. `503` — bot
 * sozlanmagan (bunda interfeys telefon oqimiga tushishi kerak).
 */
export function startTelegramLogin(): Promise<TelegramLoginStartResponse> {
  return http.post<TelegramLoginStartResponse>(
    '/api/v1/auth/telegram/start', undefined, { auth: false })
}

/**
 * 2-QADAM: `GET /api/v1/auth/telegram/status` — chipta holati.
 *
 * 🔴 HAR DOIM 200: noma'lum yoki eskirgan chipta ham `"yoq"` holati bilan
 * qaytadi. Bu so'rov bir necha soniyada takrorlanadi va uning xato
 * bo'lishi mijozda qayta urinish mantig'ini ikkilantirardi.
 */
export function fetchTelegramLoginStatus(
  token: string,
  options?: { signal?: AbortSignal },
): Promise<TelegramLoginStatusResponse> {
  return http.get<TelegramLoginStatusResponse>('/api/v1/auth/telegram/status', {
    query: { token },
    auth: false,
    signal: options?.signal,
  })
}

/**
 * 3-QADAM: `POST /api/v1/auth/telegram/verify` — kodni tasdiqlab, sessiya
 * ochadi. Javob AYNI `AuthResponse`.
 */
export function verifyTelegramLogin(
  payload: TelegramLoginVerifyRequest,
): Promise<AuthResponse> {
  return http.post<AuthResponse>('/api/v1/auth/telegram/verify', payload, { auth: false })
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
