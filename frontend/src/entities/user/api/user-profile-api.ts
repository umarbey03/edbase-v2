import { http } from '@/shared/api'
import type { TelegramUnlinkRequest, TelegramUnlinkResponse, UserProfileDto } from '@/shared/types'

const BASE = '/api/v1/users'

/**
 * `GET /api/v1/users/{id}/profile` — profil drawer'ining BUTUN mazmuni.
 *
 * ★ NEGA BITTA SO'ROV: drawer ochilganda 7 ta parallel so'rov yuborilsa
 * telefon internetida 2–3 sekund BO'SH panel ko'rinardi. Backend ataylab
 * agregat qilgan; frontend uni parchalamaydi.
 *
 * 🔴 RUXSAT SERVERDA KESILADI (frontendda yashirish YETARLI EMAS):
 *   • `Academic`/`Admin` — hamma blok;
 *   • `Teacher`/`Assistant` o'z guruhida — `finance: null`, begona guruh **403**;
 *   • `Student` o'zi — `notes: null` va `finance.transactions: null`.
 * Ya'ni `null` bo'lgan blok UMUMAN render qilinmaydi.
 */
export function fetchUserProfile(
  id: number,
  options?: { signal?: AbortSignal },
): Promise<UserProfileDto> {
  return http.get<UserProfileDto>(`${BASE}/${id}/profile`, { signal: options?.signal })
}

/**
 * `POST /api/v1/users/{id}/telegram/unlink` — faqat `Academic`/`Admin`.
 *
 * 🔴 YON TA'SIRI KATTA: server `TokenVersion` ni oshiradi, ya'ni o'quvchining
 * MAVJUD kirish tokeni DARHOL 401 bo'ladi va u platformaga kira olmaydi.
 * Shu sababli chaqiruvdan oldin `danger` tasdiq MAJBURIY.
 *
 * Xatolar: **403** — nishon Admin/Academic (uni faqat Admin uzadi) ·
 * **404** — foydalanuvchi yo'q · **409** — allaqachon bog'lanmagan
 * (sabab `problem.detail` da, `toUserMessage` o'zi o'qiydi).
 *
 * Sabab (`reason`) IXTIYORIY, lekin audit iziga tushadigan qaytarilmas
 * ma'lumot — UI uni so'raydi, majburlamaydi.
 */
export function unlinkTelegram(
  id: number,
  body: TelegramUnlinkRequest = {},
): Promise<TelegramUnlinkResponse> {
  return http.post<TelegramUnlinkResponse>(`${BASE}/${id}/telegram/unlink`, body)
}
