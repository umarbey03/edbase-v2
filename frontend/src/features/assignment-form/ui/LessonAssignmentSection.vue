<script setup lang="ts">
import { useMutation, useQuery } from '@tanstack/vue-query'
import { computed, ref, watch } from 'vue'

import { createAssignment, fetchAssignments, updateAssignment } from '@/entities/assignment'
import { toUserMessage } from '@/shared/api'
import { useConfirm } from '@/shared/lib/useConfirm'
import type { AssignmentAttachmentDto, AssignmentDto } from '@/shared/types'
import { AppIcon, BaseButton, EmptyState, SectionLoader } from '@/shared/ui'

import {
  buildCreateRequest,
  buildUpdateRequest,
  changedAssignmentFields,
  createAssignmentFormState,
  isAssignmentFormValid,
  validateAssignmentForm,
} from '../model/assignment-form'
import type { AssignmentFormState } from '../model/assignment-form'
import AssignmentAttachmentsSection from './AssignmentAttachmentsSection.vue'
import AssignmentFormFields from './AssignmentFormFields.vue'

/**
 * ========================================================================
 * DARS DRAWER'INING 4-BO'LIMI: UY VAZIFASI
 * ========================================================================
 *
 * Talab: *"Uy vazifasi — biriktirilganmi; yo'q bo'lsa «Vazifa qo'shish»"*.
 *
 * ★ MAYDONLAR VA TEKSHIRUV `AssignmentFormDialog` BILAN AYNI KODDAN
 * (`AssignmentFormFields` + `model/assignment-form.ts`) — talab shunday:
 * *"mavjud `AssignmentFormDialog` mantig'i shu bo'limga ko'chiriladi — ikki
 * nusxa saqlanmaydi"*. Dialog `ManageAssignmentsPage` va ustozning baholash
 * sahifasida qolgani uchun umumiy qism ajratildi, dialog ham SHUNDAN
 * foydalanadi.
 *
 * ── NIMA UCHUN VAZIFA ALOHIDA SO'ROV BILAN OLINADI ────────────────────
 *
 * Kurs daraxti darsda vazifa BOR-YO'QLIGINI beradi (`hasAssignment`), lekin
 * vazifaning O'ZINI bermaydi va "darsning vazifasi" degan alohida endpoint
 * ham yo'q. Shuning uchun ro'yxat endpointi DARS bo'yicha filtrlanadi
 * (`GET /assignments?ModuleLessonId=…`). Bitta darsga bitta vazifa
 * biriktiriladi (`pageSize: 1` — ro'yxatda birinchisi olinadi).
 *
 * ── 🔴 `PUT` = TO'LIQ ALMASHTIRISH ────────────────────────────────────
 *
 * `buildUpdateRequest` HAMMA maydonni qaytaradi (jumladan eskirgan
 * `imageKey`) — aks holda `curl` bilan biriktirilgan eski rasm birinchi
 * saqlashdayoq yo'qolardi.
 */
const props = defineProps<{
  lessonId: number
  /** Drawer ochiqmi — yopiq panelda so'rov yuborilmasin. */
  enabled: boolean
}>()

const emit = defineEmits<{ changed: [] }>()

const confirm = useConfirm()

const assignmentQuery = useQuery({
  queryKey: computed(() => ['lesson-assignment', props.lessonId]),
  queryFn: ({ signal }) =>
    fetchAssignments({ moduleLessonId: props.lessonId, page: 1, pageSize: 1 }, { signal }),
  enabled: computed(() => props.enabled && props.lessonId > 0),
})

// `items` server shartnomasida DOIM massiv, lekin tur `null` ni ham
// qo'llaydi (mudofaa uchun) — shuning uchun `?? []`.
const assignment = computed<AssignmentDto | null>(
  () => (assignmentQuery.data.value?.items ?? [])[0] ?? null,
)

/** Yangi vazifa formasi ochiqmi (mavjud vazifada forma doim ochiq). */
const creating = ref(false)

const form = ref<AssignmentFormState>(createAssignmentFormState(null))
const attachments = ref<AssignmentAttachmentDto[]>([])
const errorMessage = ref<string | null>(null)
const submitted = ref(false)

/*
  Serverdan kelgan vazifa formaga YOZILADI.

  🔴 KUZATUV `id` GA BOG'LANGAN, ob'ektga EMAS: `useQuery` har qayta so'rovda
  yangi ob'ekt qaytaradi va ob'ektni kuzatsak, biriktirma yuklanishi yoki
  saqlashdan keyingi qayta so'rov paytida foydalanuvchining yozib turgan
  matni "sakrab" almashardi. Id o'zgarishi esa AYNAN ikki holat: boshqa dars
  ochildi yoki vazifa endigina yaratildi — ikkalasida ham formani serverdagi
  holatdan qayta yuklash TO'G'RI.
*/
watch(
  () => assignment.value?.id ?? null,
  () => {
    const value = assignment.value
    form.value = createAssignmentFormState(value)
    attachments.value = [...(value?.attachments ?? [])]
    errorMessage.value = null
    submitted.value = false
    if (value !== null) creating.value = false
  },
  { immediate: true },
)

watch(
  () => props.lessonId,
  () => {
    creating.value = false
  },
)

const errors = computed(() => validateAssignmentForm(form.value))

