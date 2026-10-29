<script setup lang="ts">
import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { computed, ref } from 'vue'

import {
  cancelSubscription,
  fetchStudentSubscriptions,
  fetchSubscriptionPlans,
  grantSubscription,
  periodState,
  periodStateLabel,
} from '@/entities/subscription'
import type { SubscriptionPeriodDto } from '@/entities/subscription'
import { toUserMessage } from '@/shared/api'
import { formatDate } from '@/shared/lib/datetime'
import { formatMoney } from '@/shared/lib/money'
import { showToast } from '@/shared/lib/useToast'
import type { PaymentMethodName } from '@/shared/types'
import { AppIcon, BaseBadge, BaseButton } from '@/shared/ui'

/**
 * O'QUVCHI PROFILIDAGI "MUSTAQIL O'QISH OBUNASI" BO'LIMI (2026-10-23).
 *
 * Faqat o'quv bo'limi va administrator ko'radi (server ham shunday:
 * `SubscriptionsController` — `Academic,Admin`). Obuna berish = to'lov
 * qo'lda qabul qilindi; uzaytirish avvalgi davr tugagan kundan boshlanadi.
 *
 * ★ GURUHDAGI O'QUVCHIGA OBUNA BERILMAYDI — server 409 qaytaradi va
 *   sababini aytadi; bu yerda takrorlanmaydi.
 */
const props = defineProps<{ studentId: number }>()

const queryClient = useQueryClient()

const periodsQuery = useQuery({
  queryKey: computed(() => ['subscriptions', 'student', props.studentId]),
  queryFn: ({ signal }) => fetchStudentSubscriptions(props.studentId, { signal }),
})
const plansQuery = useQuery({
  queryKey: ['subscriptions', 'plans'],
  queryFn: ({ signal }) => fetchSubscriptionPlans({ signal }),
})

const periods = computed(() => periodsQuery.data.value ?? [])
const activePlans = computed(() =>
  (plansQuery.data.value ?? []).filter((p) => p.isActive && p.kind === 'SelfStudy'),
)

const formOpen = ref(false)
const planId = ref<number | null>(null)
const method = ref<PaymentMethodName>('Cash')
const note = ref('')


function refresh(): void {
  void queryClient.invalidateQueries({ queryKey: ['subscriptions', 'student', props.studentId] })
  void queryClient.invalidateQueries({ queryKey: ['users', 'profile'] })
}

const grant = useMutation({
  mutationFn: () =>
    grantSubscription({
      studentId: props.studentId,
      planId: planId.value as number,
      method: method.value,
      note: note.value.trim() === '' ? null : note.value.trim(),
    }),
  onSuccess: (period) => {
    showToast(`Obuna ${formatDate(period.endsAt)} gacha berildi`, 'success')
    formOpen.value = false
    note.value = ''
    refresh()
  },
  onError: (error) => showToast(toUserMessage(error), 'error'),
})

const cancel = useMutation({
  mutationFn: (period: SubscriptionPeriodDto) => cancelSubscription(period.id, null),
  onSuccess: () => {
    showToast('Obuna bekor qilindi', 'success')
    refresh()
  },
  onError: (error) => showToast(toUserMessage(error), 'error'),
})

function canCancel(period: SubscriptionPeriodDto): boolean {
  const state = periodState(period)
  return state === 'active' || state === 'upcoming'
}
</script>

