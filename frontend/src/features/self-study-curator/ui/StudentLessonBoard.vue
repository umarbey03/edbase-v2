<script setup lang="ts">
import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { computed, ref } from 'vue'

import { fetchSubmissions } from '@/entities/assignment'
import { lessonLockReasonLabel } from '@/entities/course'
import {
  adminUnlockLesson,
  decideLessonMastery,
  fetchMasteryEvents,
  fetchStudentLessonBoard,
  MASTERY_EVENT_LABELS,
  SELF_STUDY_CURATOR_KEYS,
} from '@/entities/self-study-curator'
import type { LessonBoardRowDto } from '@/entities/self-study-curator'
import GradeDialog from '@/features/grading/ui/GradeDialog.vue'
import { toUserMessage } from '@/shared/api'
import { formatDate, formatDateTime } from '@/shared/lib/datetime'
import { useConfirm } from '@/shared/lib/useConfirm'
import { showToast } from '@/shared/lib/useToast'
import type { SubmissionDto } from '@/shared/types'
import { AppIcon, BaseBadge, BaseButton, BaseCard, DataStatus } from '@/shared/ui'

/**
 * ════════════════════════════════════════════════════════════════════════
 *  KURATORNING "DARSLAR TAXTASI" (Mustaqil, 2026-11-18)
 * ════════════════════════════════════════════════════════════════════════
 *
 * Har dars uchun: o'quvchi nimani bajargan (video, vazifa, test), javobni
 * baholash va kuratorning qarori. «O'zlashtirdi» faqat ish to'liq
 * bajarilganda bosiladi (server ham tekshiradi), «qayta ishlasin» — izoh
 * bilan. Qaror keyingi darsni ochadi yoki yopiq qoldiradi.
 *
 * ★ Administrator darsni ISTISNO sifatida ocha oladi — sabab majburiy va
 *   pastdagi jurnalda saqlanadi.
 */
const props = defineProps<{ studentId: number }>()

const queryClient = useQueryClient()
const confirm = useConfirm()

const boardQuery = useQuery({
  queryKey: computed(() => SELF_STUDY_CURATOR_KEYS.board(props.studentId)),
  queryFn: ({ signal }) => fetchStudentLessonBoard(props.studentId, { signal }),
  refetchInterval: 60_000,
})
const eventsQuery = useQuery({
  queryKey: computed(() => SELF_STUDY_CURATOR_KEYS.events(props.studentId)),
  queryFn: ({ signal }) => fetchMasteryEvents(props.studentId, { signal }),
})

const board = computed(() => boardQuery.data.value ?? null)
const lessons = computed(() => board.value?.lessons ?? [])

/*
  Ko'rinish: o'quvchi hozir turgan joy (oxirgi ochiq dars) atrofi muhim.
  Kurs 70+ dars bo'lishi mumkin — butun ro'yxat bir ekranda chalg'itadi,
  shuning uchun yopiq darslar standart holatda yig'iladi.
*/
const showLocked = ref(false)
const lastUnlocked = computed(() => {
  let last = -1
  lessons.value.forEach((l, i) => { if (l.unlocked) last = i })
  return last
})
const visible = computed(() => showLocked.value
  ? lessons.value
  : lessons.value.filter((l, i) => l.unlocked || i === lastUnlocked.value + 1))
const hiddenCount = computed(() => lessons.value.length - visible.value.length)

const awaiting = computed(() =>
  lessons.value.filter((l) => l.unlocked && l.workDone && l.mastery !== 'Mastered').length)

function refresh(): void {
  void queryClient.invalidateQueries({ queryKey: SELF_STUDY_CURATOR_KEYS.board(props.studentId) })
  void queryClient.invalidateQueries({ queryKey: SELF_STUDY_CURATOR_KEYS.events(props.studentId) })
  void queryClient.invalidateQueries({ queryKey: SELF_STUDY_CURATOR_KEYS.mine })
}

/* ------------------------------------------------------------- qaror --- */

const noteFor = ref<number | null>(null)
const note = ref('')

const decide = useMutation({
  mutationFn: (v: { lessonId: number, status: 'Mastered' | 'NeedsWork', note: string | null }) =>
    decideLessonMastery(props.studentId, v.lessonId, { status: v.status, note: v.note }),
  onSuccess: (data, v) => {
    queryClient.setQueryData(SELF_STUDY_CURATOR_KEYS.board(props.studentId), data)
    showToast(v.status === 'Mastered' ? 'Belgilandi — keyingi dars ochildi' : 'Qayta ishlashga qaytarildi', 'success')
    noteFor.value = null
    note.value = ''
    refresh()
  },
  onError: (error) => showToast(toUserMessage(error), 'error'),
})

