<script setup lang="ts">
import type { BlockHarf } from '@/entities/interactive-unit'

import ExerciseList from '../exercises/ExerciseList.vue'
import ArabicText from '../parts/ArabicText.vue'
import MixedText from '../parts/MixedText.vue'
import PlayButton from '../parts/PlayButton.vue'
import WordCard from '../parts/WordCard.vue'
import WritingPad from '../parts/WritingPad.vue'

/**
 * Harf: katta shakl, nomi va tovushi, 4 ko'rinishi, misollar va yozish maydoni.
 * ★ Ko'rinishlar tartibi kitobdagidek: alohida, boshida, o'rtasida, oxirida.
 */
defineProps<{ block: BlockHarf }>()

const SHAPE_NAMES = ['alohida', 'boshida', 'o‘rtasida', 'oxirida']
</script>

<template>
  <p
    v-if="block.korsatma"
    class="mt-3 text-[15px] leading-relaxed text-slate-300"
  >
    <MixedText :text="block.korsatma" />
  </p>

  <article
    v-for="(letter, li) in block.harflar"
    :key="li"
    class="mt-4"
  >
    <div class="flex items-center gap-4">
      <span
        lang="ar"
        dir="rtl"
        class="flex size-24 shrink-0 items-center justify-center rounded-2xl bg-brand-500/10 font-arabic text-[64px] leading-none text-brand-500"
      >{{ letter.h }}</span>
      <div class="min-w-0">
        <div class="flex items-center gap-2">
          <ArabicText
            :text="letter.nom"
            size="lg"
          />
          <PlayButton
            :handle="letter.audio"
            :label="letter.nom"
          />
        </div>
        <div class="mt-1 grid grid-cols-4 gap-1.5">
          <div
            v-for="(shape, si) in letter.shakllar"
            :key="si"
            class="flex flex-col items-center rounded-lg border border-line bg-ink-850 px-1 py-1"
          >
            <ArabicText
              :text="shape"
              size="md"
            />
            <small class="text-[11px] text-slate-400">{{ SHAPE_NAMES[si] ?? '' }}</small>
          </div>
        </div>
      </div>
    </div>

    <p
      v-if="letter.tovush"
      class="mt-3 rounded-xl bg-amber-500/10 px-3.5 py-2.5 text-[13.5px] leading-relaxed text-slate-200"
    >
      <MixedText :text="letter.tovush" />
    </p>

    <div
      v-if="letter.misollar.length > 0"
      class="mt-3 grid grid-cols-3 gap-2"
    >
      <WordCard
        v-for="(m, mi) in letter.misollar"
        :key="mi"
        :ar="m.ar"
        :uz="m.uz"
        :audio="m.audio"
      />
    </div>

    <WritingPad :letter="letter.h" />
  </article>

  <ExerciseList
    v-if="block.mashqlar"
    :mashqlar="block.mashqlar"
  />
</template>
