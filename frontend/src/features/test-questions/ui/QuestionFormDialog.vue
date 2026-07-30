<script setup lang="ts">
import { useMutation } from '@tanstack/vue-query'
import { computed, ref, watch } from 'vue'

import {
  addTestQuestion,
  correctCount,
  MIN_OPTIONS,
  OPTION_BODY_MAX,
  QUESTION_BODY_MAX,
  updateTestQuestion,
  validateOptions,
} from '@/entities/test'
import type { QuestionOptionDraft } from '@/entities/test'
import { toUserMessage } from '@/shared/api'
import type { AuthoringQuestionDto, SaveOptionRequest, SaveQuestionRequest } from '@/shared/types'
import { AppIcon, BaseButton, BaseField, BaseModal } from '@/shared/ui'

/**
 * Savol + variantlar formasi.
 *
 * ★★ VARIANTLAR — TO'LIQ ALMASHTIRISH ★★
 * `PUT /tests/{id}/questions/{questionId}` serverda variantlarni BUTUNLAY
 * o'chirib, yuborilgan ro'yxatdan qaytadan yozadi (`TestService`:
 * "VARIANTLAR BUTUNLAY ALMASHTIRILADI"). Ya'ni forma mavjud variantlarni
 * yuklab, HAMMASINI qaytarishi shart — bittasini yubormaslik uni o'chirish
 * bilan barobar. `imageKey` ham shu sababli UI'da tahrirlanmasa ham
 * qaytariladi (aks holda savol rasmi birinchi tahrirlashdayoq yo'qolardi).
 *
 * ★ KO'P TO'G'RI JAVOB: bir nechta variantni "to'g'ri" deb belgilash MUMKIN
 * va bu ATAYLAB — eski tizimda faqat oxirgi to'g'ri variant hisoblanardi.
 * Baholash "hammasi yoki hech nima" (`TestQuestion.Score`), shuning uchun
 * formada bu qoida oshkora yozilgan.
 */
const props = defineProps<{
  open: boolean
  testId: number
  /** `null` — yangi savol rejimi. */
  question: AuthoringQuestionDto | null
}>()

const emit = defineEmits<{ close: []; saved: [] }>()

const isEdit = computed(() => props.question !== null)

/** Ko'p variantli savolda 8 tadan ortig'i telefon ekranida o'qilmaydi. */
const MAX_OPTIONS = 8

const body = ref('')
const pointsText = ref('1')
const options = ref<QuestionOptionDraft[]>([])
const errorMessage = ref<string | null>(null)

/**
 * UI'da tahrirlanmaydi (rasm yuklash endpointi hali yo'q), lekin saqlashda
 * QAYTARILADI — yuqoridagi izohga qarang.
 */
const imageKey = ref<string | null>(null)

function emptyOptions(): QuestionOptionDraft[] {
  return Array.from({ length: MIN_OPTIONS }, () => ({ body: '', isCorrect: false }))
}

function resetForm(): void {
  const question = props.question
  body.value = question?.body ?? ''
  pointsText.value = question !== null ? String(question.points) : '1'
  imageKey.value = question?.imageKey ?? null

  const existing = question?.options ?? []
  options.value =
    existing.length > 0
      ? existing.map((option) => ({ body: option.body ?? '', isCorrect: option.isCorrect }))
      : emptyOptions()

  errorMessage.value = null
}

watch(() => [props.open, props.question], resetForm, { immediate: true })

function addOption(): void {
  if (options.value.length >= MAX_OPTIONS) return
  options.value = [...options.value, { body: '', isCorrect: false }]
}

function removeOption(index: number): void {
  if (options.value.length <= MIN_OPTIONS) return
  options.value = options.value.filter((_option, position) => position !== index)
}

function toggleCorrect(index: number): void {
  options.value = options.value.map((option, position) =>
    position === index ? { ...option, isCorrect: !option.isCorrect } : option,
  )
}

