<script setup lang="ts">
import { useQuery, useQueryClient } from '@tanstack/vue-query'
import { computed, nextTick, ref, watch } from 'vue'

import {
  fetchSessionGrades,
  lessonGradeClass,
  lessonGradeText,
} from '@/entities/lesson-grade'
import type { LessonGradeRowDto } from '@/entities/lesson-grade'
import { sessionTypeShortLabel } from '@/entities/session'
import { toUserMessage } from '@/shared/api'
import { formatDateTime, formatTime } from '@/shared/lib/datetime'
import { useBreakpoint } from '@/shared/lib/useBreakpoint'
import { useNow } from '@/shared/lib/use-now'
import type { ScheduledSessionDto } from '@/shared/types'
import { BaseButton, DataStatus } from '@/shared/ui'

import {
  ATTENDANCE_WINDOW,
  collectStudents,
  indexRows,
  selectAttendanceColumns,
} from '../model/attendance-matrix'
import { downloadCsv } from '../model/csv'
import { useGroupSchedule } from '../model/use-group-schedule'
import LessonGradeCellDialog from './LessonGradeCellDialog.vue'

/**
 * ========================================================================
 * R24 · "Baholar" tabining DARS ko'rinishi — loyiha egasining talabi
 * ========================================================================
 *
 * *"baholar qismida guruh studentlari baholari jadval ko'rinishida
 * joylashsin"* va *"baholar har bitta darsga qo'yiladi"*.
 *
 * ★ TUZILISH `AttendanceTab` NING AYNAN NUSXASI — ataylab. Qator =
 * o'quvchi, ustun = DARS, katakni bosib baho qo'yiladi. Eski tizimdagi
 * "Baholar jadvali" ham aynan shu shaklda edi; ustoz uchun bu ikkinchi
 * jadval emas, TANISH jadval.
 *
 * ★ USTUNLAR OYNA BILAN: 8 oylik guruhda 69 dars bor va server varaqni
 * DARS kesimida beradi, ya'ni 69 ta so'rov. Oyna bugundan orqaga
 * {{ ATTENDANCE_WINDOW }} ta dars oladi, "Yana ..." tugmasi kengaytiradi
 * (davomat jadvalidagi bilan AYNI mexanizm va AYNI konstanta).
 *
 * 🔴 VAZIFA BAHOLARI BU YERDA YO'Q va ular BU YERGA KO'CHIRILMAGAN:
 * dars ↔ vazifa xaritasi mavjud emas. Ular yonma-yon, "Vazifalar"
 * ko'rinishida qoladi (`AssignmentGradesView`).
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
 * ★ BITTA `useQuery` ICHIDA `Promise.all` — davomat jadvalidagi bilan
 * AYNI sabab: har dars uchun alohida so'rov ochilsa jadval ustun-ustun
 * paydo bo'lardi va yuklanish holati o'nta bo'lardi.
 */
const sheetsQuery = useQuery({
  queryKey: ['group', props.groupId, 'lesson-grades', columnIds],
  queryFn: ({ signal }) =>
    Promise.all(columnIds.value.map((id) => fetchSessionGrades(id, { signal }))),
  enabled: computed(() => columnIds.value.length > 0),
})

const sheets = computed(() => sheetsQuery.data.value ?? [])
const students = computed(() => collectStudents(sheets.value))
const rowIndex = computed(() => indexRows(sheets.value))

/**
 * Shkala SERVERDAN keladi (`LessonGrade.DefaultMaxScore`). Varaqlar bo'sh
 * bo'lsa 5 — bu faqat oyna ochilmagan holatdagi zaxira qiymat.
 */
const defaultMaxScore = computed(() => sheets.value[0]?.defaultMaxScore ?? 5)

function cellOf(sessionId: number, studentId: number): LessonGradeRowDto | null {
  return rowIndex.value.get(`${sessionId}:${studentId}`) ?? null
}

