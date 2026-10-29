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
  REFUND_MODE_LABELS,
} from '@/entities/subscription'
import type { SubscriptionPeriodDto, SubscriptionRefundModeName } from '@/entities/subscription'
import { toUserMessage } from '@/shared/api'
import { formatDate } from '@/shared/lib/datetime'
import { formatMoney } from '@/shared/lib/money'
import { useConfirm } from '@/shared/lib/useConfirm'
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

/*
  ══════════════════════════════════════════════════════════════════════
   BEKOR QILISH VA PULNI QAYTARISH (2026-10-29, loyiha egasi qarori Q-5)
  ══════════════════════════════════════════════════════════════════════
  🔴 ILGARI: «×» davrni jimgina bekor qilardi — pul qaytarildimi yoki
    yo'qmi, hech qayerda yozilmasdi (audit P0-2).
  ★ ENDI xodim tanlaydi: pul qaytarilmaydi, balansga o'tadi yoki naqd
    qaytarildi. Summa taklifi — davrning HOZIRGI ishlatilmagan qiymati
    (server hisoblaydi, `unusedValue`), lekin xodim o'zgartira oladi
    (davr summasidan ko'p emas — server ham tekshiradi). Har qaytarish
    moliya jurnaliga yoziladi.
*/
const cancelling = ref<SubscriptionPeriodDto | null>(null)
const refundMode = ref<SubscriptionRefundModeName>('ToBalance')
const refundAmount = ref<number | null>(null)
const cancelReason = ref('')

const REFUND_MODES: readonly SubscriptionRefundModeName[] = ['ToBalance', 'Cash', 'None']

function openCancel(period: SubscriptionPeriodDto): void {
  cancelling.value = period
  refundMode.value = period.unusedValue > 0 ? 'ToBalance' : 'None'
  refundAmount.value = period.unusedValue > 0 ? period.unusedValue : null
  cancelReason.value = ''
}

const refundInvalid = computed(() => {
  const period = cancelling.value
  if (period === null || refundMode.value === 'None') return false
  const amount = refundAmount.value
  return amount === null || !(amount > 0) || amount > period.amount
})

const cancel = useMutation({
  mutationFn: (period: SubscriptionPeriodDto) =>
    cancelSubscription(period.id, {
      reason: cancelReason.value.trim() === '' ? null : cancelReason.value.trim(),
      refundMode: refundMode.value,
      refundAmount: refundMode.value === 'None' ? null : refundAmount.value,
    }),
  onSuccess: () => {
    showToast('Obuna bekor qilindi', 'success')
    cancelling.value = null
    refresh()
    // Balans va jurnal o'zgargan bo'lishi mumkin.
    void queryClient.invalidateQueries({ queryKey: ['payments'] })
  },
  onError: (error) => showToast(toUserMessage(error), 'error'),
})

const confirm = useConfirm()

async function submitCancel(): Promise<void> {
  const period = cancelling.value
  if (period === null || refundInvalid.value) return

  const money = refundMode.value === 'None'
    ? 'Pul qaytarilmaydi.'
    : refundMode.value === 'ToBalance'
      ? `${formatMoney(refundAmount.value ?? 0)} o‘quvchi balansiga o‘tkaziladi.`
      : `${formatMoney(refundAmount.value ?? 0)} naqd qaytarilgan deb yoziladi.`

  const ok = await confirm({
    title: 'Obunani bekor qilish',
    message: `${period.courseName}: ${formatDate(period.startsAt)} — ${formatDate(period.endsAt)} davri bekor qilinadi.`,
    confirmLabel: 'Bekor qilish',
    cancelLabel: 'Ortga',
    tone: 'danger',
    details: [money, 'Darslarga kirish shu zahoti yopiladi.', 'Yozuv moliya jurnalida qoladi.'],
  })
  if (ok) cancel.mutate(period)
}

