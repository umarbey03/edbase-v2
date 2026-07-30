<script setup lang="ts">
import { useMutation } from '@tanstack/vue-query'
import { computed, ref, watch } from 'vue'

import { createCourse, updateCourse } from '@/entities/course'
import { toUserMessage } from '@/shared/api'
import type { CourseDto, CourseWriteRequest } from '@/shared/types'
import { BaseButton, BaseField, BaseModal } from '@/shared/ui'

/**
 * Kurs yaratish/tahrirlash.
 *
 * `position` maydoni ATAYLAB yo'q: server uni faqat "reorder" amalida
 * o'zgartiradi. Formada ham ko'rsatilsa, ikki joydan boshqarilgan tartib
 * bir-birini bosib ketardi.
 */
const props = defineProps<{
  open: boolean
  /** `null` — yangi kurs rejimi. */
  course: CourseDto | null
}>()

const emit = defineEmits<{ close: []; saved: [] }>()

const name = ref('')
const description = ref('')
const isActive = ref(true)
const errorMessage = ref<string | null>(null)

const isEdit = computed(() => props.course !== null)

function resetForm(): void {
  const course = props.course
  name.value = course?.name ?? ''
  description.value = course?.description ?? ''
  isActive.value = course?.isActive ?? true
  errorMessage.value = null
}

watch(() => [props.open, props.course], resetForm, { immediate: true })

function buildPayload(): CourseWriteRequest {
  const text = description.value.trim()
  return {
    name: name.value.trim(),
    // Bo'sh matn `null` sifatida ketadi — bazada bo'sh satr saqlanmasin.
    description: text.length > 0 ? text : null,
    isActive: isActive.value,
  }
}

const createMutation = useMutation({
  mutationFn: () => createCourse(buildPayload()),
  onSuccess: () => {
    emit('saved')
    emit('close')
  },
  onError: (error: Error) => {
    errorMessage.value = toUserMessage(error)
  },
})

const updateMutation = useMutation({
  mutationFn: (id: number) => updateCourse(id, buildPayload()),
  onSuccess: () => {
    emit('saved')
    emit('close')
  },
  onError: (error: Error) => {
    errorMessage.value = toUserMessage(error)
  },
})

const isPending = computed(() => createMutation.isPending.value || updateMutation.isPending.value)
const canSubmit = computed(() => name.value.trim().length > 0 && !isPending.value)

function handleSubmit(): void {
  if (!canSubmit.value) return
  errorMessage.value = null
  const course = props.course
  if (course !== null) updateMutation.mutate(course.id)
  else createMutation.mutate()
}
</script>

<template>
  <BaseModal
    :open="props.open"
    :title="isEdit ? 'Kursni tahrirlash' : 'Yangi kurs'"
    @close="emit('close')"
  >
    <form
      novalidate
      @submit.prevent="handleSubmit"
    >
      <BaseField label="Kurs nomi">
        <input
          v-model="name"
          class="zn-input"
          required
        >
      </BaseField>

      <div class="mt-3">
        <BaseField
          label="Tavsif"
          hint="Ixtiyoriy. O‘quvchi kurs sahifasida ko‘radi."
        >
          <textarea
            v-model="description"
            class="zn-input min-h-24 resize-y"
            rows="3"
          />
        </BaseField>
      </div>

      <label class="mt-3 flex min-h-11 items-center gap-2.5 text-sm text-slate-300">
        <input
          v-model="isActive"
          type="checkbox"
          class="size-4 accent-brand-500"
        >
        Faol kurs
      </label>

      <p
        v-if="!isActive"
        class="mt-1 text-[11px] text-amber-400"
      >
        Arxivlangan kurs yangi guruhlarga biriktirilmaydi.
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
