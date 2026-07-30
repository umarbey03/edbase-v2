<script setup lang="ts">
import { useQuery } from '@tanstack/vue-query'
import { computed, ref } from 'vue'

import {
  assignmentTitle,
  fetchAssignments,
  fetchSubmissions,
  submissionStatusLabel,
  submissionStatusTone,
} from '@/entities/assignment'
import { toUserMessage } from '@/shared/api'
import { formatDate, formatDateTime } from '@/shared/lib/datetime'
import type { AssignmentDto, SubmissionDto } from '@/shared/types'
import { AppIcon, BaseBadge, BaseButton, BaseCard, DataStatus } from '@/shared/ui'

/**
 * "Vazifalar" tabi — eski `#tab-tasks` ("Uy vazifalari").
 *
 * Kartochka ko'rinishi eski `loadAssignments()` dan: sarlavha, kutayotgan
 * ishlar nishoni, ikki rangli progress chizig'i (yashil — baholangan,
 * oltin — topshirilgan-u baholanmagan) va "Topshiriqlar →" tugmasi bilan
 * ochiladigan ro'yxat.
 *
 * ★ "Tekshirish navbati" (to'liq ekranli tez baholash) KO'CHIRILMADI:
 * v2 da barcha vazifalar bo'yicha yagona navbat endpointi yo'q
 * (`GET /assignments/{id}/submissions` faqat bitta vazifa kesimida). Uning
 * o'rniga menyudagi "Vazifalar" sahifasi turadi — u ham vazifa tanlab
 * baholaydi.
 */
const props = defineProps<{
  groupId: number
  /** Guruhdagi o'quvchilar soni — progress chizig'i maxraji. */
  studentCount: number
}>()

const emit = defineEmits<{
  grade: [payload: { submission: SubmissionDto; maxScore: number }]
  reopen: [submission: SubmissionDto]
}>()

const assignmentsQuery = useQuery({
  queryKey: ['group', props.groupId, 'assignments'],
  queryFn: ({ signal }) =>
    fetchAssignments({ groupId: props.groupId, page: 1, pageSize: 50 }, { signal }),
})

const assignments = computed<AssignmentDto[]>(() => assignmentsQuery.data.value?.items ?? [])

const errorMessage = computed(() =>
  assignmentsQuery.error.value !== null ? toUserMessage(assignmentsQuery.error.value) : null,
)

/** Ochilgan vazifa — bir vaqtda bittasi (eski `viewSubs` naqshi). */
const openId = ref<number | null>(null)

function toggle(assignmentId: number): void {
  openId.value = openId.value === assignmentId ? null : assignmentId
}

const submissionsQuery = useQuery({
  queryKey: ['assignment-submissions', openId],
  queryFn: ({ signal }) => {
    const id = openId.value
    if (id === null) return Promise.resolve<SubmissionDto[]>([])
    return fetchSubmissions(id, { signal })
  },
  enabled: computed(() => openId.value !== null),
})

const submissions = computed(() => submissionsQuery.data.value ?? [])

const submissionsError = computed(() =>
  submissionsQuery.error.value !== null ? toUserMessage(submissionsQuery.error.value) : null,
)

/** Baholanganlar ulushi (yashil qism). */
function gradedPercent(assignment: AssignmentDto): number {
  if (props.studentCount === 0) return 0
  return Math.min(100, Math.round((assignment.gradedCount / props.studentCount) * 100))
}

/** Topshirilgan-u baholanmaganlar ulushi (oltin qism). */
function pendingPercent(assignment: AssignmentDto): number {
  if (props.studentCount === 0) return 0
  const submitted = Math.min(100, Math.round((assignment.submissionCount / props.studentCount) * 100))
  return Math.max(0, submitted - gradedPercent(assignment))
}

function pendingCount(assignment: AssignmentDto): number {
  return Math.max(0, assignment.submissionCount - assignment.gradedCount)
}
</script>

