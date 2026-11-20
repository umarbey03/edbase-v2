<script setup lang="ts">
import { useQuery, useQueryClient } from '@tanstack/vue-query'
import { computed, ref } from 'vue'

import {
  CURATOR_WORKSPACE_KEYS,
  fetchWorkspaceBackupThread,
  fetchWorkspaceStudent,
  fetchWorkspaceSubmissions,
  fetchWorkspaceTests,
  type StaffStudentSubmissionDto,
  type WorkspaceTestResultDto,
} from '@/entities/curator-workspace'
import {
  MASTERY_EVENT_LABELS,
  SELF_STUDY_CURATOR_KEYS,
  fetchMasteryEvents,
  type MasteryEventDto,
} from '@/entities/self-study-curator'
import GradeDialog from '@/features/grading/ui/GradeDialog.vue'
import StudentLessonBoard from '@/features/self-study-curator/ui/StudentLessonBoard.vue'
import { InboxThread, type InboxTimelineExtra } from '@/features/teacher-inbox'
import { toUserMessage } from '@/shared/api'
import { formatDateNumeric, formatDateTimeNumeric, formatTime } from '@/shared/lib/datetime'
import { formatPhone } from '@/shared/lib/phone'
import type { ConversationDto, MessagePageDto } from '@/shared/types'
import { AppIcon, BaseBadge, BaseButton, BaseDrawer, DataStatus } from '@/shared/ui'

import MasteryDecisionCard from './MasteryDecisionCard.vue'

/**
 * Ish stoli — o'ng panel: TANLANGAN o'quvchi bilan bog'liq HAMMA narsa
 * (2026-11-20, loyiha egasi: "kurator u yoqdan bu yoqqa yugurib qolyapti").
 *
 *   • tepada qotgan DARS QARORI kartasi (Mustaqil, qaror kutilganda);
 *   • bitta oqim: yozishma + dars savollari ("Javob berish") + vazifa
 *     javoblari ("Tekshirish" shu yerning o'zida) + test natijalari +
 *     dars qarorlari tarixi — vaqt bo'yicha;
 *   • darslar xaritasi — yon panelda, sahifadan chiqmasdan.
 *
 * Yozish va ovoz/rasm — mavjud `InboxThread` (kurator o'rgangan AYNI chat).
 */
const props = defineProps<{ studentId: number }>()

const emit = defineEmits<{ back: [] }>()

const queryClient = useQueryClient()

const headerQuery = useQuery({
  queryKey: computed(() => CURATOR_WORKSPACE_KEYS.student(props.studentId)),
  queryFn: ({ signal }) => fetchWorkspaceStudent(props.studentId, { signal }),
  refetchInterval: 60_000,
})
const header = computed(() => headerQuery.data.value ?? null)

const submissionsQuery = useQuery({
  queryKey: computed(() => CURATOR_WORKSPACE_KEYS.submissions(props.studentId)),
  queryFn: ({ signal }) => fetchWorkspaceSubmissions(props.studentId, { signal }),
  refetchInterval: 60_000,
})

const testsQuery = useQuery({
  queryKey: computed(() => CURATOR_WORKSPACE_KEYS.tests(props.studentId)),
  queryFn: ({ signal }) => fetchWorkspaceTests(props.studentId, { signal }),
  refetchInterval: 120_000,
})

const eventsQuery = useQuery({
  queryKey: computed(() => SELF_STUDY_CURATOR_KEYS.events(props.studentId)),
  queryFn: ({ signal }) => fetchMasteryEvents(props.studentId, { signal }),
  enabled: computed(() => header.value?.isSelfStudy === true),
})

/* ---------------------------------------------------------------- oqim kartalari */

type WorkspaceExtra = InboxTimelineExtra & (
  | { type: 'submission', data: StaffStudentSubmissionDto }
  | { type: 'test', data: WorkspaceTestResultDto }
  | { type: 'mastery', data: MasteryEventDto }
)

