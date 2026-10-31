<script setup lang="ts">
import { useQuery } from '@tanstack/vue-query'
import { computed } from 'vue'
import { useRouter } from 'vue-router'

import { fetchLessonBook, interactiveKeys } from '@/entities/interactive-unit'
import { AppIcon } from '@/shared/ui'

/**
 * ════════════════════════════════════════════════════════════════════════
 *  "INTERAKTIV DARSLIK" KARTASI — dars sahifasida, tablar ustida (2026-10-29)
 * ════════════════════════════════════════════════════════════════════════
 *
 * ★ TAB EMAS, KARTA: darslik — alohida to'liq ekranli marshrut
 *   (`student-lesson-book`), bu yerda faqat kirish nuqtasi va progress.
 * ★ Holatlar: `granted` — progress halqasi va "Ochish/Davom ettirish";
 *   `upsell` (oddiy Mustaqil) — "Mustaqil+ tarifida" va Tariflarga havola;
 *   `none` — hech narsa chizilmaydi (kontent borligi ham aytilmaydi).
 */
const props = defineProps<{
  lessonId: number
  /** Tik, ixcham ko'rinish — bosh sahifada Nuri bilan yonma-yon (2026-10-31). */
  compact?: boolean
}>()
const router = useRouter()

const query = useQuery({
  queryKey: computed(() => interactiveKeys.lesson(props.lessonId)),
  queryFn: ({ signal }) => fetchLessonBook(props.lessonId, { signal }),
  staleTime: 30_000,
})

const book = computed(() => query.data.value ?? null)
const unit = computed(() => book.value?.unit ?? null)

const percent = computed(() => {
  const p = unit.value?.progress
  if (p === undefined || p.blocksTotal === 0) return 0
  return Math.round((p.blocksDone / p.blocksTotal) * 100)
})

const RING = 2 * Math.PI * 18

function open(): void {
  void router.push({ name: 'student-lesson-book', params: { lessonId: String(props.lessonId) } })
}
</script>

