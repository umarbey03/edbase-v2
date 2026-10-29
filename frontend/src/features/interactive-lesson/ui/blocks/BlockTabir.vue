<script setup lang="ts">
import type { BlockTabir } from '@/entities/interactive-unit'

import ArabicText from '../parts/ArabicText.vue'
import MixedText from '../parts/MixedText.vue'
import PlayButton from '../parts/PlayButton.vue'

/** Iboralar kartochkalari: ibora, tarjima, misollar va "Siz" vazifasi. */
defineProps<{ block: BlockTabir }>()
</script>

<template>
  <p
    v-if="block.korsatma"
    class="mt-3 text-[15px] leading-relaxed text-slate-300"
  >
    <MixedText :text="block.korsatma" />
  </p>
  <div class="mt-4 grid gap-2.5 sm:grid-cols-2">
    <div
      v-for="(k, i) in block.kartochkalar"
      :key="i"
      class="flex flex-col gap-1 rounded-xl border border-line bg-ink-900 px-3.5 py-3"
    >
      <div class="flex items-center justify-between gap-2">
        <PlayButton
          :handle="k.audio"
          :label="k.uz"
        />
        <ArabicText
          :text="k.ibora"
          size="lg"
        />
      </div>
      <p
        class="text-[13px] font-semibold text-slate-300"
        v-text="k.uz"
      />
      <p
        v-for="(m, mi) in k.misollar"
        :key="mi"
        class="text-right"
      >
        <ArabicText
          :text="m"
          size="sm"
        />
      </p>
      <p
        v-if="k.siz"
        class="text-xs text-amber-400"
      >
        Siz: <MixedText :text="k.siz" />
      </p>
    </div>
  </div>
</template>