const extras = computed<WorkspaceExtra[]>(() => [
  ...(submissionsQuery.data.value ?? []).map((data) => ({
    key: `s-${data.submission.id}`, at: data.submission.submittedAt, type: 'submission' as const, data,
  })),
  ...(testsQuery.data.value ?? []).map((data) => ({
    key: `t-${data.testId}`, at: data.submittedAt, type: 'test' as const, data,
  })),
  ...(eventsQuery.data.value ?? []).map((data) => ({
    key: `e-${data.id}`, at: data.at, type: 'mastery' as const, data,
  })),
])

function asExtra(item: InboxTimelineExtra): WorkspaceExtra {
  return item as WorkspaceExtra
}

/* ---------------------------------------------------------------- yozishma */

const peer = computed<ConversationDto | null>(() => header.value === null
  ? null
  : {
      peerId: props.studentId,
      peerName: header.value.fullName,
      peerRole: 'Student',
      groupName: header.value.groupName,
      lastMessageId: null,
      lastMessagePreview: null,
      lastMessageAt: null,
      lastMessageMine: null,
      unreadCount: 0,
    })

/** Zaxira kurator — faqat o'qish; na suhbatdosh, na zaxira — yozishma yo'q (faqat kartalar). */
const backupFetcher = computed<((signal: AbortSignal) => Promise<MessagePageDto>) | null>(() =>
  header.value !== null && !header.value.canChat && header.value.isBackup
    ? (signal: AbortSignal) => fetchWorkspaceBackupThread(props.studentId, { signal })
    : null)

const hasThread = computed(() => header.value !== null && (header.value.canChat || header.value.isBackup))

const sortedExtras = computed(() =>
  [...extras.value].sort((a, b) => new Date(a.at).getTime() - new Date(b.at).getTime()))

function refreshList(): void {
  void queryClient.invalidateQueries({ queryKey: CURATOR_WORKSPACE_KEYS.students })
}

/* ---------------------------------------------------------------- baholash */

const grading = ref<StaffStudentSubmissionDto | null>(null)

async function onGraded(): Promise<void> {
  grading.value = null
  await Promise.all([
    queryClient.invalidateQueries({ queryKey: CURATOR_WORKSPACE_KEYS.all }),
    queryClient.invalidateQueries({ queryKey: SELF_STUDY_CURATOR_KEYS.board(props.studentId) }),
  ])
}

/* ---------------------------------------------------------------- darslar xaritasi */

const boardOpen = ref(false)

const progressPercent = computed(() => {
  const h = header.value
  if (h === null || h.totalLessons === 0) return 0
  return Math.round((h.masteredLessons / h.totalLessons) * 100)
})

const headerError = computed(() => (headerQuery.error.value ? toUserMessage(headerQuery.error.value) : null))
</script>