<template>
  <BaseCard
    flush
    title="Uy vazifalari"
    subtitle="Guruhga berilgan vazifalar va o‘quvchilar javoblari."
  >
    <div class="p-3.5 sm:p-5">
      <DataStatus
        :pending="assignmentsQuery.isPending.value"
        :error="errorMessage"
        :empty="assignments.length === 0"
        :retrying="assignmentsQuery.isFetching.value"
        :skeleton-rows="2"
        empty-icon="clipboard"
        empty-title="Hozircha biriktirilgan vazifa yo‘q."
        empty-text="Vazifa berish “Vazifalar” sahifasida (yon menyu)."
        @retry="assignmentsQuery.refetch()"
      >
        <ul class="space-y-2.5">
          <li
            v-for="assignment in assignments"
            :key="assignment.id"
            class="rounded-lg border border-line bg-ink-950 p-3.5"
          >
            <div class="flex items-start justify-between gap-3">
              <div class="min-w-0">
                <div class="flex flex-wrap items-center gap-2">
                  <b
                    class="text-sm text-slate-100"
                    v-text="assignmentTitle(assignment.title, assignment.id)"
                  />
                  <BaseBadge
                    v-if="pendingCount(assignment) > 0"
                    tone="warning"
                  >
                    {{ pendingCount(assignment) }} kutmoqda
                  </BaseBadge>
                  <BaseBadge
                    v-else-if="assignment.submissionCount > 0"
                    tone="success"
                  >
                    hammasi baholandi
                  </BaseBadge>
                </div>
                <p
                  v-if="assignment.description !== null"
                  class="mt-1 line-clamp-2 text-xs text-slate-400"
                  v-text="assignment.description"
                />
                <p
                  v-if="assignment.dueAt !== null"
                  class="mt-0.5 text-xs tabular-nums text-slate-400"
                >
                  Muddat: {{ formatDate(assignment.dueAt) }}
                </p>
              </div>

              <div class="shrink-0 text-right">
                <p class="text-xl font-extrabold leading-none tabular-nums text-slate-100">
                  {{ assignment.gradedCount
                  }}<span class="text-[13px] font-semibold text-slate-400">
                    /{{ props.studentCount }}</span>
                </p>
                <p class="text-xs text-slate-400">
                  baholandi
                </p>
              </div>
            </div>

            <!-- Eski ikki rangli progress chizig'i. -->
            <div class="mt-2.5 flex h-2 overflow-hidden rounded-full bg-ink-800">
              <div
                class="bg-green-500"
                :style="{ width: `${gradedPercent(assignment)}%` }"
              />
              <div
                class="bg-brand-500"
                :style="{ width: `${pendingPercent(assignment)}%` }"
              />
            </div>

            <div class="mt-2 flex flex-wrap items-center justify-between gap-2">
              <span class="text-xs text-slate-400">
                Topshirgan: <b
                  class="tabular-nums text-slate-100"
                >{{ assignment.submissionCount }}</b>/{{ props.studentCount }}
              </span>
              <button
                type="button"
                class="inline-flex min-h-11 items-center gap-1 rounded-lg px-2.5 text-xs font-semibold text-brand-500 transition-colors hover:bg-brand-500/10"
                :aria-expanded="openId === assignment.id"
                @click="toggle(assignment.id)"
              >
                Topshiriqlar
                <AppIcon
                  :name="openId === assignment.id ? 'chevron-down' : 'chevron-right'"
                  :size="14"
                />
              </button>
            </div>

            <div
              v-if="openId === assignment.id"
              class="mt-3 border-t border-line pt-3"
            >
              <DataStatus
                :pending="submissionsQuery.isPending.value"
                :error="submissionsError"
                :empty="submissions.length === 0"
                :retrying="submissionsQuery.isFetching.value"
                :skeleton-rows="2"
                empty-icon="check"
                empty-title="Navbat bo‘sh"
                empty-text="Bu vazifa bo‘yicha hali hech kim ish topshirmagan."
                @retry="submissionsQuery.refetch()"
              >
                <ul class="space-y-2">
                  <li
                    v-for="item in submissions"
                    :key="item.id"
                    class="flex flex-wrap items-center gap-2 rounded-lg border border-line bg-ink-900 p-2.5"
                  >
                    <span class="min-w-0 flex-1">
                      <b
                        class="block truncate text-[13px] text-slate-100"
                        v-text="item.studentName ?? '—'"
                      />
                      <span class="text-[11px] tabular-nums text-slate-400">
                        {{ formatDateTime(item.submittedAt) }} · {{ item.attemptNumber }}-urinish
                        <span
                          v-if="item.isLate"
                          class="text-amber-400"
                        >· kechikkan</span>
                      </span>
                    </span>
                    <BaseBadge :tone="submissionStatusTone(item.status)">
                      {{ submissionStatusLabel(item.status) }}
                    </BaseBadge>
                    <span class="text-xs tabular-nums text-slate-300">
                      {{ item.score ?? '—' }} / {{ assignment.maxScore }}
                    </span>
                    <BaseButton
                      size="sm"
                      variant="ghost"
                      @click="emit('reopen', item)"
                    >
                      Qaytarish
                    </BaseButton>
                    <BaseButton
                      size="sm"
                      variant="secondary"
                      @click="emit('grade', { submission: item, maxScore: assignment.maxScore })"
                    >
                      Baholash
                    </BaseButton>
                  </li>
                </ul>
              </DataStatus>
            </div>
          </li>
        </ul>
      </DataStatus>
    </div>
  </BaseCard>
</template>
