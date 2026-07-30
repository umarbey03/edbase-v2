<script setup lang="ts">
import { useMutation } from '@tanstack/vue-query'
import { computed, ref, watch } from 'vue'

import {
  ANSWER_FORMAT_OPTIONS,
  createAssignment,
  parseAnswerFormats,
  serializeAnswerFormats,
  updateAssignment,
} from '@/entities/assignment'
import type { AnswerFormatName } from '@/entities/assignment'
import { toUserMessage } from '@/shared/api'
import { fromDateTimeLocalInput, toDateTimeLocalInput } from '@/shared/lib/datetime'
import type { AssignmentDto, CreateAssignmentRequest, UpdateAssignmentRequest } from '@/shared/types'
import { BaseButton, BaseField, BaseModal } from '@/shared/ui'

import { emptyTarget, isTargetChosen, targetLabel } from '../model/target'
import type { AssignmentTarget } from '../model/target'
import AssignmentTargetPicker from './AssignmentTargetPicker.vue'

/**
 * Uy vazifasini yaratish/tahrirlash.
 *
 * ★★ TAHRIRLASH — TO'LIQ ALMASHTIRISH ★★
 * `PUT /assignments/{id}` C# DTO'sida ixtiyoriy maydonlar `= null` standart
 * qiymatga ega va servis ularni to'g'ridan-to'g'ri yozadi. Ya'ni FORMADA
 * KO'RSATILMAGAN maydon serverda JIMGINA o'chadi. Shuning uchun:
 *   • forma ochilganda MAVJUD qiymatlar to'liq yuklanadi;
 *   • saqlashda HAMMASI qaytariladi, jumladan UI'da tahrirlanmaydigan
 *     `imageKey` ham (uni yuborishni unutsak, vazifaning shart rasmi
 *     bir marta tahrirlashdan keyin yo'qolardi).
 * Aynan shu tuzoq bugun guruh formasida kursni o'chirib yuborgan edi.
 *
 * NISHON (guruh/dars) faqat YARATISHDA tanlanadi — server uni tahrirlashda
 * o'zgartirmaydi (mavjud javoblar begona vazifaga tegib qolardi).
 */
const props = defineProps<{
  open: boolean
  /** `null` — yangi vazifa rejimi. */
  assignment: AssignmentDto | null
  /** Kurs darsiga biriktirish mumkinmi (faqat o'quv bo'limi/admin). */
  allowCourseTarget: boolean
}>()

const emit = defineEmits<{ close: []; saved: [] }>()

const isEdit = computed(() => props.assignment !== null)

const title = ref('')
const description = ref('')
const maxScoreText = ref('5')
const dueLocal = ref('')
const formats = ref<AnswerFormatName[]>(['Text', 'Image'])
const target = ref<AssignmentTarget>(emptyTarget())
const errorMessage = ref<string | null>(null)

/**
 * Vazifa shartlari rasmining ombor kaliti. UI'da TAHRIRLANMAYDI (rasm yuklash
 * endpointi hali yo'q), lekin `PUT` da qaytarilishi SHART — aks holda
 * `curl` bilan biriktirilgan rasm birinchi tahrirlashdayoq yo'qolardi.
 */
const imageKey = ref<string | null>(null)

function resetForm(): void {
  const assignment = props.assignment
  title.value = assignment?.title ?? ''
  description.value = assignment?.description ?? ''
  maxScoreText.value = assignment !== null ? String(assignment.maxScore) : '5'
  dueLocal.value = toDateTimeLocalInput(assignment?.dueAt ?? null)
  formats.value =
    assignment !== null ? parseAnswerFormats(assignment.allowedFormats) : ['Text', 'Image']
  imageKey.value = assignment?.imageKey ?? null
  target.value = emptyTarget()
  errorMessage.value = null
}

watch(() => [props.open, props.assignment], resetForm, { immediate: true })

