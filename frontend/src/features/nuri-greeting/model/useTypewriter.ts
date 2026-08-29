import { onScopeDispose, ref } from 'vue'
import type { Ref } from 'vue'

import { useMediaQuery } from '@/shared/lib/useBreakpoint'

/**
 * ════════════════════════════════════════════════════════════════════════
 * HARFMA-HARF YOZILISH — «Nuri gapiryapti» taassuroti
 * ════════════════════════════════════════════════════════════════════════
 *
 * ★ NEGA CSS EMAS, JS: CSS'dagi mashhur "typewriter" hiylasi
 *   (`width` ni qadamlab kengaytirish) FAQAT bitta qatorli va bir xil
 *   kenglikdagi shriftda ishlaydi. Bu yerda matn o'zgaruvchan uzunlikda,
 *   ikki-uch qatorga o'raladi va shrift proporsional — ya'ni o'sha hiyla
 *   matnni o'rtasidan kesib qo'yardi.
 *
 * 🔴 `prefers-reduced-motion` — MATN DARHOL TO'LIQ CHIQADI.
 *   `style.css` dagi global qoida `@keyframes` larni to'xtatadi, lekin bu
 *   animatsiya JS'da yuradi va u qoida bu yerga YETIB KELMAYDI. Vestibulyar
 *   buzilishi bo'lgan foydalanuvchi uchun "harakat" faqat bezak emas:
 *   sekin paydo bo'ladigan matn o'qishni ham qiyinlashtiradi (WCAG 2.2.2 —
 *   avtomatik yangilanadigan kontent).
 *
 * ★ SATRLAR EMAS, BELGILAR RO'YXATI (`Array.from`): matnda `‘` va `…`
 *   bor, kelajakda emoji ham qo'shilishi mumkin. `text[i]` surrogat juftni
 *   ikkiga bo'lib, ekranga buzilgan belgi chiqarardi.
 */

/**
 * Bitta belgi orasidagi vaqt.
 *
 * ★ 20ms = 50 belgi/sekund. Tanlov o'lchov bilan: eng uzun gap ~110
 *   belgi, ya'ni yozilish 2.2 sekund davom etadi. Sekinroq (masalan
 *   30ms) qilinsa u 3.3 sekundga cho'zilardi — kirish yo'lidagi
 *   oraliq ekran uchun bu allaqachon kutish bo'lib tuyuladi.
 */
const DEFAULT_SPEED_MS = 20

export interface Typewriter {
  /** Hozir ekranda ko'rinadigan qism. */
  visible: Ref<string>
  /** Yozib bo'lindimi (tugma shundan keyin paydo bo'ladi). */
  done: Ref<boolean>
  /** Matnni boshidan yoza boshlaydi. Qayta chaqirilsa avvalgisi bekor bo'ladi. */
  start: (text: string) => void
  /** Qolganini darhol chiqaradi — foydalanuvchi ekranga teginganda. */
  finish: () => void
}

export function useTypewriter(speedMs: number = DEFAULT_SPEED_MS): Typewriter {
  const visible = ref('')
  const done = ref(false)

  const reducedMotion = useMediaQuery('(prefers-reduced-motion: reduce)')

  let letters: string[] = []
  let index = 0
  let timer: ReturnType<typeof setInterval> | null = null

  function stopTimer(): void {
    if (timer !== null) {
      clearInterval(timer)
      timer = null
    }
  }

  function finish(): void {
    stopTimer()
    visible.value = letters.join('')
    done.value = true
  }

  function start(text: string): void {
    stopTimer()

    letters = Array.from(text)
    index = 0
    visible.value = ''
    done.value = false

    /*
      Bo'sh matn ham "tugagan" hisoblanadi: aks holda tugma umuman
      chiqmasdi va ekran boshi berk ko'chaga aylanardi.
    */
    if (reducedMotion.value || letters.length === 0) {
      finish()
      return
    }

    timer = setInterval(() => {
      index += 1
      visible.value = letters.slice(0, index).join('')
      if (index >= letters.length) finish()
    }, speedMs)
  }

  // Komponent yo'q qilinganda taymer qolib ketmasin.
  onScopeDispose(stopTimer)

  return { visible, done, start, finish }
}