function canCancel(period: SubscriptionPeriodDto): boolean {
  const state = periodState(period)
  return state === 'active' || state === 'upcoming'
}

function methodLabel(period: SubscriptionPeriodDto): string {
  if (period.method === null) return 'balansdan'
  return period.method === 'Cash' ? 'naqd' : 'karta'
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
        class="rounded-lg border border-line bg-ink-900 px-3 py-2"
      >
        <div class="flex items-center gap-2">
          <span class="min-w-0 flex-1">
            <span class="block truncate text-[13px] text-slate-200">
              <template v-if="period.planName !== null">{{ period.planName }} · </template>{{ period.courseName }}
              · {{ formatDate(period.startsAt) }} — {{ formatDate(period.endsAt) }}
            </span>
            <span class="block text-[11px] text-dim">
              {{ formatMoney(period.amount) }} · {{ methodLabel(period) }}
              <template v-if="period.note !== null"> · {{ period.note }}</template>
              <template v-if="period.cancelReason !== null"> · {{ period.cancelReason }}</template>
            </span>
          </span>
          <BaseBadge
            :tone="periodState(period) === 'active' ? 'success' : 'neutral'"
            size="xs"
          >
            {{ periodStateLabel(periodState(period)) }}
          </BaseBadge>
          <button
            v-if="canCancel(period) && cancelling?.id !== period.id"
            type="button"
            class="rounded-md p-1 text-slate-400 hover:bg-ink-800 hover:text-rose-300"
            aria-label="Bekor qilish"
            :disabled="cancel.isPending.value"
            @click="openCancel(period)"
          >
            <AppIcon
              name="close"
              :size="14"
            />
          </button>
        </div>

        <!-- Bekor qilish: pul qayerga ketadi (Q-5). -->
        <form
          v-if="cancelling?.id === period.id"
          class="mt-2 space-y-2 border-t border-line pt-2"
          @submit.prevent="submitCancel()"
        >
          <p class="text-[11px] text-slate-400">
            Ishlatilmagan qismi hozir: <span class="font-semibold text-slate-200">{{ formatMoney(period.unusedValue) }}</span>
          </p>
          <label class="block space-y-1">
            <span class="text-xs text-slate-400">Pul</span>
            <select
              v-model="refundMode"
              class="w-full rounded-lg bg-ink-800 px-3 py-2 text-sm text-slate-100 ring-1 ring-inset ring-line"
            >
              <option
                v-for="mode in REFUND_MODES"
                :key="mode"
                :value="mode"
              >
                {{ REFUND_MODE_LABELS[mode] }}
              </option>
            </select>
          </label>
          <label
            v-if="refundMode !== 'None'"
            class="block space-y-1"
          >
            <span class="text-xs text-slate-400">Summa (ko‘pi bilan {{ formatMoney(period.amount) }})</span>
            <input
              v-model.number="refundAmount"
              type="number"
              min="1"
              :max="period.amount"
              step="1"
              inputmode="numeric"
              class="w-full rounded-lg bg-ink-800 px-3 py-2 text-sm text-slate-100 ring-1 ring-inset ring-line"
            >
          </label>
          <label class="block space-y-1">
            <span class="text-xs text-slate-400">Sabab (ixtiyoriy)</span>
            <input
              v-model="cancelReason"
              maxlength="500"
              class="w-full rounded-lg bg-ink-800 px-3 py-2 text-sm text-slate-100 ring-1 ring-inset ring-line"
            >
          </label>
          <div class="flex justify-end gap-2">
            <BaseButton
              size="sm"
              variant="ghost"
              type="button"
              @click="cancelling = null"
            >
              Ortga
            </BaseButton>
            <BaseButton
              size="sm"
              type="submit"
              :loading="cancel.isPending.value"
              :disabled="refundInvalid"
            >
              Bekor qilish
            </BaseButton>
          </div>
        </form>
      </li>
    </ul>
  </section>
</template>
