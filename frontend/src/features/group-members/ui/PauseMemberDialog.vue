<script setup lang="ts">
import { useMutation } from '@tanstack/vue-query'
import { ref, watch } from 'vue'

import { pauseMember } from '@/entities/group'
import { toUserMessage } from '@/shared/api'
import type { GroupMemberDto } from '@/shared/types'
import { BaseButton, BaseField, BaseModal } from '@/shared/ui'

/**
 * A'zolikni pauza qilish.
 *
 * Muddat IXTIYORIY: sana berilmasa pauza muddatsiz bo'ladi va faqat qo'lda
 * tiklanadi. Ikkalasi ham kerak — "bir oy ta'tilga chiqdi" bilan "noma'lum
 * muddatga to'xtatdi" boshqa-boshqa holat va ularni bitta tugma ostiga
 * yashirish keyin "nega o'zi qaytmadi?" degan savol tug'dirardi.
 */
const props = defineProps<{
  open: boolean
  groupId: number
  member: GroupMemberDto | null
}>()

const emit = defineEmits<{ close: []; saved: [] }>()

const pausedUntil = ref('')
const errorMessage = ref<string | null>(null)

watch(
  () => [props.open, props.member],
  () => {
    pausedUntil.value = props.member?.pausedUntil ?? ''
    errorMessage.value = null
  },
  { immediate: true },
)

const pauseMutation = useMutation({
  mutationFn: (studentId: number) =>
    pauseMember(props.groupId, studentId, {
      // Bo'sh maydon = muddatsiz. `""` yuborilsa server sanani parse qila olmay
      // 400 berardi, shuning uchun ataylab `null`.
      pausedUntil: pausedUntil.value.length > 0 ? pausedUntil.value : null,
    }),
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
  pauseMutation.mutate(member.studentId)
}
</script>

<template>
  <BaseModal
    :open="props.open"
    :title="`Pauza: ${props.member?.fullName ?? 'o‘quvchi'}`"
    @close="emit('close')"
  >
    <p class="text-sm text-slate-300">
      Pauzadagi o‘quvchi guruh darslariga qo‘shilmaydi va davomatda hisobga
      olinmaydi.
    </p>

    <div class="mt-3">
      <BaseField
        label="Qaysi sanagacha"
        hint="Bo‘sh qoldirilsa — muddatsiz pauza (keyin qo‘lda tiklanadi)."
      >
        <input
          v-model="pausedUntil"
          class="zn-input"
          type="date"
        >
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
        :loading="pauseMutation.isPending.value"
        @click="handleSubmit"
      >
        Pauza qilish
      </BaseButton>
    </template>
  </BaseModal>
</template>
