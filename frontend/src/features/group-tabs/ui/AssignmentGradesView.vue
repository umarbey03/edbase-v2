<script setup lang="ts">
import { useQuery } from '@tanstack/vue-query'
import { computed } from 'vue'

import { assignmentTitle, fetchAssignments, fetchSubmissions } from '@/entities/assignment'
import { fetchGroupMembers } from '@/entities/group'
import { toUserMessage } from '@/shared/api'
import { truncate } from '@/shared/lib/text'
import { useBreakpoint } from '@/shared/lib/useBreakpoint'
import type { AssignmentDto, SubmissionDto } from '@/shared/types'
import { DataStatus } from '@/shared/ui'

import { downloadCsv } from '../model/csv'

/**
 * ========================================================================
 * "Baholar" tabining VAZIFA ko'rinishi — R24 gacha butun tab shu edi.
 * ========================================================================
 *
 * ★ NIMA UCHUN SAQLANDI (R24 da o'chirilmadi): v2 ning BUTUN mavjud baho
 * ma'lumoti shu yerda. Baho har doim `Submission.Score` bo'lgan, ya'ni
 * VAZIFAGA bog'langan; dars bahosi esa YANGI obyekt va eskilarini unga
 * ko'chirish IMKONSIZ — dars ↔ vazifa xaritasi umuman mavjud emas
 * (`Assignment` yo guruhga, yo KURS DARSIGA bog'lanadi, jonli darsga
 * emas). Bu ko'rinish olib tashlansa, ustoz allaqachon qo'yilgan hamma
 * bahoni "Baholar" tabida KO'RA OLMAY qolardi — sof regressiya.
 *
 * Shuning uchun tab ikki ko'rinishga bo'lindi: "Darslar" (asosiy, R24) va
 * "Vazifalar" (shu fayl). Ma'lumot ko'chirilmaydi, ikkalasi YONMA-YON
 * yashaydi va ustunlar ma'nosi har birida ochiq aytiladi.
 *
 * Bu ko'rinish FAQAT KO'RSATADI; vazifa baholash "Vazifalar" tabida
 * bajariladi — ikki joyda ikkita baholash oynasi bo'lsa, qaysi biri
 * oxirgi yozgani noaniq bo'lardi.
 */
const props = defineProps<{
  groupId: number
  groupName: string
}>()

/** Jadval kengligi cheklanadi: 30 ustunli matritsa telefonda o'qilmaydi. */
const MAX_COLUMNS = 8

const assignmentsQuery = useQuery({
  queryKey: ['group', props.groupId, 'assignments'],
  queryFn: ({ signal }) =>
    fetchAssignments({ groupId: props.groupId, page: 1, pageSize: 50 }, { signal }),
})

const columns = computed<AssignmentDto[]>(() => {
  const items = assignmentsQuery.data.value?.items ?? []
  return [...items]
    .sort((a, b) => new Date(a.createdAt).getTime() - new Date(b.createdAt).getTime())
    .slice(-MAX_COLUMNS)
})

const hiddenColumns = computed(
  () => Math.max(0, (assignmentsQuery.data.value?.items ?? []).length - columns.value.length),
)

const columnIds = computed(() => columns.value.map((item) => item.id))

const membersQuery = useQuery({
  queryKey: ['group', props.groupId, 'members'],
  queryFn: ({ signal }) => fetchGroupMembers(props.groupId, { signal }),
})

/** Arxivlangan a'zolar matritsani cho'zadi — eski ilova ham faqat faollarni chizardi. */
const students = computed(() =>
  (membersQuery.data.value ?? []).filter((member) => member.status === 'Active'),
)

/**
 * ★ BITTA `useQuery` ICHIDA `Promise.all`: server "guruhning barcha
 * baholari" endpointini bermaydi, faqat `assignments/{id}/submissions`.
 * Har vazifa uchun alohida `useQuery` ochilsa jadval qism-qism (ustun
 * ketidan ustun) paydo bo'lardi — bitta so'rov guruhi bitta yuklanish
 * holatini beradi. Ustunlar soni 8 tadan oshmaydi.
 */