async function markMastered(row: LessonBoardRowDto): Promise<void> {
  const ok = await confirm({
    title: `«${row.lessonName}» o‘zlashtirildimi?`,
    message: 'Tasdiqlasangiz o‘quvchiga keyingi dars ochiladi va xabar boradi.',
    confirmLabel: 'O‘zlashtirdi',
    tone: 'primary',
  })
  if (ok) decide.mutate({ lessonId: row.lessonId, status: 'Mastered', note: null })
}

function openNeedsWork(row: LessonBoardRowDto): void {
  noteFor.value = row.lessonId
  note.value = row.mastery === 'NeedsWork' ? (row.masteryNote ?? '') : ''
}

function submitNeedsWork(row: LessonBoardRowDto): void {
  decide.mutate({ lessonId: row.lessonId, status: 'NeedsWork', note: note.value.trim() })
}

/* ---------------------------------------------------- admin istisnosi --- */

const unlockFor = ref<number | null>(null)
const unlockReason = ref('')

const unlock = useMutation({
  mutationFn: (v: { lessonId: number, unlocked: boolean, reason: string | null }) =>
    adminUnlockLesson(props.studentId, v.lessonId, { unlocked: v.unlocked, reason: v.reason }),
  onSuccess: (data, v) => {
    queryClient.setQueryData(SELF_STUDY_CURATOR_KEYS.board(props.studentId), data)
    showToast(v.unlocked ? 'Dars istisno sifatida ochildi' : 'Istisno bekor qilindi', 'success')
    unlockFor.value = null
    unlockReason.value = ''
    refresh()
  },
  onError: (error) => showToast(toUserMessage(error), 'error'),
})

/* ------------------------------------------------------------ baholash --- */

const grading = ref<{ submission: SubmissionDto, maxScore: number } | null>(null)
const opening = ref<number | null>(null)

async function openGrade(row: LessonBoardRowDto): Promise<void> {
  const a = row.assignment
  if (a?.submissionId == null) return
  opening.value = row.lessonId
  try {
    const all = await fetchSubmissions(a.assignmentId)
    const mine = all.find((s) => s.id === a.submissionId)
    if (mine === undefined) {
      showToast('Javob topilmadi — sahifani yangilang', 'error')
      return
    }
    grading.value = { submission: mine, maxScore: a.maxScore }
  } catch (error) {
    showToast(toUserMessage(error), 'error')
  } finally {
    opening.value = null
  }
}

function onGraded(): void {
  grading.value = null
  refresh()
}

/* ------------------------------------------------------------ ko'rinish --- */

function stepTone(has: boolean, done: boolean): string {
  if (!has) return 'text-slate-600'
  return done ? 'text-emerald-300' : 'text-amber-300'
}

function masteryBadge(row: LessonBoardRowDto): { tone: 'success' | 'warning' | 'danger' | 'neutral', text: string } | null {
  if (row.mastery === 'Mastered') return { tone: 'success', text: 'O‘zlashtirdi' }
  if (row.mastery === 'NeedsWork') return { tone: 'danger', text: 'Qayta ishlasin' }
  if (row.provisional === true) return { tone: 'neutral', text: 'Vaqtincha ochiq' }
  if (row.unlocked && row.workDone) return { tone: 'warning', text: 'Qaror kutilmoqda' }
  return null
}
</script>

