<script setup lang="ts">
import { useMutation, useQuery } from '@tanstack/vue-query'
import { computed, ref, watch } from 'vue'

import { fetchGroups } from '@/entities/group'
import {
  PAYMENT_METHOD_OPTIONS,
  paymentMethodLabel,
  periodLabel,
  recordPayment,
} from '@/entities/payment'
import { toUserMessage } from '@/shared/api'
import { formatMoney, formatSum, parseMoneyInput } from '@/shared/lib/money'
import type { PaymentMethodName, PaymentReceiptDto } from '@/shared/types'
import { AppIcon, BaseButton, BaseField, BaseModal } from '@/shared/ui'

import StudentPicker from './StudentPicker.vue'

/**
 * To'lov kiritish — moliya modulining YAGONA kirish nuqtasi.
 *
 * Server pulni eng eski qarzdan boshlab o'zi taqsimlaydi, ortig'ini balansga
 * qo'yadi va KVITANSIYA qaytaradi. Shuning uchun bu formada "qaysi oyga"
 * degan maydon YO'Q: taqsimotni mijoz hisoblasa, ikki kassir bir vaqtda
 * ishlaganda ikki xil natija chiqardi.
 *
 * ★ IZOH UZUNLIGI 500 BELGI BILAN CHEKLANGAN. Server buni tekshirmaydi —
 * uzun matn bazada uziladi va xato `SaveMoneyAsync` ichida "yozuv boshqa
 * so'rov bilan to'qnashdi" degan CHALG'ITUVCHI 409 ga aylanardi. Chegara
 * shu yerda qo'yilgani uchun kassir aniq sababni ko'radi.
 */
const props = defineProps<{
  open: boolean
  /** Oldindan ma'lum o'quvchi (jadval qatoridan ochilganda). `null` — qidiruv ko'rsatiladi. */
  student: { id: number; name: string } | null
  /** Oldindan tanlangan guruh (`null` — barcha guruhlar bo'yicha taqsimlanadi). */
  groupId?: number | null
}>()

const emit = defineEmits<{ close: []; saved: [] }>()

/** Server chegarasi: `PaymentService.MaxAmount`. */
const MAX_AMOUNT = 1_000_000_000
/** Server tekshirmaydi, baza ustuni `varchar(500)`. */
const NOTE_MAX = 500

const picked = ref<{ id: number; name: string } | null>(null)
const amountText = ref('')
const method = ref<PaymentMethodName>('Cash')
const selectedGroupId = ref<number | null>(null)
const note = ref('')
const receipt = ref<PaymentReceiptDto | null>(null)
const errorMessage = ref<string | null>(null)

watch(
  () => [props.open, props.student] as const,
  ([isOpen]) => {
    if (isOpen !== true) return
    picked.value = props.student
    amountText.value = ''
    method.value = 'Cash'
    selectedGroupId.value = props.groupId ?? null
    note.value = ''
    receipt.value = null
    errorMessage.value = null
  },
  { immediate: true },
)

const groupsQuery = useQuery({
  queryKey: ['groups', 'active', 'options'],
  queryFn: ({ signal }) => fetchGroups({ isActive: true, pageSize: 100 }, { signal }),
  enabled: computed(() => props.open),
})

const groups = computed(() => groupsQuery.data.value?.items ?? [])

const amount = computed(() => parseMoneyInput(amountText.value))

/*
  Mijoz tomonidagi tekshiruv server qoidasini AYNAN takrorlaydi
  (`RequirePositiveAmount`): 0 dan katta va 1 000 000 000 dan oshmasin.
  Maqsad — serverni almashtirish emas, balki xatoni forma ichida, maydon
  yonida ko'rsatish: 400 javobidagi matn oynaning pastida chiqadi va kassir
  qaysi maydonga tegishli ekanini darrov tushunmaydi.
*/
const amountError = computed(() => {
  if (amountText.value.trim().length === 0) return null
  const value = amount.value
  if (value === null) return 'Summani raqam bilan kiriting (masalan 540000).'
  if (value <= 0) return 'Summa musbat bo‘lishi kerak.'
  if (value > MAX_AMOUNT) return 'Summa juda katta — kiritishda xatolik bo‘lgan bo‘lishi mumkin.'
  return null
})

const canSubmit = computed(
  () =>
    picked.value !== null &&
    amount.value !== null &&
    amountError.value === null &&
    note.value.length <= NOTE_MAX,
)

