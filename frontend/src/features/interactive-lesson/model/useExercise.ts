import { computed, ref } from 'vue'
import type { ComputedRef, Ref } from 'vue'

import type { CheckItemBody, CheckItemResult, FirstScore } from '@/entities/interactive-unit'
import { toUserMessage } from '@/shared/api'

import { useLessonContext } from './context'

/**
 * Bitta mashqning server bilan aloqasi (2026-10-29).
 *
 * ★ `pending` paytida takroriy bosish bloklanadi (sekin tarmoqda bir javob
 *   ikki marta ketmasin); 8 soniyadan oshsa `slow` — "Internet sekin".
 * ★ Birinchi urinish natijasi kelsa (`result.first`) — umumiy kontekstga
 *   yoziladi: mashq sarlavhasidagi nishon va birlik progressi yangilanadi.
 * ⚠️ Javoblar navbati (tarmoq uzilganda saqlab, keyin yuborish) — keyingi
 *   bosqich (rejaning 4.6-bandi). Hozir xato bo'lsa xabar ko'rsatiladi va
 *   o'quvchi qayta bosadi.
 */
export interface ExerciseApi {
  first: ComputedRef<FirstScore | undefined>
  pending: Ref<boolean>
  slow: Ref<boolean>
  error: Ref<string | null>
  submit(body: CheckItemBody): Promise<CheckItemResult | null>
}

const SLOW_AFTER_MS = 8000

export function useExercise(itemKey: () => string): ExerciseApi {
  const ctx = useLessonContext()
  const pending = ref(false)
  const slow = ref(false)
  const error = ref<string | null>(null)

  const first = computed(() => ctx.firstScores.value[itemKey()])

  async function submit(body: CheckItemBody): Promise<CheckItemResult | null> {
    if (pending.value) return null

    pending.value = true
    error.value = null
    const timer = setTimeout(() => {
      slow.value = true
    }, SLOW_AFTER_MS)

    try {
      const result = await ctx.adapter.check(itemKey(), body)
      if (result.first !== null) ctx.setFirst(itemKey(), result.first)
      return result
    } catch (e) {
      error.value = toUserMessage(e)
      return null
    } finally {
      clearTimeout(timer)
      pending.value = false
      slow.value = false
    }
  }

  return { first, pending, slow, error, submit }
}
