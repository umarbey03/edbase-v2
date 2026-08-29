import { ref } from 'vue'
import type { Ref } from 'vue'

/**
 * OXIRGI QIDIRUVLAR (2026-08-19).
 *
 * ★ NEGA KERAK: qidiruv oynasi bo'sh ochilganda ilgari faqat "nima
 * yozish mumkin" degan izoh turardi — ya'ni oyna hech narsa QILMASDAN
 * ochilardi. Amalda xodim kun davomida AYNI bir necha nomni qidiradi
 * (bir guruh, bir necha qarzdor o'quvchi), shuning uchun tarix bitta
 * bosishda qaytadigan bo'lsa, oyna ochilishining o'zi foyda beradi.
 *
 * ★ `localStorage` — SERVER EMAS: bu shaxsiy odat, boshqa qurilmaga
 * ko'chishi shart emas va u uchun jadval ochish (migratsiya, tozalash
 * siyosati, GDPR savoli) qilinayotgan ishga nomutanosib.
 *
 * ★ MODUL DARAJASIDAGI `ref`: oyna har ochilganda qayta yaratiladi,
 * lekin ro'yxat BITTA bo'lishi kerak. Komponent ichidagi holat bo'lsa,
 * navbardagi tugma va (kelajakda) boshqa kirish nuqtasi har biri o'z
 * nusxasini ko'rsatardi.
 */

const STORAGE_KEY = 'zinnur.recentSearches'

/**
 * Nechta saqlanadi.
 *
 * ★ 5 TA: ro'yxat oynaning YUQORI qismida turadi va undan pastda
 * "Tez o'tish" bandlari bor. 10 ta bo'lsa tarix butun ekranni egallab,
 * asosiy vazifani (qidirish) pastga surib yuborardi.
 */
const MAX_ITEMS = 5

/** Private/incognito rejimda `localStorage` `throw` qilishi mumkin. */
function safeStorage(): Storage | null {
  try {
    return window.localStorage
  } catch {
    return null
  }
}

function load(): string[] {
  const raw = safeStorage()?.getItem(STORAGE_KEY) ?? null
  if (raw === null) return []

  try {
    const parsed: unknown = JSON.parse(raw)

    // ★ TURI TEKSHIRILADI: `localStorage` — foydalanuvchi tahrirlashi
    //   mumkin bo'lgan joy. Ishonib olingan qiymat `.trim()` da yiqilib,
    //   butun oynani ochilmaydigan qilib qo'yardi.
    if (!Array.isArray(parsed)) return []

    return parsed.filter((item): item is string => typeof item === 'string').slice(0, MAX_ITEMS)
  } catch {
    return []
  }
}

function save(items: string[]): void {
  try {
    safeStorage()?.setItem(STORAGE_KEY, JSON.stringify(items))
  } catch {
    // Saqlanmasa ham joriy sessiyada ro'yxat ishlayveradi.
  }
}

const state = ref<string[]>(load())

/** Faqat o'qish uchun — o'zgartirish quyidagi ikki funksiya orqali. */
export const recentSearches: Readonly<Ref<string[]>> = state

/**
 * Qidiruvni tarixga yozadi.
 *
 * ★ FAQAT NATIJA OCHILGANDA chaqiriladi, har bosilgan harfda EMAS:
 * aks holda "d", "do", "don", "doni" ning hammasi tarixga tushib,
 * ro'yxat bir so'zning bo'lak-bo'laklaridan iborat bo'lib qolardi.
 */
export function rememberSearch(raw: string): void {
  const value = raw.trim()
  if (value.length === 0) return

  const lowered = value.toLocaleLowerCase()
  const next = [value, ...state.value.filter((item) => item.toLocaleLowerCase() !== lowered)]
    .slice(0, MAX_ITEMS)

  state.value = next
  save(next)
}

export function clearRecentSearches(): void {
  state.value = []
  save([])
}