<template>
  <div class="flex h-full min-h-0 flex-col gap-2.5">
    <DataStatus
      :pending="headerQuery.isPending.value"
      :error="headerError"
      :empty="false"
      :skeleton-rows="2"
      @retry="headerQuery.refetch()"
    >
      <template v-if="header !== null">
        <!-- ═══════════════ Sarlavha ═══════════════ -->
        <header class="flex shrink-0 flex-wrap items-center gap-3 rounded-xl border border-line bg-ink-900 px-3.5 py-2.5">
          <button
            type="button"
            class="tap-target flex items-center justify-center rounded-lg border border-line bg-ink-800 px-2 text-slate-100 lg:hidden"
            aria-label="Ro‘yxatga qaytish"
            @click="emit('back')"
          >
            <AppIcon
              name="arrow-left"
              :size="16"
            />
          </button>
          <div class="min-w-0 flex-1">
            <p class="flex flex-wrap items-center gap-2">
              <span
                class="truncate text-base font-bold text-slate-100"
                v-text="header.fullName"
              />
              <BaseBadge :tone="header.isSelfStudy ? 'accent' : 'neutral'">
                {{ header.isSelfStudy ? 'Mustaqil' : (header.groupName ?? 'Guruh') }}
              </BaseBadge>
              <BaseBadge
                v-if="header.isSelfStudy && !header.subscriptionActive"
                tone="danger"
              >
                Obuna tugagan
              </BaseBadge>
            </p>
            <p class="mt-0.5 text-xs text-dim">
              <template v-if="header.phone">
                {{ formatPhone(header.phone) }}
              </template>
              <template v-if="header.isSelfStudy && header.subscriptionEndsAt">
                · obuna {{ formatDateNumeric(header.subscriptionEndsAt) }} gacha
              </template>
              <template v-if="header.isBackup && header.primaryCuratorName">
                · asosiy kurator: {{ header.primaryCuratorName }}
              </template>
            </p>
          </div>
          <div
            v-if="header.totalLessons > 0"
            class="flex items-center gap-2"
          >
            <div class="h-2 w-24 overflow-hidden rounded-full bg-ink-800">
              <div
                class="h-full rounded-full bg-emerald-500"
                :style="{ width: `${progressPercent}%` }"
              />
            </div>
            <span
              class="text-xs tabular-nums text-slate-300"
              v-text="`${header.masteredLessons}/${header.totalLessons} dars`"
            />
          </div>
          <BaseButton
            v-if="header.isSelfStudy"
            size="sm"
            variant="secondary"
            @click="boardOpen = true"
          >
            <template #icon>
              <AppIcon
                name="book"
                :size="14"
              />
            </template>
            Darslar xaritasi
          </BaseButton>
        </header>

        <!-- ═══════════════ Dars qarori (qotgan) ═══════════════ -->
        <MasteryDecisionCard
          v-if="header.awaitingDecision !== null"
          :student-id="studentId"
          :awaiting="header.awaitingDecision"
          @open-board="boardOpen = true"
        />
      </template>
    </DataStatus>

    <!-- ═══════════════ Oqim ═══════════════ -->
    <InboxThread
      v-if="peer !== null && hasThread"
      :peer="peer"
      :extras="extras"
      :read-only-fetcher="backupFetcher"
      hide-header
      height-class="min-h-[360px] flex-1"
      @sent="refreshList"
    >
      <template #extra="{ item }">
        <!-- Vazifa javobi -->
        <div
          v-if="asExtra(item).type === 'submission'"
          class="mx-auto w-full max-w-md rounded-xl border border-amber-500/30 bg-amber-500/[0.06] p-3"
        >
          <template
            v-for="s in [asExtra(item).data as StaffStudentSubmissionDto]"
            :key="s.submission.id"
          >
            <p class="flex items-center justify-between gap-2 text-[11px] font-bold uppercase tracking-wider text-amber-300">
              <span>📝 {{ s.submission.attemptNumber > 1 ? `${s.submission.attemptNumber}-urinish` : 'Vazifa topshirildi' }}</span>
              <span
                class="font-normal normal-case tracking-normal text-dim"
                v-text="formatTime(s.submission.submittedAt)"
              />
            </p>
            <p
              class="mt-1 text-sm font-semibold text-slate-100"
              v-text="s.assignmentTitle"
            />
            <p
              v-if="s.lessonName"
              class="text-xs text-dim"
              v-text="s.lessonName"
            />
            <p
              v-if="s.submission.text"
              class="mt-1.5 line-clamp-3 whitespace-pre-line text-xs text-slate-300"
              v-text="s.submission.text"
            />
            <p
              v-if="(s.submission.files ?? []).length > 0"
              class="mt-1 text-xs text-slate-400"
            >
              📎 {{ (s.submission.files ?? []).length }} ta fayl (audio/rasm)
            </p>
            <div class="mt-2 flex flex-wrap items-center gap-2">
              <BaseBadge
                v-if="s.submission.status === 'Graded'"
                tone="success"
              >
                Baholandi: {{ s.submission.score ?? '—' }}/{{ s.maxScore }}
              </BaseBadge>
              <BaseBadge
                v-else
                tone="warning"
              >
                Tekshiruv kutmoqda
              </BaseBadge>
              <BaseButton
                v-if="s.canGrade"
                size="sm"
                :variant="s.submission.status === 'Graded' ? 'ghost' : 'primary'"
                @click="grading = s"
              >
                {{ s.submission.status === 'Graded' ? 'Bahoni ko‘rish' : 'Tekshirish' }}
              </BaseButton>
            </div>
          </template>
        </div>

        <!-- Test natijasi -->
        <p
          v-else-if="asExtra(item).type === 'test'"
          class="mx-auto w-fit rounded-full border border-line bg-ink-950 px-3 py-1 text-xs text-slate-300"
        >
          <template
            v-for="t in [asExtra(item).data as WorkspaceTestResultDto]"
            :key="t.testId"
          >
            ✅ Test: {{ t.title }} — <b class="tabular-nums">{{ t.score ?? '—' }}/{{ t.maxScore ?? '—' }}</b>
          </template>
        </p>

        <!-- Dars qarori tarixi -->
        <p
          v-else
          class="mx-auto w-fit max-w-md rounded-full border border-emerald-500/25 bg-emerald-500/[0.06] px-3 py-1 text-center text-xs text-emerald-200"
        >
          <template
            v-for="e in [asExtra(item).data as MasteryEventDto]"
            :key="e.id"
          >
            {{ e.kind === 'Mastered' ? '✅' : e.kind === 'NeedsWork' ? '↩' : '🔓' }}
            {{ e.lessonName }} — {{ MASTERY_EVENT_LABELS[e.kind] }} · {{ e.actorName }}
            <span
              v-if="e.note"
              class="text-emerald-100/70"
            >«{{ e.note }}»</span>
          </template>
        </p>
      </template>
    </InboxThread>

    <!-- Yozishma huquqi yo'q (masalan faqat baholaydigan ustoz) — faqat kartalar. -->
    <section
      v-else-if="header !== null"
      class="scrollbar-slim min-h-[360px] flex-1 space-y-2 overflow-y-auto rounded-xl border border-line bg-ink-900 p-3.5"
    >
      <p class="mb-2 flex items-center gap-2 text-xs text-slate-400">
        <AppIcon
          name="lock"
          :size="13"
        />
        Bu o‘quvchi bilan boshqa xodim yozishadi — bu yerda uning vazifa va natijalari.
      </p>
      <p
        v-if="sortedExtras.length === 0"
        class="py-8 text-center text-sm text-slate-500"
      >
        Hali vazifa yoki test natijasi yo‘q.
      </p>
      <div
        v-for="item in sortedExtras"
        :key="item.key"
        class="rounded-lg border border-line bg-ink-950 px-3 py-2 text-sm text-slate-200"
      >
        <template v-if="item.type === 'submission'">
          📝 {{ item.data.assignmentTitle }}
          <span class="text-xs text-dim">· {{ formatDateTimeNumeric(item.at) }}</span>
          <BaseButton
            v-if="item.data.canGrade"
            class="ml-2"
            size="sm"
            @click="grading = item.data"
          >
            {{ item.data.submission.status === 'Graded' ? 'Bahoni ko‘rish' : 'Tekshirish' }}
          </BaseButton>
        </template>
        <template v-else-if="item.type === 'test'">
          ✅ {{ item.data.title }} — {{ item.data.score ?? '—' }}/{{ item.data.maxScore ?? '—' }}
        </template>
        <template v-else>
          {{ item.data.lessonName }} — {{ MASTERY_EVENT_LABELS[item.data.kind] }} · {{ item.data.actorName }}
        </template>
      </div>
    </section>

    <GradeDialog
      :submission="grading?.submission ?? null"
      :max-score="grading?.maxScore ?? 0"
      @close="grading = null"
      @graded="onGraded"
    />

    <BaseDrawer
      :open="boardOpen"
      :title="header?.fullName ?? 'Darslar xaritasi'"
      subtitle="Darslar xaritasi"
      @close="boardOpen = false"
    >
      <StudentLessonBoard
        v-if="boardOpen"
        :student-id="studentId"
      />
    </BaseDrawer>
  </div>
</template>
