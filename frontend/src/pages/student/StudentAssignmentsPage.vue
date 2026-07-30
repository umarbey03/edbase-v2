<script setup lang="ts">
import { useQuery, useQueryClient } from '@tanstack/vue-query'
import { computed, ref } from 'vue'

import {
  answerFormatsLabel,
  assignmentState,
  assignmentTitle,
  fetchMyAssignments,
} from '@/entities/assignment'
import SubmitAssignmentDialog from '@/features/assignment-submit/ui/SubmitAssignmentDialog.vue'
import { toUserMessage } from '@/shared/api'
import { formatDateTime } from '@/shared/lib/datetime'
import type { StudentAssignmentDto } from '@/shared/types'
import { AppIcon, BaseBadge, BaseButton, DataStatus } from '@/shared/ui'
import StudentSubHeader from '@/widgets/student-shell/ui/StudentSubHeader.vue'

/**
 * O'quvchining vazifalari.
 *
 * MUHIM: qulflangan dars vazifasi ham RO'YXATDA QOLADI — o'quvchi nima
 * kutayotganini bilishi kerak. Topshirib bo'lmasligining SABABI har
 * kartochkada matn bilan yoziladi.
 *
 * "Topshirish" tugmasi SERVER qaroriga tayanadi (`canSubmit` -> `blockedReason`)
 * — sahifa gating va qayta topshirish qoidalarini o'zicha hisoblamaydi.
 */
const queryClient = useQueryClient()

const assignmentsQuery = useQuery({
  queryKey: ['assignments', 'mine'],
  queryFn: ({ signal }) => fetchMyAssignments({ signal }),
})

/** Holat bir marta hisoblanadi — shablonda `assignmentState()` takror chaqirilmasin. */
const rows = computed(() =>
  (assignmentsQuery.data.value ?? []).map((item) => ({
    item,
    state: assignmentState(item),
    formats: answerFormatsLabel(item.allowedFormats),
    feedback: item.mySubmission?.feedback ?? null,
    fileCount: item.mySubmission?.files?.length ?? 0,
  })),
)

const errorMessage = computed(() =>
  assignmentsQuery.error.value !== null ? toUserMessage(assignmentsQuery.error.value) : null,
)

const submitting = ref<StudentAssignmentDto | null>(null)

function handleSubmitted(): void {
  // Javob topshirilishi gating'ni ham o'zgartiradi (keyingi dars ochilishi
  // mumkin) — shuning uchun butun ro'yxat qayta so'raladi.
  void queryClient.invalidateQueries({ queryKey: ['assignments', 'mine'] })
}
</script>

