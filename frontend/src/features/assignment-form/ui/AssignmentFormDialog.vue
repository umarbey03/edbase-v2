<script setup lang="ts">
import { useMutation } from '@tanstack/vue-query'
import { computed, ref, watch } from 'vue'

import { createAssignment, updateAssignment } from '@/entities/assignment'
import { toUserMessage } from '@/shared/api'
import { useConfirm } from '@/shared/lib/useConfirm'
import type { AssignmentAttachmentDto, AssignmentDto } from '@/shared/types'
import { BaseButton, BaseModal } from '@/shared/ui'

import {
  buildCreateRequest,
  buildUpdateRequest,
  changedAssignmentFields,
  createAssignmentFormState,
  isAssignmentFormValid,
  validateAssignmentForm,
} from '../model/assignment-form'
import type { AssignmentFormState } from '../model/assignment-form'
import { emptyTarget, isTargetChosen, targetLabel } from '../model/target'
import type { AssignmentTarget } from '../model/target'
import AssignmentAttachmentsSection from './AssignmentAttachmentsSection.vue'
import AssignmentFormFields from './AssignmentFormFields.vue'
import AssignmentTargetPicker from './AssignmentTargetPicker.vue'

/**
 * Uy vazifasini yaratish/tahrirlash — VAZIFALAR SAHIFASI va USTOZNING
 * BAHOLASH sahifasi uchun.
 *
 * ★★ TASHQI API O'ZGARMADI (`open`, `assignment`, `allowCourseTarget`,
 * `close`, `saved`): komponentni `ManageAssignmentsPage` va
 * `TeacherGradingPage` ishlatadi, ikkinchisi bu ishning qamrovidan
 * tashqarida. Bu FAQAT ichki refaktor.
 *
 * ★ MAYDONLAR VA TEKSHIRUV endi `AssignmentFormFields` +
 * `model/assignment-form.ts` da: AYNI forma dars drawer'ining "Uy vazifasi"
 * bo'limida ham ochiladi (`LessonAssignmentSection`) va ikki nusxa
 * saqlanmaydi — aks holda "kamida bitta javob formati" kabi qoida bir joyda
 * tuzatilib, ikkinchisida eski holida qolardi.
 *
 * ★★ TAHRIRLASH — TO'LIQ ALMASHTIRISH (`PUT`): forma HAMMA maydonni
 * qaytaradi, jumladan UI'da tahrirlanmaydigan `imageKey` ni ham
 * (`buildUpdateRequest` izohi).
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

const confirm = useConfirm()

const isEdit = computed(() => props.assignment !== null)

const form = ref<AssignmentFormState>(createAssignmentFormState(null))
const target = ref<AssignmentTarget>(emptyTarget())
const attachments = ref<AssignmentAttachmentDto[]>([])
const errorMessage = ref<string | null>(null)
const submitted = ref(false)

function resetForm(): void {
  form.value = createAssignmentFormState(props.assignment)
  target.value = emptyTarget()
  /*
    Biriktirmalar LOKAL holatda: ular alohida endpointlar bilan boshqariladi
    (yuklash/o'chirish DARHOL saqlanadi, `PUT` bilan emas). Ota komponent esa
    `assignment` prop'ini yangilamaydi — u ro'yxatdagi SURATNI uzatadi.
  */
  attachments.value = [...(props.assignment?.attachments ?? [])]
  errorMessage.value = null
  submitted.value = false
}

watch(() => [props.open, props.assignment], resetForm, { immediate: true })

const errors = computed(() => validateAssignmentForm(form.value))

/**
 * Biriktirma yuklandi yoki o'chirildi.
 *
 * `saved` DARHOL emit qilinadi (formani saqlashni kutmasdan): fayl serverda
 * ALLAQACHON o'zgardi va ro'yxatdagi kartochka eskirdi. Oyna esa OCHIQ
 * qoladi — foydalanuvchi yana fayl qo'shishi mumkin.
 */
function onAttachmentsChanged(next: AssignmentAttachmentDto[]): void {
  attachments.value = next
  emit('saved')
}

const targetError = computed<string | null>(() => {
  if (isEdit.value) return null
  if (isTargetChosen(target.value)) return null
  return target.value.kind === 'group' ? 'Guruhni tanlang.' : 'Darsni tanlang.'
})

/* ------------------------------------------------------------- saqlash */