<template>
  <div class="space-y-4">
    <DataStatus
      :pending="boardQuery.isPending.value"
      :error="boardQuery.isError.value ? toUserMessage(boardQuery.error.value) : null"
      :empty="boardQuery.isSuccess.value && lessons.length === 0"
      :retrying="boardQuery.isFetching.value"
      empty-icon="book"
      empty-title="Darslar yo‘q"
      empty-text="O‘quvchining obunasidagi kursda hali dars yo‘q."
      @retry="boardQuery.refetch()"
    />

    <BaseCard
      v-if="board !== null"
      class="flex flex-wrap items-center gap-x-6 gap-y-2 p-4"
    >
      <div class="min-w-0 flex-1">
        <p class="truncate text-base font-semibold text-slate-100">
          {{ board.studentName }}
        </p>
        <p class="text-xs text-slate-400">
          {{ board.courseName ?? 'Kurs' }} · {{ lessons.filter((l) => l.mastery === 'Mastered').length }}/{{ lessons.length }} dars o‘zlashtirilgan
        </p>
      </div>
      <BaseBadge
        v-if="!board.subscriptionActive"
        tone="neutral"
      >
        Obuna tugagan
      </BaseBadge>
      <BaseBadge
        v-if="awaiting > 0"
        tone="warning"
      >
        {{ awaiting }} ta dars qaroringizni kutmoqda
      </BaseBadge>
    </BaseCard>

    <ol class="space-y-2">
      <li
        v-for="row in visible"
        :key="row.lessonId"
        class="rounded-xl border bg-ink-900 p-4"
        :class="row.unlocked && row.workDone && row.mastery !== 'Mastered' ? 'border-amber-500/40' : 'border-line'"
      >
        <div class="flex flex-wrap items-start gap-3">
          <span
            class="grid size-8 shrink-0 place-items-center rounded-full text-xs font-bold"
            :class="row.unlocked ? 'bg-brand-500/15 text-brand-500' : 'bg-ink-800 text-slate-500'"
          >{{ row.index + 1 }}</span>
          <div class="min-w-0 flex-1">
            <p class="text-sm font-semibold text-slate-100">
              {{ row.lessonName }}
            </p>
            <p class="text-[11px] text-dim">
              {{ row.moduleName }}
              <template v-if="!row.unlocked && row.lockReason !== null">
                · {{ lessonLockReasonLabel(row.lockReason) }}
              </template>
              <template v-if="row.unlockedOverride">
                · admin istisnosi<template v-if="row.overrideReason !== null">
                  : {{ row.overrideReason }}
                </template>
              </template>
            </p>

            <!-- Bajarilgan ishlar -->
            <div class="mt-2 flex flex-wrap gap-x-4 gap-y-1 text-xs">
              <span
                v-if="row.hasVideo"
                class="inline-flex items-center gap-1"
                :class="stepTone(row.hasVideo, row.videoWatched)"
              >
                <AppIcon
                  :name="row.videoWatched ? 'check' : 'clock'"
                  :size="12"
                />
                Video
              </span>
              <span
                v-if="row.hasAssignment"
                class="inline-flex items-center gap-1"
                :class="stepTone(row.hasAssignment, row.assignment?.submissionId != null)"
              >
                <AppIcon
                  :name="row.assignment?.submissionId != null ? 'check' : 'clock'"
                  :size="12"
                />
                Vazifa
                <template v-if="row.assignment?.submissionStatus === 'Graded'">
                  · {{ row.assignment.score }}/{{ row.assignment.maxScore }}
                </template>
                <template v-else-if="row.assignment?.submissionStatus === 'Submitted'">
                  · tekshirilmagan
                </template>
              </span>
              <span
                v-if="row.hasTest"
                class="inline-flex items-center gap-1"
                :class="stepTone(row.hasTest, row.testTaken)"
              >
                <AppIcon
                  :name="row.testTaken ? 'check' : 'clock'"
                  :size="12"
                />
                Test<template v-if="row.testPercent !== null"> · {{ row.testPercent }}%</template>
              </span>
              <span
                v-if="!row.hasVideo && !row.hasAssignment && !row.hasTest"
                class="text-slate-500"
              >Kontentsiz dars</span>
            </div>

            <p
              v-if="row.mastery !== null && (row.masteryNote !== null || row.decidedByName !== null)"
              class="mt-2 rounded-lg bg-ink-850 px-3 py-2 text-xs text-slate-300"
            >
              <template v-if="row.masteryNote !== null">
                «{{ row.masteryNote }}»
              </template>
              <span class="text-dim">
                — {{ row.decidedByName ?? 'kurator' }}<template v-if="row.decidedAt !== null">, {{ formatDate(row.decidedAt) }}</template>
              </span>
            </p>
          </div>

          <BaseBadge
            v-if="masteryBadge(row) !== null"
            :tone="masteryBadge(row)!.tone"
            size="xs"
          >
            {{ masteryBadge(row)!.text }}
          </BaseBadge>
        </div>

        <!-- Amallar -->
        <div
          v-if="row.unlocked && board?.canDecide && row.provisional !== true"
          class="mt-3 flex flex-wrap items-center gap-2 border-t border-line pt-3"
        >
          <BaseButton
            v-if="row.assignment?.submissionId != null"
            size="sm"
            variant="secondary"
            :loading="opening === row.lessonId"
            @click="openGrade(row)"
          >
            {{ row.assignment.submissionStatus === 'Graded' ? 'Bahoni ko‘rish' : 'Baholash' }}
          </BaseButton>
          <span class="flex-1" />
          <BaseButton
            size="sm"
            variant="ghost"
            :disabled="decide.isPending.value"
            @click="openNeedsWork(row)"
          >
            Qayta ishlasin
          </BaseButton>
          <BaseButton
            size="sm"
            :disabled="!row.workDone || row.mastery === 'Mastered'"
            :loading="decide.isPending.value && decide.variables.value?.lessonId === row.lessonId && decide.variables.value?.status === 'Mastered'"
            @click="markMastered(row)"
          >
            <AppIcon
              name="check"
              :size="14"
            />
            O‘zlashtirdi
          </BaseButton>
        </div>

        <p
          v-if="row.provisional === true"
          class="mt-2 text-[11px] text-dim"
        >
          Oldingi dars testi 100% bo‘lgani uchun kurator qarorisiz ochildi. Avval oldingi darsga qaror chiqaring:
          «qayta ishlasin» desangiz, bu dars yana yopiladi.
        </p>

        <p
          v-else-if="row.unlocked && board?.canDecide && !row.workDone"
          class="mt-2 text-[11px] text-dim"
        >
          «O‘zlashtirdi» o‘quvchi video, vazifa va testni bajargach faollashadi.
        </p>

        <form
          v-if="noteFor === row.lessonId"
          class="mt-3 space-y-2"
          @submit.prevent="submitNeedsWork(row)"
        >
          <textarea
            v-model="note"
            rows="3"
            maxlength="1000"
            required
            placeholder="Nimani qayta ishlashi kerak? O‘quvchi shu izohni ko‘radi."
            class="w-full rounded-lg bg-ink-800 px-3 py-2 text-sm text-slate-100 ring-1 ring-inset ring-line"
          />
          <div class="flex justify-end gap-2">
            <BaseButton
              size="sm"
              variant="ghost"
              type="button"
              @click="noteFor = null"
            >
              Bekor
            </BaseButton>
            <BaseButton
              size="sm"
              variant="danger"
              type="submit"
              :disabled="note.trim().length === 0"
              :loading="decide.isPending.value"
            >
              Qayta ishlashga qaytarish
            </BaseButton>
          </div>
        </form>

        <!-- Administrator istisnosi -->
        <div
          v-if="board?.canOverride && (!row.unlocked || row.unlockedOverride)"
          class="mt-3 border-t border-line pt-3"
        >
          <button
            v-if="unlockFor !== row.lessonId"
            type="button"
            class="text-xs text-slate-400 underline-offset-2 hover:text-slate-200 hover:underline"
            @click="unlockFor = row.lessonId; unlockReason = ''"
          >
            {{ row.unlockedOverride ? 'Istisnoni bekor qilish' : 'Istisno sifatida ochish (faqat admin)' }}
          </button>
          <form
            v-else
            class="space-y-2"
            @submit.prevent="unlock.mutate({ lessonId: row.lessonId, unlocked: !row.unlockedOverride, reason: unlockReason.trim() || null })"
          >
            <input
              v-model="unlockReason"
              maxlength="1000"
              :required="!row.unlockedOverride"
              :placeholder="row.unlockedOverride ? 'Sabab (ixtiyoriy)' : 'Sabab — jurnalda saqlanadi (masalan: kasallik, ma’lumotnoma bor)'"
              class="w-full rounded-lg bg-ink-800 px-3 py-2 text-sm text-slate-100 ring-1 ring-inset ring-line"
            >
            <div class="flex justify-end gap-2">
              <BaseButton
                size="sm"
                variant="ghost"
                type="button"
                @click="unlockFor = null"
              >
                Bekor
              </BaseButton>
              <BaseButton
                size="sm"
                variant="warning"
                type="submit"
                :disabled="!row.unlockedOverride && unlockReason.trim().length < 5"
                :loading="unlock.isPending.value"
              >
                {{ row.unlockedOverride ? 'Bekor qilish' : 'Ochish' }}
              </BaseButton>
            </div>
          </form>
        </div>
      </li>
    </ol>

    <button
      v-if="hiddenCount > 0 || showLocked"
      type="button"
      class="text-xs text-slate-400 hover:text-slate-200"
      @click="showLocked = !showLocked"
    >
      {{ showLocked ? 'Yopiq darslarni yig‘ish' : `Yana ${hiddenCount} ta yopiq darsni ko‘rsatish` }}
    </button>

    <!-- Jurnal -->
    <details
      v-if="(eventsQuery.data.value ?? []).length > 0"
      class="rounded-xl border border-line bg-ink-900 p-4 text-xs text-slate-300"
    >
      <summary class="cursor-pointer select-none text-sm font-semibold text-slate-200">
        Qarorlar jurnali
      </summary>
      <ul class="mt-3 space-y-1.5">
        <li
          v-for="e in eventsQuery.data.value"
          :key="e.id"
        >
          <span class="text-dim">{{ formatDateTime(e.at) }}</span>
          · <b>{{ MASTERY_EVENT_LABELS[e.kind] }}</b> · {{ e.lessonName }} · {{ e.actorName }}
          <template v-if="e.note !== null">
            — «{{ e.note }}»
          </template>
        </li>
      </ul>
    </details>

    <GradeDialog
      :submission="grading?.submission ?? null"
      :max-score="grading?.maxScore ?? 0"
      @close="grading = null"
      @graded="onGraded"
    />
  </div>
</template>