<template>
  <section class="rounded-2xl border border-line bg-ink-800 p-3.5">
    <header class="mb-3 flex items-center gap-2">
      <AppIcon
        name="book"
        :size="16"
        class="text-slate-400"
      />
      <h3 class="flex-1 text-sm font-semibold text-slate-100">
        Mustaqil o‘qish obunasi
      </h3>
      <BaseButton
        v-if="!formOpen"
        size="sm"
        variant="secondary"
        @click="formOpen = true"
      >
        {{ periods.length > 0 ? 'Uzaytirish' : 'Obuna berish' }}
      </BaseButton>
    </header>

    <form
      v-if="formOpen"
      class="mb-3 space-y-2.5 rounded-xl border border-line bg-ink-900 p-3"
      @submit.prevent="grant.mutate()"
    >
      <p
        v-if="activePlans.length === 0"
        class="text-xs text-slate-400"
      >
        Faol obuna rejasi yo‘q. Avval «Moliya» sahifasida reja yarating.
      </p>
      <template v-else>
        <label class="block space-y-1">
          <span class="text-xs text-slate-400">Reja</span>
          <select
            v-model="planId"
            required
            class="w-full rounded-lg bg-ink-800 px-3 py-2 text-sm text-slate-100 ring-1 ring-inset ring-line"
          >
            <option
              :value="null"
              disabled
            >
              Tanlang
            </option>
            <option
              v-for="plan in activePlans"
              :key="plan.id"
              :value="plan.id"
            >
              {{ plan.name }} — {{ plan.courseName }}, {{ plan.months }} oy, {{ formatMoney(plan.price) }}
            </option>
          </select>
        </label>
        <label class="block space-y-1">
          <span class="text-xs text-slate-400">To‘lov usuli</span>
          <select
            v-model="method"
            class="w-full rounded-lg bg-ink-800 px-3 py-2 text-sm text-slate-100 ring-1 ring-inset ring-line"
          >
            <option value="Cash">
              Naqd
            </option>
            <option value="Card">
              Karta
            </option>
          </select>
        </label>
        <label class="block space-y-1">
          <span class="text-xs text-slate-400">Izoh (ixtiyoriy)</span>
          <input
            v-model="note"
            maxlength="500"
            class="w-full rounded-lg bg-ink-800 px-3 py-2 text-sm text-slate-100 ring-1 ring-inset ring-line"
          >
        </label>
      </template>
      <div class="flex justify-end gap-2">
        <BaseButton
          size="sm"
          variant="ghost"
          type="button"
          @click="formOpen = false"
        >
          Bekor
        </BaseButton>
        <BaseButton
          size="sm"
          type="submit"
          :loading="grant.isPending.value"
          :disabled="planId === null"
        >
          To‘lov qabul qilindi — saqlash
        </BaseButton>
      </div>
    </form>

    <p
      v-if="periods.length === 0 && !formOpen"
      class="text-xs text-slate-400"
    >
      Obuna yo‘q. Mustaqil o‘qish faqat guruhsiz o‘quvchiga beriladi.
    </p>

    <ul
      v-else
      class="space-y-1.5"
    >
      <li
        v-for="period in periods"
        :key="period.id"
        class="flex items-center gap-2 rounded-lg border border-line bg-ink-900 px-3 py-2"
      >
        <span class="min-w-0 flex-1">
          <span class="block truncate text-[13px] text-slate-200">
            {{ period.courseName }} · {{ formatDate(period.startsAt) }} — {{ formatDate(period.endsAt) }}
          </span>
          <span class="block text-[11px] text-dim">
            {{ formatMoney(period.amount) }} · {{ period.method === 'Cash' ? 'naqd' : 'karta' }}
            <template v-if="period.note !== null"> · {{ period.note }}</template>
          </span>
        </span>
        <BaseBadge
          :tone="periodState(period) === 'active' ? 'success' : 'neutral'"
          size="xs"
        >
          {{ periodStateLabel(periodState(period)) }}
        </BaseBadge>
        <button
          v-if="canCancel(period)"
          type="button"
          class="rounded-md p-1 text-slate-400 hover:bg-ink-800 hover:text-rose-300"
          aria-label="Bekor qilish"
          :disabled="cancel.isPending.value"
          @click="cancel.mutate(period)"
        >
          <AppIcon
            name="close"
            :size="14"
          />
        </button>
      </li>
    </ul>
  </section>
</template>
