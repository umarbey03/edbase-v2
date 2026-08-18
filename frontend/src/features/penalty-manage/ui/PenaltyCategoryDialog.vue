<script setup lang="ts">
import { useMutation, useQueryClient } from '@tanstack/vue-query'
import { computed, ref, watch } from 'vue'

import { createPenaltyCategory, updatePenaltyCategory } from '@/entities/penalty'
import { toUserMessage } from '@/shared/api'
import { parseMoneyInput } from '@/shared/lib/money'
import type { PenaltyCategoryDto } from '@/shared/types'
import { BaseButton, BaseField, BaseModal } from '@/shared/ui'

/**
 * JARIMA TARIFI — qo'shish/tahrirlash (2026-08-18).
 *
 * ★ `BaseModal`, `BaseDrawer` EMAS: bu to'rt maydonli qisqa forma va u
 * "Tariflar" jadvali USTIDAN ochiladi. Jadvalning o'zi sahifada turadi,
 * ya'ni drawer ichida drawer muammosi yo'q — lekin markazlashgan oyna
 * shu hajmdagi formaga ko'proq mos (loyihadagi AYNI mezon).
 */
const props = defineProps<{ open: boolean; category: PenaltyCategoryDto | null }>()

const emit = defineEmits<{ close: [] }>()

const queryClient = useQueryClient()

const label = ref('')
const amountText = ref('')
const perUnit = ref(false)
const unitLabel = ref('')
const isActive = ref(true)
const errorMessage = ref<string | null>(null)

const isEdit = computed(() => props.category !== null)

/** Tizim tarifi — nomi va turi qulflangan (kod uni kalit bo'yicha izlaydi). */
const isSystem = computed(() => props.category?.isSystem ?? false)

watch(
  () => props.open,
  (open) => {
    if (!open) return

    const source = props.category
    label.value = source?.label ?? ''
    amountText.value = source === null || source === undefined ? '' : String(source.amount)
    perUnit.value = source?.perUnit ?? false
    unitLabel.value = source?.unitLabel ?? ''
    isActive.value = source?.isActive ?? true
    errorMessage.value = null
  },
)

const amount = computed(() => parseMoneyInput(amountText.value))

const saveMutation = useMutation({
  mutationFn: () => {
    const body = {
      label: label.value.trim(),
      amount: amount.value!,
      perUnit: perUnit.value,
      unitLabel: perUnit.value ? unitLabel.value.trim() : null,
      isActive: isActive.value,
    }

    return props.category === null
      ? createPenaltyCategory(body)
      : updatePenaltyCategory(props.category.id, body)
  },
  onSuccess: () => {
    void queryClient.invalidateQueries({ queryKey: ['penalty-categories'] })

    // 🔴 JARIMALAR JADVALI HAM ESKIRADI: tarif nomi o'sha yerda
    // ko'rsatiladi — qayta nomlangach eski nom qolib ketmasin.
    void queryClient.invalidateQueries({ queryKey: ['penalties'] })
    emit('close')
  },
  onError: (error: Error) => {
    errorMessage.value = toUserMessage(error)
  },
})

function handleSubmit(): void {
  errorMessage.value = null

  if (label.value.trim().length === 0) {
    errorMessage.value = 'Tarif nomini kiriting.'
    return
  }

  // ★ `0` RUXSAT: tizim tarifida bu "avtomatik jarimani to'xtatish"
  //   degani. Shuning uchun shart `< 0`, `<= 0` emas.
  if (amount.value === null || amount.value < 0) {
    errorMessage.value = 'Summani to‘g‘ri kiriting.'
    return
  }

  if (perUnit.value && unitLabel.value.trim().length === 0) {
    errorMessage.value = 'Birlik nomini kiriting (masalan: daqiqa).'
    return
  }

  saveMutation.mutate()
}
</script>

<template>
  <BaseModal
    :open="props.open"
    :title="isEdit ? 'Tarifni tahrirlash' : 'Yangi tarif'"
    @close="emit('close')"
  >
    <p
      v-if="isSystem"
      class="rounded-lg border border-sky-500/25 bg-sky-500/10 px-3 py-2 text-xs text-sky-200"
    >
      Bu — <span class="font-semibold">tizim tarifi</span>. Uni avtomatik jarima
      ishlatadi, shuning uchun nomi va hisoblash usuli qulflangan. Avtomatik
      jarimani to‘xtatish uchun summasini <span class="font-semibold">0</span> qiling.
    </p>

    <div class="mt-3 space-y-3">
      <BaseField label="Nomi">
        <input
          v-model="label"
          class="zn-input"
          maxlength="100"
          :disabled="isSystem"
          placeholder="masalan: Darsga kechikish"
        >
      </BaseField>

      <BaseField
        :label="perUnit ? `Summa (so‘m / ${unitLabel.trim() || 'birlik'})` : 'Summa (so‘m)'"
        hint="0 — bu tarif bo‘yicha jarima yozilmaydi."
      >
        <input
          v-model="amountText"
          class="zn-input"
          inputmode="numeric"
          placeholder="masalan: 50000"
        >
      </BaseField>

      <label
        class="flex cursor-pointer items-start gap-2.5"
        :class="isSystem ? 'cursor-not-allowed opacity-60' : ''"
      >
        <input
          v-model="perUnit"
          type="checkbox"
          class="mt-0.5"
          :disabled="isSystem"
        >
        <span class="text-sm text-slate-200">
          Songa qarab hisoblansin
          <span class="block text-xs text-slate-400">
            Summa = tarif × miqdor. Jarima kiritilayotganda miqdor so‘raladi.
          </span>
        </span>
      </label>

      <BaseField
        v-if="perUnit"
        label="Birlik nomi"
        hint="Jarima oynasida “Necha daqiqa?” deb so‘raladi."
      >
        <input
          v-model="unitLabel"
          class="zn-input"
          maxlength="30"
          :disabled="isSystem"
          placeholder="daqiqa, soat, dona..."
        >
      </BaseField>

      <label
        v-if="isEdit && !isSystem"
        class="flex cursor-pointer items-start gap-2.5"
      >
        <input
          v-model="isActive"
          type="checkbox"
          class="mt-0.5"
        >
        <span class="text-sm text-slate-200">
          Faol
          <span class="block text-xs text-slate-400">
            O‘chirilsa yangi jarimada tanlanmaydi, lekin eski yozuvlarda nomi qoladi.
          </span>
        </span>
      </label>
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
        :loading="saveMutation.isPending.value"
        @click="handleSubmit"
      >
        Saqlash
      </BaseButton>
    </template>
  </BaseModal>
</template>