/* ---------------------------------------------------------- tekshiruvlar */

const trimmedBody = computed(() => body.value.trim())

const bodyError = computed<string | null>(() => {
  if (trimmedBody.value.length > QUESTION_BODY_MAX) {
    return `Savol matni ${QUESTION_BODY_MAX} belgidan oshmasin.`
  }
  return null
})

// Vergul bilan yozilgan ball ("0,5") ham qabul qilinsin — o'zbek
// klaviaturasida odatiy (`GradeDialog`, `AssignmentFormDialog` da ham shunday).
const parsedPoints = computed(() => Number(pointsText.value.replace(',', '.')))

const pointsError = computed<string | null>(() => {
  if (pointsText.value.trim().length === 0) return 'Ball kiritilishi kerak.'
  if (!Number.isFinite(parsedPoints.value)) return 'Ball raqam bo‘lishi kerak.'
  if (parsedPoints.value <= 0) return 'Ball noldan katta bo‘lishi kerak.'
  return null
})

const optionsError = computed(() => validateOptions(options.value))

const correctSelected = computed(() => correctCount(options.value))

/* ------------------------------------------------------------- saqlash */

function payload(): SaveQuestionRequest {
  const filled: SaveOptionRequest[] = options.value
    // Bo'sh qatorlar TASHLAB YUBORILADI: foydalanuvchi "yana variant" bosib
    // to'ldirmasa, server bo'sh matnli variantni 409 bilan rad etardi.
    .filter((option) => option.body.trim().length > 0)
    .map((option, index) => ({
      body: option.body.trim(),
      isCorrect: option.isCorrect,
      // Tartib ro'yxatdagi joyi bo'yicha — server `null` da ham shunday
      // qiladi, lekin oshkora yuborish niyatni aniq qiladi.
      position: index,
    }))

  return {
    body: trimmedBody.value,
    options: filled,
    points: parsedPoints.value,
    // Tahrirlashda mavjud tartib SAQLANADI, yaratishda server oxiriga qo'shadi.
    position: props.question?.position ?? null,
    imageKey: imageKey.value,
  }
}

const createMutation = useMutation({
  mutationFn: () => addTestQuestion(props.testId, payload()),
  onSuccess: () => {
    emit('saved')
    emit('close')
  },
  onError: (error: Error) => {
    // 409 — Domain qoidasi (kamida 2 variant, kamida 1 to'g'ri, ball > 0)
    // YOKI "o'quvchilar yechishni boshlagan — o'zgartirib bo'lmaydi".
    errorMessage.value = toUserMessage(error)
  },
})

const updateMutation = useMutation({
  mutationFn: (questionId: number) => updateTestQuestion(props.testId, questionId, payload()),
  onSuccess: () => {
    emit('saved')
    emit('close')
  },
  onError: (error: Error) => {
    errorMessage.value = toUserMessage(error)
  },
})

const isPending = computed(() => createMutation.isPending.value || updateMutation.isPending.value)

const canSubmit = computed(
  () =>
    trimmedBody.value.length > 0 &&
    bodyError.value === null &&
    pointsError.value === null &&
    optionsError.value === null &&
    !isPending.value,
)

function handleSubmit(): void {
  if (!canSubmit.value) return
  errorMessage.value = null
  const question = props.question
  if (question !== null) updateMutation.mutate(question.id)
  else createMutation.mutate()
}
</script>

