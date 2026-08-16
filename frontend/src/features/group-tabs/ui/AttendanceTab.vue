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
import { formatMoney } from '@/shared/lib/money'
import { useBreakpoint } from '@/shared/lib/useBreakpoint'
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

/** ★ 2026-08-16: qaysi darslar "bepul" deb belgilangani — ustun sarlavhasida ko'rsatish uchun. */
const freeSessionIds = computed(() => new Set(sheets.value.filter((s) => s.isFreeLesson).map((s) => s.sessionId)))

const SYMBOL_CLASS: Record<AttendanceStatusName | 'none', string> = {
  Present: 'text-green-400 bg-green-500/10',
  Late: 'text-amber-400 bg-amber-500/10',
  Partial: 'text-amber-400 bg-amber-500/10',
  Absent: 'text-rose-400 bg-rose-500/10',
  none: 'text-dim',
}

function symbolClass(status: AttendanceStatusName | null): string {
  return SYMBOL_CLASS[status ?? 'none']
}

function cellClass(row: AttendanceRowDto | null): string {
  return symbolClass(row?.status ?? null)
}

function cellTitle(session: ScheduledSessionDto, row: AttendanceRowDto | null): string {
  const status = attendanceStatusLabel(row?.status ?? null)
  const reason = row?.reason ?? null
  const base = `${formatDateTime(session.scheduledStart)} — ${status}`
  const withReason = reason === null ? base : `${base} · ${reason}`
  // ★ 2026-08-16: "qaysi dars uchun qancha yechilgan" — tooltipga qo'shildi,
  // 44×44 katakka to'rtinchi vizual belgi sig'dirish o'rniga (allaqachon
  // qo'lda/sababli nuqtalari bor).
  if (row?.lessonAmount == null) return withReason
  return `${withReason} · ${formatMoney(row.lessonChargedAmount ?? 0)} yechildi`
}

/* ═══════════════════════════════════════════════════════════════════════
   "JAMI" — R23 ning ikkinchi yarmi (birinchisi: yopishqoq sarlavha).

   🔴 ENG MUHIM CHEKLOV: jadval BUTUN kursni ko'rsatmaydi. Ustunlar oyna
   bilan olinadi (`ATTENDANCE_WINDOW = 10`), "Yana ..." tugmasi oynani
   kengaytiradi. Shuning uchun bu yerda "70%" degan YAKKA foiz ATAYLAB
   chizilmadi: u amalda "oxirgi 10 darsning 70%i" bo'lardi va ustoz uni
   kursning umumiy davomati deb o'qigan bo'lardi — noto'g'ri son
   sondan ko'ra YOMONROQ.

   Buning o'rniga ikkita himoya:
    1) ustun sarlavhasida oynaning O'ZI yozilgan ("N ta dars") va jadval
       ostida bir qatorlik eslatma bor;
    2) son PARCHALANIB ko'rsatiladi (`+7 ±1 −2`) — legendaning aynan o'sha
       alifbosida. Ya'ni yig'indi hech qanday yangi tushuncha kiritmaydi
       va `Late`/`Partial` ni "qatnashgan" ga qo'shib yubormaydi (bu
       taqiq telefon ko'rinishidagi xulosa izohida ham bor).
   ═══════════════════════════════════════════════════════════════════════ */

interface StatusTally {
  present: number
  /** `Late` + `Partial` — legendada ular BITTA belgi (`±`) ostida turadi. */
  partial: number
  absent: number
  blank: number
}

function emptyTally(): StatusTally {
  return { present: 0, partial: 0, absent: 0, blank: 0 }
}

function addStatus(tally: StatusTally, status: AttendanceStatusName | null): void {
  if (status === 'Present') tally.present += 1
  else if (status === 'Late' || status === 'Partial') tally.partial += 1
  else if (status === 'Absent') tally.absent += 1
  else tally.blank += 1
}

