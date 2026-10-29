<script setup lang="ts">
import { computed } from 'vue'

import type { WorkbookItem } from '@/entities/interactive-unit'

import ExChoice from '../exercises/ExChoice.vue'
import ExDiktant from '../exercises/ExDiktant.vue'
import ExToldir from '../exercises/ExToldir.vue'
import ExUla from '../exercises/ExUla.vue'
import MixedText from '../parts/MixedText.vue'

/**
 * ════════════════════════════════════════════════════════════════════════
 *  MASHQ DAFTARI — uyda, 1–3-mashg'ulot (2026-10-29)
 * ════════════════════════════════════════════════════════════════════════
 *
 * ★ Avtomatik topshiriqlar (ula, to'ldir, tanla, diktant) shu yerda, server
 *   tekshiradi va birinchi urinish hisobga olinadi.
 * ★ Ovozli va yozma topshiriqlarni ustoz tekshiradi. ⚠️ Ularni "Vazifa"
 *   sifatida yuborish oqimi (Assignment, `Source = InteractiveWorkbook`) —
 *   keyingi bosqich (rejaning 8-bo'limi); hozircha matni ko'rsatiladi.
 */
const props = defineProps<{ items: WorkbookItem[] }>()

const sessions = computed(() => {
  const map = new Map<number, WorkbookItem[]>()
  for (const item of props.items) {
    const s = item.sessiya ?? 1
    map.set(s, [...(map.get(s) ?? []), item])
  }
  return [...map.entries()].sort(([a], [b]) => a - b)
})

const TAG: Record<string, string> = {
  ochiq: 'Yozma topshiriq',
  ovoz: 'Ovoz yozib yuborish',
}
</script>

<template>
  <p class="mt-3 text-[15px] leading-relaxed text-slate-300">
    Uyda bajariladi, haftasiga 3 marta. Ovozli va yozma topshiriqlarni ustoz tekshiradi.
  </p>

  <section
    v-for="[session, list] in sessions"
    :key="session"
    class="mt-5"
  >
    <h3 class="text-base font-bold text-slate-100">
      {{ session }}-mashg‘ulot
    </h3>

    <template
      v-for="item in list"
      :key="item.id"
    >
      <ExUla
        v-if="item.tur === 'ula'"
        :item-key="item.id"
        :korsatma="item.uz"
        :chap="item.chap"
        :ong="item.ong"
      />
      <ExToldir
        v-else-if="item.tur === 'toldir'"
        :item-key="item.id"
        :korsatma="item.uz"
        :gaplar="item.gaplar"
      />
      <ExChoice
        v-else-if="item.tur === 'tanla'"
        :item-key="item.id"
        :korsatma="item.uz"
        :savollar="item.savollar"
      />
      <ExDiktant
        v-else-if="item.tur === 'diktant'"
        :item-key="item.id"
        :korsatma="item.uz"
        :audio="item.audio"
      />
      <div
        v-else
        class="mt-4 rounded-xl border border-line bg-ink-850 px-3.5 py-3"
      >
        <span class="inline-block rounded-full bg-amber-500/10 px-2.5 py-0.5 text-xs font-bold text-amber-400">
          {{ TAG[item.tur] ?? item.tur }}
        </span>
        <p class="mt-1.5 text-[15px] leading-relaxed text-slate-200">
          <MixedText :text="item.uz" />
        </p>
      </div>
    </template>
  </section>
</template>