<template>
  <BaseModal
    :open="props.open"
    :title="isEdit ? 'Savolni tahrirlash' : 'Yangi savol'"
    wide
    @close="emit('close')"
  >
    <form
      novalidate
      @submit.prevent="handleSubmit"
    >
      <BaseField
        label="Savol matni"
        :error="bodyError"
      >
        <textarea
          v-model="body"
          class="zn-input min-h-24 resize-y"
          rows="3"
          required
          placeholder="Savolni yozing"
        />
      </BaseField>

      <div class="mt-3 sm:max-w-40">
        <BaseField
          label="Ball"
          :error="pointsError"
        >
          <input
            v-model="pointsText"
            class="zn-input"
            inputmode="decimal"
            placeholder="1"
          >
        </BaseField>
      </div>

      <div class="mt-4">
        <div class="mb-1.5 flex flex-wrap items-center justify-between gap-2">
          <span class="text-xs font-medium text-slate-400">
            Variantlar (to‘g‘risini belgilang)
          </span>
          <span
            v-if="correctSelected > 1"
            class="rounded-full bg-sky-500/15 px-2 py-0.5 text-[11px] font-semibold text-sky-300"
          >
            Ko‘p javobli savol
          </span>
        </div>

        <!--
          ★ Bir nechta to'g'ri variant belgilansa savol AVTOMATIK ko'p javobli
          bo'ladi va o'quvchida checkbox ko'rinadi. Baholash qoidasi shu yerda
          aytiladi — xodim uni bilmasdan "qisman ball beriladi" deb o'ylardi.
        -->
        <p class="mb-2 text-[11px] leading-relaxed text-dim">
          Bir nechta variant belgilansa, savol ko‘p javobli bo‘ladi: o‘quvchi
          BARCHA to‘g‘ri variantni tanlashi kerak, qisman ball berilmaydi.
        </p>

        <ul class="space-y-2">
          <li
            v-for="(option, index) in options"
            :key="index"
            class="flex items-start gap-2"
          >
            <button
              type="button"
              class="tap-target mt-px flex shrink-0 items-center justify-center rounded-lg border transition-colors"
              :class="
                option.isCorrect
                  ? 'border-green-500/40 bg-green-500/15 text-green-400'
                  : 'border-line bg-ink-800 text-slate-500 hover:bg-ink-750'
              "
              :aria-pressed="option.isCorrect"
              :title="option.isCorrect ? 'To‘g‘ri variant' : 'To‘g‘ri deb belgilash'"
              @click="toggleCorrect(index)"
            >
              <AppIcon
                name="check"
                :size="16"
              />
            </button>

            <input
              v-model="option.body"
              class="zn-input"
              :maxlength="OPTION_BODY_MAX"
              :placeholder="`${index + 1}-variant`"
            >

            <button
              type="button"
              class="tap-target mt-px flex shrink-0 items-center justify-center rounded-lg text-slate-500 transition-colors hover:bg-rose-500/10 hover:text-rose-300 disabled:opacity-30"
              :disabled="options.length <= MIN_OPTIONS"
              title="Variantni olib tashlash"
              @click="removeOption(index)"
            >
              <AppIcon
                name="trash"
                :size="15"
              />
            </button>
          </li>
        </ul>

        <p
          v-if="optionsError !== null"
          class="mt-1.5 text-[11px] text-rose-400"
          v-text="optionsError"
        />

        <BaseButton
          class="mt-2"
          size="sm"
          variant="ghost"
          :disabled="options.length >= MAX_OPTIONS"
          @click="addOption"
        >
          <template #icon>
            <AppIcon
              name="plus"
              :size="14"
            />
          </template>
          Variant qo‘shish
        </BaseButton>
      </div>

      <p
        v-if="imageKey !== null"
        class="mt-3 text-[11px] text-dim"
      >
        Savolga rasm biriktirilgan — u saqlashda o‘zgarishsiz qoladi.
      </p>

      <p
        v-if="errorMessage !== null"
        class="mt-3 text-xs text-rose-400"
        role="alert"
        v-text="errorMessage"
      />
    </form>

    <template #footer>
      <BaseButton
        variant="secondary"
        @click="emit('close')"
      >
        Bekor qilish
      </BaseButton>
      <BaseButton
        :disabled="!canSubmit"
        :loading="isPending"
        @click="handleSubmit"
      >
        {{ isEdit ? 'Saqlash' : 'Qo‘shish' }}
      </BaseButton>
    </template>
  </BaseModal>
</template>
