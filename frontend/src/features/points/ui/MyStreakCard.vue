<script setup lang="ts">
import { useQuery } from '@tanstack/vue-query'
import { computed } from 'vue'

import { fetchMyStreak, type StreakDayState } from '@/entities/points'

/**
 * 🔥 KUNLIK SERIYA — Mustaqil o'quvchi (2026-11-21).
 *
 * Mustaqil o'quvchida jonli dars yo'q, shuning uchun guruhdagi "ketma-ket
 * dars" seriyasi unga hech qachon tegmasdi. Bu yerda — KUNLAR: video, test,
 * vazifa yoki darslik birligi = faol kun. Haftasiga bitta o'tkazib
 * yuborilgan kun seriyani kuydirmaydi (❄️). Qoida — server `DailyStreak`.
 */
const query = useQuery({
  queryKey: ['progress', 'streak'],
  queryFn: ({ signal }) => fetchMyStreak({ signal }),
  staleTime: 60_000,
})

const streak = computed(() => (query.data.value?.enabled === true ? query.data.value : null))

const daysToBonus = computed(() => (streak.value === null ? 0 : streak.value.nextBonusDay - streak.value.current))

const headline = computed(() => {
  const s = streak.value
  if (s === null) return ''
  if (s.current === 0) return 'Seriyani bugun boshlang'
  return `${s.current} kunlik seriya`
})

const hint = computed(() => {
  const s = streak.value
  if (s === null) return ''
  if (s.doneToday) return 'Bugun o‘qidingiz — seriya davom etyapti ✓'
  if (s.current === 0) return 'Bitta video ko‘ring yoki testni ishlang — 1-kun hisoblanadi.'
  return 'Bugun hali o‘qimadingiz — bitta video yoki test yetarli, seriya kuymasin.'
})

const DAY_CLASS: Record<StreakDayState, string> = {
  Active: 'bg-orange-500 text-white',
  Frozen: 'bg-sky-500/25 text-sky-200',
  Missed: 'bg-ink-800 text-slate-600',
  Today: 'border border-dashed border-orange-400/70 text-orange-300',
}

const WEEKDAYS = ['Ya', 'Du', 'Se', 'Ch', 'Pa', 'Ju', 'Sh']

function weekday(iso: string): string {
  return WEEKDAYS[new Date(`${iso}T12:00:00`).getDay()] ?? ''
}
</script>

<template>
  <section
    v-if="streak !== null"
    class="rounded-xl border border-line bg-ink-900 p-[18px] lg:p-5"
    aria-label="Kunlik seriya"
  >
    <div class="flex items-center gap-3">
      <span
        class="flex size-12 shrink-0 items-center justify-center rounded-2xl text-2xl"
        :class="streak.current > 0 ? 'bg-orange-500/15' : 'bg-ink-800 grayscale'"
        aria-hidden="true"
      >🔥</span>
      <div class="min-w-0 flex-1">
        <p
          class="text-lg font-bold text-slate-100"
          v-text="headline"
        />
        <p
          class="text-xs"
          :class="streak.doneToday ? 'text-emerald-300' : 'text-slate-400'"
          v-text="hint"
        />
      </div>
    </div>

    <!-- Oxirgi 14 kun -->
    <ol class="mt-3.5 grid grid-cols-7 gap-1.5">
      <li
        v-for="day in streak.days.slice(-14)"
        :key="day.date"
        class="flex flex-col items-center gap-0.5"
      >
        <span
          class="flex size-7 items-center justify-center rounded-full text-[11px] font-bold"
          :class="DAY_CLASS[day.state]"
          :title="day.date"
        >{{ day.state === 'Active' ? '✓' : day.state === 'Frozen' ? '❄' : '' }}</span>
        <span
          class="text-[10px] text-dim"
          v-text="weekday(day.date)"
        />
      </li>
    </ol>

    <!-- Haftalik maqsad (2026-11-21): haftada 4 kun → +10 ball. -->
    <div class="mt-3 rounded-lg bg-ink-950 px-3 py-2">
      <div class="flex items-center justify-between gap-2 text-xs">
        <span class="font-semibold text-slate-200">
          🎯 Haftalik maqsad: {{ streak.weekGoalDays }} kun
        </span>
        <span
          :class="streak.weekActiveDays >= streak.weekGoalDays ? 'font-bold text-emerald-300' : 'text-slate-400'"
        >
          {{ Math.min(streak.weekActiveDays, streak.weekGoalDays) }}/{{ streak.weekGoalDays }}
          {{ streak.weekActiveDays >= streak.weekGoalDays ? `· +${streak.weekGoalBonus} ✓` : `· +${streak.weekGoalBonus} ball` }}
        </span>
      </div>
      <div
        class="mt-1.5 grid gap-1"
        :style="{ gridTemplateColumns: `repeat(${streak.weekGoalDays}, minmax(0, 1fr))` }"
      >
        <span
          v-for="i in streak.weekGoalDays"
          :key="i"
          class="h-1.5 rounded-full"
          :class="i <= streak.weekActiveDays ? 'bg-emerald-500' : 'bg-ink-800'"
        />
      </div>
    </div>

    <div class="mt-3 flex flex-wrap items-center gap-x-4 gap-y-1 text-xs text-slate-400">
      <span>
        🎁 <b class="text-slate-200">+{{ streak.nextBonusPoints }}</b> ball —
        {{ streak.nextBonusDay }} kunlik seriyada
        <template v-if="daysToBonus > 0">({{ daysToBonus }} kun qoldi)</template>
      </span>
      <span :class="streak.freezeAvailable ? 'text-sky-300' : 'text-slate-500'">
        ❄️ {{ streak.freezeAvailable ? 'Bu hafta 1 kun dam olsangiz ham kuymaydi' : 'Bu haftaning muzlatishi ishlatilgan' }}
      </span>
      <span v-if="streak.best > streak.current">🏅 Eng uzuni: {{ streak.best }} kun</span>
    </div>
  </section>
</template>
