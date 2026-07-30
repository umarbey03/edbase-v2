<script setup lang="ts">
import { useMutation, useQuery } from '@tanstack/vue-query'
import { computed, ref, watch } from 'vue'

import { fetchGroups } from '@/entities/group'
import {
  createDiscount,
  DISCOUNT_KIND_OPTIONS,
  todayIsoDate,
  updateDiscount,
} from '@/entities/payment'
import { toUserMessage } from '@/shared/api'
import { formatSum, parseMoneyInput } from '@/shared/lib/money'
import type { DiscountKindName, StudentDiscountDto } from '@/shared/types'
import { BaseButton, BaseField, BaseModal } from '@/shared/ui'

/**
 * Chegirma berish/tahrirlash.
 *
 * ★ `PUT` — TO'LIQ ALMASHTIRISH (`UpdateTariffRequest` bilan bir xil naqsh):
 * `groupId`, `validTo`, `reason` yuborilmasa `null` bo'lib yoziladi va guruhga
 * biriktirilgan chegirma jimgina BARCHA guruhlarga tarqalardi. Shuning uchun
 * forma hamma maydonni yuklaydi va hammasini qaytaradi.
 *
 * ★ CHEGIRMALAR YIG'ILMAYDI: server aniqlik bo'yicha BITTASINI tanlaydi
 * (guruhga biriktirilgani umumiydan ustun). Ikkita chegirma qo'shib "20%"
 * chiqadi deb kutib bo'lmaydi.
 */
const props = defineProps<{
  open: boolean
  student: { id: number; name: string } | null
  discount: StudentDiscountDto | null
}>()

const emit = defineEmits<{ close: []; saved: [] }>()

/** Server chegaralari: foiz 100 gacha, summa 1e9 gacha, sabab 500 belgi. */
const MAX_PERCENT = 100
const MAX_AMOUNT = 1_000_000_000
const REASON_MAX = 500

const kind = ref<DiscountKindName>('Percent')
const valueText = ref('')
const groupId = ref<number | null>(null)
const validFrom = ref(todayIsoDate())
const validTo = ref('')
const reason = ref('')
const isActive = ref(true)
const errorMessage = ref<string | null>(null)

const isEdit = computed(() => props.discount !== null)

function resetForm(): void {
  const discount = props.discount
  kind.value = discount?.kind ?? 'Percent'
  valueText.value = discount === null ? '' : String(discount.value)
  groupId.value = discount?.groupId ?? null
  validFrom.value = discount?.validFrom ?? todayIsoDate()
  validTo.value = discount?.validTo ?? ''
  reason.value = discount?.reason ?? ''
  isActive.value = discount?.isActive ?? true
  errorMessage.value = null
}

watch(() => [props.open, props.discount], resetForm, { immediate: true })

const groupsQuery = useQuery({
  queryKey: ['groups', 'active', 'options'],
  queryFn: ({ signal }) => fetchGroups({ isActive: true, pageSize: 100 }, { signal }),
  enabled: computed(() => props.open),
})

const groups = computed(() => groupsQuery.data.value?.items ?? [])

const missingGroupOption = computed(() => {
  const discount = props.discount
  if (discount?.groupId == null) return null
  if (groups.value.some((item) => item.id === discount.groupId)) return null
  return { id: discount.groupId, name: `${discount.groupName ?? 'Guruh'} (ro‘yxatda yo‘q)` }
})

const value = computed(() => parseMoneyInput(valueText.value))

const valueError = computed(() => {
  if (valueText.value.trim().length === 0) return null
  const parsed = value.value
  if (parsed === null) return 'Qiymatni raqam bilan kiriting.'
  if (parsed <= 0) return 'Chegirma qiymati musbat bo‘lishi kerak.'
  if (kind.value === 'Percent' && parsed > MAX_PERCENT) return 'Foizli chegirma 100 dan oshmaydi.'
  if (kind.value === 'Amount' && parsed > MAX_AMOUNT) return 'Chegirma summasi juda katta.'
  return null
})

const dateError = computed(() =>
  validTo.value.length > 0 && validTo.value < validFrom.value
    ? 'Tugash sanasi boshlanish sanasidan oldin bo‘lmaydi.'
    : null,
)

