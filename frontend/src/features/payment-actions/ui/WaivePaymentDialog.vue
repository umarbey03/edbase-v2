<script setup lang="ts">
import { useMutation } from '@tanstack/vue-query'
import { computed, ref, watch } from 'vue'

import { debtAmount, periodLabel, waivePayment } from '@/entities/payment'
import { toUserMessage } from '@/shared/api'
import { formatSum } from '@/shared/lib/money'
import { useConfirm } from '@/shared/lib/useConfirm'
import type { PaymentDto } from '@/shared/types'
import { BaseButton, BaseField, BaseModal } from '@/shared/ui'

/**
 * Kechirim: pul olinmaydi, lekin oy qarz bo'lib qolmaydi.
 *
 * NEGA `window.confirm` EMAS (eski ilovada shunday edi): server 409 bilan
 * SABABNI matn qilib qaytaradi ("To'langan oyni kechirib bo'lmaydi"), brauzer
 * oynasi yopilgach esa uni ko'rsatadigan joy qolmasdi. Bu oyna xato kelganda
 * OCHIQ turadi.
 *
 * ★ SABAB MAYDONI — audit uchun. Serverda u MAJBURIY EMAS, lekin kechirim
 * pul olmasdan qarzni yopadigan amal: kim va nima uchun kechirganini keyin
 * hech qayerdan bilib bo'lmasdi.
 */
const props = defineProps<{ open: boolean; payment: PaymentDto | null }>()

const emit = defineEmits<{ close: []; saved: [] }>()

const confirm = useConfirm()

/** Baza ustuni `varchar(500)`; server tekshirmaydi (uzun matn 409 ga aylanadi). */
const REASON_MAX = 500

const reason = ref('')
const errorMessage = ref<string | null>(null)

watch(
  () => props.open,
  (isOpen) => {
    if (!isOpen) return
    reason.value = ''
    errorMessage.value = null
  },
  { immediate: true },
)

const mutation = useMutation({
  mutationFn: () => {
    const payment = props.payment
    if (payment === null) throw new Error('Yozuv tanlanmagan.')
    const trimmed = reason.value.trim()
    return waivePayment(payment.id, { reason: trimmed.length > 0 ? trimmed : null })
  },
  onSuccess: () => {
    emit('saved')
    emit('close')
  },
  onError: (error: Error) => {
    errorMessage.value = toUserMessage(error)
  },
})

const canSubmit = computed(
  () => props.payment !== null && reason.value.length <= REASON_MAX && !mutation.isPending.value,
)

/**
 * R4 — kechirim QAYTARILMAYDI, shuning uchun `danger` tasdiq.
 *
 * ★ "UNWAIVE" ENDPOINTI YO'Q (`payment-api.ts`: `waivePayment` bor, teskarisi
 * yo'q). Ya'ni noto'g'ri qatorda bosilgan "Kechirish" ni interfeys orqali
 * qaytarib bo'lmaydi — markaz o'sha pulni yo'qotadi. Bu oynaning o'zi
 * o'quvchi/oy/summani ko'rsatsa ham, u FORMA: xodim sabab yozib turib
 * "Kechirish" ni forma tugmasi sifatida bosadi. Tasdiq esa amalning
 * QAYTARILMASLIGINI aytadi — formada bunday jumla yo'q edi.
 */
async function submit(): Promise<void> {
  if (!canSubmit.value) return

  const payment = props.payment
  if (payment === null) return

  const ok = await confirm({
    title: 'Oyni kechirish',
    message:
      `${payment.studentName} — ${periodLabel(payment.period)} uchun `
      + `${formatSum(debtAmount(payment))} qarz hisoblanmaydi. Kechirimni bekor qilib bo‘lmaydi.`,
    confirmLabel: 'Kechirish',
    tone: 'danger',
    details: [payment.groupName, 'Pul olinmaydi — oy shunchaki yopiq deb belgilanadi.'],
  })
  if (!ok) return

  errorMessage.value = null
  mutation.mutate()
}
</script>

<template>
  <BaseModal
    :open="props.open"
    title="Oyni kechirish"
    @close="emit('close')"
  >
    <template v-if="props.payment !== null">
      <p class="text-sm leading-relaxed text-slate-300">
        <span
          class="font-semibold text-slate-100"
          v-text="props.payment.studentName"
        />
        — {{ periodLabel(props.payment.period) }} ({{ props.payment.groupName }}).
      </p>
      <p class="mt-1.5 text-xs text-slate-400">
        Pul olinmaydi, lekin qolgan
        <span
          class="font-semibold text-rose-300"
          v-text="formatSum(debtAmount(props.payment))"
        />
        qarz sifatida hisoblanmaydi.
      </p>

      <div class="mt-3.5">
        <BaseField
          label="Sababi"
          hint="Auditda saqlanadi."
          :error="reason.length > REASON_MAX ? `Sabab ${REASON_MAX} belgidan oshmasin.` : null"
        >
          <textarea
            v-model="reason"
            class="zn-input"
            rows="2"
            :maxlength="REASON_MAX"
            placeholder="Masalan: o‘quvchi kasal bo‘lib, oy davomida darsga kelmadi"
          />
        </BaseField>
      </div>

      <div
        v-if="errorMessage !== null"
        class="mt-3 rounded-lg border border-rose-500/25 bg-rose-500/10 p-3.5 text-xs leading-relaxed text-rose-200"
        role="alert"
        v-text="errorMessage"
      />
    </template>

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
        Kechirish
      </BaseButton>
    </template>
  </BaseModal>
</template>