const submissionsQuery = useQuery({
  queryKey: ['group', props.groupId, 'grade-matrix', columnIds],
  queryFn: async ({ signal }) => {
    const ids = columnIds.value
    const lists = await Promise.all(ids.map((id) => fetchSubmissions(id, { signal })))
    return ids.map((assignmentId, index) => ({
      assignmentId,
      submissions: lists[index] ?? [],
    }))
  },
  enabled: computed(() => columnIds.value.length > 0),
})

const byCell = computed(() => {
  const map = new Map<string, SubmissionDto>()
  for (const group of submissionsQuery.data.value ?? []) {
    for (const submission of group.submissions) {
      map.set(`${group.assignmentId}:${submission.studentId}`, submission)
    }
  }
  return map
})

/**
 * TELEFON uchun bitta katak: baho + U QAYSI VAZIFAGA tegishli ekani.
 *
 * Jadvalda bu bog'lanishni SARLAVHA qatori beradi ("qaysi ustundaman?"),
 * kartochkada esa sarlavha qatori yo'q — shuning uchun har katak o'z nomini
 * va maksimal ballini o'zi olib yuradi.
 */
interface GradeChip {
  assignmentId: number
  /** Chipga sig'adigan qisqartma (jadval sarlavhasidagidek, lekin qisqaroq). */
  label: string
  /** To'liq nom — `title` maslahatida. */
  fullLabel: string
  maxScore: number
  cell: SubmissionDto | null
}

interface GradeRow {
  studentId: number
  name: string
  cells: (SubmissionDto | null)[]
  /** `cells` ning telefon ko'rinishi uchun boyitilgan nusxasi (bir xil tartib). */
  chips: GradeChip[]
  /** Baholangan ishlarning foizdagi o'rtachasi. `null` — baho yo'q. */
  average: number | null
  gradedCount: number
}

const rows = computed<GradeRow[]>(() =>
  students.value.map((member) => {
    // Bitta o'tishda ikkala shakl ham yig'iladi: `byCell` dan ikki marta
    // qidirish (jadval uchun alohida, kartochka uchun alohida) ikki manba
    // yaratardi va biri ikkinchisidan farq qilib qolishi mumkin edi.
    const chips = columns.value.map<GradeChip>((assignment) => {
      const fullLabel = assignmentTitle(assignment.title, assignment.id)
      return {
        assignmentId: assignment.id,
        label: truncate(fullLabel, 12),
        fullLabel,
        maxScore: assignment.maxScore,
        cell: byCell.value.get(`${assignment.id}:${member.studentId}`) ?? null,
      }
    })
    const cells = chips.map((chip) => chip.cell)
    const percents = cells
      .filter((cell): cell is SubmissionDto => cell !== null && cell.status === 'Graded')
      .map((cell) => cell.scorePercent)
      .filter((value): value is number => value !== null)

    return {
      studentId: member.studentId,
      name: member.fullName ?? `#${member.studentId}`,
      cells,
      chips,
      average:
        percents.length === 0
          ? null
          : Math.round(percents.reduce((sum, value) => sum + value, 0) / percents.length),
      gradedCount: cells.filter((cell) => cell !== null && cell.status === 'Graded').length,
    }
  }),
)

const pending = computed(
  () =>
    assignmentsQuery.isPending.value ||
    membersQuery.isPending.value ||
    (columnIds.value.length > 0 && submissionsQuery.isPending.value),
)

const errorMessage = computed(() => {
  const error =
    assignmentsQuery.error.value ?? membersQuery.error.value ?? submissionsQuery.error.value
  return error !== null ? toUserMessage(error) : null
})

function refetch(): void {
  void assignmentsQuery.refetch()
  void membersQuery.refetch()
  void submissionsQuery.refetch()
}