const mutation = useMutation({
  mutationFn: () => {
    const student = picked.value
    const value = amount.value
    if (student === null || value === null) throw new Error('Ma’lumot to‘liq emas.')
    const trimmedNote = note.value.trim()
    return recordPayment({
      studentId: student.id,
      amount: value,
      method: method.value,
      groupId: selectedGroupId.value,
      note: trimmedNote.length > 0 ? trimmedNote : null,
    })
  },
  onSuccess: (data) => {
    receipt.value = data
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

const affectedMonths = computed(() => receipt.value?.affectedMonths ?? [])

/*
  ★ "Hammasi balansga tushdi" holati.

  Ochiq oyi yo'q o'quvchiga to'lov kiritish XATO EMAS — server 201 qaytaradi
  va butun summa balansga o'tadi (keyingi oy ochilganda avtomatik sarflanadi).
  Buni aytmasak, kassir "pul yo'qoldi" deb o'ylardi: kvitansiyada birorta oy
  ko'rinmasdi.
*/
const allToBalance = computed(() => receipt.value !== null && receipt.value.applied === 0)
</script>

<template>
  <BaseModal
    :open="props.open"
    :title="receipt === null ? 'To‘lov kiritish' : 'Kvitansiya'"
    @close="emit('close')"
  >
    <!-- ------------------------------------------------------ kvitansiya -->
    <div v-if="receipt !== null">
      <div class="rounded-xl border border-brand-500/40 bg-brand-500/10 p-4 text-center">
        <p class="text-[11px] uppercase tracking-wide text-slate-400">
          Kvitansiya raqami
        </p>
        <p
          class="mt-1 font-mono text-lg font-bold tracking-tight text-brand-300"
          v-text="receipt.receiptNo"
        />
        <p class="mt-1.5 text-sm text-slate-200">
          {{ receipt.studentName }} — {{ formatSum(receipt.amount) }}
          <span class="text-slate-400">({{ paymentMethodLabel(receipt.method) }})</span>
        </p>
      </div>

      <p
        v-if="allToBalance"
        class="mt-3 rounded-lg border border-amber-500/30 bg-amber-500/10 p-3 text-xs leading-relaxed text-amber-100"
      >
        Ochiq qarz topilmadi — butun summa balansga yozildi. Keyingi oy yozuvlari ochilganda u
        avtomatik sarflanadi.
      </p>

      <dl class="mt-3 grid grid-cols-2 gap-2.5">
        <div class="rounded-lg border border-line bg-ink-800 p-3">
          <dd
            class="text-base font-bold tabular-nums text-green-400"
            v-text="formatMoney(receipt.applied)"
          />
          <dt class="mt-0.5 text-[11px] text-slate-400">
            qarzga taqsimlandi
          </dt>
        </div>
        <div class="rounded-lg border border-line bg-ink-800 p-3">
          <dd
            class="text-base font-bold tabular-nums text-brand-400"
            v-text="formatMoney(receipt.toBalance)"
          />
          <dt class="mt-0.5 text-[11px] text-slate-400">
            balansga o‘tdi
          </dt>
        </div>
        <div class="rounded-lg border border-line bg-ink-800 p-3">
          <dd class="text-base font-bold tabular-nums text-slate-100">
            {{ receipt.monthsClosed }}<span
              v-if="receipt.monthsPartial > 0"
              class="text-sm font-normal text-slate-400"
            > + {{ receipt.monthsPartial }} qisman</span>
          </dd>
          <dt class="mt-0.5 text-[11px] text-slate-400">
            oy yopildi
          </dt>
        </div>
        <div class="rounded-lg border border-line bg-ink-800 p-3">
          <dd
            class="text-base font-bold tabular-nums"
            :class="receipt.debtAfter > 0 ? 'text-rose-400' : 'text-green-400'"
            v-text="formatMoney(receipt.debtAfter)"
          />
          <dt class="mt-0.5 text-[11px] text-slate-400">
            qolgan qarz
          </dt>
        </div>
      </dl>

      <div
        v-if="affectedMonths.length > 0"
        class="mt-3"
      >
        <p class="mb-1.5 text-xs font-semibold text-slate-300">
          Ta’sirlangan oylar
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
            <span class="shrink-0 text-xs tabular-nums text-slate-400">
              {{ formatMoney(month.paidAmount) }} / {{ formatMoney(month.amount) }}
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
      <StudentPicker v-model="picked" />

      <div class="mt-3">
        <BaseField
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
            placeholder="540000"
          >
        </BaseField>
      </div>

      <div class="mt-3">
        <span class="mb-1.5 block text-xs font-medium text-slate-400">To‘lov usuli</span>
        <!--
          Ikki tugmali tanlov (eski ilovadagi `#pm-modal` setkasi). `<select>`
          emas: atigi ikki variant bor va kassir uni har to'lovda bosadi —
          bitta teginish ikkitadan tez.
        -->
        <div class="grid grid-cols-2 gap-2">
          <button
            v-for="option in PAYMENT_METHOD_OPTIONS"
            :key="option.value"
            type="button"
            class="min-h-11 rounded-lg border text-sm font-semibold transition-colors"
            :class="
              method === option.value
                ? 'border-brand-500 bg-brand-500 text-on-brand'
                : 'border-line bg-ink-800 text-slate-300 hover:bg-ink-750'
            "
            @click="method = option.value"
          >
            {{ option.label }}
          </button>
        </div>
      </div>

      <div class="mt-3">
        <BaseField
          label="Guruh"
          hint="Tanlanmasa — pul barcha guruhlar bo‘yicha eng eski qarzdan taqsimlanadi."
        >
          <select
            v-model="selectedGroupId"
            class="zn-input"
          >
            <option :value="null">
              Barcha guruhlar
            </option>
            <option
              v-for="group in groups"
              :key="group.id"
              :value="group.id"
            >
              {{ group.name }}
            </option>
          </select>
        </BaseField>
      </div>

      <div class="mt-3">
        <BaseField
          label="Izoh (ixtiyoriy)"
          :error="note.length > NOTE_MAX ? `Izoh ${NOTE_MAX} belgidan oshmasin.` : null"
        >
          <textarea
            v-model="note"
            class="zn-input"
            rows="2"
            :maxlength="NOTE_MAX"
            placeholder="Masalan: Karta orqali oylik to‘lov olindi"
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
      <template v-if="receipt !== null">
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
          :disabled="!canSubmit"
          :loading="mutation.isPending.value"
          @click="submit"
        >
          <template #icon>
            <AppIcon
              name="check"
              :size="15"
            />
          </template>
          Saqlash
        </BaseButton>
      </template>
    </template>
  </BaseModal>
</template>
