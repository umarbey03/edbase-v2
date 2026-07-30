<script setup lang="ts">
import { computed, ref } from 'vue'

import { sessionStartState, sessionStatusLabel, sessionTypeLabel } from '@/entities/session'
import { toUserMessage } from '@/shared/api'
import { formatWeekdayDateTime, formatTime, monthNameCapitalized } from '@/shared/lib/datetime'
import { useNow } from '@/shared/lib/use-now'
import type { ScheduledSessionDto } from '@/shared/types'
import { BaseButton, BaseCard, DataStatus } from '@/shared/ui'

import type { CalendarEventTone } from '../model/calendar'
import { buildMonthGrid, calendarEvent, TEACHER_WEEKDAYS } from '../model/calendar'
import { useGroupSchedule, useSessionStart } from '../model/use-group-schedule'

/**
 * "Darslar" tabi — eski `#tab-lessons` dagi oy kalendari.
 *
 * ★ ATAYLAB KO'CHIRILMAGAN IKKI BLOK (sabab: v2 da endpoint yo'q):
 *  1) "Kurs sur'ati" kartochkasi (`/course-progress` + `/taught`) — ustoz
 *     qaysi darsni o'tganini belgilar va o'quvchilarga keyingi video dars
 *     ochilardi. v2 da kurs gating'i bor (`LessonLockReason.TeacherPace`),
 *     lekin uni ustoz qo'lda siljitadigan endpoint yozilmagan.
 *  2) "Yordamchi dars qo'shish" formasi — v2 da darslar guruh JADVALIDAN
 *     avtomatik yaratiladi (`schedule/regenerate`), bitta dars qo'shish
 *     endpointi yo'q.
 */
const props = defineProps<{ groupId: number }>()

const now = useNow()
const scheduleQuery = useGroupSchedule(props.groupId)
const { start, openRoom, pendingId, error: actionError } = useSessionStart(props.groupId)

const sessions = computed(() => scheduleQuery.data.value ?? [])

const scheduleError = computed(() =>
  scheduleQuery.error.value !== null ? toUserMessage(scheduleQuery.error.value) : null,
)

/* ------------------------------------------------------------------- oy */

const anchor = ref(new Date())

const monthTitle = computed(
  () => `${monthNameCapitalized(anchor.value.getMonth())} ${anchor.value.getFullYear()}`,
)

const grid = computed(() => buildMonthGrid(anchor.value, sessions.value, now.value))

function moveMonth(delta: number): void {
  anchor.value = new Date(anchor.value.getFullYear(), anchor.value.getMonth() + delta, 1)
}

function goToday(): void {
  anchor.value = new Date()
}

/* ---------------------------------------------------------- tanlangan dars */

/**
 * ★ CHEKINISH: eski ilova darsni bosganda `toast()` yoki `confirm()`
 * ko'rsatardi. v2 xodim karkasida toast yo'q va `confirm()` — brauzer
 * modali (uslubsiz, telefonda qo'pol). Shuning uchun tanlangan dars
 * kalendar OSTIDA panel bo'lib ochiladi: bir xil ma'lumot, lekin
 * yo'qolib ketmaydi va amal tugmasi shu yerda.
 */
const selectedId = ref<number | null>(null)

const selected = computed<ScheduledSessionDto | null>(
  () => sessions.value.find((item) => item.id === selectedId.value) ?? null,
)

const selectedState = computed(() =>
  selected.value === null ? null : sessionStartState(selected.value, now.value),
)

const TONE_CLASS: Record<CalendarEventTone, string> = {
  live: 'bg-rose-500/20 text-rose-400 font-bold',
  held: 'bg-green-500/15 text-green-400',
  missed: 'bg-rose-500/12 text-rose-400',
  teacher: 'bg-brand-500/14 text-brand-500',
  assistant: 'bg-green-500/15 text-green-400',
}

function eventOf(session: ScheduledSessionDto) {
  return calendarEvent(session, now.value)
}
</script>

