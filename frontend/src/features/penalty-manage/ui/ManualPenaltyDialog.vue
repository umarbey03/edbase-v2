<script setup lang="ts">
import { useMutation, useQuery } from '@tanstack/vue-query'
import { computed, ref, watch } from 'vue'

import { createManualPenalty } from '@/entities/penalty'
import { fetchUsers, USER_SEARCH_MIN } from '@/entities/user'
import { toUserMessage } from '@/shared/api'
import { useDebounced } from '@/shared/lib/debounce'
import { parseMoneyInput } from '@/shared/lib/money'
import { BaseButton, BaseField, BaseModal, SearchSelect } from '@/shared/ui'

/**
 * QO'LDA JARIMA KIRITISH (2026-08-18).
 *
 * ★ FAQAT USTOZ VA KURATOR tanlanadi — server ham shuni tekshiradi
 * (o'quvchiga yoki adminga jarima ma'nosiz). Shuning uchun xodim
 * qidiruvi rol bo'yicha ikki so'rovga bo'linmaydi: `fetchUsers` bitta
 * rol filtrini oladi, biz esa IKKI rol kerakligi uchun filtrsiz
 * so'rab, natijani mijozda saralaymiz.
 *
 * ★ QO'LDA KIRITILGAN JARIMA HAM `Kutilmoqda` bo'ladi: avtomatik
 * jarima bilan AYNI yo'ldan o'tadi va oylikka faqat admin
 * tasdiqlagach tushadi. Aks holda ikki xil qoida bo'lardi.
 */
const props = defineProps<{ open: boolean }>()

const emit = defineEmits<{ close: []; saved: [] }>()

const search = ref('')
const debouncedSearch = useDebounced(search)
const selected = ref<{ id: number; name: string } | null>(null)
const amountText = ref('')
const reason = ref('')
const errorMessage = ref<string | null>(null)

watch(
  () => props.open,
  () => {
    search.value = ''
    selected.value = null
    amountText.value = ''
    reason.value = ''
    errorMessage.value = null
  },
)

const effectiveSearch = computed(() => {
  const term = debouncedSearch.value.trim()
  return term.length >= USER_SEARCH_MIN ? term : undefined
})

const staffQuery = useQuery({
  queryKey: ['users', 'penalty-staff', effectiveSearch],
  queryFn: ({ signal }) =>
    fetchUsers({ search: effectiveSearch.value, isActive: true, pageSize: 50 }, { signal }),
  enabled: computed(() => props.open),
})

/** Faqat ustoz va kurator (server ham shuni talab qiladi). */
const staffOptions = computed(() => {
  const list = (staffQuery.data.value?.items ?? [])
    .filter((user) => user.role === 'Teacher' || user.role === 'Assistant')
    .map((user) => ({ id: user.id, name: `${user.fullName} · ${user.role === 'Teacher' ? 'Ustoz' : 'Kurator'}` }))

  // Tanlangan xodim ro'yxatdan chiqib ketmasin (qidiruv o'zgarganda).
  const picked = selected.value
  if (picked !== null && !list.some((option) => option.id === picked.id)) return [picked, ...list]

  return list
})

const amount = computed(() => parseMoneyInput(amountText.value))

const createMutation = useMutation({
  mutationFn: () =>
    createManualPenalty({
      userId: selected.value!.id,
      amount: amount.value!,
      reason: reason.value.trim(),
    }),
  onSuccess: () => {
    emit('saved')
    emit('close')
  },
  onError: (error: Error) => {
    errorMessage.value = toUserMessage(error)
  },
})

function handleSubmit(): void {
  errorMessage.value = null

  if (selected.value === null) {
    errorMessage.value = 'Xodimni tanlang.'
    return
  }

  if (amount.value === null || amount.value <= 0) {
    errorMessage.value = 'Summani to‘g‘ri kiriting (musbat son).'
    return
  }

  if (reason.value.trim().length === 0) {
    errorMessage.value = 'Jarima sababini kiriting.'
    return
  }

  createMutation.mutate()
}
</script>

<template>
  <BaseModal
    :open="props.open"
    title="Qo‘lda jarima kiritish"
    @close="emit('close')"
  >
    <p class="text-sm text-slate-300">
      Jarima <span class="font-semibold">“Kutilmoqda”</span> holatida yoziladi va
      oylikka <span class="font-semibold">tasdiqlangandan keyin</span> tushadi.
    </p>

    <div class="mt-3 space-y-3">
      <BaseField
        label="Xodim"
        hint="Faqat ustoz va kurator"
      >
        <SearchSelect
          v-model="selected"
          :search="search"
          :options="staffOptions"
          :loading="staffQuery.isFetching.value"
          placeholder="Ism bo‘yicha qidirish"
          label="Xodimni tanlash"
          @update:search="search = $event"
        />
      </BaseField>

      <BaseField
        label="Summa (so‘m)"
        hint="Musbat son — oylikdan ushlab qolinadi."
      >
        <input
          v-model="amountText"
          class="zn-input"
          inputmode="numeric"
          placeholder="masalan: 50000"
        >
      </BaseField>

      <BaseField label="Sabab">
        <textarea
          v-model="reason"
          class="zn-input min-h-20"
          maxlength="500"
          rows="2"
          placeholder="Nima uchun jarima yozilyapti?"
        />
      </BaseField>
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
        variant="danger"
        :loading="createMutation.isPending.value"
        @click="handleSubmit"
      >
        Jarima yozish
      </BaseButton>
    </template>
  </BaseModal>
</template>
