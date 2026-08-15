<script setup lang="ts">
import { useQuery, useQueryClient } from '@tanstack/vue-query'
import { computed, ref, watch } from 'vue'
import { useRouter } from 'vue-router'

import { answerFormatsLabel, assignmentState, assignmentTitle, fetchMyAssignments } from '@/entities/assignment'
import {
  fetchAvailableTests,
  scoreLabel,
  testBlockedReason,
  testKindLabel,
  testStatusLabel,
  testStatusTone,
  testTitle,
} from '@/entities/test'
import SubmitAssignmentDialog from '@/features/assignment-submit/ui/SubmitAssignmentDialog.vue'
import { LessonChatPanel } from '@/features/lesson-chat'
import { LessonVideoPlayer } from '@/features/lesson-video-player'
import { formatDateTime } from '@/shared/lib/datetime'
import type { CourseLessonDto, StudentAssignmentDto } from '@/shared/types'
import { AppIcon, BaseBadge, BaseButton, BaseModal } from '@/shared/ui'
import type { IconName } from '@/shared/ui'

/**
 * ════════════════════════════════════════════════════════════════════════
 *  DARS DASHBOARD — `LessonSheet.vue`ning O'RNIGA (2026-08-15)
 * ════════════════════════════════════════════════════════════════════════
 *
 * `LessonSheet.vue`da "bo'sh varaq" edi, chunki o'sha payt video maydoni
 * yo'q va vazifa/test ID'lari yetkazilmasdi (izoh: eski faylning boshi).
 * Ikkalasi ham endi hal: `CourseLessonDto.assets` video qismlarini beradi
 * (`LessonAssetsController.Get`/`Ticket` Student uchun ochiq), vazifa/test
 * esa `StudentAssignmentDto`/`AvailableTestDto`dagi `moduleLessonId`
 * orqali CLIENT tarafda bu darsga filtrlanadi (ular allaqachon yuklangan —
 * `StudentAssignmentsPage`/`StudentTestsPage` bilan BIR XIL query key,
 * ya'ni ikkinchi so'rov emas, keshni ULASHISH).
 *
 * Video-birinchi joylashuv (Coursera/Udemy uslubi): tepada pleer, pastda
 * bo'limlar — Vazifa, Test, Savol-javob. Har biri INLINE (marshrut
 * sakramaydi), faqat test yechish o'zining sahifasiga o'tadi — sabab
 * `student-test-take` marshrutining o'zidagi izohda (20+ savol).
 */
const props = defineProps<{
  lesson: CourseLessonDto | null
  moduleName: string
}>()

const emit = defineEmits<{ close: [] }>()

const router = useRouter()
const queryClient = useQueryClient()

const assignmentsQuery = useQuery({
  queryKey: ['assignments', 'mine'],
  queryFn: ({ signal }) => fetchMyAssignments({ signal }),
  enabled: computed(() => props.lesson !== null),
})

const testsQuery = useQuery({
  queryKey: ['tests', 'available'],
  queryFn: ({ signal }) => fetchAvailableTests({ signal }),
  enabled: computed(() => props.lesson !== null),
})

const lessonAssignments = computed(() => {
  const lessonId = props.lesson?.id
  if (lessonId === undefined) return []
  return (assignmentsQuery.data.value ?? []).filter((a) => a.moduleLessonId === lessonId)
})

const assignmentRows = computed(() =>
  lessonAssignments.value.map((item) => ({
    item,
    state: assignmentState(item),
    formats: answerFormatsLabel(item.allowedFormats),
  })),
)

const lessonTests = computed(() => {
  const lessonId = props.lesson?.id
  if (lessonId === undefined) return []
  return (testsQuery.data.value ?? []).filter((t) => t.moduleLessonId === lessonId)
})

const testRows = computed(() =>
  lessonTests.value.map((test) => ({
    test,
    label: testStatusLabel(test),
    tone: testStatusTone(test),
    blockedReason: testBlockedReason(test),
  })),
)

