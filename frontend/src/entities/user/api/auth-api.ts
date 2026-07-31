import { http } from '@/shared/api'
import type { AuthResponse, LoginRequest, UserDto } from '@/shared/types'

/** SPEC 5: `POST /api/v1/auth/login` — anonim. */
export function login(payload: LoginRequest): Promise<AuthResponse> {
  return http.post<AuthResponse>('/api/v1/auth/login', payload, { auth: false })
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
