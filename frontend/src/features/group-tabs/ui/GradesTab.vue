<script setup lang="ts">
import { useQuery } from '@tanstack/vue-query'
import { computed } from 'vue'

import { assignmentTitle, fetchAssignments, fetchSubmissions } from '@/entities/assignment'
import { fetchGroupMembers } from '@/entities/group'
import { toUserMessage } from '@/shared/api'
import { truncate } from '@/shared/lib/text'
import type { AssignmentDto, SubmissionDto } from '@/shared/types'
import { AppIcon, BaseButton, BaseCard, DataStatus } from '@/shared/ui'

import { downloadCsv } from '../model/csv'

/**
 * "Baholar" tabi — eski `#tab-grades` ("Baholar jadvali").
 *
 * ★ USTUNLAR MA'NOSI O'ZGARDI (sabab: ma'lumot modeli boshqacha).
 * Eski tizimda erkin "baho" yozuvi bor edi va u TURLARGA bo'linardi
 * (Uy ishi · Test · Faollik · Imtihon) — ustoz istagan vaqtda qo'lda baho
 * qo'yardi. v2 da bunday obyekt YO'Q: baho har doim BIR TOPSHIRIQqa
 * bog'langan (`Submission.Score`), ya'ni "baho qo'yish" — ishni baholash.
 * Shuning uchun matritsa shakli SAQLANDI (qator = o'quvchi, ustun = mezon),
 * lekin ustunlar endi guruhning VAZIFALARI.
 *
 * Bu tab FAQAT KO'RSATADI; baholash "Vazifalar" tabida (yoki "Vazifalar va
 * baholash" sahifasida) bajariladi — ikki joyda ikkita baholash oynasi
 * bo'lsa, qaysi biri oxirgi yozgani noaniq bo'lardi.
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

interface GradeRow {
  studentId: number
  name: string
  cells: (SubmissionDto | null)[]
  /** Baholangan ishlarning foizdagi o'rtachasi. `null` — baho yo'q. */
  average: number | null
  gradedCount: number
}

const rows = computed<GradeRow[]>(() =>
  students.value.map((member) => {
    const cells = columns.value.map(
      (assignment) => byCell.value.get(`${assignment.id}:${member.studentId}`) ?? null,
    )
    const percents = cells
      .filter((cell): cell is SubmissionDto => cell !== null && cell.status === 'Graded')
      .map((cell) => cell.scorePercent)
      .filter((value): value is number => value !== null)

    return {
      studentId: member.studentId,
      name: member.fullName ?? `#${member.studentId}`,
      cells,
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
  downloadCsv(`${props.groupName}_baholar.csv`, [header, ...body])
}
</script>

<template>
  <BaseCard
    flush
    title="Baholar jadvali"
    subtitle="Ustunlar — guruhning uy vazifalari, kataklar — qo‘yilgan ball."
  >
    <template #actions>
      <BaseButton
        size="sm"
        variant="secondary"
        :disabled="rows.length === 0 || columns.length === 0"
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
      <DataStatus
        :pending="pending"
        :error="errorMessage"
        :empty="rows.length === 0 || columns.length === 0"
        :retrying="assignmentsQuery.isFetching.value"
        :skeleton-rows="3"
        empty-icon="award"
        empty-title="Baho yo‘q."
        empty-text="Guruhda faol o‘quvchi va kamida bitta uy vazifasi bo‘lgach jadval to‘ladi."
        @retry="refetch"
      >
        <div class="scroll-x-safe scrollbar-slim">
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
                  {{ row.average === null ? '—' : `${row.average}%` }}
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
    </div>
  </BaseCard>
</template>
