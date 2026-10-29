<script setup lang="ts">
import { ref } from 'vue'

import type { BlockYakun } from '@/entities/interactive-unit'

import MixedText from '../parts/MixedText.vue'

/**
 * "Men qila olaman" — darsning `can_do` ro'yxati bo'yicha o'zini tekshirish.
 * Belgilar saqlanmaydi (o'quvchining shaxsiy tekshiruvi, baholanmaydi).
 */
defineProps<{ block: BlockYakun; canDo: string[] }>()

const checked = ref<Set<number>>(new Set())

function toggle(i: number): void {
  const next = new Set(checked.value)
  if (next.has(i)) next.delete(i)
  else next.add(i)
  checked.value = next
}
</script>

<template>
  <ul
    v-if="block.cando"
    class="mt-3"
  >
    <li
      v-for="(c, i) in canDo"
      :key="i"
      class="border-b border-dashed border-line"
    >
      <label class="flex min-h-11 cursor-pointer items-start gap-3 py-2.5 text-[15px] text-slate-200">
        <input
          type="checkbox"
          class="mt-1 size-5 shrink-0 accent-green-500"
          :checked="checked.has(i)"
          @change="toggle(i)"
        >
        <MixedText :text="c" />
      </label>
    </li>
  </ul>
</template>