<template>
  <BaseCard flush>
    <div class="p-3.5 sm:p-5">
      <header class="mb-3.5 flex flex-wrap items-center justify-between gap-2.5">
        <h2 class="text-[15px] font-semibold">
          Darslar — <span v-text="monthTitle" />
        </h2>
        <!--
          Oy navigatsiyasi NATIV tugmalarda: `BaseButton` `aria-label` ni
          turlangan prop sifatida qabul qilmaydi, "‹"/"›" belgilarining
          o'zi esa ekran o'quvchisiga hech nima demaydi.
        -->
        <div class="flex items-center gap-1.5">
          <button
            type="button"
            class="tap-target rounded-lg text-slate-300 transition-colors hover:bg-ink-800 hover:text-slate-100"
            aria-label="Oldingi oy"
            @click="moveMonth(-1)"
          >
            ‹
          </button>
          <button
            type="button"
            class="tap-target rounded-lg px-3 text-xs font-semibold text-slate-300 transition-colors hover:bg-ink-800 hover:text-slate-100"
            @click="goToday"
          >
            Bugun
          </button>
          <button
            type="button"
            class="tap-target rounded-lg text-slate-300 transition-colors hover:bg-ink-800 hover:text-slate-100"
            aria-label="Keyingi oy"
            @click="moveMonth(1)"
          >
            ›
          </button>
        </div>
      </header>

      <p
        v-if="actionError !== null"
        class="mb-3 rounded-lg border border-rose-500/25 bg-rose-500/10 p-3 text-xs text-rose-200"
        role="alert"
        v-text="actionError"
      />

      <DataStatus
        :pending="scheduleQuery.isPending.value"
        :error="scheduleError"
        :empty="sessions.length === 0"
        :retrying="scheduleQuery.isFetching.value"
        :skeleton-rows="3"
        empty-icon="calendar"
        empty-title="Hali darslar yo‘q"
        @retry="scheduleQuery.refetch()"
      >
        <div class="grid grid-cols-7 gap-1.5">
          <p
            v-for="weekday in TEACHER_WEEKDAYS"
            :key="weekday"
            class="py-1 text-center text-[11px] font-semibold uppercase tracking-[0.4px] text-slate-400"
            v-text="weekday"
          />

          <div
            v-for="cell in grid"
            :key="cell.key"
            class="min-h-[58px] rounded-lg border p-1.5 sm:min-h-[85px]"
            :class="[
              cell.inMonth ? 'border-line bg-ink-900' : 'border-line bg-ink-900 opacity-25',
              cell.isToday ? 'border-brand-500 bg-brand-500/12' : '',
            ]"
          >
            <p class="flex items-center justify-between text-[11px] font-bold text-slate-400">
              <span
                :class="cell.isToday ? 'text-brand-500' : ''"
                v-text="cell.dayNumber"
              />
              <span
                v-if="cell.isToday"
                class="rounded-[10px] bg-brand-500 px-1.5 text-[9px] text-on-brand"
              >Bugun</span>
            </p>

            <button
              v-for="session in cell.sessions"
              :key="session.id"
              type="button"
              class="mt-1 block w-full truncate rounded-md px-1.5 py-0.5 text-left text-[11px] font-medium"
              :class="[
                TONE_CLASS[eventOf(session).tone],
                session.id === selectedId ? 'ring-1 ring-brand-500' : '',
              ]"
              :title="`${session.title ?? sessionTypeLabel(session.type)} — ${eventOf(session).label}`"
              @click="selectedId = session.id"
            >
              {{ formatTime(session.scheduledStart) }} {{ eventOf(session).label }}
            </button>
          </div>
        </div>

        <!-- Eski `.legend` — kalendar ranglarining ma'nosi. -->
        <div
          class="mt-3.5 flex flex-wrap gap-4 rounded-lg border border-line bg-ink-950 px-4 py-3 text-xs text-slate-400"
        >
          <span class="inline-flex items-center gap-1.5">
            <b class="text-brand-500">●</b>Rejadagi dars
          </span>
          <span class="inline-flex items-center gap-1.5">
            <b class="text-green-400">●</b>O‘tilgan
          </span>
          <span class="inline-flex items-center gap-1.5">
            <b class="text-rose-400">●</b>O‘tilmagan / jonli
          </span>
        </div>

        <!-- ===================== Tanlangan dars paneli ===================== -->
        <div
          v-if="selected !== null"
          class="mt-3.5 flex flex-wrap items-center justify-between gap-3 rounded-lg border border-line bg-ink-950 p-3.5"
        >
          <div class="min-w-0">
            <p
              class="truncate text-sm font-semibold text-slate-100"
              v-text="selected.title ?? sessionTypeLabel(selected.type)"
            />
            <p class="mt-0.5 text-xs text-slate-400">
              {{ sessionTypeLabel(selected.type) }} ·
              {{ formatWeekdayDateTime(selected.scheduledStart) }} ·
              {{ sessionStatusLabel(selected.status) }}
            </p>
          </div>
          <BaseButton
            v-if="selectedState?.kind === 'live'"
            size="sm"
            variant="success"
            @click="openRoom(selected.id)"
          >
            Darsga qaytish
          </BaseButton>
          <BaseButton
            v-else-if="selectedState?.kind === 'ready'"
            size="sm"
            :loading="pendingId === selected.id"
            @click="start(selected.id)"
          >
            Darsni boshlash
          </BaseButton>
          <span
            v-else-if="selectedState?.kind === 'wait'"
            class="text-xs text-slate-400"
          >
            Dars boshlanishiga vaqt bor — {{ selectedState.text }} qoldi
          </span>
        </div>
      </DataStatus>
    </div>
  </BaseCard>
</template>
