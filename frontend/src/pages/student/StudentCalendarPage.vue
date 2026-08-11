<script setup lang="ts">
import { computed, ref } from 'vue'
import { useRouter } from 'vue-router'

import { sessionTitle } from '@/entities/session'
import {
  sessionState,
  useStudentSchedule,
} from '@/features/student-schedule/model/useStudentSchedule'
import { formatTime, monthNameCapitalized, WEEKDAY_HEADERS_UZ } from '@/shared/lib/datetime'
import { useNow } from '@/shared/lib/use-now'
import type { LiveSessionDto } from '@/shared/types'
import { AppIcon, BaseBadge, BaseButton } from '@/shared/ui'

/**
 * KALENDAR — eski `#calendar` bo'limi.
 *
 * Tuzilishi eski ilovadan aynan: guruh chiplari -> oy setkasi -> tanlangan
 * kun darslari. Hafta YAKSHANBADAN boshlanadi (eski `WD` massivi).
 *
 * ★ SERVER CHEGARASI: `GET /api/v1/live-sessions` faqat `scheduledEnd >=
 *   hozir - 6 soat` bo'lgan darslarni beradi va boshqa endpoint yo'q
 *   (`/groups/{id}/schedule` o'quvchiga 403). Ya'ni O'TGAN OYLAR bo'sh
 *   ko'rinadi. Buni yashirmaymiz — o'tgan oyga o'tilganda sabab yoziladi,
 *   aks holda o'quvchi "darslarim yo'qolibdi" deb o'ylardi.
 */
const now = useNow()
const router = useRouter()
const schedule = useStudentSchedule(now)

/** `null` — "Barchasi". */
const selectedGroupId = ref<number | null>(null)

const today = computed(() => {
  const value = new Date(now.value)
  value.setHours(0, 0, 0, 0)
  return value
})

/** Ko'rsatilayotgan oyning birinchi kuni. */
const viewMonth = ref(new Date(new Date().getFullYear(), new Date().getMonth(), 1))

/** Tanlangan kun; boshida — bugun. */
const selectedDay = ref(new Date(new Date().setHours(0, 0, 0, 0)))

const visibleSessions = computed(() =>
  selectedGroupId.value === null
    ? schedule.sessions.value
    : schedule.sessions.value.filter((item) => item.groupId === selectedGroupId.value),
)

/** `2026-6-30` -> shu kundagi darslar. Kalendar setkasi shu xaritadan o'qiydi. */
const sessionsByDay = computed(() => {
  const map = new Map<string, LiveSessionDto[]>()
  for (const item of visibleSessions.value) {
    const date = new Date(item.scheduledStart)
    if (Number.isNaN(date.getTime())) continue
    const key = `${date.getFullYear()}-${date.getMonth()}-${date.getDate()}`
    const bucket = map.get(key)
    if (bucket === undefined) map.set(key, [item])
    else bucket.push(item)
  }
  return map
})

interface CalendarCell {
  key: string
  day: number
  isToday: boolean
  isSelected: boolean
  /** Kun ostidagi nuqtalar: dars turlari (takrorsiz, ko'pi bilan 3 ta). */
  marks: string[]
}

const monthLabel = computed(
  () => `${monthNameCapitalized(viewMonth.value.getMonth())} ${viewMonth.value.getFullYear()}`,
)

/** Ko'rsatilayotgan oy butunlay o'tib ketganmi (server ma'lumot bermaydi). */
const isPastMonth = computed(() => {
  const current = new Date(now.value.getFullYear(), now.value.getMonth(), 1)
  return viewMonth.value.getTime() < current.getTime()
})

const cells = computed<CalendarCell[]>(() => {
  const year = viewMonth.value.getFullYear()
  const month = viewMonth.value.getMonth()
  const leading = new Date(year, month, 1).getDay()
  const dayCount = new Date(year, month + 1, 0).getDate()

  const result: CalendarCell[] = []
  for (let index = 0; index < leading; index += 1) {
    result.push({ key: `empty-${index}`, day: 0, isToday: false, isSelected: false, marks: [] })
  }
  for (let day = 1; day <= dayCount; day += 1) {
    const key = `${year}-${month}-${day}`
    const daySessions = sessionsByDay.value.get(key) ?? []
    const cellDate = new Date(year, month, day)
    result.push({
      key,
      day,
      isToday: cellDate.getTime() === today.value.getTime(),
      isSelected: cellDate.getTime() === selectedDay.value.getTime(),
      marks: [...new Set(daySessions.map((item) => item.type))].slice(0, 3),
    })
  }
  return result
})

