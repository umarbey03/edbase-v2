<script setup lang="ts">
import { computed, ref } from 'vue'

import type { BlockDialog } from '@/entities/interactive-unit'
import { AppIcon } from '@/shared/ui'

import { useLessonContext } from '../../model/context'
import ExerciseList from '../exercises/ExerciseList.vue'
import ArabicText from '../parts/ArabicText.vue'
import MixedText from '../parts/MixedText.vue'
import PlayButton from '../parts/PlayButton.vue'

/**
 * Suhbat: vaziyat, bosqichlar, satrlar (personaj rangi bilan), butun suhbatni
 * tinglash va "matnni yashirish" (1-bosqich — ko'rmasdan tinglash).
 * ★ Ketma-ket tinglashda joriy satr ajratib ko'rsatiladi.
 */
const props = defineProps<{ block: BlockDialog }>()
const { audio, personas } = useLessonContext()

const showText = ref(true)

const lines = computed(() =>
  props.block.satrlar.map((s) => {
    const p = personas.get(s.kim ?? '')
    return { ...s, name: p?.uz ?? s.kim ?? '?', color: p?.rang ?? '#5c6b61' }
  }),
)

function playAll(): void {
  if (props.block.audioToliq !== null) void audio.play(props.block.audioToliq)
  else void audio.playSeq(props.block.satrlar.map((s) => s.audio))
}
</script>

<template>
  <p
    v-if="block.vaziyat"
    class="mt-3 rounded-xl bg-amber-500/10 px-3.5 py-2.5 text-[13.5px] leading-relaxed text-slate-200"
  >
    <MixedText :text="block.vaziyat" />
  </p>

  <ol class="mt-3 space-y-1.5">
    <li
      v-for="s in block.bosqichlar"
      :key="s.n ?? 0"
      class="flex gap-2.5 text-sm text-slate-300"
    >
      <b class="flex size-6 shrink-0 items-center justify-center rounded-full bg-brand-500/10 text-xs text-brand-500">{{ s.n }}</b>
      <MixedText :text="s.uz" />
    </li>
  </ol>

  <div class="mt-3 flex flex-wrap gap-2">
    <button
      type="button"
      class="inline-flex min-h-10 items-center gap-1.5 rounded-lg border border-line bg-ink-900 px-3 text-[13px] font-semibold text-slate-300 hover:bg-ink-800"
      @click="playAll"
    >
      <AppIcon
        name="play"
        :size="13"
      />
      Butun suhbatni tinglash
    </button>
    <button
      type="button"
      class="min-h-10 rounded-lg border border-line bg-ink-900 px-3 text-[13px] font-semibold text-slate-300 hover:bg-ink-800"
      :aria-pressed="!showText"
      @click="showText = !showText"
    >
      {{ showText ? 'Matnni yashirish' : 'Matnni ko‘rsatish' }}
    </button>
  </div>

  <ul class="mt-4 space-y-2.5">
    <li
      v-for="(line, i) in lines"
      :key="i"
      class="grid grid-cols-[auto_minmax(0,1fr)_auto] items-start gap-2.5"
    >
      <span
        class="flex size-9 items-center justify-center rounded-full text-sm font-bold text-white"
        :style="{ background: line.color }"
        :title="line.name"
        aria-hidden="true"
      >{{ line.name.charAt(0) }}</span>
      <div
        class="min-w-0 rounded-2xl rounded-tl-sm px-3.5 py-2 transition-colors"
        :class="audio.current.value !== null && audio.current.value === line.audio ? 'bg-brand-500/15' : 'bg-ink-800'"
      >
        <p class="text-xs text-slate-400">
          {{ line.name }}
        </p>
        <template v-if="showText">
          <ArabicText
            :text="line.ar"
            size="md"
          />
          <p
            class="text-[13px] text-slate-400"
            v-text="line.uz"
          />
        </template>
        <p
          v-else
          class="text-[13px] text-slate-400"
        >
          ••• avval tinglang •••
        </p>
      </div>
      <PlayButton
        :handle="line.audio"
        :label="line.name"
        small
      />
    </li>
  </ul>

  <ExerciseList
    v-if="block.mashqlar"
    :mashqlar="block.mashqlar"
  />
</template>