function toggleFormat(value: AnswerFormatName): void {
  formats.value = formats.value.includes(value)
    ? formats.value.filter((item) => item !== value)
    : [...formats.value, value]
}

/* ---------------------------------------------------------- tekshiruvlar */

/** Server: `Assignment.MaxTitleLength`. */
const MAX_TITLE = 200
/** Server: `Assignment.MaxDescriptionLength`. */
const MAX_DESCRIPTION = 4000

const trimmedTitle = computed(() => title.value.trim())

const titleError = computed<string | null>(() => {
  if (trimmedTitle.value.length > MAX_TITLE) return `Sarlavha ${MAX_TITLE} belgidan oshmasin.`
  return null
})

const descriptionError = computed<string | null>(() =>
  description.value.trim().length > MAX_DESCRIPTION
    ? `Tavsif ${MAX_DESCRIPTION} belgidan oshmasin.`
    : null,
)

// Vergul bilan yozilgan ball ("4,5") ham qabul qilinsin — o'zbek
// klaviaturasida odatiy (`GradeDialog` da ham shunday).
const parsedMaxScore = computed(() => Number(maxScoreText.value.replace(',', '.')))

const maxScoreError = computed<string | null>(() => {
  if (maxScoreText.value.trim().length === 0) return 'Maksimal ball kiritilishi kerak.'
  if (!Number.isFinite(parsedMaxScore.value)) return 'Ball raqam bo‘lishi kerak.'
  if (parsedMaxScore.value <= 0) return 'Ball noldan katta bo‘lishi kerak.'
  return null
})

const formatError = computed<string | null>(() =>
  formats.value.length === 0 ? 'Kamida bitta javob formati tanlanishi kerak.' : null,
)

const targetError = computed<string | null>(() => {
  if (isEdit.value) return null
  if (isTargetChosen(target.value)) return null
  return target.value.kind === 'group' ? 'Guruhni tanlang.' : 'Darsni tanlang.'
})

/* ------------------------------------------------------------- saqlash */

function commonFields(): UpdateAssignmentRequest {
  const text = description.value.trim()
  return {
    title: trimmedTitle.value,
    // Bo'sh matn `null` sifatida ketadi — bazada bo'sh satr saqlanmasin.
    description: text.length > 0 ? text : null,
    maxScore: parsedMaxScore.value,
    dueAt: fromDateTimeLocalInput(dueLocal.value),
    allowedFormats: serializeAnswerFormats(formats.value),
    // ★ Tahrirlanmaydigan, lekin QAYTARILADIGAN maydon (yuqoridagi izoh).
    imageKey: imageKey.value,
  }
}

function buildCreatePayload(): CreateAssignmentRequest {
  const chosen = target.value
  return {
    ...commonFields(),
    // Server "YOKI guruh, YOKI dars" ni talab qiladi — ikkinchisi doim `null`.
    groupId: chosen.kind === 'group' ? chosen.groupId : null,
    moduleLessonId: chosen.kind === 'lesson' ? chosen.lessonId : null,
  }
}

const createMutation = useMutation({
  mutationFn: () => createAssignment(buildCreatePayload()),
  onSuccess: () => {
    emit('saved')
    emit('close')
  },
  onError: (error: Error) => {
    // 403 — begona guruh yoki ustozning kurs vazifasiga urinishi;
    // 409 — Domain qoidasi (sarlavha, ball, format, nishon).
    errorMessage.value = toUserMessage(error)
  },
})