const submitting = ref<StudentAssignmentDto | null>(null)

/*
  ============================================================================
   VAZIFA / TEST / SAVOL-JAVOB — TAB KO'RINISHIDA, YONMA-YON
  ============================================================================

  Loyiha egasi: bo'limlar bir-birining ostiga TIZILGAN emas, segmentlangan
  tab qatorida yonma-yon turishi va faqat BITTASI ochiq bo'lishi kerak
  (`ManageAcademicSettingsPage`dagi tab naqshi bilan AYNI shakl).

  ★ "Savol-javob" DOIM ko'rinadi (kontentga bog'liq emas) — o'quvchi
  vazifa/test bo'lmagan darsda ham savol berishi mumkin. Vazifa/Test esa
  faqat MAVJUD bo'lganda tab sifatida chiqadi.
*/
type DashboardTab = 'assignment' | 'test' | 'chat'

const tabs = computed<{ key: DashboardTab; label: string; icon: IconName }[]>(() => {
  const list: { key: DashboardTab; label: string; icon: IconName }[] = []
  if (assignmentRows.value.length > 0) list.push({ key: 'assignment', label: 'Vazifa', icon: 'clipboard' })
  if (testRows.value.length > 0) list.push({ key: 'test', label: 'Test', icon: 'award' })
  list.push({ key: 'chat', label: 'Savol-javob', icon: 'chat' })
  return list
})

const activeTab = ref<DashboardTab>('chat')

// Har yangi dars ochilganda birinchi mavjud tab tanlanadi (Vazifa bo'lsa —
// u, aks holda Test, aks holda Savol-javob) — oldingi darsning tanlovi
// keyingisida QOLIB ketmasin.
watch(
  () => props.lesson?.id,
  () => {
    activeTab.value = tabs.value[0]?.key ?? 'chat'
  },
)

function openTest(testId: number): void {
  emit('close')
  void router.push({ name: 'student-test-take', params: { testId: String(testId) } })
}

function handleSubmitted(): void {
  submitting.value = null
  // Javob topshirilishi gating'ni ham o'zgartirishi mumkin (keyingi dars
  // ochilishi) — `StudentAssignmentsPage`dagi AYNI qoida.
  void queryClient.invalidateQueries({ queryKey: ['assignments', 'mine'] })
}
</script>

