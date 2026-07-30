<script setup lang="ts">
import { useQuery, useQueryClient } from '@tanstack/vue-query'
import { computed, nextTick, ref, watch } from 'vue'

import {
  attendanceStatusLabel,
  attendanceSymbol,
  fetchSessionAttendance,
} from '@/entities/attendance'
import type { AttendanceRowDto, AttendanceStatusName } from '@/entities/attendance'
import { sessionTypeShortLabel } from '@/entities/session'
import { toUserMessage } from '@/shared/api'
import { formatDateTime, formatTime } from '@/shared/lib/datetime'
import { useNow } from '@/shared/lib/use-now'
import type { ScheduledSessionDto } from '@/shared/types'
import { AppIcon, BaseButton, BaseCard, DataStatus } from '@/shared/ui'

import {
  ATTENDANCE_WINDOW,
  collectStudents,
  indexRows,
  selectAttendanceColumns,
} from '../model/attendance-matrix'
import { downloadCsv } from '../model/csv'
import { useGroupSchedule } from '../model/use-group-schedule'
import AttendanceCellDialog from './AttendanceCellDialog.vue'

/**
 * "Davomat" tabi — eski `#tab-att` (davomat matritsasi).
 *
 * Qator = o'quvchi, ustun = dars, katak = `+ / ± / − / ·`; katakni bosib
 * holatni QO'LDA tuzatish mumkin (eski `openAttCell()` -> `saveAtt()`).
 * Qo'lda tuzatilgan katakda o'ng yuqori burchakda oltin nuqta —
 * eski `.manual-dot`.
 *
 * ★ YAGONA TUZILISH FARQI: eski server butun matritsani BITTA javobda
 * berardi, v2 endpointi esa DARS kesimida ishlaydi. 8 oylik guruhda 69
 * dars bor, ya'ni 69 ta so'rov — shuning uchun ustunlar OYNA bilan
 * yuklanadi (bugundan orqaga {{ ATTENDANCE_WINDOW }} ta), "Yana ..."
 * tugmasi oynani kengaytiradi.
 */
const props = defineProps<{
  groupId: number
  groupName: string
}>()

const now = useNow()
const queryClient = useQueryClient()

const scheduleQuery = useGroupSchedule(props.groupId)
const sessions = computed<ScheduledSessionDto[]>(() => scheduleQuery.data.value ?? [])

const limit = ref(ATTENDANCE_WINDOW)

const columns = computed(() => selectAttendanceColumns(sessions.value, limit.value, now.value))
const columnIds = computed(() => columns.value.map((item) => item.id))
const hasMore = computed(() => columns.value.length < sessions.value.length)

/**
 * ★ BITTA `useQuery` ICHIDA `Promise.all`: har dars uchun alohida so'rov
 * ochilsa jadval ustun-ustun paydo bo'lardi va yuklanish holati o'nta
 * bo'lardi. Ustunlar soni oyna bilan cheklangan.
 */
const sheetsQuery = useQuery({
  queryKey: ['group', props.groupId, 'attendance', columnIds],
  queryFn: ({ signal }) =>
    Promise.all(columnIds.value.map((id) => fetchSessionAttendance(id, { signal }))),
  enabled: computed(() => columnIds.value.length > 0),
})

const sheets = computed(() => sheetsQuery.data.value ?? [])
const students = computed(() => collectStudents(sheets.value))
const rowIndex = computed(() => indexRows(sheets.value))

function cellOf(sessionId: number, studentId: number): AttendanceRowDto | null {
  return rowIndex.value.get(`${sessionId}:${studentId}`) ?? null
}

const SYMBOL_CLASS: Record<AttendanceStatusName | 'none', string> = {
  Present: 'text-green-400 bg-green-500/10',
  Late: 'text-amber-400 bg-amber-500/10',
  Partial: 'text-amber-400 bg-amber-500/10',
  Absent: 'text-rose-400 bg-rose-500/10',
  none: 'text-dim',
}

function cellClass(row: AttendanceRowDto | null): string {
  return SYMBOL_CLASS[row?.status ?? 'none']
}

function cellTitle(session: ScheduledSessionDto, row: AttendanceRowDto | null): string {
  const status = attendanceStatusLabel(row?.status ?? null)
  const reason = row?.reason ?? null
  const base = `${formatDateTime(session.scheduledStart)} — ${status}`
  return reason === null ? base : `${base} · ${reason}`
}

