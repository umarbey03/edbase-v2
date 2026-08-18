<script setup lang="ts">
import { useMutation, useQuery } from '@tanstack/vue-query'
import { computed, ref, watch } from 'vue'

import { fetchGroups, groupDisplayName, moveMember } from '@/entities/group'
import { AttritionReasonSelect } from '@/features/attrition'
import { toUserMessage } from '@/shared/api'
import type { GroupDto, GroupMemberDto } from '@/shared/types'
import { AppIcon, BaseButton, BaseField, BaseModal } from '@/shared/ui'

/**
 * O'quvchini boshqa guruhga ko'chirish.
 *
 * Serverda ATOMIK: eski yozuv `Moved`, yangisi `Active` bo'ladi va ikkalasi
 * bitta tranzaksiyada yoziladi. Shuning uchun UI "avval chiqar, keyin qo'sh"
 * ketma-ketligini TAKRORLAMAYDI — yarim bajarilgan ko'chirish (hech qaysi
 * guruhda bo'lmagan o'quvchi) yuzaga kelmasin.
 *
 * ★ IKKI QO'SHIMCHA (loyiha egasi, 2026-08-15):
 *  1) NISHON GURUH QIDIRUV BILAN TANLANADI — native `<select>` 100 tagacha
 *     guruhda ochilmaydigan ro'yxat berardi (`AddMemberDialog`dagi student
 *     qidiruvi bilan BIR XIL naqsh, faqat SERVERGA emas — guruhlar
 *     allaqachon yuklangan, mijozda filtrlash yetarli).
 *  2) SABAB MAJBURIY — "guruhdan guruhga olib o'tishda sabab kiritilishi
 *     shart". Server ham buni tekshiradi (`GroupService.MoveMemberAsync`
 *     -> 409), mijozdagi tekshiruv faqat QULAYLIK.
 */
const props = defineProps<{
  open: boolean
  groupId: number
  member: GroupMemberDto | null
}>()

const emit = defineEmits<{ close: []; saved: [] }>()

const targetGroupId = ref<number | null>(null)
const targetSearch = ref('')
const reason = ref('')
const reasonId = ref<number | null>(null)
const errorMessage = ref<string | null>(null)

watch(
  () => [props.open, props.member],
  () => {
    targetGroupId.value = null
    targetSearch.value = ''
    reason.value = ''
    reasonId.value = null
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

/** Qidiruv — SERVERGA EMAS, allaqachon yuklangan ro'yxat ustida (≤100 ta). */
const filteredTargets = computed(() => {
  const query = targetSearch.value.trim().toLowerCase()
  if (query.length === 0) return targets.value
  return targets.value.filter((group) => groupDisplayName(group).toLowerCase().includes(query))
})

const selectedTarget = computed<GroupDto | null>(
  () => targets.value.find((group) => group.id === targetGroupId.value) ?? null,
)

function selectTarget(group: GroupDto): void {
  targetGroupId.value = group.id
}

const moveMutation = useMutation({
  mutationFn: (input: { studentId: number; targetGroupId: number; reason: string }) =>
    moveMember(props.groupId, input.studentId, {
      targetGroupId: input.targetGroupId,
      reason: input.reason,
      reasonId: reasonId.value ?? undefined,
    }),
  onSuccess: () => {
    emit('saved')
    emit('close')
  },
  onError: (error: Error) => {
    errorMessage.value = toUserMessage(error)
  },
})

const reasonMissing = computed(() => reason.value.trim().length === 0)

const canSubmit = computed(
  () =>
    props.member !== null &&
    targetGroupId.value !== null &&
    !reasonMissing.value &&
    !moveMutation.isPending.value,
)

function handleSubmit(): void {
  const member = props.member
  const target = targetGroupId.value
  if (member === null || target === null || reasonMissing.value) return
  errorMessage.value = null
  moveMutation.mutate({ studentId: member.studentId, targetGroupId: target, reason: reason.value.trim() })
}
</script>

<template>
  <BaseModal
    :open="props.open"
    wide
    :title="`Ko‘chirish: ${props.member?.fullName ?? 'o‘quvchi'}`"
    @close="emit('close')"
  >
    <BaseField
      label="Qaysi guruhga"
      hint="Eski guruhdagi yozuv “Ko‘chirilgan” holatida saqlanadi — davomat tarixi yo‘qolmaydi."
    >
      <div class="relative">
        <AppIcon
          name="search"
          :size="14"
          class="pointer-events-none absolute left-3 top-1/2 -translate-y-1/2 text-slate-500"
        />
        <input
          v-model="targetSearch"
          type="text"
          class="zn-input pl-9"
          placeholder="Guruh nomini qidirish..."
        >
      </div>

      <p
        v-if="selectedTarget !== null"
        class="mt-2 flex items-center gap-1.5 rounded-lg bg-brand-500/10 px-3 py-2 text-xs font-semibold text-brand-300"
      >
        <AppIcon
          name="check"
          :size="13"
        />
        {{ groupDisplayName(selectedTarget) }}
      </p>

      <ul class="mt-2 max-h-56 space-y-1.5 overflow-y-auto scrollbar-slim">
        <li
          v-for="group in filteredTargets"
          :key="group.id"
        >
          <button
            type="button"
            class="flex w-full items-center justify-between gap-2 rounded-lg border px-3 py-2 text-left text-sm transition-colors"
            :class="
              targetGroupId === group.id
                ? 'border-brand-500 bg-brand-500/10 text-brand-200'
                : 'border-line bg-ink-950 text-slate-200 hover:border-line-strong hover:bg-ink-900'
            "
            @click="selectTarget(group)"
          >
            {{ groupDisplayName(group) }}
          </button>
        </li>
        <li
          v-if="!groupsQuery.isPending.value && filteredTargets.length === 0"
          class="px-3 py-2 text-xs text-slate-400"
        >
          Guruh topilmadi.
        </li>
      </ul>
    </BaseField>

    <div class="mt-3 space-y-3">
      <AttritionReasonSelect
        v-model="reasonId"
        :open="props.open"
      />

      <BaseField
        label="Izoh"
        hint="Majburiy — nega bu o‘quvchi ko‘chirilyapti (masalan: darajasi mos kelmadi)."
        :error="reasonMissing && reason.length > 0 ? 'Izoh bo‘sh bo‘lishi mumkin emas.' : null"
      >
        <textarea
          v-model="reason"
          class="zn-input"
          rows="3"
          maxlength="500"
          placeholder="Ko‘chirish sababini yozing..."
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
        :disabled="!canSubmit"
        :loading="moveMutation.isPending.value"
        @click="handleSubmit"
      >
        Ko‘chirish
      </BaseButton>
    </template>
  </BaseModal>
</template>
