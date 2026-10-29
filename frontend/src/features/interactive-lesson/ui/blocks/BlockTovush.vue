<script setup lang="ts">
import { computed } from 'vue'

import type { BlockTovush } from '@/entities/interactive-unit'
import { AppIcon } from '@/shared/ui'

import { useLessonContext } from '../../model/context'
import ExChoice from '../exercises/ExChoice.vue'
import MixedText from '../parts/MixedText.vue'
import WordCard from '../parts/WordCard.vue'

/**
 * Tovushlar: so'z/bo'g'in juftliklari (bosib tinglash, ketma-ket tinglash)
 * va "eshitib tanlang" testi.
 *
 * 🔴 Test savolining audiosi ekrandagi so'zning audiosi bilan bir xil FAYL,
 *   lekin server ularga BOSHQA-BOSHQA belgi beradi — javobni belgidan topib
 *   bo'lmaydi. Test kaliti `testKey` ("A4:test") bilan tekshiriladi.
 */
const props = defineProps<{ block: BlockTovush }>()
const { audio } = useLessonContext()

const questions = computed(() => props.block.test.map((t) => ({ uz: null, variantlar: t.variantlar, audio: t.audio })))
</script>

<template>
  <p
    v-if="block.izoh"
    class="mt-3 text-[15px] leading-relaxed text-slate-300"
  >
    <MixedText :text="block.izoh" />
  </p>

  <div
    v-for="(row, ri) in block.juftlar"
    :key="ri"
    class="mt-4"
  >
    <button
      type="button"
      class="mb-2 inline-flex min-h-9 items-center gap-1.5 rounded-lg border border-line bg-ink-900 px-3 text-[13px] font-semibold text-slate-300 hover:bg-ink-800"
      @click="audio.playSeq(row.audio)"
    >
      <AppIcon
        name="play"
        :size="13"
      />
      Ketma-ket tinglash
    </button>
    <div class="grid grid-cols-2 gap-2 sm:grid-cols-4">
      <WordCard
        v-for="(word, wi) in row.sozlar"
        :key="wi"
        :ar="word"
        :uz="row.uz[wi] ?? null"
        :audio="row.audio[wi] ?? null"
      />
    </div>
  </div>

  <ExChoice
    v-if="questions.length > 0"
    :item-key="block.testKey"
    korsatma="Eshitib farqlang: tinglang va to‘g‘ri bo‘g‘inni tanlang."
    :savollar="questions"
    listen-prompt
  />

  <div
    v-if="block.ehtiyot"
    class="mt-5 rounded-xl bg-amber-500/10 px-3.5 py-3 text-[13.5px] leading-relaxed text-slate-200"
  >
    <b class="text-amber-400">{{ block.ehtiyot.sarlavha }}</b>
    <MixedText :text="` ${block.ehtiyot.matn ?? ''}`" />
  </div>
</template>
