<script setup lang="ts">
import { useQuery } from '@tanstack/vue-query'
import { computed, ref } from 'vue'

import { fetchMyLetters, type LetterProgressDto, type LetterState } from '@/entities/points'

/**
 * 🔤 HARFLAR JADVALI — profil (2026-11-21, loyiha egasi g'oyasi).
 *
 * 28 harf alifbo tartibida, o'ngdan chapga. O'zlashtirilgan dars harflari
 * "yonadi" — o'quvchi o'sishini bir qarashda ko'radi. Harf bosilsa —
 * uni qaysi darsda o'rganishi.
 */
const query = useQuery({
  queryKey: ['progress', 'letters'],
  queryFn: ({ signal }) => fetchMyLetters({ signal }),
  staleTime: 60_000,
})

const data = computed(() => query.data.value ?? null)

// Hech bir darsga harf belgilanmagan kurs (masalan, boshqa fan) — karta chiqmaydi.
const visible = computed(() => data.value !== null && data.value.letters.some((l) => l.lessonId !== null))

const selected = ref<LetterProgressDto | null>(null)

const STATE_CLASS: Record<LetterState, string> = {
  Mastered: 'border-emerald-500/50 bg-emerald-500/15 text-emerald-200',
  Learning: 'border-amber-400/50 bg-amber-400/10 text-amber-200',
  Locked: 'border-line bg-ink-950 text-slate-600',
}

const STATE_LABEL: Record<LetterState, string> = {
  Mastered: 'o‘zlashtirildi',
  Learning: 'hozir o‘rganilmoqda',
  Locked: 'hali oldinda',
}

const percent = computed(() =>
  data.value === null || data.value.total === 0 ? 0 : Math.round((data.value.mastered / data.value.total) * 100))
</script>

<template>
  <section
    v-if="visible && data !== null"
    class="rounded-xl border border-line bg-ink-900 p-[18px] lg:p-5"
    aria-label="Harflar jadvali"
  >
    <div class="flex items-baseline justify-between gap-3">
      <p class="text-xs font-bold uppercase tracking-[1.4px] text-slate-400">
        Harflar
      </p>
      <p class="text-sm text-slate-300">
        <b class="text-lg text-slate-100">{{ data.mastered }}</b> / {{ data.total }}
      </p>
    </div>
    <div class="mt-2 h-1.5 overflow-hidden rounded-full bg-ink-800">
      <div
        class="h-full rounded-full bg-emerald-500 transition-[width]"
        :style="{ width: `${percent}%` }"
      />
    </div>

    <ul
      class="mt-3.5 grid grid-cols-7 gap-1.5"
      dir="rtl"
    >
      <li
        v-for="item in data.letters"
        :key="item.letter"
      >
        <button
          type="button"
          class="font-arabic flex aspect-square w-full items-center justify-center rounded-lg border text-2xl leading-none transition-transform active:scale-95"
          :class="[STATE_CLASS[item.state], selected?.letter === item.letter ? 'ring-2 ring-brand-400' : '']"
          :aria-label="`${item.letter} — ${STATE_LABEL[item.state]}`"
          @click="selected = selected?.letter === item.letter ? null : item"
        >
          {{ item.letter }}
        </button>
      </li>
    </ul>

    <p
      v-if="selected !== null"
      class="mt-3 rounded-lg bg-ink-950 px-3 py-2 text-sm text-slate-300"
    >
      <span class="font-arabic mr-1 text-lg text-slate-100">{{ selected.letter }}</span>
      — {{ STATE_LABEL[selected.state] }}
      <template v-if="selected.lessonNumber !== null">
        · {{ selected.lessonName }}
      </template>
    </p>
    <p
      v-else
      class="mt-3 flex flex-wrap gap-x-3 gap-y-1 text-[11px] text-dim"
    >
      <span><span class="mr-1 inline-block size-2 rounded-full bg-emerald-500" />o‘zlashtirilgan</span>
      <span><span class="mr-1 inline-block size-2 rounded-full bg-amber-400" />o‘rganilmoqda</span>
      <span><span class="mr-1 inline-block size-2 rounded-full bg-slate-600" />oldinda</span>
    </p>
  </section>
</template>
