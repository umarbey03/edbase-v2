<script setup lang="ts">
import { useMutation } from '@tanstack/vue-query'
import { computed, ref, watch } from 'vue'

import { debtAmount, periodLabel, reversePayment } from '@/entities/payment'
import { toUserMessage } from '@/shared/api'
import { formatMoney, formatSum, parseMoneyInput } from '@/shared/lib/money'
import type { ReversalDto } from '@/shared/types'
import { BaseButton, BaseField, BaseModal } from '@/shared/ui'

/**
 * Pulni orqaga qaytarish.
 *
 * Server avval BALANSDAN, so'ng eng YANGI to'langan oylardan yechadi (eski
 * oylar yopiq qoladi).
 *
 * ★ QISMAN QAYTARISH — XATO EMAS. So'ralgan summaning bir qismi tizimda
 * umuman tushmagan bo'lishi mumkin; server 200 qaytaradi va qoldiqni
 * `unreturned` da aytadi. Shuning uchun natija ekrani ALOHIDA: "qaytarildi"
 * deb yopib qo'yish hisobni buzardi — kassir qo'lidan haqiqatda qancha pul
 * chiqishini bilmasdi.
 */
const props = defineProps<{ open: boolean; student: { id: number; name: string } | null }>()

const emit = defineEmits<{ close: []; saved: [] }>()

/** Server chegarasi: `PaymentService.MaxAmount`. */
const MAX_AMOUNT = 1_000_000_000
const REASON_MAX = 500

const amountText = ref('')
const reason = ref('')
const result = ref<ReversalDto | null>(null)
const errorMessage = ref<string | null>(null)

watch(
  () => [props.open, props.student] as const,
  ([isOpen]) => {
    if (isOpen !== true) return
    amountText.value = ''
    reason.value = ''
    result.value = null
    errorMessage.value = null
  },
  { immediate: true },
)

const amount = computed(() => parseMoneyInput(amountText.value))

const amountError = computed(() => {
  if (amountText.value.trim().length === 0) return null
  const value = amount.value
  if (value === null) return 'Summani raqam bilan kiriting.'
  if (value <= 0) return 'Summa musbat bo‘lishi kerak.'
  if (value > MAX_AMOUNT) return 'Summa juda katta — kiritishda xatolik bo‘lgan bo‘lishi mumkin.'
  return null
})

const canSubmit = computed(
  () =>
    props.student !== null &&
    amount.value !== null &&
    amountError.value === null &&
    reason.value.length <= REASON_MAX,
)

const mutation = useMutation({
  mutationFn: () => {
    const student = props.student
    const value = amount.value
    if (student === null || value === null) throw new Error('Ma’lumot to‘liq emas.')
    const trimmed = reason.value.trim()
    return reversePayment({
      studentId: student.id,
      amount: value,
      reason: trimmed.length > 0 ? trimmed : null,
    })
  },
  onSuccess: (data) => {
    result.value = data
    emit('saved')
  },
  onError: (error: Error) => {
    errorMessage.value = toUserMessage(error)
  },
})

function submit(): void {
  if (!canSubmit.value || mutation.isPending.value) return
  errorMessage.value = null
  mutation.mutate()
}

const affectedMonths = computed(() => result.value?.affectedMonths ?? [])
</script>

