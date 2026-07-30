import { onScopeDispose, ref, watch } from 'vue'
import type { Ref } from 'vue'

/**
 * Manba ref'ining kechiktirilgan nusxasi.
 *
 * NEGA kerak: qidiruv maydoni har bosilgan harfda so'rov yuborsa, 1500 ta
 * foydalanuvchi bazasida server bekorga yuklanadi va natijalar tartibsiz
 * kelib qolishi mumkin. Kechikkan qiymat `queryKey` sifatida ishlatiladi.
 */
export function useDebounced<T>(source: Ref<T>, delayMs = 350): Readonly<Ref<T>> {
  const output = ref(source.value) as Ref<T>
  let timer: number | null = null

  watch(source, (value) => {
    if (timer !== null) window.clearTimeout(timer)
    timer = window.setTimeout(() => {
      output.value = value
    }, delayMs)
  })

  onScopeDispose(() => {
    if (timer !== null) window.clearTimeout(timer)
  })

  return output
}
