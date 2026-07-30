<script setup lang="ts">
import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { computed, ref, watch } from 'vue'

import { BLOCK_SCOPE_OPTIONS, fetchFinanceSettings, updateFinanceSettings } from '@/entities/payment'
import { toUserMessage } from '@/shared/api'
import { formatSum, parseMoneyInput } from '@/shared/lib/money'
import type { PaymentBlockScopeName } from '@/shared/types'
import { BaseButton, BaseCard, BaseField, BaseSpinner } from '@/shared/ui'

/**
 * Blok sozlamalari (eski ilovadagi `#ps-modal`).
 *
 * ★ `PUT /payments/settings` — TO'LIQ ALMASHTIRISH. Ikkala maydon ham HAR
 * DOIM yuboriladi. Faqat chegarani o'zgartirib, qamrovni yubormasak, u
 * standart qiymatga (`None`) tushib, bloklash jimgina o'chib qolardi.
 * Shuning uchun forma avval mavjud qiymatlarni YUKLAYDI.
 *
 * ★ `enforce` — FAQAT O'QISH uchun. U muhit xossasi (`Payments:EnforceBlock`)
 * va shu yerdan o'zgartirilmaydi; server uni javobda qaytaradi, lekin
 * so'rovda kutmaydi. Uni forma maydoni qilib qo'ysak, xodim uni o'zgartirdim
 * deb o'ylab, natijani ko'rmasdi.
 */
const queryClient = useQueryClient()

const thresholdText = ref('')
const scope = ref<PaymentBlockScopeName>('Video')
const errorMessage = ref<string | null>(null)
const savedNote = ref<string | null>(null)

const settingsQuery = useQuery({
  queryKey: ['payments', 'settings'],
  queryFn: ({ signal }) => fetchFinanceSettings({ signal }),
})

const settings = computed(() => settingsQuery.data.value ?? null)

/* Server qiymatlari kelganda forma to'ldiriladi (va saqlangandan keyin ham). */
watch(
  settings,
  (value) => {
    if (value === null) return
    thresholdText.value = String(value.blockThreshold)
    scope.value = value.blockScope
  },
  { immediate: true },
)

const threshold = computed(() => parseMoneyInput(thresholdText.value))

/** Server qoidasi: `BlockThreshold` manfiy bo'lmasin. Yuqori chegara YO'Q. */
const thresholdError = computed(() => {
  if (thresholdText.value.trim().length === 0) return null
  const value = threshold.value
  if (value === null) return 'Chegarani raqam bilan kiriting (masalan 540000).'
  if (value < 0) return 'Chegara manfiy bo‘lmaydi.'
  return null
})

const mutation = useMutation({
  mutationFn: () => {
    const value = threshold.value
    if (value === null) throw new Error('Chegara kiritilmagan.')
    return updateFinanceSettings({ blockThreshold: value, blockScope: scope.value })
  },
  onSuccess: () => {
    savedNote.value = 'Sozlamalar saqlandi.'
    void queryClient.invalidateQueries({ queryKey: ['payments'] })
  },
  onError: (error: Error) => {
    errorMessage.value = toUserMessage(error)
  },
})

const canSubmit = computed(
  () => threshold.value !== null && thresholdError.value === null && !mutation.isPending.value,
)

function submit(): void {
  if (!canSubmit.value) return
  errorMessage.value = null
  savedNote.value = null
  mutation.mutate()
}
</script>

<template>
  <BaseCard
    title="Blok sozlamalari"
    subtitle="Qarzi chegaradan oshgan o‘quvchiga tanlangan qism yopiladi. Istisno qilingan o‘quvchilarga ta’sir qilmaydi."
  >
    <div
      v-if="settingsQuery.isPending.value"
      class="flex justify-center py-6"
    >
      <BaseSpinner />
    </div>

    <p
      v-else-if="settingsQuery.error.value !== null"
      class="text-xs text-rose-400"
      role="alert"
      v-text="toUserMessage(settingsQuery.error.value)"
    />

    <form
      v-else
      novalidate
      @submit.prevent="submit"
    >
      <div class="grid gap-3 sm:grid-cols-2">
        <BaseField
          label="Chegara summasi (so‘m)"
          :error="thresholdError"
          :hint="threshold === null ? '' : formatSum(threshold)"
        >
          <input
            v-model="thresholdText"
            class="zn-input tabular-nums"
            type="text"
            inputmode="numeric"
            autocomplete="off"
          >
        </BaseField>
        <BaseField label="Nima yopilsin?">
          <select
            v-model="scope"
            class="zn-input"
          >
            <option
              v-for="option in BLOCK_SCOPE_OPTIONS"
              :key="option.value"
              :value="option.value"
            >
              {{ option.label }}
            </option>
          </select>
        </BaseField>
      </div>

      <p
        v-if="settings !== null"
        class="mt-3 rounded-lg border border-line bg-ink-800 p-3 text-xs leading-relaxed"
        :class="settings.enforce ? 'text-slate-300' : 'text-amber-200'"
      >
        <template v-if="settings.enforce">
          Blok rejimi <span class="font-semibold text-green-400">yoqilgan</span> — chegaradan
          oshgan qarz tanlangan qismni yopadi.
        </template>
        <template v-else>
          Blok rejimi <span class="font-semibold text-amber-400">o‘chiq</span> — qarz hisoblanadi
          va ko‘rsatiladi, lekin hech kim bloklanmaydi. Yoqish serverda amalga oshiriladi
          (<span class="font-mono">Payments:EnforceBlock</span>), bu yerdan o‘zgartirilmaydi.
        </template>
      </p>

      <p
        v-if="errorMessage !== null"
        class="mt-2.5 text-xs text-rose-400"
        role="alert"
        v-text="errorMessage"
      />
      <p
        v-else-if="savedNote !== null"
        class="mt-2.5 text-xs text-green-400"
        v-text="savedNote"
      />

      <div class="mt-3.5 flex justify-end">
        <BaseButton
          :disabled="!canSubmit"
          :loading="mutation.isPending.value"
          @click="submit"
        >
          Saqlash
        </BaseButton>
      </div>
    </form>
  </BaseCard>
</template>
