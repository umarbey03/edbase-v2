<script setup lang="ts">
import { computed, reactive } from 'vue'

import { useExercise } from '../../model/useExercise'
import ArabicText from '../parts/ArabicText.vue'
import PlayButton from '../parts/PlayButton.vue'
import ExerciseShell from './ExerciseShell.vue'

/**
 * Diktant (mashq daftari): tinglang va arab yozuvida yozing.
 * 🔴 So'zlar klientda YO'Q — to'g'ri yozilishi faqat javobdan keyin keladi.
 * ★ Harakatsiz yozilsa ham qabul (server `ArabicNorm`).
 */
const props = defineProps<{ itemKey: string; korsatma: string | null; audio: (string | null)[] }>()

const exercise = useExercise(() => props.itemKey)

const texts = reactive<Record<number, string>>({})
const results = reactive(new Map<number, { correct: boolean; javob: string | null }>())
const finished = computed(() => results.size === props.audio.length)

function border(i: number): string {
  const r = results.get(i)
  if (r === undefined) return 'border-line-strong'
  return r.correct ? 'border-green-500' : 'border-rose-500'
}

async function send(i: number): Promise<void> {
  const text = (texts[i] ?? '').trim()
  if (text.length === 0 || results.has(i)) return
  const result = await exercise.submit({ index: i, text })
  if (result !== null) results.set(i, { correct: result.correct, javob: result.reveal.javob ?? null })
}

function retry(): void {
  results.clear()
  for (const k of Object.keys(texts)) delete texts[Number(k)]
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
    <ol class="mt-2 space-y-3">
      <li
        v-for="(handle, i) in audio"
        :key="i"
        class="flex flex-wrap items-center gap-2"
      >
        <span class="w-5 text-sm font-semibold text-slate-400">{{ i + 1 }}.</span>
        <PlayButton
          :handle="handle"
          :label="`${i + 1}-so‘z`"
          small
        />
        <input
          v-model="texts[i]"
          lang="ar"
          dir="rtl"
          class="min-h-11 min-w-0 flex-1 rounded-xl border bg-ink-900 px-3 font-arabic text-xl text-slate-100"
          :class="border(i)"
          :aria-label="`${i + 1}-so‘zni yozing`"
          :disabled="results.has(i)"
          @keydown.enter.prevent="send(i)"
        >
        <button
          v-if="!results.has(i)"
          type="button"
          class="min-h-11 rounded-xl border border-line bg-ink-900 px-3 text-sm font-semibold text-slate-200 hover:bg-ink-800"
          :disabled="exercise.pending.value"
          @click="send(i)"
        >
          Tekshirish
        </button>
        <span
          v-else
          class="text-sm"
          :class="results.get(i)?.correct ? 'text-green-400' : 'text-rose-400'"
        >
          {{ results.get(i)?.correct ? '✓' : '✗' }}
          <ArabicText
            v-if="!results.get(i)?.correct"
            :text="results.get(i)?.javob"
            size="sm"
          />
        </span>
      </li>
    </ol>
  </ExerciseShell>
</template>
