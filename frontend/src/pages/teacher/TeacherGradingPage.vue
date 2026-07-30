<script setup lang="ts">
import { useQuery, useQueryClient } from '@tanstack/vue-query'
import { computed, ref, watch } from 'vue'

import {
  answerFormatsLabel,
  assignmentTitle,
  fetchAssignments,
  fetchSubmissions,
  submissionStatusLabel,
  submissionStatusTone,
} from '@/entities/assignment'
import AssignmentFormDialog from '@/features/assignment-form/ui/AssignmentFormDialog.vue'
import GradeDialog from '@/features/grading/ui/GradeDialog.vue'
import ReopenDialog from '@/features/grading/ui/ReopenDialog.vue'
import { toUserMessage } from '@/shared/api'
import { formatDateTime } from '@/shared/lib/datetime'
import type { AssignmentDto, SubmissionDto } from '@/shared/types'
import { AppIcon, BaseBadge, BaseButton, BaseCard, DataStatus, PageHeader } from '@/shared/ui'

/**
 * Vazifalar va baholash navbati (ustoz/kurator).
 *
 * Backendda "barcha topshiriqlar bo'yicha navbat" endpoint'i YO'Q —
 * `GET /assignments/{id}/submissions` faqat bitta vazifa kesimida ishlaydi.
 * Shuning uchun oqim ikki bosqichli: vazifa tanlanadi -> uning ishlari
 * yuklanadi. Tanlov chip'lar bilan (telefonda ham bir qatorda skroll qiladi).
 *
 * RUXSAT CHEGARASI (server qoidasining aksi, uni ALMASHTIRMAYDI):
 *  • ustoz/kurator FAQAT guruh vazifasini yarata va tahrirlay oladi;
 *  • kurs darsi vazifasi ro'yxatda KO'RINADI (o'z o'quvchisini baholash
 *    uchun kerak), lekin uni faqat o'quv bo'limi tahrirlaydi — shuning uchun
 *    bunday vazifada "Tahrirlash" tugmasi ko'rsatilmaydi (aks holda tugma
 *    bosilib, 403 bilan qaytardi).
 */
const queryClient = useQueryClient()

const assignmentsQuery = useQuery({
  queryKey: ['assignments', 'list'],
  queryFn: ({ signal }) => fetchAssignments({ page: 1, pageSize: 50 }, { signal }),
})

const assignments = computed(() => assignmentsQuery.data.value?.items ?? [])
const selectedId = ref<number | null>(null)

// Ro'yxat kelgach birinchi vazifa avtomatik tanlanadi — bo'sh ekran ko'rsatmaymiz.
watch(assignments, (list) => {
  if (selectedId.value === null && list.length > 0) selectedId.value = list[0]?.id ?? null
})

const selected = computed(
  () => assignments.value.find((item) => item.id === selectedId.value) ?? null,
)

/** Kurs vazifasini ustoz tahrirlay olmaydi (server: "faqat o'quv bo'limi"). */
const canEditSelected = computed(
  () => selected.value !== null && selected.value.moduleLessonId === null,
)

const submissionsQuery = useQuery({
  queryKey: ['assignment-submissions', selectedId],
  queryFn: ({ signal }) => {
    const id = selectedId.value
    if (id === null) return Promise.resolve<SubmissionDto[]>([])
    return fetchSubmissions(id, { signal })
  },
  enabled: computed(() => selectedId.value !== null),
})

const submissions = computed(() => submissionsQuery.data.value ?? [])

const assignmentsError = computed(() =>
  assignmentsQuery.error.value !== null ? toUserMessage(assignmentsQuery.error.value) : null,
)
const submissionsError = computed(() =>
  submissionsQuery.error.value !== null ? toUserMessage(submissionsQuery.error.value) : null,
)

const grading = ref<SubmissionDto | null>(null)
const reopening = ref<SubmissionDto | null>(null)

function refreshSubmissions(): void {
  void queryClient.invalidateQueries({ queryKey: ['assignment-submissions'] })
  // Vazifa chip'idagi "baholangan/topshirilgan" sanog'i ham eskiradi.
  void queryClient.invalidateQueries({ queryKey: ['assignments', 'list'] })
}