<template>
  <BaseModal
    :open="props.lesson !== null"
    :title="props.lesson?.name ?? ''"
    wide
    xl
    @close="emit('close')"
  >
    <div v-if="props.lesson !== null">
      <p
        class="text-[10.5px] font-bold uppercase tracking-[0.5px] text-slate-400"
        v-text="props.moduleName"
      />

      <p
        v-if="props.lesson.durationMin !== null"
        class="mt-1.5 inline-flex items-center gap-1.5 text-xs text-slate-400"
      >
        <AppIcon
          name="clock"
          :size="13"
        />
        {{ props.lesson.durationMin }} daq
      </p>

      <p
        v-if="(props.lesson.description ?? '').length > 0"
        class="mt-2.5 whitespace-pre-wrap rounded-xl border border-line bg-ink-800 p-3.5 text-sm leading-relaxed text-slate-300"
        v-text="props.lesson.description"
      />

      <!-- ------------------------------------------------------------- video -->
      <div class="mt-4">
        <LessonVideoPlayer :assets="props.lesson.assets ?? []" />
      </div>

      <!-- ------------------------------------------- Vazifa/Test/Chat tablari -->
      <div
        class="mt-5 inline-flex gap-1 rounded-2xl border border-line bg-ink-900 p-1"
        role="tablist"
      >
        <button
          v-for="tab in tabs"
          :key="tab.key"
          type="button"
          role="tab"
          :aria-selected="activeTab === tab.key"
          class="flex items-center gap-1.5 rounded-xl px-4 py-2 text-sm font-semibold transition-colors"
          :class="
            activeTab === tab.key
              ? 'bg-brand-500 text-on-brand'
              : 'text-slate-400 hover:bg-ink-800 hover:text-slate-100'
          "
          @click="activeTab = tab.key"
        >
          <AppIcon
            :name="tab.icon"
            :size="15"
          />
          {{ tab.label }}
        </button>
      </div>

      <!-- ---------------------------------------------------------- vazifa -->
      <div
        v-if="activeTab === 'assignment' && assignmentRows.length > 0"
        class="mt-3 space-y-3"
      >
        <article
          v-for="row in assignmentRows"
          :key="row.item.id"
          class="rounded-xl border border-line bg-ink-900 p-3.5"
        >
          <div class="flex flex-wrap items-start justify-between gap-2">
            <h4
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

      <!-- ------------------------------------------------------------ test -->
      <div
        v-if="activeTab === 'test' && testRows.length > 0"
        class="mt-3 space-y-3"
      >
        <article
          v-for="row in testRows"
          :key="row.test.id"
          class="rounded-xl border border-line bg-ink-900 p-3.5"
        >
          <div class="flex items-start justify-between gap-2">
            <h4
              class="min-w-0 flex-1 text-sm font-semibold text-slate-100"
              v-text="testTitle(row.test)"
            />
            <BaseBadge :tone="row.tone">
              {{ row.label }}
            </BaseBadge>
          </div>

          <p
            class="mt-1 text-xs text-dim"
            v-text="testKindLabel(row.test.kind)"
          />

          <dl class="mt-2.5 flex flex-wrap gap-x-4 gap-y-1.5 text-xs text-slate-400">
            <div class="inline-flex items-center gap-1.5">
              <AppIcon
                name="file-text"
                :size="13"
              />
              <span class="tabular-nums">{{ row.test.questionCount }} savol</span>
            </div>
            <div class="inline-flex items-center gap-1.5">
              <AppIcon
                name="star"
                :size="13"
              />
              <span class="tabular-nums">{{ row.test.maxScore }} ball</span>
            </div>
            <div
              v-if="row.test.timeLimitMinutes !== null"
              class="inline-flex items-center gap-1.5"
            >
              <AppIcon
                name="clock"
                :size="13"
              />
              <span class="tabular-nums">{{ row.test.timeLimitMinutes }} daqiqa</span>
            </div>
          </dl>

          <p
            v-if="row.test.myScore !== null"
            class="mt-3 rounded-lg bg-ink-800 px-3 py-2 text-xs text-slate-200"
          >
            Natijangiz:
            <span
              class="font-semibold tabular-nums text-green-400"
              v-text="scoreLabel(row.test.myScore, row.test.maxScore)"
            />
          </p>
          <p
            v-else-if="row.blockedReason !== null"
            class="mt-3 flex items-start gap-2 rounded-lg bg-ink-800 px-3 py-2 text-xs text-slate-300"
          >
            <AppIcon
              name="lock"
              :size="13"
              class="mt-px"
            />
            <span v-text="row.blockedReason" />
          </p>

          <div class="mt-3 flex justify-end">
            <BaseButton
              size="sm"
              :variant="row.test.canStart ? 'primary' : 'secondary'"
              @click="openTest(row.test.id)"
            >
              <template #icon>
                <AppIcon
                  :name="row.test.canStart ? 'play' : 'award'"
                  :size="13"
                />
              </template>
              {{
                row.test.myStatus === 'Submitted'
                  ? 'Natijani ko‘rish'
                  : row.test.myStatus === 'InProgress'
                    ? 'Davom ettirish'
                    : 'Boshlash'
              }}
            </BaseButton>
          </div>
        </article>
      </div>

      <!-- ------------------------------------------------------- savol-javob -->
      <div
        v-if="activeTab === 'chat'"
        class="mt-3"
      >
        <LessonChatPanel :lesson-id="props.lesson.id" />
      </div>
    </div>

    <SubmitAssignmentDialog
      :assignment="submitting"
      @close="submitting = null"
      @submitted="handleSubmitted"
    />
  </BaseModal>
</template>
