<script setup lang="ts">
import { useMutation } from '@tanstack/vue-query'
import { computed, ref, watch } from 'vue'

import { gradeSubmission } from '@/entities/assignment'
import { toUserMessage } from '@/shared/api'
import { formatDateTime } from '@/shared/lib/datetime'
import type { SubmissionDto } from '@/shared/types'
import { BaseButton, BaseField, BaseModal } from '@/shared/ui'

/**
 * Ish baholash oynasi.
 *
 * Ball QAT'IY tekshiriladi (0..maxScore): serverdan 400 qaytishini kutish
 * o'rniga xatoni darhol ko'rsatamiz — ustoz navbatda o'nlab ishni baholaydi
 * va har safar server javobini kutish sekin.
 */
const props = defineProps<{
  submission: SubmissionDto | null
  maxScore: number
}>()

const emit = defineEmits<{ close: []; graded: [] }>()

const score = ref('')
const feedback = ref('')
const errorMessage = ref<string | null>(null)

// Oyna boshqa ish uchun qayta ochilganda maydonlar eski qiymatda qolmasin.
watch(
  () => props.submission,
  (submission) => {
    score.value = submission !== null && submission.score !== null ? String(submission.score) : ''
    feedback.value = submission?.feedback ?? ''
    errorMessage.value = null
  },
  { immediate: true },
)

const mutation = useMutation({
  mutationFn: (payload: { id: number; score: number; feedback: string }) =>
    gradeSubmission(payload.id, {
      score: payload.score,
      feedback: payload.feedback.length > 0 ? payload.feedback : null,
    }),
  onSuccess: () => emit('graded'),
  onError: (error: Error) => {
    errorMessage.value = toUserMessage(error)
  },
})

// Vergul bilan yozilgan ball ("4,5") ham qabul qilinsin — o'zbek klaviaturasida odatiy.
const parsedScore = computed(() => Number(score.value.replace(',', '.')))
const scoreError = computed<string | null>(() => {
  if (score.value.trim().length === 0) return null
  if (!Number.isFinite(parsedScore.value)) return 'Ball raqam bo‘lishi kerak.'
  if (parsedScore.value < 0) return 'Ball manfiy bo‘lmaydi.'
  if (parsedScore.value > props.maxScore) return `Maksimal ball: ${props.maxScore}.`
  return null
})

const canSubmit = computed(
  () => score.value.trim().length > 0 && scoreError.value === null && !mutation.isPending.value,
)

function handleSubmit(): void {
  const submission = props.submission
  if (submission === null || !canSubmit.value) return
  errorMessage.value = null
  mutation.mutate({ id: submission.id, score: parsedScore.value, feedback: feedback.value.trim() })
}
</script>

<template>
  <BaseModal
    :open="props.submission !== null"
    title="Ishni baholash"
    @close="emit('close')"
  >
    <template v-if="props.submission !== null">
      <div class="mb-4 rounded-lg border border-line bg-ink-950 p-3">
        <p
          class="text-sm font-semibold text-slate-100"
          v-text="props.submission.studentName ?? '—'"
        />
        <p class="mt-0.5 text-xs text-slate-400">
          {{ formatDateTime(props.submission.submittedAt) }} ·
          {{ props.submission.attemptNumber }}-urinish
          <span
            v-if="props.submission.isLate"
            class="text-amber-400"
          > · kechikkan</span>
        </p>
      </div>

      <div
        v-if="props.submission.text !== null && props.submission.text.length > 0"
        class="mb-4 max-h-64 overflow-y-auto whitespace-pre-wrap rounded-lg border border-line bg-ink-950 p-3 text-sm text-slate-200 scrollbar-slim"
        v-text="props.submission.text"
      />
      <p
        v-else
        class="mb-4 text-xs text-dim"
      >
        Matnli javob yo‘q.
      </p>

      <p
        v-if="(props.submission.files ?? []).length > 0"
        class="mb-4 text-xs text-slate-400"
      >
        Ilova qilingan fayllar: {{ (props.submission.files ?? []).length }} ta
        <span class="text-dim">(fayl ko‘rish hali qo‘shilmagan)</span>
      </p>

      <form
        novalidate
        @submit.prevent="handleSubmit"
      >
        <BaseField
          :label="`Ball (0 – ${props.maxScore})`"
          :error="scoreError"
        >
          <input
            v-model="score"
            class="zn-input"
            inputmode="decimal"
            placeholder="0"
          >
        </BaseField>

        <div class="mt-3">
          <BaseField label="Izoh (ixtiyoriy)">
            <textarea
              v-model="feedback"
              class="zn-input min-h-24"
              rows="3"
            />
          </BaseField>
        </div>

        <p
          v-if="errorMessage !== null"
          class="mt-3 text-xs text-rose-400"
          role="alert"
          v-text="errorMessage"
        />
      </form>
    </template>

    <template #footer>
      <BaseButton
        variant="secondary"
        @click="emit('close')"
      >
        Bekor qilish
      </BaseButton>
      <BaseButton
        :disabled="!canSubmit"
        :loading="mutation.isPending.value"
        @click="handleSubmit"
      >
        Saqlash
      </BaseButton>
    </template>
  </BaseModal>
</template>