const createMutation = useMutation({
  mutationFn: () => {
    const chosen = target.value
    return createAssignment(
      buildCreateRequest(form.value, {
        // Server "YOKI guruh, YOKI dars" ni talab qiladi — ikkinchisi doim `null`.
        groupId: chosen.kind === 'group' ? chosen.groupId : null,
        moduleLessonId: chosen.kind === 'lesson' ? chosen.lessonId : null,
      }),
    )
  },
  onSuccess: () => {
    emit('saved')
    emit('close')
  },
  onError: (error: Error) => {
    // 403 — begona guruh yoki ustozning kurs vazifasiga urinishi;
    // 400/409 — validatsiya va Domain qoidalari.
    errorMessage.value = toUserMessage(error)
  },
})

const updateMutation = useMutation({
  mutationFn: (id: number) => updateAssignment(id, buildUpdateRequest(form.value)),
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
  () => isAssignmentFormValid(errors.value) && targetError.value === null && !isPending.value,
)

/**
 * Saqlash.
 *
 * TASDIQ FAQAT TAHRIRLASHDA (B2 jadvali: "ma'lumotni ALMASHTIRUVCHI
 * saqlash → HAR DOIM, `primary`, o'zgargan maydonlar ro'yxati bilan").
 * Yaratishda tasdiq so'ralmaydi: yangi yozuv hech narsani almashtirmaydi va
 * xato bo'lsa uni o'chirish mumkin — har "Yaratish" tugmasiga oyna qo'yish
 * esa formani ikki qadamli qilib yuborardi.
 *
 * 🔴 TASDIQ `changes` BO'SH BO'LGANDA HAM SO'RALADI (2026-08-13 da tuzatildi).
 * Ilgari shart `changes.length > 0` edi va u ikki xil narsani chalkashtirardi:
 * "foydalanuvchi hech nima o'zgartirmadi" va "`changedAssignmentFields`
 * o'zgargan maydonni ko'ra olmadi". Ikkinchisida `PUT` TASDIQSIZ ketardi,
 * ya'ni diff funksiyasidagi bitta unutilgan maydon butun himoyani jimgina
 * o'chirib qo'yardi. `PUT` — TO'LIQ ALMASHTIRISH, shuning uchun tasdiq
 * diffga emas, AMALGA bog'lanadi; diff faqat `details` ni boyitadi.
 */
async function handleSubmit(): Promise<void> {
  submitted.value = true
  if (!canSubmit.value) return

  const assignment = props.assignment
  if (assignment === null) {
    errorMessage.value = null
    createMutation.mutate()
    return
  }

  const changes = changedAssignmentFields(assignment, form.value)
  const ok = await confirm({
    title: 'Vazifani saqlash',
    message:
      'Vazifa ma’lumotlari ALMASHTIRILADI. O‘quvchilar darhol yangi shartni '
      + 'ko‘radi (topshirilgan javoblar va baholar saqlanadi).',
    confirmLabel: 'Saqlash',
    tone: 'primary',
    details:
      changes.length > 0
        ? changes
        : ['Formada o‘zgarish topilmadi — barcha maydon eski qiymati bilan qayta yoziladi.'],
  })
  if (!ok) return

  errorMessage.value = null
  updateMutation.mutate(assignment.id)
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
          v-if="targetError !== null && submitted"
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

      <AssignmentFormFields
        v-model="form"
        :submitted="submitted"
        :disabled="isPending"
      />

      <!--
        BIRIKTIRMALAR faqat MAVJUD vazifada: yuklash uchun `assignmentId`
        kerak, u esa yaratilgandan keyin paydo bo'ladi. Yangi vazifada
        maslahat ko'rsatiladi — "nega bu yerda yo'q?" degan savol qolmasin.
      -->
      <hr class="my-4 border-line">

      <AssignmentAttachmentsSection
        v-if="props.assignment !== null"
        :assignment-id="props.assignment.id"
        :attachments="attachments"
        @update:attachments="onAttachmentsChanged"
      />
      <p
        v-else
        class="text-[11px] leading-relaxed text-dim"
      >
        Shart biriktirmalarini (rasm, ovozli izoh, PDF) vazifa yaratilgandan keyin
        qo‘shish mumkin — fayl mavjud vazifaga bog‘lanadi.
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
        :disabled="isPending"
        @click="emit('close')"
      >
        Bekor qilish
      </BaseButton>
      <BaseButton
        :disabled="isPending"
        :loading="isPending"
        @click="handleSubmit"
      >
        {{ isEdit ? 'Saqlash' : 'Yaratish' }}
      </BaseButton>
    </template>
  </BaseModal>
</template>
