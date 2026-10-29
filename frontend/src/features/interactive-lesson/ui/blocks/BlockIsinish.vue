<script setup lang="ts">
import { computed } from 'vue'

import type { BlockIsinish } from '@/entities/interactive-unit'

import { useLessonContext } from '../../model/context'
import MixedText from '../parts/MixedText.vue'
import WordCard from '../parts/WordCard.vue'

/** Isinish: ko'prik so'zlar (o'zbekchadan tanish harflar) yoki o'tgan dars lug'ati. */
const props = defineProps<{ block: BlockIsinish }>()
const { words } = useLessonContext()

const vocab = computed(() =>
  props.block.lugatIds.map((id) => words.get(id)).filter((w) => w !== undefined),
)
</script>

<template>
  <p
    v-if="block.korsatma"
    class="mt-3 text-[15px] leading-relaxed text-slate-300"
  >
    <MixedText :text="block.korsatma" />
  </p>
  <div class="mt-4 grid grid-cols-2 gap-2.5 sm:grid-cols-3">
    <WordCard
      v-for="(k, i) in block.kopru"
      :key="`k${i}`"
      :ar="k.ar"
      :uz="k.uz"
    />
    <WordCard
      v-for="w in vocab"
      :key="w.id ?? ''"
      :ar="w.ar"
      :uz="w.uz"
      :audio="w.audio"
    />
  </div>
</template>
