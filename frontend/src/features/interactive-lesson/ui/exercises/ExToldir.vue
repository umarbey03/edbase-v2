<script setup lang="ts">
import { computed, reactive } from 'vue'

import type { FillSentence } from '@/entities/interactive-unit'

import { useExercise } from '../../model/useExercise'
import ExerciseShell from './ExerciseShell.vue'
import OptionButton from './OptionButton.vue'

/** Bo'sh joyni to'ldirish ("..." o'rniga variant). Javobdan keyin to'g'ri harf joyiga tushadi. */
const props = defineProps<{ itemKey: string; korsatma: string | null; gaplar: FillSentence[] }>()

const exercise = useExercise(() => props.itemKey)

interface Answer {
  picked: number
  correct: number
  javob: string | null
}

const answers = reactive(new Map<number, Answer>())
const finished = computed(() => answers.size === props.gaplar.length)

function parts(text: string | null): [string, string] {
  const [before = '', after = ''] = (text ?? '').split('...')
  return [before, after]
}

function stateOf(i: number, v: number): 'idle' | 'right' | 'wrong' {
  const a = answers.get(i)
  if (a === undefined) return 'idle'
  if (v === a.correct) return 'right'
  return v === a.picked ? 'wrong' : 'idle'
}

async function pick(i: number, v: number): Promise<void> {
  if (answers.has(i)) return
  const result = await exercise.submit({ index: i, choice: v })
  if (result === null) return
  answers.set(i, { picked: v, correct: result.reveal.correctChoice ?? -1, javob: result.reveal.javob ?? null })
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
        <p
          lang="ar"
          dir="rtl"
          class="font-arabic text-[22px] leading-[1.9] text-slate-100"
        >
          {{ parts(g.ar)[0] }}<span class="mx-0.5 rounded-md bg-ink-800 px-2">{{ answers.get(i)?.javob ?? '…' }}</span>{{ parts(g.ar)[1] }}
        </p>
        <div class="mt-1 flex flex-wrap gap-2">
          <OptionButton
            v-for="(v, vi) in g.variantlar"
            :key="vi"
            :text="v"
            :state="stateOf(i, vi)"
            :disabled="answers.has(i) || exercise.pending.value"
            @pick="pick(i, vi)"
          />
        </div>
      </li>
    </ol>
  </ExerciseShell>
</template>
