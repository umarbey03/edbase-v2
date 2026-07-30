import { readonly, ref } from 'vue'
import type { DeepReadonly, Ref } from 'vue'

/**
 * Qisqa xabar ("toast") — eski ilovadagi `toast()` funksiyasining o'rnini
 * bosadi. O'quvchi qulflangan darsni bosganda AYNAN shu yerda sabab chiqadi.
 *
 * HOLAT MODUL DARAJASIDA: xabarni chaqiradigan joy (kurs sahifasi) va uni
 * chizadigan joy (`StudentShell`) — ikki xil komponent. Pinia store bu
 * ma'lumot uchun ortiqcha (bitta satr va bitta taymer), `provide/inject` esa
 * har chaqiruvchi komponentni shellga bog'lab qo'yardi.
 */
const TOAST_MS = 2400

const message = ref<string | null>(null)
let timer: number | null = null

export function showToast(text: string): void {
  message.value = text
  if (timer !== null) window.clearTimeout(timer)
  timer = window.setTimeout(() => {
    message.value = null
    timer = null
  }, TOAST_MS)
}

export function useToastMessage(): DeepReadonly<Ref<string | null>> {
  return readonly(message)
}