const pending = computed(
  () => scheduleQuery.isPending.value || (columnIds.value.length > 0 && sheetsQuery.isPending.value),
)

const errorMessage = computed(() => {
  const error = scheduleQuery.error.value ?? sheetsQuery.error.value
  return error !== null ? toUserMessage(error) : null
})

function refetch(): void {
  void scheduleQuery.refetch()
  void sheetsQuery.refetch()
}

/**
 * Jadval o'ngga — eng yaqin darsga siljitiladi (eski ilova bugungi ustunga
 * avtomatik skroll qilardi). Oynaning oxirgi ustuni aynan shu dars.
 */
const scroller = ref<HTMLElement | null>(null)

watch(
  () => sheets.value.length,
  (count) => {
    if (count === 0) return
    void nextTick(() => {
      const element = scroller.value
      if (element !== null) element.scrollLeft = element.scrollWidth
    })
  },
)

/* --------------------------------------------------------------- tuzatish */

type OpenCell = InstanceType<typeof AttendanceCellDialog>['$props']['cell']

const openCell = ref<OpenCell>(null)

function openDialog(session: ScheduledSessionDto, studentId: number, studentName: string): void {
  const sheet = sheets.value.find((item) => item.sessionId === session.id)
  openCell.value = {
    sessionId: session.id,
    sessionTitle: session.title ?? '',
    sessionType: session.type,
    sessionStatus: session.status,
    sessionStart: session.scheduledStart,
    canEdit: sheet?.canEdit ?? false,
    studentId,
    studentName,
    row: cellOf(session.id, studentId),
  }
}

function handleSaved(): void {
  openCell.value = null
  // Varaq yangilansin: `isManual`, sabab va tuzatgan xodim javobda keladi,
  // lekin matritsa bir necha darsdan yig'ilgani uchun butun to'plam qayta
  // olinadi (bitta so'rovlar guruhi).
  void queryClient.invalidateQueries({ queryKey: ['group', props.groupId, 'attendance'] })
  // Oylik davomat foizi ham o'zgaradi — "Reyting" tabidagi ustun eskirmasin.
  void queryClient.invalidateQueries({ queryKey: ['leaderboard', 'group', props.groupId] })
}

/* ------------------------------------------------------------------- CSV */

function exportCsv(): void {
  const header = [
    'O‘quvchi',
    ...columns.value.map(
      (session) =>
        `${formatDateTime(session.scheduledStart)} (${sessionTypeShortLabel(session.type)})`,
    ),
  ]
  const body = students.value.map((student) => [
    student.name,
    ...columns.value.map((session) =>
      attendanceStatusLabel(cellOf(session.id, student.studentId)?.status ?? null),
    ),
  ])
  downloadCsv(`${props.groupName}_davomat.csv`, [header, ...body])
}
</script>