function cellTitle(session: ScheduledSessionDto, row: LessonGradeRowDto | null): string {
  const when = formatDateTime(session.scheduledStart)
  if (row === null || row.score === null) return `${when} — baho yo‘q`

  const base = `${when} — ${row.score}/${row.maxScore ?? defaultMaxScore.value}`
  return row.comment === null ? base : `${base} · ${row.comment}`
}

/* ═══════════════════════════════════════════════════════════════════════
   "JAMI" USTUNI — O'RTACHA FOIZ, YIG'INDI EMAS

   ★ DAVOMAT JADVALIDAN FARQ: u yerda yig'indi `+7 ±1 −2` shaklida
   PARCHALANIB beriladi, chunki holatlar SANALADI. Baho esa o'lchov —
   uning tabiiy xulosasi O'RTACHA. Ballarni qo'shib chiqarish
   ("jami 23 ball") ustunlar soni oyna bilan o'zgargani uchun ma'nosiz
   bo'lardi: "Yana ..." bosilganda son sakrab ketardi.

   🔴 O'RTACHA HAM FAQAT KO'RINIB TURGAN OYNA BO'YICHA — jadval ostida
   bu ochiq aytiladi (davomatdagi bilan AYNI ogohlantirish).

   ★ O'RTACHA FOIZDA, BALLDA EMAS: bitta darsda 5 ballik, boshqasida
   100 ballik shkala bo'lishi mumkin va ularning ballarini o'rtachalash
   mutlaqo noto'g'ri son berardi.
   ═══════════════════════════════════════════════════════════════════════ */

interface Tally {
  /** Baholangan kataklar soni (maxraj). */
  graded: number
  /** Foizlar o'rtachasi. `null` — baho umuman yo'q. */
  average: number | null
}

function tallyOf(percents: readonly number[]): Tally {
  if (percents.length === 0) return { graded: 0, average: null }
  const sum = percents.reduce((total, value) => total + value, 0)
  return { graded: percents.length, average: Math.round((sum / percents.length) * 10) / 10 }
}

function averageText(tally: Tally): string {
  return tally.average === null ? '—' : `${tally.average}%`
}

interface MatrixRow {
  studentId: number
  name: string
  totals: Tally
  totalsTitle: string
}

/*
  ★ QATORLAR ALOHIDA `computed` DA, shablonda funksiya chaqirig'i EMAS:
  yig'indi har bir qayta chizishda 25 × 10 katakni aylanib chiqadi va
  shablondagi chaqiruv keshlanmaydi (`AttendanceTab` dagi bilan AYNI
  mulohaza).
*/
const matrixRows = computed<MatrixRow[]>(() =>
  students.value.map((student) => {
    const percents: number[] = []
    for (const session of columns.value) {
      const percent = cellOf(session.id, student.studentId)?.percent
      if (percent != null) percents.push(percent)
    }
    const totals = tallyOf(percents)
    return {
      studentId: student.studentId,
      name: student.name,
      totals,
      totalsTitle: `${totals.graded} / ${columns.value.length} dars baholangan`,
    }
  }),
)

interface MatrixColumn {
  session: ScheduledSessionDto
  totals: Tally
  totalsTitle: string
}

/** Pastdagi "Jami" qatori — dars kesimida guruhning o'rtacha bahosi. */
const matrixColumns = computed<MatrixColumn[]>(() =>
  columns.value.map((session) => {
    const percents: number[] = []
    for (const student of students.value) {
      const percent = cellOf(session.id, student.studentId)?.percent
      if (percent != null) percents.push(percent)
    }
    const totals = tallyOf(percents)
    return {
      session,
      totals,
      totalsTitle: `${totals.graded} / ${students.value.length} o‘quvchi baholangan`,
    }
  }),
)

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

