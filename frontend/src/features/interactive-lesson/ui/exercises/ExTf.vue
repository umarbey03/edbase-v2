<script setup lang="ts">
import { computed, reactive } from 'vue'

import { useExercise } from '../../model/useExercise'
import ArabicText from '../parts/ArabicText.vue'
import ExerciseShell from './ExerciseShell.vue'
import OptionButton from './OptionButton.vue'

/** To'g'ri / noto'g'ri. Xato bo'lsa server bergan tuzatish ko'rsatiladi. */
const props = defineProps<{ itemKey: string; korsatma: string | null; gaplar: { ar: string | null }[] }>()

const exercise = useExercise(() => props.itemKey)

interface Answer {
  picked: boolean
  correct: boolean
  tuzatish: string | null
}

const answers = reactive(new Map<number, Answer>())
const finished = computed(() => answers.size === props.gaplar.length)

function stateOf(i: number, value: boolean): 'idle' | 'right' | 'wrong' {
  const a = answers.get(i)
  if (a === undefined) return 'idle'
  if (value === a.correct) return 'right'
  return value === a.picked ? 'wrong' : 'idle'
}

async function pick(i: number, value: boolean): Promise<void> {
  if (answers.has(i)) return
  const result = await exercise.submit({ index: i, value })
  if (result === null) return
  answers.set(i, {
    picked: value,
    correct: result.reveal.correctValue ?? value,
    tuzatish: result.reveal.tuzatish ?? null,
  })
}
</script>

<template>
  <ExerciseShell
    :korsatma="korsatma"
    :first="exercise.first.value"
    :finished="finished"
    :slow="exercise.slow.value"
    :error="exercise.error.value"
    @retry="answers.clear()"
  >
    <ol class="mt-1 space-y-4">
      <li
        v-for="(g, i) in gaplar"
        :key="i"
      >
        <ArabicText :text="g.ar" />
        <div class="mt-1 flex flex-wrap gap-2">
          <OptionButton
            text="To‘g‘ri"
            :state="stateOf(i, true)"
            :disabled="answers.has(i) || exercise.pending.value"
            @pick="pick(i, true)"
          />
          <OptionButton
            text="Noto‘g‘ri"
            :state="stateOf(i, false)"
            :disabled="answers.has(i) || exercise.pending.value"
            @pick="pick(i, false)"
          />
        </div>
        <p
          v-if="answers.get(i)?.tuzatish"
          class="mt-1.5 text-[13px] text-slate-400"
        >
          To‘g‘risi: <ArabicText
            :text="answers.get(i)?.tuzatish"
            size="sm"
          />
        </p>
      </li>
    </ol>
  </ExerciseShell>
</template>
