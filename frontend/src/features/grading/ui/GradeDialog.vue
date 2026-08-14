<script setup lang="ts">
import { useMutation } from '@tanstack/vue-query'
import { computed, ref, watch } from 'vue'

import {
  buildSubmissionFeedbackForm,
  deleteSubmissionFeedbackFile,
  gradeSubmission,
  submissionFeedbackUploadPath,
} from '@/entities/assignment'
import SubmissionAttachments from '@/entities/assignment/ui/SubmissionAttachments.vue'
import SubmissionFeedbackFiles from '@/entities/assignment/ui/SubmissionFeedbackFiles.vue'
import { uploadWithProgress } from '@/features/lesson-media'
import { toUserMessage } from '@/shared/api'
import { formatDateTime } from '@/shared/lib/datetime'
import type { SubmissionFeedbackFileDto } from '@/shared/types'
import type { SubmissionDto } from '@/shared/types'
import { AppIcon, BaseButton, BaseField, BaseModal, BaseSpinner } from '@/shared/ui'

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

/* ================================================================== R37 ==
   USTOZNING TEKSHIRUV FAYLLARI

   ★ BAHODAN MUSTAQIL: fayl `POST /submissions/{id}/feedback-files` orqali
   DARHOL ketadi, "Saqlash" tugmasini kutmaydi. Sabab amaliy — ustoz
   ko'pincha faylni qo'yib, ballni keyin o'ylaydi; ikkalasini bitta
   tugmaga bog'lash "rasmim ketdimi?" degan noaniqlik yaratardi.

   🔴 `gradeSubmission` (JSON) TEGILMADI: uni `multipart` ga o'tkazish
   HAR BIR mavjud chaqiruvni 415 ga olib borardi (backend
   `ISubmissionFeedbackFileService` izohida to'liq asoslash).
   ========================================================================= */

/**
 * Serverdan kelgan ro'yxatning MAHALLIY nusxasi.
 *
 * ★ NEGA nusxa: `props.submission` — ota komponentning ro'yxat kesh'idan
 * kelgan obyekt va u FAQAT `graded` hodisasidan keyin yangilanadi. Fayl
 * qo'shilishi esa bahodan mustaqil, ya'ni ro'yxat darhol o'zgarishi kerak.
 * Serverdan qaytgan DTO shu yerga qo'shiladi (`invalidateQueries` ni
 * kutmasdan) — aks holda ustoz yuklagan faylini ko'rmasdi va ikkinchi
 * marta yuklardi.
 */
const feedbackFiles = ref<SubmissionFeedbackFileDto[]>([])
const feedbackInput = ref<HTMLInputElement | null>(null)
const uploadPercent = ref<number | null>(null)
const feedbackError = ref<string | null>(null)
const deletingId = ref<number | null>(null)

/** Kattalashtirilgan rasm (izohi pastdagi `zoomUrl` blokida). */
const zoomUrl = ref<string | null>(null)

// Oyna boshqa ish uchun qayta ochilganda maydonlar eski qiymatda qolmasin.
watch(
  () => props.submission,
  (submission) => {
    score.value = submission !== null && submission.score !== null ? String(submission.score) : ''
    feedback.value = submission?.feedback ?? ''
    errorMessage.value = null
    feedbackFiles.value = [...(submission?.feedbackFiles ?? [])]
    feedbackError.value = null
    uploadPercent.value = null
    deletingId.value = null
    zoomUrl.value = null
  },
  { immediate: true },
)

function pickFeedbackFile(): void {
  feedbackInput.value?.click()
}

async function onFeedbackFileChosen(event: Event): Promise<void> {
  const input = event.target as HTMLInputElement
  const file = input.files?.[0]

  // Bir xil faylni qayta tanlash mumkin bo'lsin uchun DARHOL tozalanadi.
  input.value = ''

  const submission = props.submission
  if (file === undefined || submission === null) return

  feedbackError.value = null
  uploadPercent.value = 0

  try {
    const created = await uploadWithProgress<SubmissionFeedbackFileDto>({
      path: submissionFeedbackUploadPath(submission.id),
      form: buildSubmissionFeedbackForm(file),
      onProgress: (progress) => {
        uploadPercent.value = progress.percent
      },
    })

    feedbackFiles.value = [...feedbackFiles.value, created]
  } catch (error) {
    feedbackError.value = toUserMessage(error)
  } finally {
    uploadPercent.value = null
  }
}

