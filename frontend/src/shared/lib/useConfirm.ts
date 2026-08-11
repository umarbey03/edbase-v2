import { shallowRef } from 'vue'
import type { ShallowRef } from 'vue'

/**
 * IMPERATIV TASDIQLASH — `await confirm({...})`.
 *
 * Talab: *"Platformadagi har qanday edit, delete, change qilingan ma'lumotlar
 * tasdiqlashni so'rashi kerak. Ya'ni, delete button bosilganda rostdan ham
 * delete qilmoqchimisan deb so'rashi kerak."*
 *
 * NEGA IMPERATIV (Promise), nega deklarativ oyna emas: mavjud oqimlar
 * `useMutation` chaqiruvlari ustida qurilgan. Har biriga `open`/`pending`
 * holati va ikki emit qo'shish o'nlab sahifani qayta yozishni talab qilardi.
 * Promise bilan esa BITTA qator qo'shiladi:
 *
 * ```ts
 * const confirm = useConfirm()
 *
 * async function onRemove(): Promise<void> {
 *   const ok = await confirm({
 *     title: 'Guruhdan chiqarish',
 *     message: 'Aziza Karimova guruhdan chiqariladi. Davomat yozuvlari saqlanadi.',
 *     confirmLabel: 'Chiqarish',
 *     tone: 'danger',
 *     details: ['3 ta to‘lov yozuvi saqlanadi'],
 *   })
 *   if (!ok) return
 *   await removeMutation.mutateAsync(...)
 * }
 * ```
 *
 * QAYERDA TASDIQ SO'RALADI (reja B2 jadvali — hamma joyga oyna qo'yilsa
 * interfeys foydalanishga yaramaydi):
 *   • o'chirish, chiqarish, bekor qilish, bloklash, Telegram uzish, pul
 *     qaytarish → HAR DOIM, `tone: 'danger'`;
 *   • ma'lumotni almashtiruvchi saqlash (`PUT`) → HAR DOIM, `tone: 'primary'`,
 *     o'zgargan maydonlar `details` da;
 *   • yon ta'siri kattaligi (jadval qayta generatsiyasi, ±N dars) → HAR DOIM,
 *     `tone: 'warning'`, RAQAMLAR `details` da;
 *   • filtr, qidiruv, tab almashish, tartiblash, sahifalash → YO'Q;
 *   • forma to'ldirish jarayonidagi har maydon → YO'Q (saqlashda bir marta).
 *
 * 🔴 `window.confirm` ISHLATILMAYDI (sabab `ConfirmDeleteDialog.vue` izohida:
 * brauzer oynasi yopilgach server 409 sababini ko'rsatadigan joy qolmaydi).
 * Server xatosini OYNANING O'ZIDA ushlab turish kerak bo'lgan joyda
 * `ConfirmDeleteDialog` ishlatiladi — u ataylab saqlangan.
 */

export type ConfirmTone = 'danger' | 'warning' | 'primary'

export interface ConfirmOptions {
  title: string
  message: string
  /** Tasdiq tugmasi matni. Standart — "Tasdiqlash". */
  confirmLabel?: string
  cancelLabel?: string
  /** Amal og'irligi: ikonka va tugma rangini belgilaydi. Standart — `primary`. */
  tone?: ConfirmTone
  /** "Nima o'zgaradi / nima saqlanadi" ro'yxati. */
  details?: readonly string[]
}

/** Navbatdagi bitta so'rov: oyna uchun ma'lumot + kutayotgan Promise. */
export interface ConfirmQueueItem {
  id: number
  options: ConfirmOptions
  settle: (result: boolean) => void
}

/*
  ★ NAVBAT (queue) — NEGA KERAK: ikki tasdiq ketma-ket so'ralsa (masalan
  ro'yxatda tez-tez bosilgan ikki tugma, yoki bir amal ichida ikki savol),
  ikkinchi chaqiruv birinchi oynani ALMASHTIRIB, birinchi Promise'ni abadiy
  kutib qoldirardi — chaqiruvchi kod `await` da muzlab qolardi. Navbat bilan
  har so'rov o'z javobini oladi.
*/
const queue: ConfirmQueueItem[] = []
const current = shallowRef<ConfirmQueueItem | null>(null)
let nextId = 1

function pump(): void {
  if (current.value !== null) return
  current.value = queue.shift() ?? null
}

/**
 * Tasdiq so'raydi. `true` — foydalanuvchi tasdiqladi, `false` — bekor qildi
 * (ESC, fon bosilishi, "Bekor qilish" — hammasi `false`).
 *
 * Komponentdan TASHQARIDA ham chaqirilishi mumkin (store, router guard):
 * holat modul darajasida yashaydi, `ConfirmHost` esa `App.vue` da bitta.
 */
export function askConfirm(options: ConfirmOptions): Promise<boolean> {
  return new Promise<boolean>((resolve) => {
    queue.push({ id: nextId++, options, settle: resolve })
    pump()
  })
}

/**
 * Komponent ichidagi qulay shakl. Composable bo'lgani uchun kelajakda
 * kontekstga bog'lanishi mumkin (masalan drawer ichidagi oynani drawer
 * ustiga chiqarish) — chaqiruv joylari o'zgarmasin.
 */
export function useConfirm(): (options: ConfirmOptions) => Promise<boolean> {
  return askConfirm
}

/* -------------------------------------------------------------------------- */

/**
 * FAQAT `ConfirmHost.vue` uchun. Boshqa joyda ishlatilmaydi — host butun
 * ilovada BITTA bo'lishi kerak (`App.vue`), aks holda bir tasdiq ikki marta
 * chizilardi.
 */
export function useConfirmHostState(): {
  current: ShallowRef<ConfirmQueueItem | null>
  settle: (result: boolean) => void
} {
  function settle(result: boolean): void {
    const item = current.value
    if (item === null) return
    current.value = null
    item.settle(result)
    /*
      Navbatdagi keyingi oyna ORADAN keyin ochiladi: `current` avval `null`
      bo'ladi, ya'ni oyna yopiladi (fokus chaqiruvchi tugmaga qaytadi, skroll
      qulfi sanog'i nolga tushadi) va shundan keyin yangisi toza holatda
      ochiladi. Bir freymda almashtirilsa fokus va animatsiya "yopishib"
      qolardi.
    */
    if (queue.length > 0) requestAnimationFrame(pump)
  }

  return { current, settle }
}