/* ═══════════════════════════════════════════════════════════════════════
   TELEFON KO'RINISHI — MATRITSA EMAS, BITTA DARS VARAQASI

   ★ YECHIM `AttendanceTab` DAN KO'CHIRILDI va sabablari AYNAN o'sha:
   25 o'quvchi × 10 dars = 250 katak; kartochka ro'yxati 250 qatorli
   sahifa yasardi va hech qanday savolga javob bermasdi. Shuning uchun
   bir vaqtda BITTA O'Q: avval DARS tanlanadi, keyin uning varaqasi
   ko'rsatiladi.

   ★ BU YERDA U DAVOMATDAGIDAN HAM TABIIYROQ: ustoz baholarni dars
   OXIRIDA, bitta dars bo'yicha ketma-ket qo'yadi — ya'ni telefon
   ko'rinishi asosiy ish oqimining o'zi.
   ═══════════════════════════════════════════════════════════════════════ */

const { isDesktop } = useBreakpoint()

/** `null` — hali qo'lda tanlanmagan, ya'ni eng yaqin dars ko'rsatiladi. */
const pickedSessionId = ref<number | null>(null)

/*
  ★ `computed` ichida zaxira qiymat, `watch` bilan `pickedSessionId` ni
  to'ldirish EMAS: "Yana ..." tugmasi bosilganda ustunlar ro'yxati
  yangilanadi va watch tanlovni ustma-ust qayta yozib yuborardi.
*/
const activeSession = computed<ScheduledSessionDto | null>(() => {
  const list = columns.value
  return list.find((item) => item.id === pickedSessionId.value) ?? list.at(-1) ?? null
})

const activeHeader = computed(() => {
  const session = activeSession.value
  if (session === null) return null
  return {
    when: formatDateTime(session.scheduledStart),
    type: sessionTypeShortLabel(session.type),
    title: session.title ?? '',
  }
})

interface SheetRow {
  studentId: number
  name: string
  score: number | null
  maxScore: number | null
  percent: number | null
  comment: string | null
}

const activeRows = computed<SheetRow[]>(() => {
  const session = activeSession.value
  if (session === null) return []
  return students.value.map((student) => {
    const row = cellOf(session.id, student.studentId)
    return {
      studentId: student.studentId,
      name: student.name,
      score: row?.score ?? null,
      maxScore: row?.maxScore ?? null,
      percent: row?.percent ?? null,
      comment: row?.comment ?? null,
    }
  })
})

/*
  Varaqa ustidagi qisqa xulosa — FAQAT QOLGAN ISHNI ko'rsatadi
  ("nechta o'quvchi hali baholanmagan"). Hammasi baholanganda qator
  umuman chizilmaydi: bo'sh xabar joy egallardi.
*/
const activeMissing = computed(
  () => activeRows.value.filter((row) => row.score === null).length,
)

function openActiveDialog(row: SheetRow): void {
  const session = activeSession.value
  if (session === null) return
  openDialog(session, row.studentId, row.name)
}

/**
 * Jadval o'ngga — eng yaqin darsga siljitiladi. `AttendanceTab` dagi bilan
 * AYNI xatti-harakat va AYNI sabab, `isDesktop` ham kuzatiladi: ikki
 * ko'rinish `v-if` bilan ajratilgani uchun chegara kesib o'tilganda
 * jadval YANGIDAN quriladi va skroll holati nolga tushadi.
 */
const scroller = ref<HTMLElement | null>(null)
const sessionStrip = ref<HTMLElement | null>(null)

watch(
  [() => sheets.value.length, isDesktop],
  ([count]) => {
    if (count === 0) return
    void nextTick(() => {
      for (const element of [scroller.value, sessionStrip.value]) {
        if (element !== null) element.scrollLeft = element.scrollWidth
      }
    })
  },
)

/* --------------------------------------------------------------- baholash */

type OpenCell = InstanceType<typeof LessonGradeCellDialog>['$props']['cell']

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
    defaultMaxScore: sheet?.defaultMaxScore ?? defaultMaxScore.value,
    studentId,
    studentName,
    row: cellOf(session.id, studentId),
  }
}

