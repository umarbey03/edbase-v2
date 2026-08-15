import { readonly, ref } from 'vue'
import type { DeepReadonly, Ref } from 'vue'

/**
 * ============================================================================
 *  QISQA XABAR (TOAST) — ILOVA BO'YLAB YAGONA
 * ============================================================================
 *
 * Loyiha egasi (2026-08-15): *"telefon holatida tepada, desktop va planshet
 * holatida tepa o'ng tarafda kerakli rang va yozuv bilan alert chiqsin —
 * masalan rasm yuklansa yoki ism o'zgartirib saqlansa, ish tasdiqlanib
 * bajarilganini bildirish uchun"*.
 *
 * ── NIMA UCHUN `features/student-toast` O'RNIGA `shared` ───────────────────
 *
 * Eski toast O'QUVCHI karkasiga qotib qolgan edi (`StudentToast.vue` faqat
 * `StudentShell` da chizilardi). Natijada XODIM panelida toast UMUMAN
 * ishlamasdi — `LessonsTab` izohida "v2 xodim karkasida toast yo'q" deb
 * yozib ham qo'yilgan edi.
 *
 * Profil oynasi esa IKKALA karkasda ham ochiladi, ya'ni "saqlandi"
 * xabarini bitta karkasga bog'lab bo'lmaydi. Shuning uchun mexanizm
 * `shared` ga ko'chirildi va host `App.vue` da BIR MARTA chiziladi —
 * `ConfirmHost` bilan AYNI naqsh.
 *
 * ── NIMA UCHUN NAVBAT (massiv), nega bitta satr emas ───────────────────────
 *
 * Eski versiyada bitta `message` bor edi va ikkinchi xabar birinchisini
 * ALMASHTIRARDI. Profil oynasida esa foydalanuvchi ketma-ket ikki amal
 * qilishi mumkin (rasm yukladi, keyin ismni saqladi) — bunda birinchi
 * tasdiq ko'rinmay yo'qolardi.
 *
 * ★ CHEGARA (<see cref="MAX_VISIBLE"/>): uchtadan ko'p toast ekranning
 * yarmini egallaydi va u ogohlantirishdan ko'ra to'siqqa aylanadi.
 */

export type ToastTone = 'success' | 'error' | 'warning' | 'info'

export interface ToastItem {
  id: number
  text: string
  tone: ToastTone
}

/** Bir vaqtda ko'rinadigan eng ko'p xabar. */
const MAX_VISIBLE = 3

/**
 * Xabar qancha turadi.
 *
 * ★ XATO UZOQROQ TURADI: muvaffaqiyat xabari tasdiq (foydalanuvchi nima
 * bo'lganini allaqachon biladi), xato esa O'QILISHI kerak — unda sabab
 * va ba'zan keyingi qadam yozilgan bo'ladi.
 */
const DURATION: Record<ToastTone, number> = {
  success: 2600,
  info: 2600,
  warning: 4000,
  error: 5000,
}

const items = ref<ToastItem[]>([])
const timers = new Map<number, ReturnType<typeof setTimeout>>()

let nextId = 1

/** Xabarni yopadi (taymer bo'yicha yoki bosilganda). */
export function dismissToast(id: number): void {
  const timer = timers.get(id)

  if (timer !== undefined) {
    window.clearTimeout(timer)
    timers.delete(id)
  }

  items.value = items.value.filter((item) => item.id !== id)
}

/**
 * Xabar ko'rsatadi.
 *
 * @param text Foydalanuvchi o'qiydigan matn — QISQA va TUGALLANGAN
 * ("Rasm yangilandi"), texnik tafsilotsiz.
 * @param tone Standart — `success`: bu funksiya eng ko'p AMAL TASDIG'I
 * uchun chaqiriladi. Xato yo'llarida `error` ATAYLAB oshkora yoziladi,
 * shunda chaqiruv joyida "bu xato yo'li" ekani ko'rinib turadi.
 */
export function showToast(text: string, tone: ToastTone = 'success'): void {
  const id = nextId++

  items.value = [...items.value, { id, text, tone }]

  // Eng eskisi chiqarib tashlanadi — quyida emas, SHU YERDA: aks holda
  // uning taymeri osilib qolardi.
  while (items.value.length > MAX_VISIBLE) {
    const oldest = items.value[0]
    if (oldest === undefined) break
    dismissToast(oldest.id)
  }

  timers.set(id, setTimeout(() => {
    dismissToast(id)
  }, DURATION[tone]))
}

/**
 * FAQAT `ToastHost.vue` uchun. Boshqa joyda ishlatilmaydi — host butun
 * ilovada BITTA bo'lishi kerak (`ConfirmHost` dagi AYNI qoida), aks holda
 * har xabar ikki marta chizilardi.
 */
export function useToasts(): DeepReadonly<Ref<ToastItem[]>> {
  return readonly(items)
}
