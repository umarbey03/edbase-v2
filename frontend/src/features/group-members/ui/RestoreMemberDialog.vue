<script setup lang="ts">
import { useMutation, useQuery } from '@tanstack/vue-query'
import { computed, ref, watch } from 'vue'

import { addMember, fetchGroups, groupDisplayName } from '@/entities/group'
import { toUserMessage } from '@/shared/api'
import type { GroupDto, GroupMemberDto } from '@/shared/types'
import { AppIcon, BaseButton, BaseField, BaseModal } from '@/shared/ui'

/**
 * ARXIVDAGI (chiqarilgan/ko'chirilgan) o'quvchini BOSHQA guruhga qo'shish
 * (loyiha egasi, 2026-08-16: *"Arxiv qismidagi o'quvchilarni ... boshqa
 * guruhga olib o'tish imkoni ham bo'lsin"*).
 *
 * ★ NEGA `MoveMemberDialog` EMAS: u FAOL a'zolikni ko'chiradi
 * (`GroupService.MoveMemberAsync` — eski yozuvni `Moved`, yangisini
 * `Active` qiladi, BITTA tranzaksiyada). Arxivdagi qatorning esa hech
 * qanday FAOL a'zoligi yo'q — ko'chiradigan narsa yo'q, faqat YANGI
 * guruhga QO'SHISH bor. Shu sabab bu yerda `addMember` chaqiriladi
 * (`AddMemberDialog` allaqachon ishlatadigan YO'L — server uni Stopped/
 * Moved qatorni ham TIKLASHGA tayyor, sabab `GroupService.AddMemberAsync`
 * izohida: "TIKLASH, yangi qator EMAS").
 *
 * ★ SABAB MAYDONI YO'Q (MoveMemberDialog'dan farqli): bu yerda "guruhdan
 * guruhga ko'chirish" emas, oddiy "qo'shish" — sabab talabi faqat
 * `MoveMemberAsync` shartida, `AddMemberAsync` uni so'ramaydi.
 */
const props = defineProps<{
  open: boolean
  /** O'quvchi HOZIR arxivda turgan guruh — nishon ro'yxatidan CHIQARILADI. */
  currentGroupId: number
  member: GroupMemberDto | null
}>()

const emit = defineEmits<{ close: []; saved: [] }>()

const targetGroupId = ref<number | null>(null)
const targetSearch = ref('')
const errorMessage = ref<string | null>(null)

watch(
  () => [props.open, props.member],
  () => {
    targetGroupId.value = null
    targetSearch.value = ''
    errorMessage.value = null
  },
  { immediate: true },
)

const groupsQuery = useQuery({
  queryKey: ['groups', 'restore-targets'],
  queryFn: ({ signal }) => fetchGroups({ isActive: true, pageSize: 100 }, { signal }),
  enabled: computed(() => props.open),
})

/** Joriy (arxiv) guruh ro'yxatdan chiqariladi — "qaytarish" uchun alohida tugma bor. */
const targets = computed(() =>
  (groupsQuery.data.value?.items ?? []).filter((group) => group.id !== props.currentGroupId),
)

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

const addMutation = useMutation({
  mutationFn: (input: { targetGroupId: number; studentId: number }) =>
    addMember(input.targetGroupId, { studentId: input.studentId }),
  onSuccess: () => {
    emit('saved')
    emit('close')
  },
  onError: (error: Error) => {
    errorMessage.value = toUserMessage(error)
  },
})

const canSubmit = computed(
  () => props.member !== null && targetGroupId.value !== null && !addMutation.isPending.value,
)

function handleSubmit(): void {
  const member = props.member
  const target = targetGroupId.value
  if (member === null || target === null) return
  errorMessage.value = null
  addMutation.mutate({ targetGroupId: target, studentId: member.studentId })
}
</script>

<template>
  <BaseModal
    :open="props.open"
    wide
    :title="`Boshqa guruhga qo‘shish: ${props.member?.fullName ?? 'o‘quvchi'}`"
    @close="emit('close')"
  >
    <BaseField
      label="Qaysi guruhga"
      hint="O‘quvchi shu guruhda FAOL a‘zo bo‘ladi. Arxivdagi eski yozuv (ushbu guruhda) o‘zgarishsiz qoladi."
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
        :loading="addMutation.isPending.value"
        @click="handleSubmit"
      >
        Qo‘shish
      </BaseButton>
    </template>
  </BaseModal>
</template>