/* ═══════════════════════════════════════════════════════════════════════
   TELEFON KO'RINISHI — O'QUVCHI KARTOCHKASI, BAHOLAR CHIP BO'LIB

   Bu ham matritsa, LEKIN "Davomat" tabidagi yechim (avval darsni tanlash,
   keyin ro'yxatni ko'rish) BU YERGA TO'G'RI KELMAYDI — uchta farq bor:

   1. USTUNLAR SONI CHEKLANGAN: `MAX_COLUMNS` = {{ MAX_COLUMNS }}. Davomatda
      ustunlar 69 tagacha o'sadi, bu yerda esa sakkiztadan oshmaydi — ya'ni
      bitta o'quvchining BUTUN qatori chip sifatida ikki-uch qatorga
      sig'adi va gorizontal skroll umuman kerak emas.
   2. BU KO'RINISH FAQAT KO'RSATADI (baholash "Vazifalar" tabida). Katakni
      bosish oynasi yo'q, demak "avval mezonni tanla" bosqichi hech qanday
      amalni osonlashtirmaydi — faqat qo'shimcha bosish qo'shardi.
   3. XULOSA USTUNLARI O'QUVCHI KESIMIDA: "O'rtacha" va "Soni" — qatorning
      o'zi haqida. Vazifa kesimiga o'tilsa ular bo'sh qolardi, ya'ni
      jadvalning eng qimmatli ikki ustuni yo'qolardi.

   Shuning uchun bu yerda ikkala o'q ham saqlanadi: kartochka — o'quvchi,
   ichidagi chiplar — uning sakkiz vazifasi.
   ═══════════════════════════════════════════════════════════════════════ */
const { isDesktop } = useBreakpoint()

/** Ikki ko'rinishda bir xil bo'lishi uchun: `null` — baho yo'q, nol emas. */
function averageText(row: GradeRow): string {
  return row.average === null ? '—' : `${row.average}%`
}

function cellText(cell: SubmissionDto | null): string {
  if (cell === null) return '—'
  if (cell.status !== 'Graded') return '•'
  return String(cell.score ?? '—')
}

function cellClass(cell: SubmissionDto | null): string {
  if (cell === null) return 'text-dim'
  if (cell.status !== 'Graded') return 'text-amber-400'
  const percent = cell.scorePercent
  if (percent === null) return 'text-slate-100'
  if (percent >= 80) return 'text-green-400'
  if (percent >= 60) return 'text-brand-500'
  return 'text-rose-400'
}

function exportCsv(): void {
  const header = [
    'O‘quvchi',
    ...columns.value.map((item) => assignmentTitle(item.title, item.id)),
    'O‘rtacha',
    'Soni',
  ]
  const body = rows.value.map((row) => [
    row.name,
    ...row.cells.map((cell) => cellText(cell)),
    row.average === null ? '—' : `${row.average}%`,
    String(row.gradedCount),
  ])
  downloadCsv(`${props.groupName}_vazifa-baholari.csv`, [header, ...body])
}

/*
  CSV tugmasi kartochka SARLAVHASIDA turadi (ota komponentda), eksport
  mantiqi esa MA'LUMOT shu yerda bo'lgani uchun shu yerda qoladi. Ota
  FAOL ko'rinishning shu ikki a'zosini shablon havolasi orqali oladi —
  ma'lumotni otaga ko'chirish "bitta manba" qoidasini buzardi va ikkala
  ko'rinish ham har doim yuklanib turardi.
*/
const hasData = computed(() => rows.value.length > 0 && columns.value.length > 0)

defineExpose({ exportCsv, hasData })
</script>

