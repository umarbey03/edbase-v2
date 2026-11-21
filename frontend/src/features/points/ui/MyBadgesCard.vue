<script setup lang="ts">
import { useQuery } from '@tanstack/vue-query'
import { computed, ref } from 'vue'

import { fetchMyBadges, type BadgeDto } from '@/entities/points'
import { formatDateNumeric } from '@/shared/lib/datetime'

/**
 * 🏅 NISHONLAR — profil (2026-11-21, gamifikatsiya 3-bosqich).
 *
 * Olinganlari rangli, oldindagilari kulrang va bajarilish chizig'i bilan
 * (masalan 3/5). Oxirgi 3 kunda olingani — «yangi» belgisi. Bosilsa —
 * nimani qilish kerakligi.
 */
const query = useQuery({
  queryKey: ['progress', 'badges'],
  queryFn: ({ signal }) => fetchMyBadges({ signal }),
  staleTime: 60_000,
})

const data = computed(() => query.data.value ?? null)

// Olinganlari oldin; ichida — eng yaqinlari (bajarilishi ko'pi) tepada.
const ordered = computed(() =>
  [...(data.value?.badges ?? [])].sort((a, b) =>
    Number(b.earned) - Number(a.earned) || b.progress / b.target - a.progress / a.target))

const selected = ref<BadgeDto | null>(null)

const NEW_MS = 3 * 24 * 60 * 60 * 1000

function isNew(badge: BadgeDto): boolean {
  return badge.earnedAt !== null && Date.now() - new Date(badge.earnedAt).getTime() < NEW_MS
}
</script>

<template>
  <section
    v-if="data !== null && data.total > 0"
    class="rounded-xl border border-line bg-ink-900 p-[18px] lg:p-5"
    aria-label="Nishonlar"
  >
    <div class="flex items-baseline justify-between gap-3">
      <p class="text-xs font-bold uppercase tracking-[1.4px] text-slate-400">
        Nishonlar
      </p>
      <p class="text-sm text-slate-300">
        <b class="text-lg text-slate-100">{{ data.earned }}</b> / {{ data.total }}
      </p>
    </div>

    <ul class="mt-3 grid grid-cols-4 gap-2 sm:grid-cols-5">
      <li
        v-for="badge in ordered"
        :key="badge.code"
      >
        <button
          type="button"
          class="relative flex w-full flex-col items-center gap-1 rounded-xl border px-1 py-2 text-center transition-transform active:scale-95"
          :class="[
            badge.earned ? 'border-amber-400/40 bg-amber-400/10' : 'border-line bg-ink-950',
            selected?.code === badge.code ? 'ring-2 ring-brand-400' : '',
          ]"
          :aria-label="`${badge.title} — ${badge.earned ? 'olingan' : `${badge.progress}/${badge.target}`}`"
          @click="selected = selected?.code === badge.code ? null : badge"
        >
          <span
            class="text-2xl leading-none"
            :class="badge.earned ? '' : 'opacity-35 grayscale'"
            aria-hidden="true"
          >{{ badge.emoji }}</span>
          <span
            class="line-clamp-2 text-[10px] font-semibold leading-tight"
            :class="badge.earned ? 'text-amber-100' : 'text-slate-500'"
            v-text="badge.title"
          />
          <span
            v-if="!badge.earned && badge.target > 1"
            class="h-1 w-full overflow-hidden rounded-full bg-ink-800"
          >
            <span
              class="block h-full rounded-full bg-slate-500"
              :style="{ width: `${Math.round((badge.progress / badge.target) * 100)}%` }"
            />
          </span>
          <span
            v-if="isNew(badge)"
            class="absolute -right-1 -top-1.5 rounded-full bg-rose-500 px-1.5 text-[9px] font-bold uppercase text-white"
          >yangi</span>
        </button>
      </li>
    </ul>

    <p
      v-if="selected !== null"
      class="mt-3 rounded-lg bg-ink-950 px-3 py-2 text-sm text-slate-300"
    >
      <span class="mr-1">{{ selected.emoji }}</span>
      <b class="text-slate-100">{{ selected.title }}</b> — {{ selected.description }}.
      <template v-if="selected.earned">
        <span class="text-emerald-300">Olindi{{ selected.earnedAt ? ` (${formatDateNumeric(selected.earnedAt)})` : '' }} ✓</span>
      </template>
      <template v-else>
        Hozir: {{ selected.progress }}/{{ selected.target }}.
      </template>
    </p>
  </section>
</template>
