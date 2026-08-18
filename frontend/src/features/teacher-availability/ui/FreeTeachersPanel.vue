<script setup lang="ts">
import { useQuery } from '@tanstack/vue-query'
import { computed, ref } from 'vue'

import { fetchFreeTeachers, todayIso } from '@/entities/teacher-availability'
import { toUserMessage } from '@/shared/api'
import { useDebounced } from '@/shared/lib/debounce'
import { AppIcon, BaseBadge, BaseCard, DataStatus } from '@/shared/ui'

/**
 * ════════════════════════════════════════════════════════════════════════
 *  BO'SH USTOZLAR (2026-08-18)
 * ════════════════════════════════════════════════════════════════════════
 *
 * Loyiha egasi: *"14:00 da bugunni belgilasam qaysi ustozlar bo'shligini
 * ko'rsatsin, ind qo'yib berayotganda birinchi shunga qarardim"*.
 *
 * ★ NEGA "JONLI DARSLAR" JADVALIDAN TOPIB BO'LMAYDI: u KIM DARS
 * O'TAYAPTI ni ko'rsatadi, bu yerda esa TESKARISI kerak — kim dars
 * o'tMAYAPTI. Bo'sh ustozni band darslar ro'yxatidan ayirib topish ko'z
 * bilan bajariladigan ish edi va xatoga juda moyil.
 *
 * ★ YUKLAMA HAM KO'RSATILADI ("bugun 3 ta darsi bor, 08:00–14:20"):
 * bo'sh ustozlar bir nechta bo'lsa, operator odatda eng kam yuklangani
 * ni tanlaydi. Faqat "bo'sh" deyish bu qarorni qo'llab-quvvatlamasdi.
 *
 * ★ "O'TOLMAYMAN" DEGAN USTOZ BO'SH SANALMAYDI: kunlik so'rovga rad
 * javobi bergan ustozning jadvali bo'sh ko'rinishi mumkin, lekin unga
 * dars qo'yish xato bo'lardi (server ham shu qoidani qo'llaydi).
 */

/** Tez tanlash uchun odatiy dars vaqtlari. */
const TIME_PRESETS = ['09:00', '11:00', '14:00', '16:00', '18:00'] as const

const DURATIONS = [
  { value: 45, label: '45 daq' },
  { value: 60, label: '1 soat' },
  { value: 80, label: '80 daq' },
  { value: 120, label: '2 soat' },
] as const

const date = ref(todayIso())
const time = ref('14:00')
const durationMinutes = ref<number>(60)
const includeAssistants = ref(false)
const onlyFree = ref(true)
const search = ref('')
const debouncedSearch = useDebounced(search)

const effectiveSearch = computed(() => {
  const term = debouncedSearch.value.trim()
  return term.length > 0 ? term : undefined
})

const params = computed(() => ({
  date: date.value,
  time: time.value,
  durationMinutes: durationMinutes.value,
  includeAssistants: includeAssistants.value,
  onlyFree: onlyFree.value,
  search: effectiveSearch.value,
}))

const freeQuery = useQuery({
  queryKey: ['free-teachers', params],
  queryFn: ({ signal }) => fetchFreeTeachers(params.value, { signal }),
  enabled: computed(() => date.value.length > 0 && time.value.length > 0),
})

const result = computed(() => freeQuery.data.value ?? null)
const teachers = computed(() => result.value?.teachers ?? [])

const loadError = computed(() =>
  freeQuery.error.value !== null ? toUserMessage(freeQuery.error.value) : null,
)

/** `HH:mm:ss` → `HH:mm` (server `TimeOnly` ni soniya bilan qaytaradi). */
function shortTime(value: string | null): string {
  return value === null ? '—' : value.slice(0, 5)
}

function roleLabel(role: string): string {
  return role === 'Assistant' ? 'Kurator' : 'Ustoz'
}

/** "08:00 – 14:20" yoki darsi bo'lmasa `null`. */
function dayRange(first: string | null, last: string | null): string | null {
  if (first === null || last === null) return null

  return `${shortTime(first)} – ${shortTime(last)}`
}
</script>

