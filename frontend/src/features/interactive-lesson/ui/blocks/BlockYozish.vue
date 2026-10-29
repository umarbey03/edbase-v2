<script setup lang="ts">
import { reactive } from 'vue'

import type { BlockYozish } from '@/entities/interactive-unit'
import { toUserMessage } from '@/shared/api'

import { useLessonContext } from '../../model/context'
import ArabicText from '../parts/ArabicText.vue'
import MixedText from '../parts/MixedText.vue'

/**
 * Yozish pog'onalari. Namuna OLDINDAN yuborilmaydi: o'quvchi daftarga yozib
 * "Yozdim" ni bosgandan keyin server uni beradi (`reveal`) — o'z ishini
 * namuna bilan solishtiradi. Baholanmaydi.
 */
const props = defineProps<{ block: BlockYozish }>()
const { adapter } = useLessonContext()

const samples = reactive(new Map<number, string | null>())
const errors = reactive(new Map<number, string>())

async function wrote(n: number | null): Promise<void> {
  if (n === null || samples.has(n)) return
  try {
    samples.set(n, await adapter.reveal(props.block.id, n))
  } catch (e) {
    errors.set(n, toUserMessage(e))
  }
}
</script>

<template>
  <ol class="mt-3 space-y-3">
    <li
      v-for="p in block.pogonalar"
      :key="p.n ?? 0"
      class="flex gap-3"
    >
      <span class="flex size-7 shrink-0 items-center justify-center rounded-full bg-brand-500/10 text-xs font-bold text-brand-500">
        {{ p.n }}
      </span>
      <div class="min-w-0 flex-1">
        <p class="text-[15px] leading-relaxed text-slate-200">
          <MixedText :text="p.uz" />
        </p>
        <template v-if="p.hasNamuna && p.n !== null">
          <button
            v-if="!samples.has(p.n)"
            type="button"
            class="mt-2 min-h-10 rounded-lg border border-line bg-ink-900 px-3 text-[13px] font-semibold text-slate-300 hover:bg-ink-800"
            @click="wrote(p.n)"
          >
            Yozdim — namunani ko‘rsat
          </button>
          <p
            v-else
            class="mt-2 rounded-lg bg-ink-850 px-3 py-1.5 text-sm text-slate-300"
          >
            Namuna:
            <ArabicText :text="samples.get(p.n)" />
          </p>
          <p
            v-if="errors.get(p.n)"
            class="mt-1 text-xs text-rose-400"
            v-text="errors.get(p.n)"
          />
        </template>
      </div>
    </li>
  </ol>
</template>
