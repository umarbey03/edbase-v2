<script setup lang="ts">
import { useMutation } from '@tanstack/vue-query'
import { computed, ref, watch } from 'vue'

import { removeMember } from '@/entities/group'
import { toUserMessage } from '@/shared/api'
import type { GroupMemberDto } from '@/shared/types'
import { BaseButton, BaseField, BaseModal } from '@/shared/ui'

/**
 * GURUHDAN CHIQARISH — MAJBURIY SABAB BILAN (2026-08-17).
 *
 * ★ NIMA UCHUN `useConfirm` O'RNIGA ALOHIDA DIALOG: chiqarish endi sabab
 * talab qiladi, `useConfirm` esa faqat "ha/yo'q" so'raydi va MATN
 * MAYDONINI ko'rsatmaydi. Sababni oldindan so'ramasdan yuborish esa
 * serverdan 400 olib, xodimga tushunarsiz xato ko'rsatardi.
 *
 * ★ Ogohlantirish matnlari ESKI `confirm` dialogidan AYNAN ko'chirildi —
 * xodim o'rgangan ma'lumot yo'qolmasin.
 */
const props = defineProps<{
  open: boolean
  groupId: number
  member: GroupMemberDto | null
}>()

const emit = defineEmits<{ close: []; saved: [] }>()

const reason = ref('')
const errorMessage = ref<string | null>(null)

const reasonMissing = computed(() => reason.value.trim().length === 0)

watch(
  () => [props.open, props.member],
  () => {
    reason.value = ''
    errorMessage.value = null
  },
  { immediate: true },
)

const removeMutation = useMutation({
  mutationFn: (studentId: number) =>
    removeMember(props.groupId, studentId, { reason: reason.value.trim() }),
  onSuccess: () => {
    emit('saved')
    emit('close')
  },
  onError: (error: Error) => {
    errorMessage.value = toUserMessage(error)
  },
})

function handleSubmit(): void {
  const member = props.member
  if (member === null) return

  errorMessage.value = null

  if (reasonMissing.value) {
    errorMessage.value = 'Chiqarish sababini kiriting.'
    return
  }

  removeMutation.mutate(member.studentId)
}
</script>

<template>
  <BaseModal
    :open="props.open"
    :title="`Guruhdan chiqarish: ${props.member?.fullName ?? 'o‘quvchi'}`"
    @close="emit('close')"
  >
    <ul class="space-y-1 text-sm text-slate-300">
      <li>• Yozuv o‘chirilmaydi — holati “Chiqarilgan” bo‘ladi.</li>
      <li>• Davomat va to‘lov tarixi saqlanadi.</li>
      <li>• Qaytarish uchun o‘quvchini guruhga qaytadan qo‘shish kerak bo‘ladi.</li>
    </ul>

    <div class="mt-3">
      <BaseField
        label="Sabab"
        hint="Masalan: boshqa markazga o‘tdi, to‘lov imkoni bo‘lmadi, vaqti to‘g‘ri kelmadi."
        :error="reasonMissing && reason.length > 0 ? 'Sabab bo‘sh bo‘lishi mumkin emas.' : null"
      >
        <textarea
          v-model="reason"
          class="zn-input min-h-20"
          maxlength="500"
          rows="2"
          placeholder="Nima uchun chiqarilyapti?"
        />
      </BaseField>
    </div>

    <p
      v-if="errorMessage !== null"
      class="mt-3 text-xs text-rose-400"
      role="alert"
      v-text="errorMessage"
    />

    <template #footer>
      <BaseButton
        variant="secondary"
        @click="emit('close')"
      >
        Bekor qilish
      </BaseButton>
      <BaseButton
        variant="danger"
        :loading="removeMutation.isPending.value"
        @click="handleSubmit"
      >
        Chiqarish
      </BaseButton>
    </template>
  </BaseModal>
</template>