function handleGraded(): void {
  grading.value = null
  refreshSubmissions()
}

/* ------------------------------------------------------- vazifa formasi */

const formOpen = ref(false)
const editing = ref<AssignmentDto | null>(null)

function openCreate(): void {
  editing.value = null
  formOpen.value = true
}

function openEdit(assignment: AssignmentDto): void {
  editing.value = assignment
  formOpen.value = true
}

function handleSaved(): void {
  void queryClient.invalidateQueries({ queryKey: ['assignments'] })
}
</script>

<template>
  <div>
    <PageHeader
      title="Vazifalar va baholash"
      subtitle="Uy vazifasi berish va o‘quvchilar ishlarini baholash"
    >
      <template #actions>
        <BaseButton @click="openCreate">
          <template #icon>
            <AppIcon
              name="plus"
              :size="16"
            />
          </template>
          Yangi vazifa
        </BaseButton>
      </template>
    </PageHeader>

    <DataStatus
      :pending="assignmentsQuery.isPending.value"
      :error="assignmentsError"
      :empty="assignments.length === 0"
      :retrying="assignmentsQuery.isFetching.value"
      :skeleton-rows="2"
      empty-icon="clipboard"
      empty-title="Vazifa yo‘q"
      empty-text="“Yangi vazifa” tugmasi bilan o‘z guruhingizga uy vazifasi bering."
      @retry="assignmentsQuery.refetch()"
    >
      <template #empty-action>
        <BaseButton @click="openCreate">
          <template #icon>
            <AppIcon
              name="plus"
              :size="16"
            />
          </template>
          Yangi vazifa
        </BaseButton>
      </template>

      <!-- Vazifa tanlash. Telefonda bir qatorda gorizontal skroll — sahifa emas, SHU blok. -->
      <div class="scroll-x-safe scrollbar-slim -mx-4 mb-4 px-4 sm:mx-0 sm:px-0">
        <div class="flex gap-2 pb-1">
          <button
            v-for="item in assignments"
            :key="item.id"
            type="button"
            class="flex min-h-11 shrink-0 items-center gap-2 rounded-lg border px-3 text-xs font-medium transition-colors"
            :class="
              item.id === selectedId
                ? 'border-brand-500 bg-brand-500/16 text-brand-400'
                : 'border-line bg-ink-800 text-slate-300 hover:bg-ink-750'
            "
            @click="selectedId = item.id"
          >
            <span
              class="max-w-40 truncate"
              v-text="assignmentTitle(item.title, item.id)"
            />
            <span class="rounded-full bg-ink-950/60 px-1.5 py-0.5 tabular-nums text-[11px]">
              {{ item.gradedCount }}/{{ item.submissionCount }}
            </span>
          </button>
        </div>
      </div>

      <BaseCard
        v-if="selected !== null"
        flush
        :title="assignmentTitle(selected.title, selected.id)"
        :subtitle="`Maksimal ball: ${selected.maxScore} · Topshirilgan: ${selected.submissionCount} · Baholangan: ${selected.gradedCount}`"
      >
        <template #actions>
          <BaseButton
            v-if="canEditSelected"
            size="sm"
            variant="secondary"
            @click="openEdit(selected)"
          >
            <template #icon>
              <AppIcon
                name="edit"
                :size="13"
              />
            </template>
            Tahrirlash
          </BaseButton>
          <span
            v-else
            class="text-[11px] text-dim"
          >
            Kurs vazifasi — tahrirlashni o‘quv bo‘limi bajaradi
          </span>
        </template>

        <div class="p-3.5 sm:p-5">
          <dl class="mb-4 flex flex-wrap gap-x-4 gap-y-1.5 text-xs text-slate-400">
            <div
              v-if="selected.dueAt !== null"
              class="inline-flex items-center gap-1.5"
            >
              <AppIcon
                name="clock"
                :size="13"
              />
              <span
                class="tabular-nums"
                v-text="formatDateTime(selected.dueAt)"
              />
            </div>
            <div class="text-dim">
              Javob turi: {{ answerFormatsLabel(selected.allowedFormats) }}
            </div>
          </dl>

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
            <!-- Telefon: kartochka -->
            <ul class="space-y-2 md:hidden">
              <li
                v-for="item in submissions"
                :key="item.id"
                class="rounded-lg border border-line bg-ink-950 p-3"
              >
                <div class="flex items-start justify-between gap-2">
                  <p
                    class="min-w-0 flex-1 truncate text-sm font-medium text-slate-100"
                    v-text="item.studentName ?? '—'"
                  />
                  <BaseBadge :tone="submissionStatusTone(item.status)">
                    {{ submissionStatusLabel(item.status) }}
                  </BaseBadge>
                </div>
                <p class="mt-1 text-xs tabular-nums text-slate-400">
                  {{ formatDateTime(item.submittedAt) }} · {{ item.attemptNumber }}-urinish
                  <span
                    v-if="item.isLate"
                    class="text-amber-400"
                  > · kechikkan</span>
                  <span
                    v-if="item.allowResubmit"
                    class="text-brand-400"
                  > · qayta yuborishga ruxsat bor</span>
                </p>
                <div class="mt-2 flex flex-wrap items-center justify-between gap-2">
                  <span class="text-xs tabular-nums text-slate-300">
                    Ball: {{ item.score ?? '—' }} / {{ selected.maxScore }}
                  </span>
                  <div class="flex items-center gap-2">
                    <BaseButton
                      size="sm"
                      variant="secondary"
                      @click="reopening = item"
                    >
                      Qayta yuborish
                    </BaseButton>
                    <BaseButton
                      size="sm"
                      @click="grading = item"
                    >
                      Baholash
                    </BaseButton>
                  </div>
                </div>
              </li>
            </ul>

            <!-- Desktop: jadval -->
            <div class="scroll-x-safe scrollbar-slim hidden md:block">
              <table class="zn-table">
                <thead>
                  <tr>
                    <th>O‘quvchi</th>
                    <th>Topshirilgan</th>
                    <th>Urinish</th>
                    <th>Holat</th>
                    <th>Ball</th>
                    <th />
                  </tr>
                </thead>
                <tbody>
                  <tr
                    v-for="item in submissions"
                    :key="item.id"
                  >
                    <td
                      class="font-medium text-slate-100"
                      v-text="item.studentName ?? '—'"
                    />
                    <td class="tabular-nums text-slate-400">
                      {{ formatDateTime(item.submittedAt) }}
                      <span
                        v-if="item.isLate"
                        class="text-amber-400"
                      >(kech)</span>
                    </td>
                    <td
                      class="tabular-nums text-slate-400"
                      v-text="item.attemptNumber"
                    />
                    <td>
                      <BaseBadge :tone="submissionStatusTone(item.status)">
                        {{ submissionStatusLabel(item.status) }}
                      </BaseBadge>
                      <span
                        v-if="item.allowResubmit"
                        class="ml-1.5 text-[11px] text-brand-400"
                      >ruxsat</span>
                    </td>
                    <td class="tabular-nums text-slate-200">
                      {{ item.score ?? '—' }} / {{ selected.maxScore }}
                    </td>
                    <td>
                      <div class="flex items-center justify-end gap-1.5">
                        <BaseButton
                          size="sm"
                          variant="ghost"
                          @click="reopening = item"
                        >
                          Qayta yuborish
                        </BaseButton>
                        <BaseButton
                          size="sm"
                          variant="secondary"
                          @click="grading = item"
                        >
                          Baholash
                        </BaseButton>
                      </div>
                    </td>
                  </tr>
                </tbody>
              </table>
            </div>
          </DataStatus>
        </div>
      </BaseCard>
    </DataStatus>

    <GradeDialog
      :submission="grading"
      :max-score="selected?.maxScore ?? 0"
      @close="grading = null"
      @graded="handleGraded"
    />

    <ReopenDialog
      :submission="reopening"
      @close="reopening = null"
      @reopened="refreshSubmissions"
    />

    <!--
      Ustoz FAQAT guruh vazifasini beradi: kurs darsiga biriktirish barcha
      guruhlarga tegadi va uni server o'quv bo'limiga qoldirgan.
    -->
    <AssignmentFormDialog
      :open="formOpen"
      :assignment="editing"
      :allow-course-target="false"
      @close="formOpen = false"
      @saved="handleSaved"
    />
  </div>
</template>
