<script setup lang="ts">
import { useQuery } from '@tanstack/vue-query'
import { computed } from 'vue'

import { fetchPayrollDetail, payrollPeriodLabel, payrollRoleLabel } from '@/entities/payroll'
import { toUserMessage } from '@/shared/api'
import { formatDateTime } from '@/shared/lib/datetime'
import { formatMoney } from '@/shared/lib/money'
import { AppIcon, BaseBadge, BaseButton, BaseModal, BaseSpinner } from '@/shared/ui'

/** Bitta xodimning dars-dars tafsiloti — hisobot qatorini bosganda ochiladi. */
const props = defineProps<{ open: boolean; userId: number | null; period: string }>()

const emit = defineEmits<{ close: [] }>()

const enabled = computed(() => props.open && props.userId !== null)
const userId = computed(() => props.userId)
const period = computed(() => props.period)

const detailQuery = useQuery({
  queryKey: ['payroll', 'detail', userId, period],
  queryFn: ({ signal }) => {
    const id = userId.value
    if (id === null) throw new Error('userId yo‘q.')
    return fetchPayrollDetail(id, { period: period.value }, { signal })
  },
  enabled,
})

const detail = computed(() => detailQuery.data.value ?? null)

const errorMessage = computed(() =>
  detailQuery.error.value !== null ? toUserMessage(detailQuery.error.value) : null,
)
</script>

<template>
  <BaseModal
    :open="props.open"
    wide
    :title="detail === null ? 'Dars-dars tafsilot' : `${detail.fullName} — ${payrollPeriodLabel(detail.period)}`"
    @close="emit('close')"
  >
    <div
      v-if="detailQuery.isPending.value"
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
      v-else-if="detail !== null && detail.sessions.length === 0"
      class="text-xs text-slate-400"
    >
      Shu davrda yakunlangan dars yo‘q.
    </p>

    <template v-else-if="detail !== null">
      <p class="mb-3 text-xs text-slate-400">
        {{ payrollRoleLabel(detail.role) }} · {{ detail.sessions.length }} dars · jami
        <span class="font-semibold text-slate-200">{{ formatMoney(detail.grandTotal) }}</span>
      </p>

      <div class="scroll-x-safe scrollbar-slim">
        <table class="zn-table">
          <thead>
            <tr>
              <th>Sana</th>
              <th>Guruh</th>
              <th>Qatnashgan</th>
              <th>Stavka</th>
              <th>Bonus</th>
              <th>Jami</th>
            </tr>
          </thead>
          <tbody>
            <tr
              v-for="session in detail.sessions"
              :key="session.sessionId"
            >
              <td class="text-slate-300">
                {{ formatDateTime(session.scheduledStart) }}
              </td>
              <td class="text-slate-400">
                {{ session.groupName }}
              </td>
              <td class="tabular-nums text-slate-300">
                {{ session.attendedStudents }}
              </td>
              <td class="tabular-nums text-slate-300">
                {{ formatMoney(session.sessionRate) }}
              </td>
              <td class="tabular-nums text-slate-300">
                {{ formatMoney(session.bonusAmount) }}
              </td>
              <td class="tabular-nums font-semibold text-slate-100">
                <span
                  v-if="session.rateMissing"
                  class="inline-flex items-center gap-1"
                >
                  <AppIcon
                    name="alert"
                    :size="13"
                    class="text-amber-400"
                  />
                  <BaseBadge tone="warning">
                    Stavka yo‘q
                  </BaseBadge>
                </span>
                <span v-else>{{ formatMoney(session.total) }}</span>
              </td>
            </tr>
          </tbody>
        </table>
      </div>
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