function handleSaved(): void {
  openCell.value = null
  // Matritsa bir necha varaqdan yig'ilgani uchun butun to'plam qayta
  // olinadi (bitta so'rovlar guruhi).
  void queryClient.invalidateQueries({ queryKey: ['group', props.groupId, 'lesson-grades'] })
  // 🔴 REYTING HAM ESKIRADI: dars bahosi oylik reytingning TO'RTINCHI
  // mezoni (R24). Bu qatorsiz ustoz baho qo'yib, "Reyting" tabida eski
  // ballni ko'rib turardi.
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
    'O‘rtacha',
  ]
  const body = matrixRows.value.map((row) => [
    row.name,
    // CSV'da BALL yoziladi (foiz emas): u ustozning qo'ygan qiymati va
    // hisobotda aynan shu kutiladi. O'rtacha esa foizda — sabab
    // "Jami" ustuni izohida (turli shkalalarni qo'shib bo'lmaydi).
    ...columns.value.map((session) => lessonGradeText(cellOf(session.id, row.studentId)?.score ?? null)),
    averageText(row.totals),
  ])
  downloadCsv(`${props.groupName}_dars-baholari.csv`, [header, ...body])
}

const hasData = computed(() => students.value.length > 0 && columns.value.length > 0)

defineExpose({ exportCsv, hasData })
</script>