<template>
  <div>
    <!--
      `PageHeader` o'rniga `StudentSubHeader`: bu sahifa endi "O'quv" tabining
      ichida yashaydi va o'quvchiga u yerga qaytish yo'li ko'rinib turishi
      kerak (Mini App karkasida "orqaga" tugmasi yo'q).
    -->
    <StudentSubHeader
      title="Vazifalarim"
      subtitle="Topshirish kerak bo‘lgan va baholangan ishlar"
    />

    <DataStatus
      :pending="assignmentsQuery.isPending.value"
      :error="errorMessage"
      :empty="rows.length === 0"
      :retrying="assignmentsQuery.isFetching.value"
      empty-icon="clipboard"
      empty-title="Vazifa yo‘q"
      empty-text="Ustoz vazifa bergach shu yerda ko‘rinadi."
      @retry="assignmentsQuery.refetch()"
    >
      <div class="space-y-3">
        <article
          v-for="row in rows"
          :key="row.item.id"
          class="rounded-xl border border-line bg-ink-900 p-3.5 sm:p-4"
        >
          <div class="flex flex-wrap items-start justify-between gap-2">
            <h3
              class="min-w-0 flex-1 text-sm font-semibold text-slate-100"
              v-text="assignmentTitle(row.item.title, row.item.id)"
            />
            <BaseBadge :tone="row.state.tone">
              {{ row.state.label }}
            </BaseBadge>
          </div>

          <p
            v-if="row.item.description !== null && row.item.description.length > 0"
            class="mt-1.5 text-xs text-slate-400"
            v-text="row.item.description"
          />

          <dl class="mt-2.5 flex flex-wrap gap-x-4 gap-y-1.5 text-xs text-slate-400">
            <div
              v-if="row.item.moduleLessonName !== null"
              class="inline-flex min-w-0 items-center gap-1.5"
            >
              <AppIcon
                name="file-text"
                :size="13"
              />
              <span
                class="truncate"
                v-text="row.item.moduleLessonName"
              />
            </div>
            <div
              v-if="row.item.groupName !== null"
              class="inline-flex min-w-0 items-center gap-1.5"
            >
              <AppIcon
                name="users"
                :size="13"
              />
              <span
                class="truncate"
                v-text="row.item.groupName"
              />
            </div>
            <div
              v-if="row.item.dueAt !== null"
              class="inline-flex items-center gap-1.5"
            >
              <AppIcon
                name="clock"
                :size="13"
              />
              <span
                class="tabular-nums"
                v-text="formatDateTime(row.item.dueAt)"
              />
            </div>
            <div class="inline-flex items-center gap-1.5">
              <AppIcon
                name="star"
                :size="13"
              />
              <span class="tabular-nums">{{ row.item.maxScore }} ball</span>
            </div>
            <div
              v-if="row.formats.length > 0"
              class="text-dim"
            >
              Javob turi: {{ row.formats }}
            </div>
          </dl>

          <!-- Yuborilgan javobning qisqacha holati. -->
          <p
            v-if="row.item.mySubmission !== null"
            class="mt-2.5 flex flex-wrap items-center gap-x-3 gap-y-1 text-xs text-slate-400"
          >
            <span class="tabular-nums">
              {{ row.item.mySubmission.attemptNumber }}-urinish ·
              {{ formatDateTime(row.item.mySubmission.submittedAt) }}
            </span>
            <span
              v-if="row.fileCount > 0"
              class="inline-flex items-center gap-1.5"
            >
              <AppIcon
                name="paperclip"
                :size="13"
              />
              {{ row.fileCount }} ta fayl
            </span>
            <span
              v-if="row.item.mySubmission.isLate"
              class="text-amber-400"
            >kechikkan</span>
          </p>

          <!-- Nega topshira olmaslik sababi — qulflangan darsda ENG muhim ma'lumot. -->
          <p
            v-if="row.state.blockedReason !== null"
            class="mt-3 flex items-start gap-2 rounded-lg bg-ink-800 px-3 py-2 text-xs text-slate-300"
          >
            <AppIcon
              :name="row.item.lessonUnlocked ? 'alert' : 'lock'"
              :size="14"
              class="mt-px"
            />
            <span v-text="row.state.blockedReason" />
          </p>

          <!-- Ustoz qayta topshirishga ruxsat bergan bo'lsa — sababi ko'rsatiladi. -->
          <p
            v-if="(row.item.mySubmission?.resubmitNote ?? '').length > 0"
            class="mt-2 rounded-lg border border-amber-500/25 bg-amber-500/10 px-3 py-2 text-xs text-amber-200"
          >
            <span class="font-semibold">Qayta yuborish so‘raldi: </span>
            <span v-text="row.item.mySubmission?.resubmitNote" />
          </p>

          <p
            v-if="row.feedback !== null && row.feedback.length > 0"
            class="mt-2 rounded-lg border border-line bg-ink-950 px-3 py-2 text-xs text-slate-300"
          >
            <span class="font-semibold text-slate-200">Ustoz izohi: </span>
            <span v-text="row.feedback" />
          </p>

          <div
            v-if="row.state.blockedReason === null"
            class="mt-3 flex justify-end"
          >
            <BaseButton
              size="sm"
              @click="submitting = row.item"
            >
              <template #icon>
                <AppIcon
                  name="send"
                  :size="14"
                />
              </template>
              {{ row.item.mySubmission !== null ? 'Qayta yuborish' : 'Topshirish' }}
            </BaseButton>
          </div>
        </article>
      </div>
    </DataStatus>

    <SubmitAssignmentDialog
      :assignment="submitting"
      @close="submitting = null"
      @submitted="handleSubmitted"
    />
  </div>
</template>