/*
  Belgilar jadvalda RANG bilan beriladi (ustun tor), shuning uchun to'liq
  matn `title` da: rangni ajratmaydigan foydalanuvchi ham sonni o'qiy oladi.
*/
function tallyTitle(tally: StatusTally): string {
  return [
    `${tally.present} qatnashgan`,
    `${tally.partial} qisman / kech`,
    `${tally.absent} qatnashmagan`,
    `${tally.blank} belgilanmagan`,
  ].join(' · ')
}

interface MatrixRow {
  studentId: number
  name: string
  totals: StatusTally
  totalsTitle: string
}

/*
  ★ QATORLAR ALOHIDA `computed` DA, shablonda `studentTotals(id)` chaqirig'i
  EMAS: yig'indi har bir qayta chizishda 25 × 10 katakni aylanib chiqadi va
  shablondagi chaqiruv keshlanmaydi. Bu yerda esa `columns`/`rowIndex`
  o'zgarmaguncha bir marta hisoblanadi.
*/
const matrixRows = computed<MatrixRow[]>(() =>
  students.value.map((student) => {
    const totals = emptyTally()
    for (const session of columns.value) {
      addStatus(totals, cellOf(session.id, student.studentId)?.status ?? null)
    }
    return {
      studentId: student.studentId,
      name: student.name,
      totals,
      totalsTitle: tallyTitle(totals),
    }
  }),
)

interface MatrixColumn {
  session: ScheduledSessionDto
  totals: StatusTally
  totalsTitle: string
}

