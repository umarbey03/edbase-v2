<script setup lang="ts">
import { computed } from 'vue'

import type { Exercise, ExUnknown } from '@/entities/interactive-unit'

import ExChoice from './ExChoice.vue'
import ExJadval from './ExJadval.vue'
import ExOvlash from './ExOvlash.vue'
import ExTartibla from './ExTartibla.vue'
import ExTf from './ExTf.vue'
import ExToldir from './ExToldir.vue'
import ExUla from './ExUla.vue'

/**
 * Blok ichidagi mashqlar — tur bo'yicha (mashq reyestri). Noma'lum tur xato
 * emas: faqat ko'rsatmasi chiqadi (kontentga yangi tur qo'shilsa sahifa
 * yiqilmasin).
 */
const props = defineProps<{ mashqlar: (Exercise | ExUnknown)[] }>()

const KNOWN = new Set(['tanla', 'tf', 'ula', 'ovlash', 'jadval', 'toldir', 'tartibla'])

type Entry = { known: true; ex: Exercise } | { known: false; ex: ExUnknown }

const entries = computed<Entry[]>(() =>
  props.mashqlar.map((e) => (KNOWN.has(e.tur) ? { known: true, ex: e as Exercise } : { known: false, ex: e })),
)
</script>

<template>
  <template
    v-for="entry in entries"
    :key="entry.ex.id"
  >
    <template v-if="entry.known">
      <ExChoice
        v-if="entry.ex.tur === 'tanla'"
        :item-key="entry.ex.id"
        :korsatma="entry.ex.korsatma"
        :savollar="entry.ex.savollar"
      />
      <ExTf
        v-else-if="entry.ex.tur === 'tf'"
        :item-key="entry.ex.id"
        :korsatma="entry.ex.korsatma"
        :gaplar="entry.ex.gaplar"
      />
      <ExUla
        v-else-if="entry.ex.tur === 'ula'"
        :item-key="entry.ex.id"
        :korsatma="entry.ex.korsatma"
        :chap="entry.ex.chap"
        :ong="entry.ex.ong"
      />
      <ExOvlash
        v-else-if="entry.ex.tur === 'ovlash'"
        :item-key="entry.ex.id"
        :korsatma="entry.ex.korsatma"
        :sozlar="entry.ex.sozlar"
        :soni="entry.ex.soni"
      />
      <ExJadval
        v-else-if="entry.ex.tur === 'jadval'"
        :item-key="entry.ex.id"
        :korsatma="entry.ex.korsatma"
        :ustunlar="entry.ex.ustunlar"
        :qatorlar="entry.ex.qatorlar"
      />
      <ExToldir
        v-else-if="entry.ex.tur === 'toldir'"
        :item-key="entry.ex.id"
        :korsatma="entry.ex.korsatma"
        :gaplar="entry.ex.gaplar"
      />
      <ExTartibla
        v-else-if="entry.ex.tur === 'tartibla'"
        :item-key="entry.ex.id"
        :korsatma="entry.ex.korsatma"
        :gaplar="entry.ex.gaplar"
      />
    </template>
    <p
      v-else
      class="mt-5 border-t border-line pt-4 text-sm text-slate-400"
      v-text="entry.ex.korsatma"
    />
  </template>
</template>
