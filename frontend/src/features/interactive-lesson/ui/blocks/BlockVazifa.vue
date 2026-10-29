<script setup lang="ts">
import type { BlockVazifa } from '@/entities/interactive-unit'

import ArabicText from '../parts/ArabicText.vue'
import MixedText from '../parts/MixedText.vue'

/**
 * Muloqot vazifasi (juftlikda o'yin): maqsad, qolip, rol kartochkalari va
 * baholash mezoni (egasi qarori 8 — mezon o'quvchiga ko'rinadi, u o'yin qoidasi).
 */
defineProps<{ block: BlockVazifa }>()
</script>

<template>
  <p
    v-if="block.maqsad"
    class="mt-3 text-[15px] leading-relaxed text-slate-200"
  >
    <MixedText :text="block.maqsad" />
  </p>

  <div class="mt-4 grid gap-2.5 sm:grid-cols-2">
    <div
      v-if="block.qolip.length > 0"
      class="rounded-xl border border-line bg-ink-850 px-3.5 py-3"
    >
      <p class="text-xs font-bold uppercase tracking-wider text-slate-400">
        Qolip
      </p>
      <div class="mt-1 flex flex-wrap justify-end gap-x-3">
        <ArabicText
          v-for="(q, i) in block.qolip"
          :key="i"
          :text="q"
        />
      </div>
    </div>

    <div
      v-for="(r, i) in block.rollar"
      :key="`r${i}`"
      class="rounded-xl border border-line bg-ink-850 px-3.5 py-3"
    >
      <p class="text-sm font-semibold text-slate-100">
        {{ r.ism }} · {{ r.belgi }}
      </p>
      <p
        v-for="(line, li) in r.qatorlar"
        :key="li"
        class="mt-1 text-[13px] text-slate-300"
      >
        <MixedText :text="line" />
      </p>
    </div>

    <div
      v-if="block.baholash.length > 0"
      class="rounded-xl border border-line bg-ink-850 px-3.5 py-3"
    >
      <p class="text-xs font-bold uppercase tracking-wider text-slate-400">
        Baholash
      </p>
      <ul class="mt-1 space-y-0.5 text-[13px] text-slate-300">
        <li
          v-for="(b, bi) in block.baholash"
          :key="bi"
        >
          {{ b.mezon }} — {{ b.ball }} ball
        </li>
      </ul>
    </div>
  </div>

  <p
    v-if="block.natija"
    class="mt-3 text-sm text-slate-400"
  >
    Natija: <MixedText :text="block.natija" />
  </p>
</template>
