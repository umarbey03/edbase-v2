<script setup lang="ts">
import { computed, ref } from 'vue'

import type { MatchItem } from '@/entities/interactive-unit'

import { useExercise } from '../../model/useExercise'
import ExerciseShell from './ExerciseShell.vue'
import OptionButton from './OptionButton.vue'

/**
 * Ulash: chap elementni tanlang, keyin o'ngdagi juftini.
 *
 * 🔴 Juftlik klientda YO'Q: chap/o'ng — server bergan opaque belgilar, o'ng
 *   ustun serverda aralashtirilgan. Har urinish serverda tekshiriladi.
 */
const props = defineProps<{ itemKey: string; korsatma: string | null; chap: MatchItem[]; ong: MatchItem[] }>()

const exercise = useExercise(() => props.itemKey)

const picked = ref<string | null>(null)
const solved = ref(new Map<string, string>())
const flash = ref<string | null>(null)

const solvedRight = computed(() => new Set(solved.value.values()))
const finished = computed(() => solved.value.size === props.chap.length)

function choose(left: string): void {
  if (solved.value.has(left)) return
  picked.value = picked.value === left ? null : left
}

async function match(right: string): Promise<void> {
  const left = picked.value
  if (left === null || solvedRight.value.has(right)) return

  const result = await exercise.submit({ left, right })
  if (result === null) return

  if (result.correct) {
    solved.value.set(left, right)
    picked.value = null
  } else {
    flash.value = right
    setTimeout(() => {
      if (flash.value === right) flash.value = null
    }, 700)
  }
}

function retry(): void {
  solved.value = new Map()
  picked.value = null
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
    <p class="mt-1 text-xs text-slate-400">
      Avval chapdagini, keyin unga mos o‘ngdagini bosing.
    </p>
    <div class="mt-2 grid grid-cols-2 gap-2.5">
      <div class="flex min-w-0 flex-col gap-2">
        <OptionButton
          v-for="item in chap"
          :key="item.id"
          :text="item.text"
          :state="solved.has(item.id) ? 'right' : picked === item.id ? 'selected' : 'idle'"
          :disabled="solved.has(item.id)"
          @pick="choose(item.id)"
        />
      </div>
      <div class="flex min-w-0 flex-col gap-2">
        <OptionButton
          v-for="item in ong"
          :key="item.id"
          :text="item.text"
          :state="solvedRight.has(item.id) ? 'right' : flash === item.id ? 'wrong' : 'idle'"
          :disabled="solvedRight.has(item.id) || picked === null || exercise.pending.value"
          @pick="match(item.id)"
        />
      </div>
    </div>
  </ExerciseShell>
</template>