<template>
  <DataStatus
    :pending="pending"
    :error="errorMessage"
    :empty="rows.length === 0 || columns.length === 0"
    :retrying="assignmentsQuery.isFetching.value"
    :skeleton-rows="3"
    empty-icon="award"
    empty-title="Vazifa bahosi yo‘q."
    empty-text="Guruhda faol o‘quvchi va kamida bitta uy vazifasi bo‘lgach jadval to‘ladi."
    @retry="refetch"
  >
    <!--
      Telefon: har o'quvchi — bitta kartochka, baholari chip bo'lib
      ichida. Nega dars/vazifa tanlagichi EMASligi skriptdagi izohda.
    -->
    <ul
      v-if="!isDesktop"
      class="space-y-2"
    >
      <li
        v-for="row in rows"
        :key="row.studentId"
        class="rounded-lg border border-line bg-ink-950 p-3"
      >
        <div class="flex items-start justify-between gap-2">
          <p
            class="min-w-0 flex-1 truncate text-sm font-medium text-slate-100"
            v-text="row.name"
          />
          <span class="shrink-0 whitespace-nowrap">
            <b class="text-[15px] tabular-nums text-brand-500">{{ averageText(row) }}</b>
            <span class="ml-1 text-[10px] text-dim">O‘rtacha</span>
          </span>
        </div>
        <!-- Jadvaldagi "Soni" ustuni. Maxraj QO'SHILDI: yolg'iz "3"
             kartochkada nimaning uchdanligini bildirmasdi. -->
        <p class="mt-0.5 text-[11px] text-dim">
          Soni: {{ row.gradedCount }} / {{ columns.length }}
        </p>

        <div class="mt-2 flex flex-wrap gap-1.5">
          <span
            v-for="chip in row.chips"
            :key="chip.assignmentId"
            class="inline-flex items-baseline gap-1 rounded-md border border-line bg-ink-900 px-1.5 py-1 text-[11px]"
            :title="chip.fullLabel"
          >
            <span class="text-dim">{{ chip.label }}</span>
            <b
              class="tabular-nums"
              :class="cellClass(chip.cell)"
            >{{ cellText(chip.cell) }}</b>
            <!-- `/maks` FAQAT baholangan katakda: "—/10" yoki "•/10"
                 ma'nosiz bo'lardi (baho hali yo'q). -->
            <span
              v-if="chip.cell !== null && chip.cell.status === 'Graded'"
              class="text-dim"
            >/{{ chip.maxScore }}</span>
          </span>
        </div>
      </li>
    </ul>

    <!-- Desktop: jadval. Gorizontal skroll SHU konteynerda. -->
    <div
      v-else
      class="scroll-x-safe scrollbar-slim"
    >
      <table class="zn-table">
        <thead>
          <tr>
            <th>O‘quvchi</th>
            <th
              v-for="assignment in columns"
              :key="assignment.id"
              :title="assignmentTitle(assignment.title, assignment.id)"
            >
              {{ truncate(assignmentTitle(assignment.title, assignment.id), 16) }}
              <span class="text-dim">/{{ assignment.maxScore }}</span>
            </th>
            <th>O‘rtacha</th>
            <th>Soni</th>
          </tr>
        </thead>
        <tbody>
          <tr
            v-for="row in rows"
            :key="row.studentId"
          >
            <td
              class="font-medium text-slate-100"
              v-text="row.name"
            />
            <td
              v-for="(cell, index) in row.cells"
              :key="index"
              class="text-center font-semibold tabular-nums"
              :class="cellClass(cell)"
              :title="cell !== null && cell.status !== 'Graded' ? 'Topshirdi — hali baholanmagan' : ''"
            >
              {{ cellText(cell) }}
            </td>
            <td class="font-bold tabular-nums text-brand-500">
              {{ averageText(row) }}
            </td>
            <td
              class="tabular-nums text-slate-400"
              v-text="row.gradedCount"
            />
          </tr>
        </tbody>
      </table>
    </div>

    <p class="mt-3 text-[11px] text-dim">
      “•” — ish topshirilgan, lekin hali baholanmagan · “—” — javob yo‘q.
      <span v-if="hiddenColumns > 0">
        Jadvalda oxirgi {{ MAX_COLUMNS }} vazifa ko‘rsatilgan (yana
        {{ hiddenColumns }} ta bor) — hammasi “Vazifalar” tabida.
      </span>
    </p>
  </DataStatus>
</template>
