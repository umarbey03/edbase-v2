<script setup lang="ts">
import { computed, reactive } from 'vue'

import type { ChoiceQuestion } from '@/entities/interactive-unit'

import { useExercise } from '../../model/useExercise'
import MixedText from '../parts/MixedText.vue'
import PlayButton from '../parts/PlayButton.vue'
import ExerciseShell from './ExerciseShell.vue'
import OptionButton from './OptionButton.vue'

/**
 * Variantli savollar: `tanla`, tovush blokining "eshitib tanlang" testi va
 * daftardagi `tanla`. Har savolga javob — darhol server tekshiruvi; to'g'ri
 * javob shundan KEYIN yashil bo'ladi.
 */
const props = defineProps<{
  itemKey: string
  korsatma: string | null
  savollar: ChoiceQuestion[]
  /** Savol matni yo'q bo'lsa (tovush testi) — "Tinglang va tanlang". */
  listenPrompt?: boolean
}>()

const exercise = useExercise(() => props.itemKey)

interface Answer {
  picked: number
  correct: number
}

const answers = reactive(new Map<number, Answer>())
const finished = computed(() => answers.size === props.savollar.length)

function stateOf(q: number, option: number): 'idle' | 'right' | 'wrong' {
  const a = answers.get(q)
  if (a === undefined) return 'idle'
  if (option === a.correct) return 'right'
  if (option === a.picked) return 'wrong'
  return 'idle'
}

async function pick(q: number, option: number): Promise<void> {
  if (answers.has(q)) return
  const result = await exercise.submit({ index: q, choice: option })
  if (result === null) return
  answers.set(q, { picked: option, correct: result.reveal.correctChoice ?? -1 })
}

function retry(): void {
  answers.clear()
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
        v-for="(q, qi) in savollar"
        :key="qi"
      >
        <div class="mb-2 flex items-center gap-2 text-sm text-slate-300">
          <span class="font-semibold text-slate-400">{{ qi + 1 }}.</span>
          <MixedText
            v-if="q.uz"
            :text="q.uz"
          />
          <span v-else-if="listenPrompt">Tinglang va tanlang:</span>
          <PlayButton
            :handle="q.audio"
            :label="`${qi + 1}-savol`"
            small
          />
        </div>
        <div class="flex flex-wrap gap-2">
          <OptionButton
            v-for="(v, vi) in q.variantlar"
            :key="vi"
            :text="v"
            :state="stateOf(qi, vi)"
            :disabled="answers.has(qi) || exercise.pending.value"
            @pick="pick(qi, vi)"
          />
        </div>
      </li>
    </ol>
  </ExerciseShell>
</template>