<template>
  <DataStatus
    :pending="pending"
    :error="errorMessage"
    :empty="columns.length === 0 || students.length === 0"
    :retrying="sheetsQuery.isFetching.value"
    :skeleton-rows="4"
    empty-icon="award"
    :empty-title="sessions.length === 0 ? 'Hali darslar yo‘q.' : 'O‘quvchilar yo‘q.'"
    empty-text="Guruhda faol o‘quvchi va kamida bitta dars bo‘lgach jadval to‘ladi."
    @retry="refetch"
  >
    <!--
      TELEFON: dars tanlagich + shu darsning varaqasi. Nega matritsa
      emasligi skriptdagi katta izohda yozilgan.
    -->
    <div v-if="!isDesktop">
      <div
        ref="sessionStrip"
        class="scroll-x-safe scrollbar-none -mx-3.5 flex gap-2 px-3.5 sm:-mx-5 sm:px-5"
      >
        <button
          v-for="session in columns"
          :key="session.id"
          type="button"
          class="min-h-11 shrink-0 rounded-xl border px-3 py-1.5 text-center leading-tight transition-colors"
          :class="
            activeSession?.id === session.id
              ? 'border-brand-500 bg-brand-500/14 text-brand-500'
              : 'border-line bg-ink-900 text-slate-400'
          "
          :title="session.title ?? ''"
          @click="pickedSessionId = session.id"
        >
          <span class="block text-[9px] uppercase text-dim">
            {{ sessionTypeShortLabel(session.type) }}
          </span>
          <span class="block text-[13px] font-semibold tabular-nums">
            {{ new Date(session.scheduledStart).getDate() }}/{{
              new Date(session.scheduledStart).getMonth() + 1
            }}
          </span>
        </button>
      </div>

      <div class="mt-3 flex flex-wrap items-baseline gap-x-2 gap-y-0.5">
        <p class="text-[13px] font-semibold text-slate-100">
          {{ activeHeader?.when }}
        </p>
        <p class="text-[11px] uppercase text-dim">
          {{ activeHeader?.type }}
        </p>
        <p
          v-if="(activeHeader?.title ?? '').length > 0"
          class="w-full truncate text-xs text-slate-400"
        >
          {{ activeHeader?.title }}
        </p>
      </div>

      <p
        v-if="activeMissing > 0"
        class="mt-1 text-[11px] text-slate-400"
      >
        {{ activeMissing }} ta o‘quvchi baholanmagan
      </p>

      <ul class="mt-2.5 divide-y divide-line rounded-xl border border-line">
        <li
          v-for="row in activeRows"
          :key="row.studentId"
        >
          <!--
            Butun qator bosiladi (44px dan baland) — jadvaldagi 44×44
            katakning telefondagi ekvivalenti. Ochiladigan oyna AYNAN
            o'sha, ya'ni baholash oqimi ikki ko'rinishda bir xil.
          -->
          <button
            type="button"
            class="flex w-full items-center gap-3 px-3 py-2.5 text-left transition-colors hover:bg-ink-800"
            @click="openActiveDialog(row)"
          >
            <span
              class="flex size-9 shrink-0 items-center justify-center rounded-lg text-[15px] font-bold tabular-nums"
              :class="lessonGradeClass(row.percent)"
            >
              {{ lessonGradeText(row.score) }}
            </span>
            <span class="min-w-0 flex-1">
              <span
                class="block truncate text-sm font-medium text-slate-100"
                v-text="row.name"
              />
              <span
                v-if="row.comment !== null"
                class="block truncate text-[11px] text-dim"
                v-text="row.comment"
              />
            </span>
            <!--
              Jadvalda maxraj YO'Q (ustun tor), ro'yxatda esa joy bor —
              "4/5" yozilsa ma'lumot faqat rangga bog'liq bo'lib qolmaydi.
            -->
            <span
              v-if="row.score !== null"
              class="shrink-0 text-xs tabular-nums text-slate-400"
            >{{ row.score }} / {{ row.maxScore ?? defaultMaxScore }}</span>
            <span
              v-else
              class="shrink-0 text-xs text-dim"
            >Baholanmagan</span>
          </button>
        </li>
      </ul>
    </div>

    <!--
      ═══════════════════════════════════════════════════════════════
      DESKTOP MATRITSASI

      🔴 `max-h-[70dvh]` + `overflow-y-auto` — BEZAK EMAS, YOPISHQOQ
      SARLAVHANING SHARTI (`AttendanceTab` da batafsil): `position:
      sticky` eng yaqin SKROLL konteyneriga nisbatan ishlaydi va
      balandlik cheklanmasa vertikal skroll SAHIFANIKI bo'lib qolardi.

      ★ z-qatlamlari: sarlavha burchagi (6) > sarlavha (4) > "Jami"
      qatori burchagi (5) > "Jami" qatori (3) > ism ustuni (2).
      ═══════════════════════════════════════════════════════════════
    -->
    <div
      v-else
      ref="scroller"
      class="scroll-x-safe scrollbar-slim max-h-[70dvh] overflow-y-auto rounded-xl border border-line"
    >
      <table class="w-max border-separate border-spacing-0 text-[13px]">
        <thead>
          <tr>
            <th
              class="sticky left-0 top-0 z-[6] min-w-[185px] border-b border-r border-line bg-ink-800 px-3.5 py-2.5 text-left text-[11px] font-semibold text-slate-400"
            >
              O‘quvchi
            </th>
            <th
              v-for="session in columns"
              :key="session.id"
              class="sticky top-0 z-[4] border-b border-r border-line bg-ink-800 px-2.5 py-2 text-center text-[11px] font-semibold text-slate-400"
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
            <th
              class="sticky top-0 z-[4] border-b border-line bg-ink-800 px-3 py-2 text-center text-[11px] font-semibold text-slate-400"
            >
              O‘rtacha
              <span class="block text-[9px] font-normal text-dim">
                {{ columns.length }} ta dars
              </span>
            </th>
          </tr>
        </thead>
        <tbody>
          <!--
            Zebra: qator foni `<tr>` da BITTA sinf bilan beriladi
            (`[&>td]:bg-*`) — sabab `AttendanceTab` da batafsil.
            Yopishqoq ism ustuni uchun fon MAJBURIY: shaffof bo'lsa
            gorizontal skrollda kataklar uning ostidan ko'rinib o'tadi.
          -->
          <tr
            v-for="(row, index) in matrixRows"
            :key="row.studentId"
            :class="index % 2 === 1 ? '[&>td]:bg-ink-850' : '[&>td]:bg-ink-900'"
          >
            <td
              class="sticky left-0 z-[2] whitespace-nowrap border-b border-r border-line px-3.5 py-2.5 font-semibold text-slate-100"
              v-text="row.name"
            />
            <td
              v-for="session in columns"
              :key="session.id"
              class="border-b border-r border-line p-0"
            >
              <button
                type="button"
                class="flex h-11 w-11 items-center justify-center text-[15px] font-bold tabular-nums transition-colors hover:bg-ink-750 hover:shadow-[inset_0_0_0_2px_var(--color-brand-500)]"
                :class="lessonGradeClass(cellOf(session.id, row.studentId)?.percent ?? null)"
                :title="cellTitle(session, cellOf(session.id, row.studentId))"
                @click="openDialog(session, row.studentId, row.name)"
              >
                {{ lessonGradeText(cellOf(session.id, row.studentId)?.score ?? null) }}
              </button>
            </td>
            <!--
              O'rtacha FOIZDA (ball emas): turli darslarda turli shkala
              bo'lishi mumkin. Maxraj yonida — "nechta darsdan" degan
              savol katakdan uzoqlashmasin.
            -->
            <td
              class="whitespace-nowrap border-b border-line px-3 text-center text-[11px] tabular-nums"
              :title="row.totalsTitle"
            >
              <span class="font-bold text-brand-500">{{ averageText(row.totals) }}</span>
              <span class="ml-1.5 text-dim">{{ row.totals.graded }}/{{ columns.length }}</span>
            </td>
          </tr>
        </tbody>
        <!-- "Jami" QATORI — dars kesimida guruhning o'rtacha bahosi. -->
        <tfoot>
          <tr>
            <th
              class="sticky bottom-0 left-0 z-[5] whitespace-nowrap border-r border-t border-line bg-ink-800 px-3.5 py-2 text-left text-[11px] font-semibold text-slate-400"
            >
              O‘rtacha
            </th>
            <td
              v-for="column in matrixColumns"
              :key="column.session.id"
              class="sticky bottom-0 z-[3] border-r border-t border-line bg-ink-800 px-2.5 py-2 text-center text-[11px] font-bold tabular-nums text-brand-500"
              :title="column.totalsTitle"
            >
              {{ averageText(column.totals) }}
            </td>
            <td class="sticky bottom-0 z-[3] border-t border-line bg-ink-800" />
          </tr>
        </tfoot>
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

    <!--
      🔴 "·" NOL DEMAK EMAS. Bu farq reytingga chiqadi: 0 — "bajarmadi"
      degan HAQIQIY baho va o'rtachaga to'liq kiradi, "·" esa hech kim
      baholamagan va hisobda UMUMAN qatnashmaydi.
    -->
    <p class="mt-3 text-[11px] text-dim">
      “·” — baho yo‘q (hech kim qo‘ymagan); u o‘rtachaga kirmaydi. “0” esa
      qo‘yilgan baho va o‘rtachaga to‘liq kiradi.
    </p>

    <!--
      🔴 Qamrov ochiq aytiladi (davomat jadvalidagi bilan AYNI ogohlantirish):
      o'rtacha butun kurs bo'yicha EMAS, faqat ekrandagi oyna bo'yicha.
    -->
    <p
      v-if="isDesktop"
      class="mt-1 text-[11px] text-dim"
    >
      “O‘rtacha” ustuni va qatori faqat shu yerda ko‘rinib turgan
      {{ columns.length }} ta dars bo‘yicha hisoblanadi.
    </p>
  </DataStatus>

  <LessonGradeCellDialog
    :cell="openCell"
    @close="openCell = null"
    @saved="handleSaved"
  />
</template>
