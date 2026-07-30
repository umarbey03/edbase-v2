<script setup lang="ts">
import { useMutation } from '@tanstack/vue-query'
import { computed, ref, watch } from 'vue'

import { createModule, updateModule } from '@/entities/course'
import { toUserMessage } from '@/shared/api'
import type { CourseModuleDto } from '@/shared/types'
import { BaseButton, BaseField, BaseModal } from '@/shared/ui'

/** Modul yaratish/tahrirlash. Modulda nomdan boshqa maydon yo'q (backend shunday). */
const props = defineProps<{
  open: boolean
  courseId: number
  /** `null` — yangi modul rejimi. */
  module: CourseModuleDto | null
}>()

const emit = defineEmits<{ close: []; saved: [] }>()

const name = ref('')
const errorMessage = ref<string | null>(null)

const isEdit = computed(() => props.module !== null)

watch(
  () => [props.open, props.module],
  () => {
    name.value = props.module?.name ?? ''
    errorMessage.value = null
  },
  { immediate: true },
)

const createMutation = useMutation({
  mutationFn: () => createModule(props.courseId, { name: name.value.trim() }),
  onSuccess: () => {
    emit('saved')
    emit('close')
  },
  onError: (error: Error) => {
    errorMessage.value = toUserMessage(error)
  },
})

const updateMutation = useMutation({
  mutationFn: (moduleId: number) =>
    updateModule(props.courseId, moduleId, { name: name.value.trim() }),
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
  const module = props.module
  if (module !== null) updateMutation.mutate(module.id)
  else createMutation.mutate()
}
</script>

<template>
  <BaseModal
    :open="props.open"
    :title="isEdit ? 'Modulni tahrirlash' : 'Yangi modul'"
    @close="emit('close')"
  >
    <form
      novalidate
      @submit.prevent="handleSubmit"
    >
      <BaseField
        label="Modul nomi"
        hint="Yangi modul ro‘yxat oxiriga qo‘shiladi."
      >
        <input
          v-model="name"
          class="zn-input"
          required
        >
      </BaseField>

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