/** Pastdagi "Jami" qatori — dars kesimida nechta o'quvchi qatnashgani. */
const matrixColumns = computed<MatrixColumn[]>(() =>
  columns.value.map((session) => {
    const totals = emptyTally()
    for (const student of students.value) {
      addStatus(totals, cellOf(session.id, student.studentId)?.status ?? null)
    }
    return { session, totals, totalsTitle: tallyTitle(totals) }
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

   ★ NEGA KARTOCHKA RO'YXATI EMAS (ilovadagi qolgan jadvallardagidek):
   bu jadval RO'YXAT emas, MATRITSA. 25 o'quvchi × 10 dars = 250 katak;
   har o'quvchini kartochkaga aylantirsak, kartochka ichida yana 10 qator
   bo'lardi — 250 qatorli sahifa hech qanday savolga javob bermaydi.
   Ustunlar soni ham cheklanmagan: "Yana ..." tugmasi bilan 69 tagacha
   o'sadi, ya'ni kartochka ham cheksiz cho'ziladi.

   ★ NIMA TANLANDI: bir vaqtda BITTA O'Q. Telefondagi ustoz "12-avgust
   darsida kim yo'q edi?" deb qaraydi — ya'ni avval DARSNI, keyin
   o'quvchilar ro'yxatini. Bu aynan davomat varaqasining o'zi va
   `AttendanceCellDialog` ham dars kesimida ishlaydi, demak tuzatish oqimi
   (katakni bosish -> oyna) hech narsa yo'qotmasdan saqlanadi.
   Teskarisi ("avval o'quvchi, keyin uning darslari") tanlanmadi: dars
   bo'yicha belgilash — kundalik ish, o'quvchi tarixi esa kamdan-kam
   so'raladigan savol va u "O'quvchilar" tabidagi profilda bor.

   ★ NIMA YO'QOLADI: bir qarashda BUTUN oyni ko'rish (kim doim qoladi).
   Bu ataylab qurbon qilindi — 375px ekranda 1400px matritsani ichma-ich
   skroll qilish bilan ham u savolga javob berib bo'lmaydi. CSV yuklab
   olish tugmasi ikkala ko'rinishda ham turadi.
   ═══════════════════════════════════════════════════════════════════════ */

const { isDesktop } = useBreakpoint()

/** `null` — hali qo'lda tanlanmagan, ya'ni eng yaqin dars ko'rsatiladi. */
const pickedSessionId = ref<number | null>(null)

/*
  Tanlangan dars. Standart qiymat — OXIRGI ustun: `selectAttendanceColumns`
  oynani bugundan orqaga oladi, demak oxirgi ustun joriy yoki eng yaqin
  dars. Desktopdagi "jadvalni o'ngga siljitish" bilan bir xil niyat.

  ★ `computed` ichida zaxira qiymat, `watch` bilan `pickedSessionId` ni
  to'ldirish EMAS: "Yana ..." tugmasi bosilganda ustunlar ro'yxati
  yangilanadi va watch tanlovni ustma-ust qayta yozib yuborardi.
*/
const activeSession = computed<ScheduledSessionDto | null>(() => {
  const list = columns.value
  return list.find((item) => item.id === pickedSessionId.value) ?? list.at(-1) ?? null
})

/*
  Varaqa sarlavhasi BITTA obyektda tayyorlanadi: shablonda `activeSession`
  ni `v-if` bilan toraytirib maydonlarini o'qish `strictTemplates` da
  ishonchsiz, `?.` esa har doim xavfsiz.
*/
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
  status: AttendanceStatusName | null
  reason: string | null
  isManual: boolean
  isExcused: boolean
  lessonAmount: number | null
  lessonChargedAmount: number | null
}

/*
  Tanlangan darsning varaqasi. Shablonda `cellOf(...)` ni to'rt marta
  chaqirish o'rniga tayyor qator: `activeSession` ning `null` tekshiruvi ham
  shu yerda bir marta bajariladi (shablonda `v-if` orqali turni toraytirish
  `strictTemplates` da mo'rt).
*/
const activeRows = computed<SheetRow[]>(() => {
  const session = activeSession.value
  if (session === null) return []
  return students.value.map((student) => {
    const row = cellOf(session.id, student.studentId)
    return {
      studentId: student.studentId,
      name: student.name,
      status: row?.status ?? null,
      reason: row?.reason ?? null,
      isManual: row?.isManual ?? false,
      isExcused: row?.isExcused ?? false,
      lessonAmount: row?.lessonAmount ?? null,
      lessonChargedAmount: row?.lessonChargedAmount ?? null,
    }
  })
})

/** Tanlangan darsning butun-dars "bepul" holati — session sathidagi ma'lumot. */
const activeSheet = computed(() => {
  const session = activeSession.value
  if (session === null) return null
  return sheets.value.find((item) => item.sessionId === session.id) ?? null
})

/*
  Varaqa ustidagi qisqa xulosa. FAQAT IKKI ANIQ son ko'rsatiladi:
  "belgilanmagan" (`status === null`) va "qatnashmagan" (`Absent`).
  `Late`/`Partial` ni "qatnashgan" ga qo'shib yuborish MA'NONI buzardi —
  jadval ostidagi izohda ular ataylab alohida turadi.
*/
const activeCounts = computed(() => {
  let blank = 0
  let absent = 0
  for (const row of activeRows.value) {
    if (row.status === null) blank += 1
    else if (row.status === 'Absent') absent += 1
  }
  return { blank, absent }
})

function openActiveDialog(row: SheetRow): void {
  const session = activeSession.value
  if (session === null) return
  openDialog(session, row.studentId, row.name)
}

/**
 * Jadval o'ngga — eng yaqin darsga siljitiladi (eski ilova bugungi ustunga
 * avtomatik skroll qilardi). Oynaning oxirgi ustuni aynan shu dars.
 * Telefondagi dars tanlagichi ham xuddi shunday siljiydi.
 */
const scroller = ref<HTMLElement | null>(null)
const sessionStrip = ref<HTMLElement | null>(null)

/*
  ★ `isDesktop` HAM KUZATILADI. Ikki ko'rinish `v-if` bilan ajratilgani
  uchun chegara kesib o'tilganda (planshetni yotiq holatga burish) jadval
  YANGIDAN quriladi va skroll holati nolga tushadi — `sheets.length`
  o'zgarmagani sababli eski watch qayta ishlamasdi va ustoz eng ESKI
  darsga qarab qolardi.
*/
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

/* --------------------------------------------------------------- tuzatish */

type OpenCell = InstanceType<typeof AttendanceCellDialog>['$props']['cell']

/**
 * ★ FAQAT TANLOVNI "MIXLAYDI" (session + student), TO'LIQ obyektni EMAS:
 * `openCell` pastda `computed` — har safar `sheets`/`rowIndex` yangilansa
 * (masalan "bepul dars" saqlangach qayta so'ralganda) dialog AVTOMATIK eng
 * so'nggi `row`/`isFreeLesson` ni ko'rsatadi. Agar bu yerda TO'LIQ obyekt
 * saqlansa (avvalgi kod shunday edi), "bepul dars" saqlangach ham summasi
 * ESKI (masalan hali 75 000) ko'rinib qolardi — real tekshiruvda topilgan
 * xato (2026-08-16).
 */
const pinned = ref<{ session: ScheduledSessionDto; studentId: number; studentName: string } | null>(
  null,
)

const openCell = computed<OpenCell>(() => {
  const selection = pinned.value
  if (selection === null) return null
  const { session, studentId, studentName } = selection
  const sheet = sheets.value.find((item) => item.sessionId === session.id)
  return {
    sessionId: session.id,
    sessionTitle: session.title ?? '',
    sessionType: session.type,
    sessionStatus: session.status,
    sessionStart: session.scheduledStart,
    canEdit: sheet?.canEdit ?? false,
    studentId,
    studentName,
    row: cellOf(session.id, studentId),
    isFreeLesson: sheet?.isFreeLesson ?? false,
    freeLessonReason: sheet?.freeLessonReason ?? null,
    payrollExcluded: sheet?.payrollExcluded ?? false,
  }
})

function openDialog(session: ScheduledSessionDto, studentId: number, studentName: string): void {
  pinned.value = { session, studentId, studentName }
}

function closeDialog(): void {
  pinned.value = null
}

function handleSaved(): void {
  closeDialog()
  refreshAttendance()
}

/**
 * "Bepul dars" saqlangach oyna OCHIQ qoladi (tanlov `pinned`da qoladi,
 * `openCell` yuqoridagi `computed` orqali AVTOMATIK yangi ma'lumot bilan
 * qayta chiziladi) — lekin BUTUN varaq (hamma o'quvchining
 * `lessonChargedAmount`i) eskirgan bo'lardi, shuning uchun `handleSaved`
 * dan FARQLI, oyna yopilmaydi.
 */
function handleFreeLessonSaved(): void {
  refreshAttendance()
}

function refreshAttendance(): void {
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
        <!--
          TELEFON: dars tanlagich + shu darsning varaqasi. Nega matritsa
          emasligi skriptdagi katta izohda yozilgan.
        -->
        <div v-if="!isDesktop">
          <!--
            Chiplar o'quvchi ilovasidagi guruh tanlagichi bilan bir xil
            naqshda (`StudentRecordingsPage`) — xodim bu ko'rinishni
            allaqachon taniydi. Kartochka chetigacha cho'ziladi: chetdagi
            chip "yana bor" degan ishorani beradi.
          -->
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
                <span
                  v-if="freeSessionIds.has(session.id)"
                  class="text-green-400"
                >· bepul</span>
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
            <span
              v-if="activeSheet?.isFreeLesson === true"
              class="rounded-full bg-green-500/15 px-1.5 py-0.5 text-[9px] font-semibold text-green-400"
            >Bepul dars</span>
            <p
              v-if="(activeHeader?.title ?? '').length > 0"
              class="w-full truncate text-xs text-slate-400"
            >
              {{ activeHeader?.title }}
            </p>
          </div>

          <!--
            Xulosa: faqat ISH QOLGANINI ko'rsatadi. "Hammasi belgilangan"
            holatida qator umuman chizilmaydi — bo'sh xabar joy egallardi.
          -->
          <p
            v-if="activeCounts.blank > 0 || activeCounts.absent > 0"
            class="mt-1 text-[11px] text-slate-400"
          >
            <span
              v-if="activeCounts.absent > 0"
              class="text-rose-400"
            >{{ activeCounts.absent }} ta qatnashmagan</span>
            <span v-if="activeCounts.absent > 0 && activeCounts.blank > 0"> · </span>
            <span v-if="activeCounts.blank > 0">{{ activeCounts.blank }} ta belgilanmagan</span>
          </p>

          <ul class="mt-2.5 divide-y divide-line rounded-xl border border-line">
            <li
              v-for="row in activeRows"
              :key="row.studentId"
            >
              <!--
                Butun qator bosiladi (44px dan baland) — jadvaldagi 44×44
                katakning telefondagi ekvivalenti. Ochiladigan oyna AYNAN
                o'sha (`AttendanceCellDialog`), ya'ni tuzatish oqimi bir xil.
              -->
              <button
                type="button"
                class="flex w-full items-center gap-3 px-3 py-2.5 text-left transition-colors hover:bg-ink-800"
                @click="openActiveDialog(row)"
              >
                <span
                  class="relative flex size-9 shrink-0 items-center justify-center rounded-lg text-[15px] font-bold"
                  :class="symbolClass(row.status)"
                >
                  {{ attendanceSymbol(row.status) }}
                  <!-- Eski `.manual-dot` — jadvaldagi bilan bir xil belgi. -->
                  <span
                    v-if="row.isManual"
                    class="absolute right-0.5 top-0.5 size-[5px] rounded-full bg-brand-500"
                    aria-hidden="true"
                  />
                </span>
                <span class="min-w-0 flex-1">
                  <span class="flex items-center gap-1.5">
                    <span
                      class="block min-w-0 truncate text-sm font-medium text-slate-100"
                      v-text="row.name"
                    />
                    <!--
                      "Sababli" (2026-08-16) — to'lovga ta'sir qiladigan
                      qaror, shuning uchun jadvalda ham (nafaqat modalda)
                      ko'rinishi kerak: "nega bu o'quvchidan pul
                      yechilmadi?" savolini oldindan javoblab qo'yadi.
                    -->
                    <span
                      v-if="row.isExcused"
                      class="shrink-0 rounded-full bg-green-500/15 px-1.5 py-0.5 text-[9px] font-semibold text-green-400"
                    >Sababli</span>
                  </span>
                  <span
                    v-if="row.reason !== null"
                    class="block truncate text-[11px] text-dim"
                    v-text="row.reason"
                  />
                  <!-- "Qancha yechilgan" (2026-08-16) — izoh: `AttendanceCellDialog` da AYNI naqsh. -->
                  <span
                    v-if="row.lessonAmount !== null"
                    class="block text-[11px] tabular-nums"
                    :class="(row.lessonChargedAmount ?? 0) > 0 ? 'text-dim' : 'text-green-400'"
                  >{{ formatMoney(row.lessonChargedAmount ?? 0) }} yechildi</span>
                </span>
                <!--
                  Jadvalda holat FAQAT rang va belgi bilan beriladi (ustun
                  tor). Ro'yxatda joy bor — to'liq nom yoziladi, ya'ni
                  ma'lumot faqat rangga bog'liq bo'lib qolmaydi.
                -->
                <span
                  class="shrink-0 text-xs text-slate-400"
                  v-text="attendanceStatusLabel(row.status)"
                />
              </button>
            </li>
          </ul>
        </div>

        <!--
          ═══════════════════════════════════════════════════════════════
          DESKTOP MATRITSASI — R23 ("davomat jadvali professional bo'lsin")

          🔴 `max-h-[70dvh]` + `overflow-y-auto` — BEZAK EMAS, YOPISHQOQ
          SARLAVHANING SHARTI. `position: sticky` eng yaqin SKROLL
          konteyneriga nisbatan ishlaydi. Bu div `scroll-x-safe` tufayli
          gorizontal skroll konteyneri edi (CSS qoidasi: bir o'qda
          `auto` bo'lsa, ikkinchisidagi `visible` ham `auto` ga aylanadi),
          lekin balandligi cheklanmagani uchun vertikal HECH QACHON
          skrollanmasdi — sahifaning o'zi skrollanardi va `top-0` hech
          nimaga yopishmasdi. Balandlik cheklangach sarlavha o'z konteyneri
          ichida qotadi: 25-o'quvchiga tushganda ham sana ko'rinib turadi.

          ★ `border-separate` ALLAQACHON bor edi (`border-collapse` da
          yopishqoq katakning chegarasi yo'qoladi) — ya'ni yopishqoq
          sarlavha uchun qo'shimcha hiyla kerak emas.

          ★ z-qatlamlari: sarlavha burchagi (6) > sarlavha (4) >
          "Jami" qatori burchagi (5) > "Jami" qatori (3) > ism ustuni (2).
          Burchaklar YUQORIROQ, chunki ular ikki yo'nalishda ham yopishadi
          va qolgan yopishqoq kataklar ustidan o'tadi.
        -->
        <div
          v-else
          ref="scroller"
          class="scroll-x-safe scrollbar-slim max-h-[70dvh] overflow-y-auto rounded-xl border border-line"
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
                    <span
                      v-if="freeSessionIds.has(session.id)"
                      class="text-green-400"
                    >· bepul</span>
                  </span>
                  {{ new Date(session.scheduledStart).getDate() }}/{{
                    new Date(session.scheduledStart).getMonth() + 1
                  }}
                  <span class="block text-[9px] font-normal text-dim">
                    {{ formatTime(session.scheduledStart) }}
                  </span>
                </th>
                <!--
                  ★ Oyna sarlavhaning O'ZIDA yozilgan ("N ta dars"): shu
                  ustundagi son NIMANING ustidan hisoblanganini katakdan
                  uzoqlashtirmasdan aytadi.
                -->
                <th
                  class="sticky top-0 z-[4] border-b border-line bg-ink-800 px-3 py-2 text-center text-[11px] font-semibold text-slate-400"
                >
                  Jami
                  <span class="block text-[9px] font-normal text-dim">
                    {{ columns.length }} ta dars
                  </span>
                </th>
              </tr>
            </thead>
            <tbody>
              <!--
                Zebra: qator foni `<tr>` da BITTA sinf bilan beriladi
                (`[&>td]:bg-*`), `<td>` da qattiq `bg-ink-900` bilan EMAS.
                Ikki xil fon utility'si bitta katakka tushsa qaysi biri
                g'olib bo'lishi CSS faylidagi tartibga bog'liq bo'lardi —
                bu yerda esa har qatorga faqat BITTA fon tegadi.
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
                    class="relative flex h-11 w-11 items-center justify-center text-[15px] font-bold transition-colors hover:bg-ink-750 hover:shadow-[inset_0_0_0_2px_var(--color-brand-500)]"
                    :class="cellClass(cellOf(session.id, row.studentId))"
                    :title="cellTitle(session, cellOf(session.id, row.studentId))"
                    @click="openDialog(session, row.studentId, row.name)"
                  >
                    {{ attendanceSymbol(cellOf(session.id, row.studentId)?.status ?? null) }}
                    <!-- Eski `.manual-dot` — holatni odam qo'ygani belgisi. -->
                    <span
                      v-if="cellOf(session.id, row.studentId)?.isManual === true"
                      class="absolute right-1 top-1 size-[5px] rounded-full bg-brand-500"
                      aria-hidden="true"
                    />
                    <!--
                      "Sababli" nuqtasi (2026-08-16) — `manual-dot` bilan
                      BIR XIL naqsh, lekin QARAMA-QARSHI burchakda (chap
                      pastda) va YASHIL: 44×44 katakda matn uchun joy yo'q
                      (`AttendanceTab` dagi asosiy dizayn qarori), lekin
                      to'lovga ta'sir qiladigan qaror ko'rinmas qolmasligi
                      kerak — aniq tafsilot modalda (`AttendanceCellDialog`).
                    -->
                    <span
                      v-if="cellOf(session.id, row.studentId)?.isExcused === true"
                      class="absolute bottom-1 left-1 size-[5px] rounded-full bg-green-500"
                      aria-hidden="true"
                    />
                  </button>
                </td>
                <!--
                  Yig'indi legendaning AYNAN o'sha belgilarida: `+ ± − ·`.
                  Nol bo'lgan guruh chizilmaydi (`+9` yonidagi `±0 −0 ·0`
                  faqat shovqin), lekin `+N` DOIM turadi — aks holda hech
                  kim kelmagan qatorda ustun bo'sh qolib, "yuklanmadi" deb
                  o'qilardi.
                -->
                <td
                  class="whitespace-nowrap border-b border-line px-3 text-center text-[11px] tabular-nums"
                  :title="row.totalsTitle"
                >
                  <span class="font-bold text-green-400">+{{ row.totals.present }}</span>
                  <span
                    v-if="row.totals.partial > 0"
                    class="ml-1.5 text-amber-400"
                  >±{{ row.totals.partial }}</span>
                  <span
                    v-if="row.totals.absent > 0"
                    class="ml-1.5 text-rose-400"
                  >−{{ row.totals.absent }}</span>
                  <span
                    v-if="row.totals.blank > 0"
                    class="ml-1.5 text-dim"
                  >·{{ row.totals.blank }}</span>
                </td>
              </tr>
            </tbody>
            <!--
              "Jami" QATORI — dars kesimida nechta o'quvchi qatnashgani.
              Katak eni 44px bo'lgani uchun bu yerda FAQAT `+N` sig'adi;
              to'liq taqsimot `title` da (ustundagi kabi parchalash
              ustunlarni kengaytirib, 44×44 setkasini buzardi).
            -->
            <tfoot>
              <tr>
                <th
                  class="sticky bottom-0 left-0 z-[5] whitespace-nowrap border-r border-t border-line bg-ink-800 px-3.5 py-2 text-left text-[11px] font-semibold text-slate-400"
                >
                  Jami
                </th>
                <td
                  v-for="column in matrixColumns"
                  :key="column.session.id"
                  class="sticky bottom-0 z-[3] border-r border-t border-line bg-ink-800 px-2.5 py-2 text-center text-[11px] font-bold tabular-nums text-green-400"
                  :title="column.totalsTitle"
                >
                  +{{ column.totals.present }}
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

        <!--
          🔴 Qamrov ochiq aytiladi. Yig'indi butun kurs bo'yicha EMAS, faqat
          ekrandagi oyna bo'yicha — bu eslatmasiz son jimgina yolg'on
          gapirardi ("Yana ..." bosilganda o'zgarib ketadigan "davomat foizi").
        -->
        <p
          v-if="isDesktop"
          class="mt-1 text-[11px] text-dim"
        >
          “Jami” ustuni va qatori faqat shu yerda ko‘rinib turgan
          {{ columns.length }} ta dars bo‘yicha hisoblanadi.
        </p>
      </DataStatus>
    </div>
  </BaseCard>

  <AttendanceCellDialog
    :cell="openCell"
    @close="closeDialog"
    @saved="handleSaved"
    @free-lesson-saved="handleFreeLessonSaved"
  />
</template>
