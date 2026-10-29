<script setup lang="ts">
import type { BlockGrammatika } from '@/entities/interactive-unit'

import ExerciseList from '../exercises/ExerciseList.vue'
import ArabicText from '../parts/ArabicText.vue'
import DataTable from '../parts/DataTable.vue'
import MixedText from '../parts/MixedText.vue'

/**
 * Grammatika (qoida): "Kuzating" → savol → qoida → jadval → qiyos → mashqlar.
 * Kitobdagi tartib — induktiv: avval misol, keyin qoida.
 */
defineProps<{ block: BlockGrammatika }>()

/** `rangli` ("و / ي / ا") qismlarini arabcha matnda ajratib ko'rsatish uchun bo'laklarga bo'ladi. */
function highlight(text: string | null, marked: string | null): { text: string; mark: boolean }[] {
  const source = text ?? ''
  const parts = (marked ?? '').split(' / ').map((p) => p.trim()).filter((p) => p.length > 0)
  if (parts.length === 0) return [{ text: source, mark: false }]

  const out: { text: string; mark: boolean }[] = []
  let rest = source
  while (rest.length > 0) {
    let best: { i: number; p: string } | null = null
    for (const p of parts) {
      const i = rest.indexOf(p)
      if (i >= 0 && (best === null || i < best.i)) best = { i, p }
    }
    if (best === null) {
      out.push({ text: rest, mark: false })
      break
    }
    if (best.i > 0) out.push({ text: rest.slice(0, best.i), mark: false })
    out.push({ text: best.p, mark: true })
    rest = rest.slice(best.i + best.p.length)
  }
  return out
}
</script>

<template>
  <p
    v-if="block.korsatma"
    class="mt-3 text-[15px] leading-relaxed text-slate-300"
  >
    <MixedText :text="block.korsatma" />
  </p>

  <div
    v-if="block.kuzat.length > 0"
    class="mt-4"
  >
    <p class="text-xs font-bold uppercase tracking-wider text-slate-400">
      Kuzating
    </p>
    <ul class="mt-1">
      <li
        v-for="(k, i) in block.kuzat"
        :key="i"
        class="flex flex-wrap-reverse items-center justify-between gap-x-4 gap-y-1 border-b border-dashed border-line py-2"
      >
        <span class="min-w-0 flex-1 text-sm text-slate-300"><MixedText :text="k.uz" /></span>
        <bdi
          lang="ar"
          dir="rtl"
          class="font-arabic text-[26px] leading-[1.7] text-slate-100"
        ><template
          v-for="(part, pi) in highlight(k.ar, k.rangli)"
          :key="pi"
        ><mark
          v-if="part.mark"
          class="rounded-md bg-amber-500/15 px-0.5 text-amber-400"
        >{{ part.text }}</mark><template v-else>{{ part.text }}</template></template></bdi>
      </li>
    </ul>
  </div>

  <p
    v-if="block.savol"
    class="mt-4 font-semibold text-slate-100"
  >
    <MixedText :text="block.savol" />
  </p>

  <ul
    v-if="block.qoida.length > 0"
    class="mt-3 list-disc space-y-1.5 ps-5 text-[15px] leading-relaxed text-slate-300"
  >
    <li
      v-for="(q, i) in block.qoida"
      :key="i"
    >
      <MixedText :text="q" />
    </li>
  </ul>

  <DataTable
    v-if="block.jadval"
    :table="block.jadval"
  />

  <div
    v-if="block.kontrast"
    class="mt-4 rounded-xl bg-brand-500/10 px-3.5 py-3"
  >
    <p class="text-sm text-slate-200">
      <MixedText :text="block.kontrast.uz" /> →
      <ArabicText :text="block.kontrast.ar" />
    </p>
    <p
      v-if="block.kontrast.izoh"
      class="mt-1 text-[13px] text-slate-400"
    >
      <MixedText :text="block.kontrast.izoh" />
    </p>
  </div>

  <ExerciseList
    v-if="block.mashqlar"
    :mashqlar="block.mashqlar"
  />
</template>