const canSubmit = computed(
  () =>
    props.student !== null &&
    value.value !== null &&
    valueError.value === null &&
    dateError.value === null &&
    validFrom.value.length > 0 &&
    reason.value.length <= REASON_MAX,
)

const mutation = useMutation({
  mutationFn: () => {
    const student = props.student
    const parsed = value.value
    if (student === null || parsed === null) throw new Error('Ma’lumot to‘liq emas.')
    const trimmed = reason.value.trim()
    /* ★ HAMMA maydon — `PUT` to'liq almashtiradi. */
    const payload = {
      kind: kind.value,
      value: parsed,
      validFrom: validFrom.value,
      validTo: validTo.value.length > 0 ? validTo.value : null,
      groupId: groupId.value,
      reason: trimmed.length > 0 ? trimmed : null,
      isActive: isActive.value,
    }
    const discount = props.discount
    return discount === null
      ? createDiscount(student.id, payload)
      : updateDiscount(student.id, discount.id, payload)
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
    wide
    :title="isEdit ? 'Chegirmani tahrirlash' : 'Chegirma berish'"
    @close="emit('close')"
  >
    <form
      novalidate
      @submit.prevent="submit"
    >
      <p class="mb-3.5 text-xs leading-relaxed text-slate-400">
        <span
          class="font-semibold text-slate-200"
          v-text="props.student?.name ?? '—'"
        />
        — chegirma
        <span class="font-semibold text-slate-200">keyingi oy yozuvlaridan</span> boshlab
        qo‘llanadi; allaqachon ochilgan oylar o‘zgarmaydi.
      </p>

      <div class="grid gap-3 sm:grid-cols-2">
        <BaseField label="Turi">
          <select
            v-model="kind"
            class="zn-input"
          >
            <option
              v-for="option in DISCOUNT_KIND_OPTIONS"
              :key="option.value"
              :value="option.value"
            >
              {{ option.label }}
            </option>
          </select>
        </BaseField>
        <BaseField
          :label="kind === 'Percent' ? 'Qiymat (%)' : 'Qiymat (so‘m)'"
          :error="valueError"
          :hint="kind === 'Amount' && value !== null ? formatSum(value) : ''"
        >
          <input
            v-model="valueText"
            class="zn-input tabular-nums"
            type="text"
            inputmode="numeric"
            autocomplete="off"
            :placeholder="kind === 'Percent' ? '10' : '50000'"
          >
        </BaseField>
      </div>

      <div class="mt-3">
        <BaseField
          label="Guruh (ixtiyoriy)"
          hint="Tanlanmasa — barcha guruhlarga. Guruhga biriktirilgani ustun turadi."
        >
          <select
            v-model="groupId"
            class="zn-input"
          >
            <option :value="null">
              — Barcha guruhlar —
            </option>
            <option
              v-if="missingGroupOption !== null"
              :value="missingGroupOption.id"
            >
              {{ missingGroupOption.name }}
            </option>
            <option
              v-for="item in groups"
              :key="item.id"
              :value="item.id"
            >
              {{ item.name }}
            </option>
          </select>
        </BaseField>
      </div>

      <div class="mt-3 grid gap-3 sm:grid-cols-2">
        <BaseField label="Boshlanish">
          <input
            v-model="validFrom"
            class="zn-input"
            type="date"
            required
          >
        </BaseField>
        <BaseField
          label="Tugash (ixtiyoriy)"
          :error="dateError"
        >
          <input
            v-model="validTo"
            class="zn-input"
            type="date"
          >
        </BaseField>
      </div>

      <div class="mt-3">
        <BaseField
          label="Sababi"
          :error="reason.length > REASON_MAX ? `Sabab ${REASON_MAX} belgidan oshmasin.` : null"
        >
          <input
            v-model="reason"
            class="zn-input"
            :maxlength="REASON_MAX"
            placeholder="Masalan: singlisi ham o‘qiydi"
          >
        </BaseField>
      </div>

      <label class="mt-3 flex min-h-11 items-center gap-2.5 text-sm text-slate-300">
        <input
          v-model="isActive"
          type="checkbox"
          class="size-4 accent-brand-500"
        >
        Faol chegirma
      </label>

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
        {{ isEdit ? 'Saqlash' : 'Berish' }}
      </BaseButton>
    </template>
  </BaseModal>
</template>
