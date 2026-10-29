<script setup lang="ts">
import { computed, ref } from 'vue'

import { useExercise } from '../../model/useExercise'
import ExerciseShell from './ExerciseShell.vue'

/**
 * So'z ovlash: matndan shartga mos so'zlarni bosing. Klient so'zning
 * TARTIB RAQAMINI yuboradi — matn so'zlari server bergan ro'yxat (`sozlar`),
 * ya'ni ikkala tomon AYNI bo'linishni ko'radi.
 * ★ 22 px / line-height 2.2 (prototipdagidek), klaviatura bilan ham ishlaydi.
 */
const props = defineProps<{ itemKey: string; korsatma: string | null; sozlar: string[]; soni: number }>()

const exercise = useExercise(() => props.itemKey)

const marks = ref(new Map<number, boolean>())
const found = computed(() => [...marks.value.values()].filter(Boolean).length)
const finished = computed(() => exercise.first.value !== undefined && found.value >= props.soni)

async function tap(index: number): Promise<void> {
  if (marks.value.has(index)) return
  const result = await exercise.submit({ word: index })
  if (result === null) return
  marks.value.set(index, result.correct)
}
</script>

<template>
  <ExerciseShell
    :korsatma="korsatma"
    :first="exercise.first.value"
    :finished="finished"
    :slow="exercise.slow.value"
    :error="exercise.error.value"
    @retry="marks = new Map()"
  >
    <p class="mt-1 text-xs text-slate-400">
      Topildi: {{ found }} / {{ soni }}
    </p>
    <p
      lang="ar"
      dir="rtl"
      class="mt-2 font-arabic text-[22px] leading-[2.2] text-slate-100"
    >
      <template
        v-for="(w, i) in sozlar"
        :key="i"
      >
        <span
          role="button"
          tabindex="0"
          class="cursor-pointer rounded-md px-0.5 outline-offset-2 transition-colors"
          :class="marks.get(i) === true
            ? 'bg-green-500/15 text-green-400'
            : marks.get(i) === false
              ? 'bg-rose-500/15 text-rose-400'
              : 'hover:bg-ink-800'"
          :aria-pressed="marks.has(i)"
          @click="tap(i)"
          @keydown.enter.prevent="tap(i)"
          @keydown.space.prevent="tap(i)"
        >{{ w }}</span>{{ ' ' }}
      </template>
    </p>
  </ExerciseShell>
</template>
