<script setup lang="ts">
import { useMutation, useQuery } from '@tanstack/vue-query'
import { computed, ref, watch } from 'vue'

import { createManualPenalty, fetchPenaltyCategories } from '@/entities/penalty'
import { fetchUsers, USER_SEARCH_MIN } from '@/entities/user'
import { toUserMessage } from '@/shared/api'
import { useDebounced } from '@/shared/lib/debounce'
import { formatMoney, parseMoneyInput } from '@/shared/lib/money'
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
 *
 * ★ SUMMA — TARIFDAN (2026-08-18): tarif tanlansa summa AVTOMATIK
 * hisoblanadi va maydon o'qish uchun bo'lib qoladi. Sabab: bir xil
 * qoidabuzarlikka har safar boshqa raqam yozilsa, jarima adolatsiz
 * ko'rinardi va ustoz bilan bahsda asos qolmasdi. "Tarifsiz" yo'l ham
 * saqlangan — takrorlanmaydigan bir martalik holatlar uchun.
 */
const props = defineProps<{ open: boolean }>()

const emit = defineEmits<{ close: []; saved: [] }>()

const search = ref('')
const debouncedSearch = useDebounced(search)
const selected = ref<{ id: number; name: string } | null>(null)
const categoryId = ref<number | ''>('')
const quantityText = ref('')
const amountText = ref('')
const reason = ref('')
const errorMessage = ref<string | null>(null)

watch(
  () => props.open,
  () => {
    search.value = ''
    selected.value = null
    categoryId.value = ''
    quantityText.value = ''
    amountText.value = ''
    reason.value = ''
    errorMessage.value = null
  },
)

/* ------------------------------------------------------------ tariflar */

const categoriesQuery = useQuery({
  // ★ FAQAT FAOL: arxivlangan tarif yangi jarimada tanlanmaydi
  //   (server ham rad etadi).
  queryKey: ['penalty-categories', 'active'],
  queryFn: ({ signal }) => fetchPenaltyCategories(true, { signal }),
  enabled: computed(() => props.open),
})

const categories = computed(() => categoriesQuery.data.value ?? [])

const category = computed(() =>
  categoryId.value === '' ? null : (categories.value.find((c) => c.id === categoryId.value) ?? null),
)

const quantity = computed(() => {
  const parsed = Number(quantityText.value.replace(',', '.'))
  return Number.isFinite(parsed) && parsed > 0 ? parsed : null
})

/** Tarif tanlangan bo'lsa summa HISOBLANADI, aks holda qo'lda kiritiladi. */
const computedAmount = computed<number | null>(() => {
  const picked = category.value
  if (picked === null) return parseMoneyInput(amountText.value)
  if (!picked.perUnit) return picked.amount

  return quantity.value === null ? null : picked.amount * quantity.value
})

// Tarif almashsa eski miqdor qolib ketmasin — "15 daqiqa" boshqa
// tarifga ko'chib, jimgina noto'g'ri summa berardi.
watch(categoryId, () => {
  quantityText.value = ''
})

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

const createMutation = useMutation({
  mutationFn: () =>
    createManualPenalty({
      userId: selected.value!.id,
      reason: reason.value.trim(),
      // Tarif berilsa server summani O'ZI hisoblaydi — `amount`
      // yuborilmaydi (ikki manba bo'lib qolmasin).
      ...(category.value === null
        ? { amount: computedAmount.value! }
        : {
            categoryId: category.value.id,
            ...(category.value.perUnit ? { quantity: quantity.value! } : {}),
          }),
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

  const picked = category.value

  if (picked !== null && picked.perUnit && quantity.value === null) {
    errorMessage.value = `Necha ${picked.unitLabel ?? 'dona'} ekanini kiriting.`
    return
  }

  if (computedAmount.value === null || computedAmount.value <= 0) {
    errorMessage.value =
      picked === null
        ? 'Summani to‘g‘ri kiriting (musbat son).'
        : `“${picked.label}” tarifi belgilanmagan — Tariflar bo‘limidan summasini kiriting.`
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
        label="Jarima turi"
        hint="Tarif tanlansa summa avtomatik hisoblanadi."
      >
        <select
          v-model="categoryId"
          class="zn-input"
        >
          <option value="">
            — Tarifsiz (summani qo‘lda kiritish) —
          </option>
          <option
            v-for="option in categories"
            :key="option.id"
            :value="option.id"
          >
            {{ option.label }} · {{ formatMoney(option.amount) }}
            {{ option.perUnit ? `so‘m / ${option.unitLabel ?? 'dona'}` : 'so‘m' }}
          </option>
        </select>
      </BaseField>

      <!--
        ★ MIQDOR MAYDONI FAQAT KERAK BO'LGANDA: qat'iy summali tarifda
        u ma'nosiz va operatorni chalg'itardi.
      -->
      <BaseField
        v-if="category !== null && category.perUnit"
        :label="`Necha ${category.unitLabel ?? 'dona'}?`"
        :hint="`Har ${category.unitLabel ?? 'dona'} uchun ${formatMoney(category.amount)} so‘m.`"
      >
        <input
          v-model="quantityText"
          class="zn-input"
          inputmode="decimal"
          placeholder="masalan: 15"
        >
      </BaseField>

      <BaseField
        v-if="category === null"
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

      <!-- Tarifdan hisoblangan summa — o'zgartirib bo'lmaydi. -->
      <div
        v-else
        class="flex items-baseline justify-between rounded-xl border border-line bg-ink-900 px-3.5 py-2.5"
      >
        <span class="text-xs text-slate-400">Hisoblangan summa</span>
        <span
          class="text-base font-bold tabular-nums text-rose-300"
          v-text="computedAmount === null ? '—' : `${formatMoney(computedAmount)} so‘m`"
        />
      </div>

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