const queryError = computed(() =>
  assignmentQuery.error.value !== null ? toUserMessage(assignmentQuery.error.value) : null,
)

/* ------------------------------------------------------------- saqlash */

const createMutation = useMutation({
  mutationFn: () =>
    createAssignment(
      // Nishon SHU DARS: guruh vazifasi bu yerda umuman bo'lmaydi.
      buildCreateRequest(form.value, { groupId: null, moduleLessonId: props.lessonId }),
    ),
  onSuccess: () => {
    creating.value = false
    void assignmentQuery.refetch()
    // Daraxtdagi "Vazifa" nishoni ham yangilanishi kerak.
    emit('changed')
  },
  onError: (error: Error) => {
    errorMessage.value = toUserMessage(error)
  },
})

const updateMutation = useMutation({
  mutationFn: (id: number) => updateAssignment(id, buildUpdateRequest(form.value)),
  onSuccess: () => {
    void assignmentQuery.refetch()
    emit('changed')
  },
  onError: (error: Error) => {
    errorMessage.value = toUserMessage(error)
  },
})

const isPending = computed(() => createMutation.isPending.value || updateMutation.isPending.value)

async function save(): Promise<void> {
  submitted.value = true
  if (!isAssignmentFormValid(errors.value) || isPending.value) return

  const current = assignment.value
  if (current === null) {
    errorMessage.value = null
    createMutation.mutate()
    return
  }

  // B2 jadvali: ma'lumotni almashtiruvchi saqlash -> `primary` + o'zgargan
  // maydonlar ro'yxati. Hech narsa o'zgarmagan bo'lsa oyna ko'rsatilmaydi.
  const changes = changedAssignmentFields(current, form.value)
  if (changes.length > 0) {
    const ok = await confirm({
      title: 'Vazifani saqlash',
      message:
        'Vazifa ma’lumotlari ALMASHTIRILADI. Kursdagi barcha guruhlar yangi '
        + 'shartni darhol ko‘radi (topshirilgan javoblar va baholar saqlanadi).',
      confirmLabel: 'Saqlash',
      tone: 'primary',
      details: changes,
    })
    if (!ok) return
  }

  errorMessage.value = null
  updateMutation.mutate(current.id)
}

function onAttachmentsChanged(next: AssignmentAttachmentDto[]): void {
  attachments.value = next
  // Fayl serverda ALLAQACHON o'zgardi — daraxt/kartochka eskirdi.
  emit('changed')
}

function startCreate(): void {
  form.value = createAssignmentFormState(null)
  attachments.value = []
  errorMessage.value = null
  submitted.value = false
  creating.value = true
}
</script>

<template>
  <section>
    <SectionLoader
      v-if="assignmentQuery.isPending.value && props.enabled"
      variant="form"
      :rows="3"
      label="Vazifa yuklanmoqda"
    />

    <div
      v-else-if="queryError !== null"
      class="rounded-lg border border-rose-500/25 bg-rose-500/10 p-3"
      role="alert"
    >
      <p
        class="text-xs text-rose-200"
        v-text="queryError"
      />
      <BaseButton
        class="mt-2.5"
        size="sm"
        variant="secondary"
        :loading="assignmentQuery.isFetching.value"
        @click="assignmentQuery.refetch()"
      >
        <template #icon>
          <AppIcon
            name="refresh"
            :size="13"
          />
        </template>
        Qayta urinish
      </BaseButton>
    </div>

    <!-- Vazifa yo'q va forma ham ochilmagan -->
    <EmptyState
      v-else-if="assignment === null && !creating"
      icon="clipboard"
      title="Vazifa biriktirilmagan"
      text="Bu darsga uy vazifasi qo‘shilsa, o‘quvchi darsni tugatish uchun uni topshirishi kerak bo‘ladi."
    >
      <BaseButton
        size="sm"
        @click="startCreate"
      >
        <template #icon>
          <AppIcon
            name="plus"
            :size="14"
          />
        </template>
        Vazifa qo‘shish
      </BaseButton>
    </EmptyState>

    <template v-else>
      <AssignmentFormFields
        v-model="form"
        :submitted="submitted"
        :disabled="isPending"
      />

      <template v-if="assignment !== null">
        <hr class="my-4 border-line">
        <AssignmentAttachmentsSection
          :assignment-id="assignment.id"
          :attachments="attachments"
          @update:attachments="onAttachmentsChanged"
        />
      </template>
      <p
        v-else
        class="mt-3 text-[11px] leading-relaxed text-dim"
      >
        Shart biriktirmalari (rasm, ovozli izoh, PDF) vazifa saqlangandan keyin
        qo‘shiladi — fayl mavjud vazifaga bog‘lanadi.
      </p>

      <p
        v-if="errorMessage !== null"
        class="mt-3 text-xs text-rose-400"
        role="alert"
        v-text="errorMessage"
      />

      <div class="mt-4 flex flex-wrap justify-end gap-2">
        <BaseButton
          v-if="assignment === null"
          size="sm"
          variant="ghost"
          :disabled="isPending"
          @click="creating = false"
        >
          Bekor qilish
        </BaseButton>
        <BaseButton
          size="sm"
          :loading="isPending"
          @click="save"
        >
          {{ assignment === null ? 'Vazifani yaratish' : 'Vazifani saqlash' }}
        </BaseButton>
      </div>
    </template>
  </section>
</template>
