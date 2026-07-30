<script setup lang="ts">
import { useMutation } from '@tanstack/vue-query'
import { computed, ref, watch } from 'vue'

import { createLesson, updateLesson } from '@/entities/course'
import { toUserMessage } from '@/shared/api'
import type { CourseLessonDto, LessonWriteRequest } from '@/shared/types'
import { BaseButton, BaseField, BaseModal } from '@/shared/ui'

/** Dars yaratish/tahrirlash (modul ichida). */
const props = defineProps<{
  open: boolean
  courseId: number
  moduleId: number
  /** `null` — yangi dars rejimi. */
  lesson: CourseLessonDto | null
}>()

const emit = defineEmits<{ close: []; saved: [] }>()

const name = ref('')
const description = ref('')
/** Bo'sh satr = "kiritilmagan" (`null`), 0 emas. */
const durationText = ref('')
const errorMessage = ref<string | null>(null)

const isEdit = computed(() => props.lesson !== null)

watch(
  () => [props.open, props.lesson],
  () => {
    const lesson = props.lesson
    name.value = lesson?.name ?? ''
    description.value = lesson?.description ?? ''
    durationText.value = lesson?.durationMin != null ? String(lesson.durationMin) : ''
    errorMessage.value = null
  },
  { immediate: true },
)

function buildPayload(): LessonWriteRequest {
  const text = description.value.trim()
  const duration = durationText.value.trim()
  return {
    name: name.value.trim(),
    description: text.length > 0 ? text : null,
    durationMin: duration.length > 0 ? Number(duration) : null,
  }
}

const createMutation = useMutation({
  mutationFn: () => createLesson(props.courseId, props.moduleId, buildPayload()),
  onSuccess: () => {
    emit('saved')
    emit('close')
  },
  onError: (error: Error) => {
    errorMessage.value = toUserMessage(error)
  },
})

const updateMutation = useMutation({
  mutationFn: (lessonId: number) =>
    updateLesson(props.courseId, props.moduleId, lessonId, buildPayload()),
  onSuccess: () => {
    emit('saved')
    emit('close')
  },
  onError: (error: Error) => {
    errorMessage.value = toUserMessage(error)
  },
})

const isPending = computed(() => createMutation.isPending.value || updateMutation.isPending.value)

/*
  Davomiylik bo'sh bo'lishi MUMKIN, lekin kiritilgan bo'lsa musbat butun son
  bo'lishi kerak — aks holda server 400 qaytaradi va foydalanuvchi nima
  xato bo'lganini formadan tashqarida bilib olardi.
*/
const durationError = computed(() => {
  const raw = durationText.value.trim()
  if (raw.length === 0) return null
  const value = Number(raw)
  if (!Number.isInteger(value) || value <= 0) return 'Daqiqa musbat butun son bo‘lishi kerak.'
  return null
})

const canSubmit = computed(
  () => name.value.trim().length > 0 && durationError.value === null && !isPending.value,
)

function handleSubmit(): void {
  if (!canSubmit.value) return
  errorMessage.value = null
  const lesson = props.lesson
  if (lesson !== null) updateMutation.mutate(lesson.id)
  else createMutation.mutate()
}
</script>

<template>
  <BaseModal
    :open="props.open"
    :title="isEdit ? 'Darsni tahrirlash' : 'Yangi dars'"
    @close="emit('close')"
  >
    <form
      novalidate
      @submit.prevent="handleSubmit"
    >
      <BaseField label="Dars nomi">
        <input
          v-model="name"
          class="zn-input"
          required
        >
      </BaseField>

      <div class="mt-3">
        <BaseField
          label="Tavsif"
          hint="Qulflangan darsda o‘quvchiga KO‘RSATILMAYDI — faqat sarlavha ko‘rinadi."
        >
          <textarea
            v-model="description"
            class="zn-input min-h-24 resize-y"
            rows="3"
          />
        </BaseField>
      </div>

      <div class="mt-3 sm:max-w-48">
        <BaseField
          label="Davomiylik (daqiqa)"
          hint="Ixtiyoriy."
          :error="durationError"
        >
          <input
            v-model="durationText"
            class="zn-input"
            type="number"
            min="1"
            inputmode="numeric"
          >
        </BaseField>
      </div>

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
