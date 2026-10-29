<script setup lang="ts">
import { computed } from 'vue'

import type { KnownBlock, UnitBlock } from '@/entities/interactive-unit'

import ExerciseList from '../exercises/ExerciseList.vue'
import MixedText from '../parts/MixedText.vue'
import BlockDialog from './BlockDialog.vue'
import BlockGrammatika from './BlockGrammatika.vue'
import BlockHarf from './BlockHarf.vue'
import BlockIsinish from './BlockIsinish.vue'
import BlockOqish from './BlockOqish.vue'
import BlockTabir from './BlockTabir.vue'
import BlockTovush from './BlockTovush.vue'
import BlockVazifa from './BlockVazifa.vue'
import BlockYakun from './BlockYakun.vue'
import BlockYozish from './BlockYozish.vue'

/**
 * Blok reyestri: `tur` → komponent. Noma'lum tur (kitobga yangi blok
 * qo'shilsa) — XATO EMAS: ko'rsatmasi va mashqlari chiqadi.
 */
const props = defineProps<{ block: UnitBlock; canDo: string[] }>()

const KNOWN = new Set(['isinish', 'grammatika', 'harf', 'tovush', 'yozish', 'yakun', 'vazifa', 'tabir', 'dialog', 'oqish'])

type Entry = { known: true; b: KnownBlock } | { known: false; b: UnitBlock }

const entry = computed<Entry>(() =>
  KNOWN.has(props.block.tur) ? { known: true, b: props.block as KnownBlock } : { known: false, b: props.block },
)
</script>

<template>
  <template v-if="entry.known">
    <BlockIsinish
      v-if="entry.b.tur === 'isinish'"
      :block="entry.b"
    />
    <BlockGrammatika
      v-else-if="entry.b.tur === 'grammatika'"
      :block="entry.b"
    />
    <BlockHarf
      v-else-if="entry.b.tur === 'harf'"
      :block="entry.b"
    />
    <BlockTovush
      v-else-if="entry.b.tur === 'tovush'"
      :block="entry.b"
    />
    <BlockYozish
      v-else-if="entry.b.tur === 'yozish'"
      :block="entry.b"
    />
    <BlockYakun
      v-else-if="entry.b.tur === 'yakun'"
      :block="entry.b"
      :can-do="canDo"
    />
    <BlockVazifa
      v-else-if="entry.b.tur === 'vazifa'"
      :block="entry.b"
    />
    <BlockTabir
      v-else-if="entry.b.tur === 'tabir'"
      :block="entry.b"
    />
    <BlockDialog
      v-else-if="entry.b.tur === 'dialog'"
      :block="entry.b"
    />
    <BlockOqish
      v-else-if="entry.b.tur === 'oqish'"
      :block="entry.b"
    />
  </template>
  <template v-else>
    <p
      v-if="entry.b.korsatma"
      class="mt-3 text-[15px] text-slate-300"
    >
      <MixedText :text="entry.b.korsatma" />
    </p>
    <ExerciseList
      v-if="entry.b.mashqlar"
      :mashqlar="entry.b.mashqlar"
    />
  </template>
</template>
