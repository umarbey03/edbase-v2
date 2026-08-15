import { http } from '@/shared/api'
import type {
  AvatarUploadedDto,
  PhoneChangeStatusDto,
  UserDto,
} from '@/shared/types'

/**
 * ============================================================================
 *  O'Z PROFILI — `/api/v1/profile` (2026-08-15)
 * ============================================================================
 *
 * 🔴 `user-api.ts` DAN ALOHIDA FAYL VA BU ATAYLAB. U yerdagi chaqiruvlar
 * XODIM vositasi: ular BOSHQA odamning profilini o'zgartiradi
 * (`/api/v1/users/{id}`) va faqat boshqaruv ekranlarida ishlatiladi. Bu
 * yerdagilar esa DOIM chaqiruvchining O'ZINI o'zgartiradi va `id` UMUMAN
 * uzatilmaydi — u serverda tokendan olinadi.
 *
 * Ikkalasi bitta faylda bo'lsa, chaqiruvchi noto'g'ri funksiyani tanlab
 * (`updateUser` o'rniga `updateProfileName`) jimgina boshqa odamning
 * ma'lumotini o'zgartirib qo'yishi mumkin edi.
 */

/** `PUT /api/v1/profile` — ism. */
export function updateProfileName(fullName: string): Promise<UserDto> {
  return http.put<UserDto>('/api/v1/profile', { fullName })
}

/**
 * `POST /api/v1/profile/avatar` — rasm yuklash (`multipart/form-data`).
 *
 * ★ TUR SERVERDA, MAZMUNDAN aniqlanadi: bu yerda `file.type` ga
 * tayanmaymiz — brauzer uni fayl KENGAYTMASIDAN taxmin qiladi va
 * `.png` deb nomlangan PDF ham `image/png` bo'lib ko'rinardi.
 */
export function uploadAvatar(file: File): Promise<AvatarUploadedDto> {
  const form = new FormData()
  form.append('file', file)

  return http.post<AvatarUploadedDto>('/api/v1/profile/avatar', form)
}

/** `DELETE /api/v1/profile/avatar` — rasmni olib tashlash (idempotent). */
export function removeAvatar(): Promise<void> {
  return http.delete<void>('/api/v1/profile/avatar')
}

/**
 * Foydalanuvchi rasmining MANZILI.
 *
 * 🔴 BU MANZILNI TO'G'RIDAN-TO'G'RI `<img src>` GA QO'YIB BO'LMAYDI:
 * endpoint `Authorization` sarlavhasini talab qiladi, brauzerning rasm
 * yuklovchisi esa uni YUBORMAYDI (dars mediasi va javob fayllaridagi
 * AYNI cheklov). Rasm `fetchAvatarBlob` orqali olinadi va `blob:` manzil
 * yasaladi.
 *
 * @param version `avatarUpdatedAt` — kesh buzish uchun (sabab
 * `UserDto.avatarUpdatedAt` izohida).
 */
export function avatarPath(userId: number, version: string | null): string {
  const suffix = version === null ? '' : `?v=${encodeURIComponent(version)}`
  return `/api/v1/profile/avatar/${userId}${suffix}`
}

/** Rasmni `Blob` sifatida oladi (`<img>` uchun `blob:` manzil yasaladi). */
export async function fetchAvatarBlob(userId: number, version: string | null): Promise<Blob> {
  const { blob } = await http.download(avatarPath(userId, version), `avatar-${userId}`)
  return blob
}

/* ------------------------------ telefon ---------------------------------- */

/**
 * `POST /api/v1/profile/phone` — almashtirishning 1-BOSQICHI.
 *
 * ⚠️ KOD SHU YERDA KELMAYDI. Javob "endi botga yangi raqamdan
 * «Raqamni ulashish» yuboring" degani — kod aynan o'sha Telegram
 * hisobiga boradi (sabab serverdagi `IPhoneChangeStore` izohida).
 */
export function requestPhoneChange(phone: string): Promise<PhoneChangeStatusDto> {
  return http.post<PhoneChangeStatusDto>('/api/v1/profile/phone', { phone })
}

/**
 * `GET /api/v1/profile/phone` — kutayotgan almashtirish holati.
 *
 * ★ SERVER 204 QAYTARADI (404 emas) — "so'rov yo'q" xato emas, oddiy
 * holat. `http.get` bo'sh tanani `null` ga aylantiradi.
 */
export async function fetchPhoneChange(options?: {
  signal?: AbortSignal
}): Promise<PhoneChangeStatusDto | null> {
  // ⚠️ `?? null` SHART: `parseBody` tanasiz javobda `undefined` qaytaradi,
  // chaqiruvchilar esa `null` bilan taqqoslaydi.
  const status = await http.get<PhoneChangeStatusDto | undefined>('/api/v1/profile/phone', {
    signal: options?.signal,
  })

  return status ?? null
}

/** `DELETE /api/v1/profile/phone` — bekor qilish (idempotent). */
export function cancelPhoneChange(): Promise<void> {
  return http.delete<void>('/api/v1/profile/phone')
}

/** `POST /api/v1/profile/phone/confirm` — 2-BOSQICH: Telegramga kelgan kod. */
export function confirmPhoneChange(code: string): Promise<UserDto> {
  return http.post<UserDto>('/api/v1/profile/phone/confirm', { code })
}
