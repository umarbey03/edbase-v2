<script setup lang="ts">
import { useMutation } from '@tanstack/vue-query'
import { computed, ref, watch } from 'vue'

import { createPayrollAdjustment } from '@/entities/payroll'
import { toUserMessage } from '@/shared/api'
import { formatSum, parseMoneyInput } from '@/shared/lib/money'
import { BaseButton, BaseField, BaseModal } from '@/shared/ui'

/**
 * Qo'lda tuzatish qo'shish (2026-08-16, loyiha egasi: "qo'lda tuzatish
 * (bonus/ushlab qolish)") — Tutorbase uslubidagi "Manual Adjustments... reason
 * tracking for transparency". Ishorasi ma'noni bildiradi: musbat — bonus,
 * manfiy — ushlab qolish (alohida "turi" tanlovi shart emas).
 *
 * Faqat Draft davrda ochiladi — `PayrollDetailDialog` tugmani Approved/Paid
 * holatda ko'rsatmaydi (server ham `EnsureDraftAsync` bilan bloklaydi).
 */
const props = defineProps<{ open: boolean; userId: number | null; period: string }>()

const emit = defineEmits<{ close: []; saved: [] }>()

const MAX_AMOUNT = 1_000_000_000

const kind = ref<'bonus' | 'deduction'>('bonus')
const amountText = ref('')
const reason = ref('')
const errorMessage = ref<string | null>(null)

function resetForm(): void {
  kind.value = 'bonus'
  amountText.value = ''
  reason.value = ''
  errorMessage.value = null
}

watch(() => props.open, (open) => {
  if (open) resetForm()
})

const amount = computed(() => parseMoneyInput(amountText.value))

const amountError = computed(() => {
  if (amountText.value.trim().length === 0) return null
  if (amount.value === null) return 'Summani raqam bilan kiriting (masalan 50000).'
  if (amount.value > MAX_AMOUNT) return 'Summa juda katta.'
  return null
})

const canSubmit = computed(
  () =>
    props.userId !== null &&
    amount.value !== null &&
    amount.value > 0 &&
    amountError.value === null &&
    reason.value.trim().length > 0,
)

const mutation = useMutation({
  mutationFn: () => {
    const userId = props.userId
    const value = amount.value
    if (userId === null || value === null) throw new Error('Forma to‘liq emas.')

    return createPayrollAdjustment({
      userId,
      period: props.period,
      amount: kind.value === 'bonus' ? value : -value,
      reason: reason.value.trim(),
    })
  },
  onSuccess: () => {
    emit('saved')
    emit('close')
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
</script>

<template>
  <BaseModal
    :open="props.open"
    title="Qo‘lda tuzatish qo‘shish"
    @close="emit('close')"
  >
    <form
      novalidate
      @submit.prevent="submit"
    >
      <div class="flex gap-2">
        <label
          class="flex-1 cursor-pointer rounded-lg border px-3 py-2.5 text-center text-sm font-semibold transition-colors"
          :class="kind === 'bonus'
            ? 'border-emerald-500/40 bg-emerald-500/10 text-emerald-300'
            : 'border-line text-slate-400 hover:bg-ink-800'"
        >
          <input
            v-model="kind"
            type="radio"
            value="bonus"
            class="sr-only"
          >
          + Bonus
        </label>
        <label
          class="flex-1 cursor-pointer rounded-lg border px-3 py-2.5 text-center text-sm font-semibold transition-colors"
          :class="kind === 'deduction'
            ? 'border-rose-500/40 bg-rose-500/10 text-rose-300'
            : 'border-line text-slate-400 hover:bg-ink-800'"
        >
          <input
            v-model="kind"
            type="radio"
            value="deduction"
            class="sr-only"
          >
          − Ushlab qolish
        </label>
      </div>

      <BaseField
        class="mt-3"
        label="Summa (so‘m)"
        :error="amountError"
        :hint="amount === null ? '' : formatSum(amount)"
      >
        <input
          v-model="amountText"
          class="zn-input tabular-nums"
          type="text"
          inputmode="numeric"
          autocomplete="off"
          placeholder="50000"
        >
      </BaseField>

      <BaseField
        class="mt-3"
        label="Sabab"
        hint="Xodim va keyingi tekshiruvchi uchun — nima uchun bu tuzatish qo‘shilgani."
      >
        <textarea
          v-model="reason"
          class="zn-input"
          rows="3"
          maxlength="500"
          placeholder="Masalan: yanvar oyida qo‘shimcha smena uchun rag‘bat"
        />
      </BaseField>

      <p
        v-if="errorMessage !== null"
        class="mt-3 text-xs text-rose-400"
        role="alert"
        v-text="errorMessage"
      />
    </form>

    <template #footer>
      <BaseButton
        variant="secondary"
        @click="emit('close')"
      >
        Bekor qilish
      </BaseButton>
      <BaseButton
        :disabled="!canSubmit"
        :loading="mutation.isPending.value"
        @click="submit"
      >
        Qo‘shish
      </BaseButton>
    </template>
  </BaseModal>
</template>
