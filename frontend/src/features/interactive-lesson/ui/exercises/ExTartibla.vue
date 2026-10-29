<script setup lang="ts">
import { computed, reactive } from 'vue'

import { useExercise } from '../../model/useExercise'
import ArabicText from '../parts/ArabicText.vue'
import ExerciseShell from './ExerciseShell.vue'
import OptionButton from './OptionButton.vue'

/**
 * So'zlarni tartiblash. Hozirgi kontentda yo'q, lekin kitob rejasida bor
 * (00_ATF_reja.md) — renderer baribir qo'llaydi. So'zlar serverda aralashtirilgan.
 */
const props = defineProps<{ itemKey: string; korsatma: string | null; gaplar: { sozlar: string[] }[] }>()

const exercise = useExercise(() => props.itemKey)

const chosen = reactive(new Map<number, number[]>())
const results = reactive(new Map<number, { correct: boolean; javob: string | null }>())
const finished = computed(() => results.size === props.gaplar.length)

function resultState(i: number): 'idle' | 'right' | 'wrong' {
  const r = results.get(i)
  if (r === undefined) return 'idle'
  return r.correct ? 'right' : 'wrong'
}

async function take(i: number, w: number): Promise<void> {
  const list = chosen.get(i) ?? []
  if (list.includes(w) || results.has(i)) return
  const next = [...list, w]
  chosen.set(i, next)

  const words = props.gaplar[i]?.sozlar ?? []
  if (next.length < words.length) return

  const result = await exercise.submit({ index: i, words: next.map((n) => words[n] ?? '') })
  if (result === null) {
    chosen.set(i, [])
    return
  }
  results.set(i, { correct: result.correct, javob: result.reveal.javob ?? null })
}

function retry(): void {
  chosen.clear()
  results.clear()
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
    <ol class="mt-1 space-y-4">
      <li
        v-for="(g, i) in gaplar"
        :key="i"
      >
        <div
          class="flex flex-wrap gap-2"
          dir="rtl"
        >
          <OptionButton
            v-for="(w, wi) in g.sozlar"
            :key="wi"
            :text="w"
            :disabled="(chosen.get(i) ?? []).includes(wi) || results.has(i)"
            @pick="take(i, wi)"
          />
        </div>
        <div
          class="mt-2 flex min-h-12 flex-wrap gap-2 rounded-xl border border-dashed border-line p-2"
          dir="rtl"
        >
          <OptionButton
            v-for="n in chosen.get(i) ?? []"
            :key="n"
            :text="g.sozlar[n] ?? ''"
            :state="resultState(i)"
            disabled
          />
        </div>
        <p
          v-if="resultState(i) === 'wrong'"
          class="mt-1 text-[13px] text-slate-400"
        >
          To‘g‘risi:
          <ArabicText
            :text="results.get(i)?.javob"
            size="sm"
          />
        </p>
      </li>
    </ol>
  </ExerciseShell>
</template>