<template>
  <BaseModal
    :open="props.open"
    title="Pulni qaytarish"
    @close="emit('close')"
  >
    <!-- ---------------------------------------------------------- natija -->
    <div v-if="result !== null">
      <div
        v-if="result.unreturned > 0"
        class="rounded-lg border border-amber-500/30 bg-amber-500/10 p-3.5"
        role="alert"
      >
        <p class="text-sm font-semibold text-amber-200">
          Qisman qaytarildi
        </p>
        <p class="mt-1 text-xs leading-relaxed text-amber-100/90">
          So‘ralgan {{ formatSum(result.requested) }} dan
          <span
            class="font-semibold"
            v-text="formatSum(result.returned)"
          /> qaytarildi.
          Qolgan
          <span
            class="font-semibold"
            v-text="formatSum(result.unreturned)"
          />
          uchun tizimda tushgan pul topilmadi — kassadan shuncha kam bering.
        </p>
      </div>
      <p
        v-else
        class="rounded-lg border border-green-500/30 bg-green-500/10 p-3.5 text-sm text-green-200"
      >
        {{ formatSum(result.returned) }} to‘liq qaytarildi.
      </p>

      <dl class="mt-3 grid grid-cols-2 gap-2.5">
        <div class="rounded-lg border border-line bg-ink-800 p-3">
          <dd
            class="text-base font-bold tabular-nums text-slate-100"
            v-text="formatMoney(result.fromBalance)"
          />
          <dt class="mt-0.5 text-[11px] text-slate-400">
            balansdan yechildi
          </dt>
        </div>
        <div class="rounded-lg border border-line bg-ink-800 p-3">
          <dd
            class="text-base font-bold tabular-nums text-slate-100"
            v-text="formatMoney(result.fromPayments)"
          />
          <dt class="mt-0.5 text-[11px] text-slate-400">
            to‘langan oylardan
          </dt>
        </div>
        <div class="rounded-lg border border-line bg-ink-800 p-3">
          <dd
            class="text-base font-bold tabular-nums text-brand-400"
            v-text="formatMoney(result.balance)"
          />
          <dt class="mt-0.5 text-[11px] text-slate-400">
            qolgan balans
          </dt>
        </div>
        <div class="rounded-lg border border-line bg-ink-800 p-3">
          <dd
            class="text-base font-bold tabular-nums"
            :class="result.debtAfter > 0 ? 'text-rose-400' : 'text-green-400'"
            v-text="formatMoney(result.debtAfter)"
          />
          <dt class="mt-0.5 text-[11px] text-slate-400">
            qarz
          </dt>
        </div>
      </dl>

      <div
        v-if="affectedMonths.length > 0"
        class="mt-3"
      >
        <p class="mb-1.5 text-xs font-semibold text-slate-300">
          Qayta ochilgan oylar
        </p>
        <ul class="divide-y divide-line rounded-lg border border-line">
          <li
            v-for="month in affectedMonths"
            :key="month.id"
            class="flex items-center justify-between gap-3 px-3 py-2"
          >
            <span class="min-w-0 flex-1 truncate text-xs text-slate-300">
              {{ periodLabel(month.period) }} · {{ month.groupName }}
            </span>
            <span class="shrink-0 text-xs tabular-nums text-rose-300">
              qarz {{ formatMoney(debtAmount(month)) }}
            </span>
          </li>
        </ul>
      </div>
    </div>

    <!-- ----------------------------------------------------------- forma -->
    <form
      v-else
      novalidate
      @submit.prevent="submit"
    >
      <p class="text-sm text-slate-300">
        <span
          class="font-semibold text-slate-100"
          v-text="props.student?.name ?? '—'"
        />
      </p>
      <p class="mt-1 text-xs leading-relaxed text-slate-400">
        Pul avval balansdan, so‘ng eng yangi to‘langan oylardan yechiladi. Kechirilgan oylarga
        tegilmaydi.
      </p>

      <div class="mt-3.5">
        <BaseField
          label="Qaytariladigan summa (so‘m)"
          :error="amountError"
          :hint="amount === null ? '' : formatSum(amount)"
        >
          <input
            v-model="amountText"
            class="zn-input tabular-nums"
            type="text"
            inputmode="numeric"
            autocomplete="off"
            placeholder="540000"
          >
        </BaseField>
      </div>

      <div class="mt-3">
        <BaseField
          label="Sababi"
          :error="reason.length > REASON_MAX ? `Sabab ${REASON_MAX} belgidan oshmasin.` : null"
        >
          <textarea
            v-model="reason"
            class="zn-input"
            rows="2"
            :maxlength="REASON_MAX"
            placeholder="Masalan: o‘quvchi kursni tark etdi"
          />
        </BaseField>
      </div>

      <p
        v-if="errorMessage !== null"
        class="mt-3 text-xs text-rose-400"
        role="alert"
        v-text="errorMessage"
      />
    </form>

    <template #footer>
      <template v-if="result !== null">
        <BaseButton @click="emit('close')">
          Yopish
        </BaseButton>
      </template>
      <template v-else>
        <BaseButton
          variant="secondary"
          @click="emit('close')"
        >
          Bekor qilish
        </BaseButton>
        <BaseButton
          variant="danger"
          :disabled="!canSubmit"
          :loading="mutation.isPending.value"
          @click="submit"
        >
          Pulni qaytarish
        </BaseButton>
      </template>
    </template>
  </BaseModal>
</template>