<template>
  <button
    v-if="props.compact && book?.access === 'granted' && unit"
    type="button"
    class="flex h-full w-full flex-col rounded-xl border border-line bg-ink-900 p-3 text-left transition-colors hover:border-brand-500"
    @click="open"
  >
    <span class="flex items-center gap-2">
      <span class="relative flex size-12 shrink-0 items-center justify-center">
        <svg
          viewBox="0 0 44 44"
          class="absolute inset-0 -rotate-90"
          aria-hidden="true"
        >
          <circle
            cx="22"
            cy="22"
            r="18"
            fill="none"
            stroke-width="4"
            class="stroke-ink-800"
          />
          <circle
            cx="22"
            cy="22"
            r="18"
            fill="none"
            stroke-width="4"
            stroke-linecap="round"
            class="stroke-green-500"
            :stroke-dasharray="RING"
            :stroke-dashoffset="RING * (1 - percent / 100)"
          />
        </svg>
        <AppIcon
          name="book"
          :size="18"
          class="text-brand-500"
        />
      </span>
      <span class="text-[10px] font-bold uppercase leading-tight tracking-[1.2px] text-brand-500">Interaktiv darslik</span>
    </span>
    <span class="mt-2 line-clamp-2 text-sm font-bold leading-snug text-slate-100">{{ unit.titleUz }}</span>
    <span class="mb-3 mt-1 text-xs text-slate-400">
      {{ unit.progress.completed ? 'Bajarildi' : `${unit.progress.blocksDone}/${unit.progress.blocksTotal} blok` }}
    </span>
    <span class="mt-auto flex h-9 items-center justify-center rounded-xl bg-brand-500 px-2 text-xs font-semibold text-on-brand">
      {{ unit.progress.blocksDone > 0 ? 'Davom ettirish' : 'Ochish' }}
    </span>
  </button>

  <div
    v-else-if="props.compact && book?.access === 'upsell'"
    class="flex h-full flex-col rounded-xl border border-dashed border-line bg-ink-850 p-3"
  >
    <span class="flex size-12 items-center justify-center rounded-full bg-ink-800 text-slate-400">
      <AppIcon
        name="lock"
        :size="18"
      />
    </span>
    <span class="mt-2 text-sm font-bold leading-snug text-slate-100">Interaktiv darslik</span>
    <span class="mb-3 mt-1 text-xs text-slate-400">Mustaqil+ tarifida: audio, mashqlar, harf yozish.</span>
    <RouterLink
      :to="{ name: 'student-plans' }"
      class="mt-auto flex h-9 items-center justify-center rounded-xl border border-line bg-ink-900 text-xs font-semibold text-slate-200 hover:bg-ink-800"
    >
      Tariflar
    </RouterLink>
  </div>

  <button
    v-else-if="book?.access === 'granted' && unit"
    type="button"
    class="mt-4 flex w-full items-center gap-3.5 rounded-xl border border-line bg-ink-900 px-4 py-3.5 text-left transition-colors hover:border-brand-500"
    @click="open"
  >
    <span class="relative flex size-12 shrink-0 items-center justify-center">
      <svg
        viewBox="0 0 44 44"
        class="absolute inset-0 -rotate-90"
        aria-hidden="true"
      >
        <circle
          cx="22"
          cy="22"
          r="18"
          fill="none"
          stroke-width="4"
          class="stroke-ink-800"
        />
        <circle
          cx="22"
          cy="22"
          r="18"
          fill="none"
          stroke-width="4"
          stroke-linecap="round"
          class="stroke-green-500 transition-[stroke-dashoffset] duration-500"
          :stroke-dasharray="RING"
          :stroke-dashoffset="RING * (1 - percent / 100)"
        />
      </svg>
      <AppIcon
        name="book"
        :size="18"
        class="text-brand-500"
      />
    </span>
    <span class="min-w-0 flex-1">
      <span class="block text-[11px] font-bold uppercase tracking-wider text-brand-500">Interaktiv darslik</span>
      <span class="block truncate text-[15px] font-semibold text-slate-100">{{ unit.titleUz }}</span>
      <span class="block text-xs text-slate-400">
        <template v-if="unit.progress.completed">Bajarildi</template>
        <template v-else>{{ unit.progress.blocksDone }}/{{ unit.progress.blocksTotal }} blok</template>
        <template v-if="unit.progress.exerciseTotal > 0"> · mashqlar {{ unit.progress.exerciseRight }}/{{ unit.progress.exerciseTotal }}</template>
        <template v-if="unit.progress.testScore !== null"> · test {{ unit.progress.testScore }}/{{ unit.progress.testMax }}</template>
      </span>
    </span>
    <span class="shrink-0 rounded-lg bg-brand-500 px-3 py-2 text-[13px] font-semibold text-on-brand">
      {{ unit.progress.blocksDone > 0 ? 'Davom ettirish' : 'Ochish' }}
    </span>
  </button>

  <div
    v-else-if="book?.access === 'upsell'"
    class="mt-4 flex items-center gap-3.5 rounded-xl border border-dashed border-line bg-ink-850 px-4 py-3.5"
  >
    <span class="flex size-12 shrink-0 items-center justify-center rounded-full bg-ink-800 text-slate-400">
      <AppIcon
        name="lock"
        :size="18"
      />
    </span>
    <span class="min-w-0 flex-1">
      <span class="block text-[15px] font-semibold text-slate-100">Interaktiv darslik Mustaqil+ tarifida</span>
      <span class="block text-xs text-slate-400">Audio, mashqlar, harf yozish va mini-test.</span>
    </span>
    <RouterLink
      :to="{ name: 'student-plans' }"
      class="shrink-0 rounded-lg border border-line bg-ink-900 px-3 py-2 text-[13px] font-semibold text-slate-200 hover:bg-ink-800"
    >
      Tariflar
    </RouterLink>
  </div>
</template>
