import { http } from '@/shared/api'
import type { SettingDto, SettingsPageDto, UpdateSettingRequest } from '@/shared/types'

/**
 * Tizim sozlamalari API'si.
 *
 * ★ HAR KALIT — ALOHIDA RESURS. Butun sahifani bitta `PUT` bilan yuboradigan
 * endpoint ATAYLAB yo'q: `finance` sozlamasini saqlash paytida `storage`
 * maydonlari ham yuborilsa, ular boshqa muhandis o'sha payt o'zgartirgan
 * qiymatni jimgina eskisiga qaytarib qo'yardi. Shu sababli mijozda ham
 * "hammasini saqlash" tugmasi yo'q — har qatorning o'z tugmasi bor.
 *
 * Barcha yo'llar serverda `[Authorize(Roles = "Admin")]`; o'quv bo'limi ham
 * 403 oladi. Marshrut guard'i shu qoidaning NUSXASI, o'rnini bosuvchisi emas.
 */
const BASE = '/api/v1/settings'

/**
 * Kalit yo'l qismiga qo'yiladi.
 *
 * Bugungi kalitlar `finance.block_threshold` ko'rinishida va kodlashsiz ham
 * xavfsiz, lekin server kalitlar ro'yxatini kengaytirmoqda — `encodeURIComponent`
 * kelajakdagi noodatiy belgida yo'lni buzilishdan saqlaydi.
 */
function keyPath(key: string): string {
  return `${BASE}/${encodeURIComponent(key)}`
}

/**
 * `GET /settings` — butun ro'yxat, guruhlarga bo'lingan holda.
 *
 * ★ Bitta kalitni o'qish uchun `GET /settings/{key}` ham bor, lekin u bu
 * yerda O'RALMAGAN: saqlash va reset javobda YANGILANGAN `SettingDto` ni
 * o'zi qaytaradi, ya'ni bitta kalitni qayta so'rashga ehtiyoj tug'ilmaydi.
 * Ishlatilmaydigan funksiya esa kelajakda "bu qayerda chaqiriladi?" degan
 * ortiqcha savol bo'lardi.
 */
export function fetchSettings(options?: { signal?: AbortSignal }): Promise<SettingsPageDto> {
  return http.get<SettingsPageDto>(BASE, options)
}

/**
 * `PUT /settings/{key}` — javobda YANGILANGAN `SettingDto` qaytadi.
 *
 * ★ Javob TASHLAB YUBORILMAYDI: saqlangandan keyin `origin` `Database` ga
 * o'tadi va `updatedAt` to'ladi. Ularni qayta so'ramasdan javobdan olsak,
 * ekrandagi "manba" nishoni darhol to'g'ri bo'ladi.
 */
export function updateSetting(key: string, body: UpdateSettingRequest): Promise<SettingDto> {
  return http.put<SettingDto>(keyPath(key), body)
}

/**
 * `POST /settings/{key}/reset` — bazadagi ustki qiymatni o'chiradi.
 *
 * ★ "Standart qiymatni YOZISH" emas: yozuv o'chgach qiymat quyi manbaga
 * tushadi va `origin` `Environment` yoki `Default` bo'ladi (jonli tekshiruvda
 * `finance.block_threshold` reset'dan keyin `Environment` ga tushdi).
 * `origin` `Database` bo'lmaganda server 400 beradi — shuning uchun UI
 * tugmani bunday holatda umuman ko'rsatmaydi.
 */
export function resetSetting(key: string): Promise<SettingDto> {
  return http.post<SettingDto>(`${keyPath(key)}/reset`)
}
