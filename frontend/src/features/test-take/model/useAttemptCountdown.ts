import { computed, onScopeDispose, ref, watch } from 'vue'
import type { ComputedRef, Ref } from 'vue'

import { formatCountdown } from '@/shared/lib/datetime'

export interface AttemptCountdown {
  /** Qolgan millisekund. `null` — vaqt chegarasi yo'q. */
  remainingMs: ComputedRef<number | null>
  /** `05:12` yoki `01:02:03`. `null` — chegara yo'q. */
  label: ComputedRef<string | null>
  /** Muddat o'tdimi (KLIENT soati bo'yicha — bu FAKT emas, TAXMIN). */
  expired: ComputedRef<boolean>
  /** Oxirgi 2 daqiqa — ko'rsatkichni qizartirish uchun. */
  urgent: ComputedRef<boolean>
}

/** Oxirgi 2 daqiqada taymer ogohlantirish rangiga o'tadi. */
const URGENT_MS = 2 * 60_000

/**
 * ★ TEST TAYMERI — FAQAT KO'RSATKICH.
 *
 * VAQT CHEGARASI SERVERDA: `TestAttempt.Deadline(test)` vaqt chegarasi bilan
 * `Test.DueAt` dan ERTAROG'INI oladi va `TestService.EnsureWithinTimeLimitAsync`
 * muddati o'tgan urinishni 0 ball bilan YOPADI. Eski tizimda taymer faqat
 * brauzerda edi: sahifani yangilash, tabni qayta ochish yoki DevTools bilan
 * taymerni to'xtatish testni cheksiz cho'zardi.
 *
 * Shuning uchun bu yerdagi hisob HECH QANDAY QAROR QABUL QILMAYDI:
 *   • `expired` — "topshirildi" degani EMAS, faqat "serverga yuborish vaqti
 *     keldi" degan ishora;
 *   • yakuniy javobni server aytadi (topshirish qabul qilinadi yoki 409).
 *
 * KLIENT SOATI SERVERDAN FARQ QILISHI MUMKIN (telefon vaqti noto'g'ri
 * sozlangan bo'lishi odatiy). Shu sababli ham qaror serverga qoldiriladi;
 * server esa `Test.SubmitGracePeriod` (60 s) tolerantligini `deadline` ichiga
 * ALLAQACHON qo'shib yuborgan, ya'ni bu yerda yana qo'shish KERAK EMAS —
 * aks holda tolerantlik ikki marta hisoblanardi.
 */
export function useAttemptCountdown(deadline: Ref<string | null>): AttemptCountdown {
  const nowMs = ref(Date.now())
  let timer: number | null = null

  const deadlineMs = computed<number | null>(() => {
    const iso = deadline.value
    if (iso === null) return null
    const parsed = new Date(iso).getTime()
    return Number.isNaN(parsed) ? null : parsed
  })

  function stop(): void {
    if (timer === null) return
    window.clearInterval(timer)
    timer = null
  }

  watch(
    deadlineMs,
    (value) => {
      stop()
      if (value === null) return
      nowMs.value = Date.now()
      timer = window.setInterval(() => {
        nowMs.value = Date.now()
      }, 1000)
    },
    { immediate: true },
  )

  onScopeDispose(stop)

  const remainingMs = computed<number | null>(() =>
    deadlineMs.value === null ? null : deadlineMs.value - nowMs.value,
  )

  const label = computed<string | null>(() =>
    remainingMs.value === null ? null : formatCountdown(Math.max(0, remainingMs.value)),
  )

  const expired = computed(() => remainingMs.value !== null && remainingMs.value <= 0)

  const urgent = computed(() => {
    const remaining = remainingMs.value
    return remaining !== null && remaining > 0 && remaining <= URGENT_MS
  })

  return { remainingMs, label, expired, urgent }
}
