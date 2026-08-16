<script setup lang="ts">
import { useQuery } from '@tanstack/vue-query'
import { computed } from 'vue'

import {
  fetchLessonCharges,
  lessonChargeSkipReasonLabel,
  periodLabel,
} from '@/entities/payment'
import { toUserMessage } from '@/shared/api'
import { formatDateTime } from '@/shared/lib/datetime'
import { formatMoney } from '@/shared/lib/money'
import { BaseBadge, BaseButton, BaseModal, BaseSpinner } from '@/shared/ui'

/**
 * "Darslar" oynasi (2026-08-16) — bitta o'quvchining bitta (guruh, oy)
 * uchun DARS-DARS tafsiloti. `PayrollDetailDialog` bilan AYNI naqsh
 * (ochilganda so'raladi, `enabled` faqat oyna ochiq bo'lganda).
 *
 * ★ NIMA UCHUN KERAK: oylik jadval faqat YIG'INDINI ko'rsatadi
 * ("540 000 / 8 dars"). Real ishda xodim/ota-ona ko'pincha "AYNAN qaysi
 * dars uchun qancha" savolini beradi — bu oyna aynan shu savolga javob.
 */
const props = defineProps<{
  open: boolean
  studentId: number | null
  groupId: number | null
  period: string | null
}>()

const emit = defineEmits<{ close: [] }>()

const studentId = computed(() => props.studentId)
const groupId = computed(() => props.groupId)
const period = computed(() => props.period)

const enabled = computed(
  () => props.open && studentId.value !== null && groupId.value !== null && period.value !== null,
)

const chargesQuery = useQuery({
  queryKey: ['payments', 'lesson-charges', studentId, groupId, period],
  queryFn: ({ signal }) => {
    const id = studentId.value
    const group = groupId.value
    const p = period.value
    if (id === null || group === null || p === null) {
      throw new Error('Parametrlar to‘liq emas.')
    }
    return fetchLessonCharges(id, { groupId: group, period: p }, { signal })
  },
  enabled,
})

const charges = computed(() => chargesQuery.data.value ?? [])

const errorMessage = computed(() =>
  chargesQuery.error.value !== null ? toUserMessage(chargesQuery.error.value) : null,
)

const total = computed(() => charges.value.reduce((sum, item) => sum + item.chargedAmount, 0))
</script>

<template>
  <BaseModal
    :open="props.open"
    :title="props.period === null ? 'Darslar' : `Darslar — ${periodLabel(props.period)}`"
    @close="emit('close')"
  >
    <div
      v-if="chargesQuery.isPending.value"
      class="flex justify-center py-8"
    >
      <BaseSpinner />
    </div>

    <p
      v-else-if="errorMessage !== null"
      class="text-xs text-rose-400"
      role="alert"
      v-text="errorMessage"
    />

    <p
      v-else-if="charges.length === 0"
      class="text-xs text-slate-400"
    >
      Shu oyda hali hisoblangan dars yo‘q.
    </p>

    <template v-else>
      <p class="mb-3 text-xs text-slate-400">
        {{ charges.length }} dars · jami
        <span class="font-semibold text-slate-200">{{ formatMoney(total) }}</span> yechildi
      </p>

      <ul class="divide-y divide-line rounded-xl border border-line">
        <li
          v-for="charge in charges"
          :key="charge.sessionId"
          class="flex items-center justify-between gap-3 p-3"
        >
          <div class="min-w-0 flex-1">
            <p class="text-sm text-slate-200">
              {{ formatDateTime(charge.scheduledStart) }}
            </p>
            <BaseBadge
              v-if="charge.skipReason !== null"
              tone="success"
              class="mt-1"
            >
              {{ lessonChargeSkipReasonLabel(charge.skipReason) }} — yechilmadi
            </BaseBadge>
          </div>
          <div class="shrink-0 text-right tabular-nums">
            <p
              class="text-sm font-semibold"
              :class="charge.chargedAmount > 0 ? 'text-slate-100' : 'text-green-400'"
            >
              {{ formatMoney(charge.chargedAmount) }}
            </p>
            <p
              v-if="charge.chargedAmount !== charge.stickerAmount"
              class="text-[11px] text-dim"
            >
              narxi {{ formatMoney(charge.stickerAmount) }}
            </p>
          </div>
        </li>
      </ul>
    </template>

    <template #footer>
      <BaseButton
        variant="secondary"
        @click="emit('close')"
      >
        Yopish
      </BaseButton>
    </template>
  </BaseModal>
</template>
