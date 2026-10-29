<script setup lang="ts">
import { computed, reactive, ref } from 'vue'

import { hasArabic } from '../../model/context'
import { useExercise } from '../../model/useExercise'
import ArabicText from '../parts/ArabicText.vue'
import ExerciseShell from './ExerciseShell.vue'

/**
 * Jadvalni to'ldirish. Berilgan kataklar matn, qolganlari — kiritish maydoni.
 * "Tekshirish" butun jadvalni BIR so'rovda yuboradi; server har katak uchun
 * to'g'ri/noto'g'ri va to'g'ri javobni qaytaradi (harakatsiz yozilsa ham qabul).
 * ★ Jadval o'z ichida gorizontal aylanadi — sahifa 390 px da kengaymaydi.
 */
const props = defineProps<{
  itemKey: string
  korsatma: string | null
  ustunlar: string[]
  qatorlar: (string | null)[][]
}>()

const exercise = useExercise(() => props.itemKey)

const values = reactive<Record<string, string>>({})
const verdict = ref<Record<string, { correct: boolean; javob: string }> | null>(null)
const finished = computed(() => verdict.value !== null)

const key = (r: number, c: number): string => `${r}:${c}`

function cellState(r: number, c: number): boolean | null {
  const v = verdict.value?.[key(r, c)]
  return v === undefined ? null : v.correct
}

async function check(): Promise<void> {
  const cells: Record<string, string> = {}
  props.qatorlar.forEach((row, r) =>
    row.forEach((cell, c) => {
      if (cell === null) cells[key(r, c)] = values[key(r, c)] ?? ''
    }),
  )
  const result = await exercise.submit({ cells })
  if (result !== null) verdict.value = result.reveal.cells ?? {}
}

function retry(): void {
  verdict.value = null
  for (const k of Object.keys(values)) delete values[k]
}
</script>

<template>
  <ExerciseShell
    :korsatma="korsatma"
    :first="exercise.first.value"
    :finished="finished"
    :slow="exercise.slow.value"
    :error="exercise.error.value"
    @retry="retry"
  >
    <div class="mt-2 overflow-x-auto rounded-xl border border-line">
      <table class="min-w-full border-collapse text-sm">
        <thead>
          <tr>
            <th
              v-for="(c, ci) in ustunlar"
              :key="ci"
              class="whitespace-nowrap border-b border-line bg-ink-800 px-3 py-2 text-left font-semibold text-slate-300"
              v-text="c"
            />
          </tr>
        </thead>
        <tbody>
          <tr
            v-for="(row, r) in qatorlar"
            :key="r"
          >
            <td
              v-for="(cell, c) in row"
              :key="c"
              class="border-b border-line px-2 py-1.5 align-top"
            >
              <ArabicText
                v-if="cell !== null && hasArabic(cell)"
                :text="cell"
                size="sm"
              />
              <span
                v-else-if="cell !== null"
                class="whitespace-nowrap"
                v-text="cell"
              />
              <template v-else>
                <input
                  v-model="values[key(r, c)]"
                  type="text"
                  class="min-h-10 w-full min-w-[110px] rounded-lg border bg-ink-900 px-2 text-slate-100"
                  :class="cellState(r, c) === true
                    ? 'border-green-500 bg-green-500/10'
                    : cellState(r, c) === false
                      ? 'border-rose-500 bg-rose-500/10'
                      : 'border-line-strong'"
                  :aria-label="`${ustunlar[c] ?? ''}, ${r + 1}-qator`"
                  :disabled="verdict !== null"
                >
                <p
                  v-if="cellState(r, c) === false"
                  class="mt-0.5 text-xs text-slate-400"
                >
                  To‘g‘risi: {{ verdict?.[key(r, c)]?.javob }}
                </p>
              </template>
            </td>
          </tr>
        </tbody>
      </table>
    </div>
    <div class="mt-3 flex flex-wrap items-center gap-3">
      <button
        v-if="verdict === null"
        type="button"
        class="min-h-11 rounded-xl bg-brand-500 px-4 text-sm font-semibold text-on-brand disabled:opacity-60"
        :disabled="exercise.pending.value"
        @click="check"
      >
        Tekshirish
      </button>
      <p class="text-xs text-slate-400">
        Harakatsiz yozsangiz ham qabul qilinadi.
      </p>
    </div>
  </ExerciseShell>
</template>
