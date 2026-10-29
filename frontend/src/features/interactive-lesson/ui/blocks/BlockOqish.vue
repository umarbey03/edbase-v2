<script setup lang="ts">
import type { BlockOqish } from '@/entities/interactive-unit'
import { AppIcon } from '@/shared/ui'

import { useLessonContext } from '../../model/context'
import ExerciseList from '../exercises/ExerciseList.vue'
import ArabicText from '../parts/ArabicText.vue'
import MixedText from '../parts/MixedText.vue'
import WordCard from '../parts/WordCard.vue'

/** O'qish matni: oldindan ko'rsatma, butun matnni tinglash, yangi so'zlar va mashqlar. */
defineProps<{ block: BlockOqish }>()
const { audio } = useLessonContext()
</script>

<template>
  <p
    v-if="block.oldin"
    class="mt-3 rounded-xl bg-amber-500/10 px-3.5 py-2.5 text-[13.5px] leading-relaxed text-slate-200"
  >
    <MixedText :text="block.oldin" />
  </p>

  <button
    v-if="block.audio"
    type="button"
    class="mt-3 inline-flex min-h-10 items-center gap-1.5 rounded-lg border border-line bg-ink-900 px-3 text-[13px] font-semibold text-slate-300 hover:bg-ink-800"
    :aria-pressed="audio.current.value === block.audio"
    @click="audio.play(block.audio)"
  >
    <AppIcon
      :name="audio.current.value === block.audio ? 'close' : 'play'"
      :size="13"
    />
    {{ audio.current.value === block.audio ? 'To‘xtatish' : 'Matnni tinglash' }}
  </button>

  <div class="mt-3 space-y-2">
    <p
      v-for="(line, i) in block.matn"
      :key="i"
      class="text-slate-100"
    >
      <ArabicText
        :text="line"
        size="md"
      />
    </p>
  </div>

  <div
    v-if="block.yangi.length > 0"
    class="mt-4"
  >
    <p class="text-xs font-bold uppercase tracking-wider text-slate-400">
      Yangi so‘zlar
    </p>
    <div class="mt-2 grid grid-cols-2 gap-2 sm:grid-cols-3">
      <WordCard
        v-for="(y, i) in block.yangi"
        :key="i"
        :ar="y.ar"
        :uz="y.uz"
      />
    </div>
  </div>

  <ExerciseList
    v-if="block.mashqlar"
    :mashqlar="block.mashqlar"
  />
</template>