const updateMutation = useMutation({
  mutationFn: (id: number) => updateAssignment(id, commonFields()),
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
    trimmedTitle.value.length > 0 &&
    titleError.value === null &&
    descriptionError.value === null &&
    maxScoreError.value === null &&
    formatError.value === null &&
    targetError.value === null &&
    !isPending.value,
)

function handleSubmit(): void {
  if (!canSubmit.value) return
  errorMessage.value = null
  const assignment = props.assignment
  if (assignment !== null) updateMutation.mutate(assignment.id)
  else createMutation.mutate()
}
</script>

<template>
  <BaseModal
    :open="props.open"
    :title="isEdit ? 'Vazifani tahrirlash' : 'Yangi vazifa'"
    @close="emit('close')"
  >
    <form
      novalidate
      @submit.prevent="handleSubmit"
    >
      <!-- NISHON: yaratishda tanlanadi, tahrirlashda faqat ko'rsatiladi. -->
      <template v-if="!isEdit">
        <AssignmentTargetPicker
          v-model="target"
          :allow-course-target="props.allowCourseTarget"
          :enabled="props.open"
        />
        <p
          v-if="targetError !== null"
          class="mt-1 text-[11px] text-rose-400"
          v-text="targetError"
        />
        <hr class="my-4 border-line">
      </template>

      <div
        v-else-if="props.assignment !== null"
        class="mb-4 rounded-lg border border-line bg-ink-950 p-3"
      >
        <p
          class="text-xs font-medium text-slate-200"
          v-text="targetLabel(props.assignment)"
        />
        <p class="mt-1 text-[11px] leading-relaxed text-dim">
          Nishon o‘zgartirilmaydi: topshirilgan javoblar begona vazifaga tegib qolardi. Boshqa
          guruh yoki dars kerak bo‘lsa — yangi vazifa yarating.
        </p>
      </div>

      <BaseField
        label="Sarlavha"
        :error="titleError"
      >
        <input
          v-model="title"
          class="zn-input"
          required
          placeholder="Masalan: 3-dars uy vazifasi"
        >
      </BaseField>

      <div class="mt-3">
        <BaseField
          label="Shart (tavsif)"
          hint="Ixtiyoriy. O‘quvchi vazifa kartochkasida ko‘radi."
          :error="descriptionError"
        >
          <textarea
            v-model="description"
            class="zn-input min-h-24 resize-y"
            rows="3"
          />
        </BaseField>
      </div>

      <div class="mt-3 grid gap-3 sm:grid-cols-2">
        <BaseField
          label="Maksimal ball"
          :error="maxScoreError"
        >
          <input
            v-model="maxScoreText"
            class="zn-input"
            inputmode="decimal"
            placeholder="5"
          >
        </BaseField>

        <BaseField
          label="Topshirish muddati"
          hint="Bo‘sh bo‘lsa — muddatsiz."
        >
          <input
            v-model="dueLocal"
            class="zn-input"
            type="datetime-local"
          >
        </BaseField>
      </div>

      <p class="mt-1 text-[11px] leading-relaxed text-dim">
        Muddat o‘tgach javob RAD ETILMAYDI — u “kechikkan” deb belgilanadi va baholashda
        ko‘rinadi.
      </p>

      <div class="mt-3">
        <BaseField
          label="Qanday javob qabul qilinadi"
          :error="formatError"
        >
          <div class="mt-0.5 space-y-1.5">
            <label
              v-for="option in ANSWER_FORMAT_OPTIONS"
              :key="option.value"
              class="flex min-h-11 items-center gap-2.5 rounded-lg border border-line bg-ink-950 px-3 text-sm text-slate-200"
            >
              <input
                type="checkbox"
                class="size-4 accent-brand-500"
                :checked="formats.includes(option.value)"
                @change="toggleFormat(option.value)"
              >
              <span v-text="option.label" />
              <span
                class="text-[11px] text-dim"
                v-text="option.hint"
              />
            </label>
          </div>
        </BaseField>
      </div>

      <p
        v-if="imageKey !== null"
        class="mt-3 text-[11px] text-dim"
      >
        Vazifaga shart rasmi biriktirilgan — u saqlashda o‘zgarishsiz qoladi.
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
        {{ isEdit ? 'Saqlash' : 'Yaratish' }}
      </BaseButton>
    </template>
  </BaseModal>
</template>