<template>
  <BaseCard
    flush
    title="Davomat jadvali"
  >
    <template #actions>
      <BaseButton
        size="sm"
        variant="secondary"
        :disabled="students.length === 0 || columns.length === 0"
        @click="exportCsv"
      >
        <template #icon>
          <AppIcon
            name="download"
            :size="13"
          />
        </template>
        CSV yuklab olish
      </BaseButton>
    </template>

    <div class="p-3.5 sm:p-5">
      <p class="mb-3 text-xs text-slate-400">
        Davomat platforma tomonidan avtomatik belgilanadi. Katakka bosib
        tuzatishingiz mumkin. <span class="text-brand-500">●</span> — qo‘lda
        o‘zgartirilgan.
      </p>

      <DataStatus
        :pending="pending"
        :error="errorMessage"
        :empty="columns.length === 0 || students.length === 0"
        :retrying="sheetsQuery.isFetching.value"
        :skeleton-rows="4"
        empty-icon="check-square"
        :empty-title="sessions.length === 0 ? 'Hali darslar yo‘q.' : 'O‘quvchilar yo‘q.'"
        @retry="refetch"
      >
        <div
          ref="scroller"
          class="scroll-x-safe scrollbar-slim rounded-xl border border-line"
        >
          <!--
            `w-max`: ustunlar KENGLIGI o'z mazmuniga qarab olinadi. `min-w-full`
            bo'lganda bitta darsli guruhda ism ustuni butun ekranga cho'zilib,
            yagona katak o'ng chekkaga tushib qolardi (brauzerda ko'rildi).
          -->
          <table class="w-max border-separate border-spacing-0 text-[13px]">
            <thead>
              <tr>
                <th
                  class="sticky left-0 z-[3] min-w-[185px] border-b border-r border-line bg-ink-800 px-3.5 py-2.5 text-left text-[11px] font-semibold text-slate-400"
                >
                  O‘quvchi
                </th>
                <th
                  v-for="session in columns"
                  :key="session.id"
                  class="border-b border-r border-line bg-ink-800 px-2.5 py-2 text-center text-[11px] font-semibold text-slate-400"
                  :title="session.title ?? ''"
                >
                  <span class="block text-[9px] uppercase text-dim">
                    {{ sessionTypeShortLabel(session.type) }}
                  </span>
                  {{ new Date(session.scheduledStart).getDate() }}/{{
                    new Date(session.scheduledStart).getMonth() + 1
                  }}
                  <span class="block text-[9px] font-normal text-dim">
                    {{ formatTime(session.scheduledStart) }}
                  </span>
                </th>
              </tr>
            </thead>
            <tbody>
              <tr
                v-for="student in students"
                :key="student.studentId"
              >
                <td
                  class="sticky left-0 z-[2] whitespace-nowrap border-b border-r border-line bg-ink-900 px-3.5 py-2.5 font-semibold text-slate-100"
                  v-text="student.name"
                />
                <td
                  v-for="session in columns"
                  :key="session.id"
                  class="border-b border-r border-line p-0"
                >
                  <button
                    type="button"
                    class="relative flex h-11 w-11 items-center justify-center text-[15px] font-bold transition-colors hover:bg-ink-750 hover:shadow-[inset_0_0_0_2px_var(--color-brand-500)]"
                    :class="cellClass(cellOf(session.id, student.studentId))"
                    :title="cellTitle(session, cellOf(session.id, student.studentId))"
                    @click="openDialog(session, student.studentId, student.name)"
                  >
                    {{ attendanceSymbol(cellOf(session.id, student.studentId)?.status ?? null) }}
                    <!-- Eski `.manual-dot` — holatni odam qo'ygani belgisi. -->
                    <span
                      v-if="cellOf(session.id, student.studentId)?.isManual === true"
                      class="absolute right-1 top-1 size-[5px] rounded-full bg-brand-500"
                      aria-hidden="true"
                    />
                  </button>
                </td>
              </tr>
            </tbody>
          </table>
        </div>

        <BaseButton
          v-if="hasMore"
          class="mt-3"
          size="sm"
          variant="secondary"
          block
          :loading="sheetsQuery.isFetching.value"
          @click="limit += ATTENDANCE_WINDOW"
        >
          Oldingi {{ ATTENDANCE_WINDOW }} ta darsni ham ko‘rsatish
        </BaseButton>

        <!-- Eski `.legend`. -->
        <div
          class="mt-3.5 flex flex-wrap gap-4 rounded-lg border border-line bg-ink-950 px-4 py-3 text-xs text-slate-400"
        >
          <span class="inline-flex items-center gap-1.5">
            <b class="text-[15px] text-green-400">+</b>Qatnashgan
          </span>
          <span class="inline-flex items-center gap-1.5">
            <b class="text-[15px] text-amber-400">±</b>Qisman / kech
          </span>
          <span class="inline-flex items-center gap-1.5">
            <b class="text-[15px] text-rose-400">−</b>Qatnashmagan
          </span>
          <span class="inline-flex items-center gap-1.5">
            <b class="text-[15px] text-dim">·</b>Belgilanmagan
          </span>
        </div>

        <!--
          `·` "kelmagan" DEMAK EMAS: yozuv umuman yo'q. Hisobotda u kelmagan
          deb sanaladi, lekin jadvalda ataylab ajratiladi — aks holda
          "qatnashmagan" deb belgilangan (ustoz qarori) va "belgilanmagan"
          (hech kim qaramagan) bir xil ko'rinardi.
        -->
        <p class="mt-2 text-[11px] text-dim">
          “·” — yozuv yo‘q (hech kim belgilamagan). Hisobotda u kelmagan deb
          sanaladi.
        </p>
      </DataStatus>
    </div>
  </BaseCard>

  <AttendanceCellDialog
    :cell="openCell"
    @close="openCell = null"
    @saved="handleSaved"
  />
</template>
