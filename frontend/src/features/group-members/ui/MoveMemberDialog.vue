<script setup lang="ts">
import { useMutation, useQuery } from '@tanstack/vue-query'
import { computed, ref, watch } from 'vue'

import { fetchGroups, groupDisplayName, moveMember } from '@/entities/group'
import { toUserMessage } from '@/shared/api'
import type { GroupMemberDto } from '@/shared/types'
import { BaseButton, BaseField, BaseModal } from '@/shared/ui'

/**
 * O'quvchini boshqa guruhga ko'chirish.
 *
 * Serverda ATOMIK: eski yozuv `Moved`, yangisi `Active` bo'ladi va ikkalasi
 * bitta tranzaksiyada yoziladi. Shuning uchun UI "avval chiqar, keyin qo'sh"
 * ketma-ketligini TAKRORLAMAYDI — yarim bajarilgan ko'chirish (hech qaysi
 * guruhda bo'lmagan o'quvchi) yuzaga kelmasin.
 */
const props = defineProps<{
  open: boolean
  groupId: number
  member: GroupMemberDto | null
}>()

const emit = defineEmits<{ close: []; saved: [] }>()

const targetGroupId = ref<number | null>(null)
const errorMessage = ref<string | null>(null)

watch(
  () => [props.open, props.member],
  () => {
    targetGroupId.value = null
    errorMessage.value = null
  },
  { immediate: true },
)

const groupsQuery = useQuery({
  queryKey: ['groups', 'move-targets'],
  queryFn: ({ signal }) => fetchGroups({ isActive: true, pageSize: 100 }, { signal }),
  enabled: computed(() => props.open),
})

/** Joriy guruh ro'yxatdan chiqariladi — o'ziga ko'chirish ma'nosiz. */
const targets = computed(() =>
  (groupsQuery.data.value?.items ?? []).filter((group) => group.id !== props.groupId),
)

const moveMutation = useMutation({
  mutationFn: (input: { studentId: number; targetGroupId: number }) =>
    moveMember(props.groupId, input.studentId, { targetGroupId: input.targetGroupId }),
  onSuccess: () => {
    emit('saved')
    emit('close')
  },
  onError: (error: Error) => {
    errorMessage.value = toUserMessage(error)
  },
})

const canSubmit = computed(
  () => props.member !== null && targetGroupId.value !== null && !moveMutation.isPending.value,
)

function handleSubmit(): void {
  const member = props.member
  const target = targetGroupId.value
  if (member === null || target === null) return
  errorMessage.value = null
  moveMutation.mutate({ studentId: member.studentId, targetGroupId: target })
}
</script>

<template>
  <BaseModal
    :open="props.open"
    :title="`Ko‘chirish: ${props.member?.fullName ?? 'o‘quvchi'}`"
    @close="emit('close')"
  >
    <BaseField
      label="Qaysi guruhga"
      hint="Eski guruhdagi yozuv “Ko‘chirilgan” holatida saqlanadi — davomat tarixi yo‘qolmaydi."
    >
      <select
        v-model="targetGroupId"
        class="zn-input"
      >
        <option :value="null">
          Guruhni tanlang
        </option>
        <option
          v-for="group in targets"
          :key="group.id"
          :value="group.id"
        >
          {{ groupDisplayName(group) }}
        </option>
      </select>
    </BaseField>

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
        :disabled="!canSubmit"
        :loading="moveMutation.isPending.value"
        @click="handleSubmit"
      >
        Ko‘chirish
      </BaseButton>
    </template>
  </BaseModal>
</template>