const selectedLabel = computed(
  () => `${selectedDay.value.getDate()}-${monthNameCapitalized(selectedDay.value.getMonth())}`,
)

const selectedSessions = computed(() => {
  const key = `${selectedDay.value.getFullYear()}-${selectedDay.value.getMonth()}-${selectedDay.value.getDate()}`
  return [...(sessionsByDay.value.get(key) ?? [])].sort(
    (a, b) => new Date(a.scheduledStart).getTime() - new Date(b.scheduledStart).getTime(),
  )
})

function moveMonth(delta: number): void {
  const next = new Date(viewMonth.value)
  next.setMonth(next.getMonth() + delta)
  viewMonth.value = next
}

function selectDay(cell: CalendarCell): void {
  if (cell.day === 0) return
  selectedDay.value = new Date(
    viewMonth.value.getFullYear(),
    viewMonth.value.getMonth(),
    cell.day,
  )
}

function open(sessionId: number): void {
  void router.push({ name: 'live-room', params: { sessionId: String(sessionId) } })
}
</script>

<template>
  <div>
    <!-- ====================== Guruh tanlash (eski `.gsel`) ================== -->
    <div
      v-if="schedule.groups.value.length > 1"
      class="scrollbar-none mb-2.5 flex gap-2 overflow-x-auto pb-2"
    >
      <button
        type="button"
        class="min-h-11 shrink-0 whitespace-nowrap rounded-[20px] border px-4 text-[13px] font-semibold transition-colors"
        :class="
          selectedGroupId === null
            ? 'border-brand-500 bg-brand-500 text-on-brand'
            : 'border-line bg-ink-900 text-slate-400'
        "
        @click="selectedGroupId = null"
      >
        Barchasi
      </button>
      <button
        v-for="group in schedule.groups.value"
        :key="group.id"
        type="button"
        class="min-h-11 shrink-0 whitespace-nowrap rounded-[20px] border px-4 text-[13px] font-semibold transition-colors"
        :class="
          selectedGroupId === group.id
            ? 'border-brand-500 bg-brand-500 text-on-brand'
            : 'border-line bg-ink-900 text-slate-400'
        "
        @click="selectedGroupId = group.id"
        v-text="group.name"
      />
    </div>

    <!-- ============================ Oy setkasi ============================= -->
    <section class="rounded-xl border border-line bg-ink-900 p-[18px]">
      <div class="mb-3.5 flex items-center justify-between">
        <button
          type="button"
          class="tap-target flex items-center justify-center rounded-[11px] border border-line bg-ink-800 text-slate-100 transition-transform active:scale-90"
          aria-label="Oldingi oy"
          @click="moveMonth(-1)"
        >
          <AppIcon
            name="chevron-right"
            :size="17"
            class="rotate-180"
          />
        </button>
        <b
          class="text-base font-bold"
          v-text="monthLabel"
        />
        <button
          type="button"
          class="tap-target flex items-center justify-center rounded-[11px] border border-line bg-ink-800 text-slate-100 transition-transform active:scale-90"
          aria-label="Keyingi oy"
          @click="moveMonth(1)"
        >
          <AppIcon
            name="chevron-right"
            :size="17"
          />
        </button>
      </div>

      <div class="grid w-full grid-cols-7 gap-[5px]">
        <div
          v-for="weekday in WEEKDAY_HEADERS_UZ"
          :key="weekday"
          class="overflow-hidden py-[3px] text-center text-[9px] font-bold uppercase text-dim"
          v-text="weekday"
        />

        <template
          v-for="cell in cells"
          :key="cell.key"
        >
          <div
            v-if="cell.day === 0"
            class="aspect-square"
            aria-hidden="true"
          />
          <button
            v-else
            type="button"
            class="relative flex aspect-square min-w-0 items-center justify-center rounded-[11px] border-[1.5px] text-[13px] transition-transform active:scale-90"
            :class="[
              cell.isSelected
                ? 'scale-105 border-transparent bg-brand-500 font-bold text-on-brand'
                : 'bg-ink-800',
              cell.isToday && !cell.isSelected ? 'border-brand-500' : 'border-transparent',
            ]"
            :aria-label="`${cell.day}-${monthNameCapitalized(viewMonth.getMonth())}`"
            :aria-pressed="cell.isSelected"
            @click="selectDay(cell)"
          >
            {{ cell.day }}
            <!--
              Kun ostidagi nuqtalar — dars TURI. Ranglar tokendan (ilgari
              `#f5b731` oltin va `#22d3ee` firuza QOTIB QOLGAN edi).
              Nuqta grafik element, shuning uchun `-500` (to'yingan) daraja:
              matn darajasi (`-300`) 5px doirada kir dog' bo'lib ko'rinadi.
            -->
            <span
              v-if="cell.marks.length > 0"
              class="absolute bottom-1 flex gap-[2.5px]"
              aria-hidden="true"
            >
              <i
                v-for="mark in cell.marks"
                :key="mark"
                class="size-[5px] rounded-full"
                :class="mark === 'Teacher' ? 'bg-brand-500' : 'bg-cyan-500'"
              />
            </span>
          </button>
        </template>
      </div>
    </section>

    <!--
      O'tgan oyga o'tilganda: setka bo'sh bo'lishining SABABI aytiladi.
      Bu vaqtinchalik — server tarixni bera boshlagach bu blok o'chiriladi.
    -->
    <p
      v-if="isPastMonth"
      class="mt-3 rounded-xl border border-brand-500/30 bg-brand-500/[0.06] px-4 py-3 text-xs leading-relaxed text-slate-400"
    >
      O‘tgan oylar bo‘sh ko‘rinadi: server hozircha faqat joriy va kelgusi
      darslarni beradi (yakunlangan dars 6 soatdan keyin ro‘yxatdan chiqadi).
    </p>

    <!-- ======================== Tanlangan kun darslari ====================== -->
    <div class="mt-[18px]">
      <p
        v-if="selectedSessions.length === 0"
        class="px-2.5 py-8 text-center text-sm text-slate-400"
      >
        {{ selectedLabel }} uchun dars yo‘q
      </p>

      <template v-else>
        <h2
          class="mb-3 ml-1 text-xs font-bold uppercase tracking-[1.4px] text-slate-400"
          v-text="selectedLabel"
        />
        <article
          v-for="item in selectedSessions"
          :key="item.id"
          class="mb-2.5 flex items-center gap-3 rounded-[13px] border border-line bg-ink-900 p-[13px]"
        >
          <!--
            Dars turi nishoni: PASTEL tint + to'q ikonka (ilgari
            `rgb(245 183 49 / .18)` + `#fcd34d` va `rgb(34 211 238 / .17)` +
            `#67e8f9` qotib qolgan edi — qorong'i fonda yorug' ikonka
            kerak edi, oq fonda esa aksincha).
          -->
          <span
            class="flex size-[42px] shrink-0 items-center justify-center rounded-xl"
            :class="
              item.type === 'Teacher'
                ? 'bg-brand-500/12 text-brand-300'
                : 'bg-cyan-500/12 text-cyan-300'
            "
            aria-hidden="true"
          >
            <AppIcon
              :name="item.type === 'Teacher' ? 'graduation' : 'user-check'"
              :size="18"
            />
          </span>

          <div class="min-w-0 flex-1">
            <b
              class="block truncate text-sm"
              v-text="sessionTitle(item)"
            />
            <span class="block truncate text-xs text-slate-400">
              {{ item.groupName }} · {{ formatTime(item.scheduledStart) }}
            </span>
          </div>

          <BaseButton
            v-if="sessionState(item, now) === 'live'"
            class="animate-pulse-btn shrink-0"
            variant="danger"
            size="sm"
            @click="open(item.id)"
          >
            Kirish
          </BaseButton>
          <BaseBadge
            v-else-if="sessionState(item, now) === 'past'"
            class="shrink-0"
            tone="neutral"
          >
            Yakunlangan
          </BaseBadge>
          <BaseBadge
            v-else
            class="shrink-0"
            tone="accent"
          >
            Rejada
          </BaseBadge>
        </article>
      </template>
    </div>
  </div>
</template>