async function removeFeedbackFile(fileId: number): Promise<void> {
  feedbackError.value = null
  deletingId.value = fileId

  try {
    await deleteSubmissionFeedbackFile(fileId)
    feedbackFiles.value = feedbackFiles.value.filter((item) => item.id !== fileId)
  } catch (error) {
    feedbackError.value = toUserMessage(error)
  } finally {
    deletingId.value = null
  }
}

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

      <div
        v-if="(props.submission.files ?? []).length > 0"
        class="mb-4"
      >
        <!--
          ★ `zoomable` ENDI YOQILGAN (R37).

          🔴 ILGARI `false` EDI va sabab quyidagicha yozilgan edi: "oyna
          ichida ikkinchi oyna ochilmaydi — ikkala `BaseModal` ham
          `document` da bitta ESC tinglovchisi qo'yadi va biri ikkinchisini
          to'sa olmaydi, ya'ni ESC IKKALA qatlamni birga yopardi".

          BU CHEKLOV ENDI KERAK EMAS: 2026-08-11 dagi refaktorda
          `BaseModal` ichki mexanikasi `useModalHost` ga ko'chirilgan va u
          QATLAM STEKINI yuritadi — ESC faqat eng TEPADAGI qatlamga tegadi
          (`useModalHost.ts`, `topLayer()`), skroll qulfi ham SANOQLI.
          Ya'ni muammoning sababi yo'qolgan, cheklovni saqlash esa
          talabning bir qismini ("tekshirishda rasmni katta ekranda ko'rish
          mumkin bo'lsin") bajarmay qoldirardi.

          ⚠️ QO'SHIMCHA HIMOYA KERAK EMAS: lightbox `BaseModal` ning O'ZI
          bo'lgani uchun u ham o'sha stekka tushadi.
        -->
        <SubmissionAttachments
          :files="props.submission.files ?? []"
          @zoom="(url) => (zoomUrl = url)"
        />
      </div>

      <!-- ===================== R37 · TEKSHIRUV FAYLLARI ===================== -->
      <div class="mb-4 rounded-lg border border-line bg-ink-950 p-3">
        <div class="mb-2 flex flex-wrap items-center justify-between gap-2">
          <h3 class="text-[11px] font-bold uppercase tracking-wide text-slate-400">
            Mening izohim uchun fayllar
          </h3>
          <!--
            `accept` — TAVSIYA, tekshiruv EMAS: haqiqiy tur serverda
            SEHRLI BAYTLARDAN aniqlanadi. PDF ATAYLAB ruxsat etilgan —
            ustozning sharhi ko'pincha shu (o'quvchining TOPSHIRISH yo'li
            esa avvalgidek faqat rasm/ovoz, u KENGAYTIRILMADI).
          -->
          <input
            ref="feedbackInput"
            class="hidden"
            type="file"
            accept="image/*,audio/*,application/pdf"
            @change="onFeedbackFileChosen"
          >
          <BaseButton
            size="sm"
            variant="secondary"
            :disabled="uploadPercent !== null"
            @click="pickFeedbackFile"
          >
            <template #icon>
              <AppIcon
                name="paperclip"
                :size="13"
              />
            </template>
            Fayl biriktirish
          </BaseButton>
        </div>

        <div
          v-if="uploadPercent !== null"
          class="mb-2 flex items-center gap-2 text-xs text-slate-400"
        >
          <BaseSpinner size="sm" />
          <span class="tabular-nums">Yuklanmoqda… {{ uploadPercent }}%</span>
        </div>

        <p
          v-if="feedbackError !== null"
          class="mb-2 text-xs text-rose-400"
          role="alert"
          v-text="feedbackError"
        />

        <SubmissionFeedbackFiles
          v-if="feedbackFiles.length > 0"
          :files="feedbackFiles"
          can-delete
          :deleting-id="deletingId"
          @zoom="(url) => (zoomUrl = url)"
          @remove="removeFeedbackFile"
        />
        <p
          v-else
          class="text-xs text-dim"
        >
          Rasm, ovoz yoki PDF biriktirsangiz o‘quvchi uni javobi bilan birga ko‘radi.
        </p>
      </div>

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

  <!--
    Kattalashtirilgan rasm. `GradingQueueOverlay` dagi AYNI naqsh
    (`BaseModal wide` + `max-h-[75dvh] object-contain`).

    ★ `BaseModal` DAN TASHQARIDA e'lon qilingan: ichida bo'lsa u ota
    oynaning skroll sohasiga tushardi. Ikkalasi ham `Teleport to="body"`
    qilgani uchun DOM'da baribir yonma-yon turadi, ESC esa
    `useModalHost` steki tufayli faqat TEPADAGISINI — ya'ni lightbox'ni —
    yopadi.
  -->
  <BaseModal
    :open="zoomUrl !== null"
    title="Rasm"
    wide
    @close="zoomUrl = null"
  >
    <img
      v-if="zoomUrl !== null"
      :src="zoomUrl"
      alt="Kattalashtirilgan rasm"
      class="mx-auto max-h-[75dvh] w-auto rounded-lg object-contain"
    >
  </BaseModal>
</template>
