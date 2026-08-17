import { http } from '@/shared/api'
import type { AvatarUploadedDto } from '@/shared/types'

/**
 * ============================================================================
 *  O'Z PROFIL RASMI — `/api/v1/profile` (2026-08-15, 2026-08-17 da qisqartirildi)
 * ============================================================================
 *
 * ⚠️ ISM VA TELEFONNI O'ZI TAHRIRLASH OLIB TASHLANDI (2026-08-17, loyiha
 * egasining qarori): "foydalanuvchi o'z ism familyasi va nomerini edit
 * qilish imkoniga ega bo'lmasligi kerak" — BARCHA rol uchun. Bu ikkala
 * maydonni endi FAQAT o'quv bo'limi/admin `user-api.ts` (`/api/v1/users/{id}`)
 * orqali o'zgartira oladi.
 *
 * 🔴 `user-api.ts` DAN ALOHIDA FAYL VA BU ATAYLAB. U yerdagi chaqiruvlar
 * XODIM vositasi: ular BOSHQA odamning profilini o'zgartiradi
 * (`/api/v1/users/{id}`) va faqat boshqaruv ekranlarida ishlatiladi. Bu
 * yerdagi (rasm) esa DOIM chaqiruvchining O'ZINI o'zgartiradi va `id`
 * UMUMAN uzatilmaydi — u serverda tokendan olinadi.
 */

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