<template>
  <div>
    <!-- ═════════════════════ TANLOV ═════════════════════ -->
    <div class="mb-4 rounded-2xl border border-line bg-ink-900 p-4">
      <div class="grid gap-2.5 sm:grid-cols-2 lg:grid-cols-4">
        <label class="block">
          <span class="mb-1 block text-[11px] font-semibold text-slate-400">Kun</span>
          <input
            v-model="date"
            class="zn-input"
            type="date"
          >
        </label>

        <label class="block">
          <span class="mb-1 block text-[11px] font-semibold text-slate-400">Vaqt</span>
          <input
            v-model="time"
            class="zn-input"
            type="time"
          >
        </label>

        <label class="block">
          <span class="mb-1 block text-[11px] font-semibold text-slate-400">Davomiyligi</span>
          <select
            v-model.number="durationMinutes"
            class="zn-input"
          >
            <option
              v-for="option in DURATIONS"
              :key="option.value"
              :value="option.value"
            >
              {{ option.label }}
            </option>
          </select>
        </label>

        <label class="block">
          <span class="mb-1 block text-[11px] font-semibold text-slate-400">Ism bo‘yicha</span>
          <input
            v-model="search"
            class="zn-input"
            placeholder="Qidirish"
          >
        </label>
      </div>

      <!-- Tez tanlanadigan vaqtlar — kunning odatiy dars slotlari. -->
      <div class="mt-3 flex flex-wrap items-center gap-1.5">
        <span class="mr-1 text-[11px] text-dim">Tez tanlash:</span>
        <button
          v-for="preset in TIME_PRESETS"
          :key="preset"
          type="button"
          class="rounded-lg border px-2.5 py-1 text-xs font-semibold transition-colors"
          :class="
            time === preset
              ? 'border-brand-500 bg-brand-500/14 text-brand-500'
              : 'border-line bg-ink-800 text-slate-400 hover:text-slate-100'
          "
          @click="time = preset"
        >
          {{ preset }}
        </button>
      </div>

      <div class="mt-3 flex flex-wrap gap-4 border-t border-line pt-3">
        <label class="flex cursor-pointer items-center gap-2 text-xs text-slate-300">
          <input
            v-model="onlyFree"
            type="checkbox"
          >
          Faqat bo‘shlarni ko‘rsat
        </label>
        <label class="flex cursor-pointer items-center gap-2 text-xs text-slate-300">
          <input
            v-model="includeAssistants"
            type="checkbox"
          >
          Kuratorlar ham
        </label>
      </div>
    </div>

    <!-- ═════════════════════ YIG'MA ═════════════════════ -->
    <div
      v-if="result !== null"
      class="mb-4 flex flex-wrap items-center gap-3 rounded-xl border border-line bg-ink-900 px-4 py-3"
    >
      <AppIcon
        name="clock"
        :size="16"
        class="text-brand-400"
      />
      <span class="text-sm text-slate-300">
        {{ result.date }} · {{ shortTime(result.time) }} dan
        {{ result.durationMinutes }} daqiqa
      </span>
      <span class="ml-auto flex items-center gap-3 text-sm">
        <span class="font-bold tabular-nums text-emerald-400">
          {{ result.freeCount }} bo‘sh
        </span>
        <span
          v-if="result.busyCount > 0"
          class="font-bold tabular-nums text-slate-500"
        >
          {{ result.busyCount }} band
        </span>
      </span>
    </div>

    <BaseCard flush>
      <DataStatus
        :pending="freeQuery.isPending.value"
        :error="loadError"
        :empty="teachers.length === 0"
        :retrying="freeQuery.isFetching.value"
        :skeleton-rows="4"
        empty-icon="graduation"
        empty-title="Bo‘sh ustoz yo‘q"
        empty-text="Bu vaqtda hamma band. Boshqa vaqt yoki kunni tanlab ko‘ring."
        @retry="freeQuery.refetch()"
      >
        <div class="scroll-x-safe scrollbar-slim">
          <table class="zn-table">
            <thead>
              <tr>
                <th class="w-10">
                  #
                </th>
                <th>Ustoz</th>
                <th>Holati</th>
                <th>Shu kundagi yuklama</th>
              </tr>
            </thead>
            <tbody>
              <tr
                v-for="(row, index) in teachers"
                :key="row.teacherId"
                :class="row.isFree ? '' : 'opacity-60'"
              >
                <td
                  class="tabular-nums text-dim"
                  v-text="index + 1"
                />
                <td class="font-medium text-slate-100">
                  {{ row.teacherName }}
                  <span
                    class="mt-0.5 block text-xs font-normal text-dim"
                    v-text="roleLabel(row.role)"
                  />
                </td>
                <td>
                  <BaseBadge :tone="row.isFree ? 'success' : 'neutral'">
                    {{ row.isFree ? 'Bo‘sh' : 'Band' }}
                  </BaseBadge>

                  <!--
                    ★ NEGA BAND EKANI AYTILADI: "band" so'zining o'zi
                    yetarli emas — operator ba'zan darsni ko'chirish
                    yoki boshqa slot tanlash uchun sababni bilishi kerak.
                  -->
                  <span
                    v-if="row.busyGroupName !== null"
                    class="mt-1 block text-xs text-slate-400"
                    v-text="row.busyGroupName"
                  />
                  <span
                    v-if="row.unavailableReason !== null"
                    class="mt-1 block text-xs text-amber-400"
                    v-text="`Bugun o‘tolmaydi: ${row.unavailableReason}`"
                  />
                </td>
                <td>
                  <span
                    v-if="row.lessonsThatDay === 0"
                    class="text-xs text-dim"
                  >Bu kuni darsi yo‘q</span>
                  <span v-else>
                    <span
                      class="tabular-nums text-slate-300"
                      v-text="`${row.lessonsThatDay} ta dars`"
                    />
                    <span
                      class="mt-0.5 block text-xs tabular-nums text-dim"
                      v-text="dayRange(row.dayFirstLesson, row.dayLastLessonEnd)"
                    />
                  </span>
                </td>
              </tr>
            </tbody>
          </table>
        </div>
      </DataStatus>
    </BaseCard>
  </div>
</template>
