<script setup lang="ts">
import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { computed, ref, watch } from 'vue'

import {
  debtAmount,
  fetchBlockStatus,
  fetchStudentAccount,
  fetchStudentTransactions,
  isOutgoingTransaction,
  paymentMethodLabel,
  paymentStatusLabel,
  paymentStatusTone,
  periodLabel,
  setStudentExempt,
  transactionKindLabel,
  transactionKindTone,
} from '@/entities/payment'
import { toUserMessage } from '@/shared/api'
import { formatDateTime } from '@/shared/lib/datetime'
import { formatMoney, formatSum } from '@/shared/lib/money'
import type { PaymentDto } from '@/shared/types'
import {
  AppIcon,
  BaseBadge,
  BaseButton,
  BaseModal,
  BaseSpinner,
  PaginationBar,
} from '@/shared/ui'

/**
 * O'quvchining moliya hisobi: qarz, balans, oylar tarixi va jurnal.
 *
 * Eski ilovadagi "To'lovlar va tranzaksiyalar tarixi" tortmasining o'rnini
 * bosadi. Bir necha farq bor va ularning sababi ATAYLAB yozilgan:
 *
 *  • "Dars zaxirasi", "sotib olingan darslar", darslar jadvali — YO'Q.
 *    Yangi backendda pul DARSGA emas, OYGA bog'langan (`Payment` = o'quvchi ×
 *    guruh × oy). Dars kreditlari uchun endpoint ham, ma'lumot ham yo'q.
 *
 *  • "Balans" — YANGI: eskisida ortiqcha to'lov shunchaki "to'langan" bo'lib
 *    qolardi va keyingi oyga o'tmasdi.
 */
const props = defineProps<{ open: boolean; studentId: number | null }>()

const emit = defineEmits<{
  close: []
  /** Sahifa "To'lov kiritish" oynasini ochsin (u yagona nusxada turadi). */
  record: [student: { id: number; name: string }]
  /** Sahifa "Pulni qaytarish" oynasini ochsin. */
  reverse: [student: { id: number; name: string }]
  /** Oy yozuvini kechirish. */
  waive: [payment: PaymentDto]
}>()

const queryClient = useQueryClient()

const TRANSACTIONS_PAGE_SIZE = 25

const transactionsPage = ref(1)
const actionError = ref<string | null>(null)

watch(
  () => [props.open, props.studentId] as const,
  () => {
    transactionsPage.value = 1
    actionError.value = null
  },
)

const enabled = computed(() => props.open && props.studentId !== null)

const accountQuery = useQuery({
  queryKey: ['payments', 'account', computed(() => props.studentId)],
  queryFn: ({ signal }) => fetchStudentAccount(props.studentId ?? 0, { signal }),
  enabled,
})

/*
  Blok holati ALOHIDA so'rov bilan olinadi.

  ★ `POST .../exempt` javobidan foydalanib bo'lmaydi: u `blocked: false` va
  `requestedScope: "None"` ni QAT'IY qiymat sifatida qaytaradi (server kodida
  shunday) — ya'ni istisnoni olib tashlagandan keyin ham "bloklanmagan" deb
  ko'rinardi. Shuning uchun har o'zgarishdan keyin shu so'rov qayta o'qiladi.

  Qamrov `Video` so'raladi: u eng past darajadagi haqiqiy blok
  (`None` dan keyingi), demak "umuman bloklanganmi?" degan savolga javob beradi.
*/
const blockQuery = useQuery({
  queryKey: ['payments', 'block', computed(() => props.studentId)],
  queryFn: ({ signal }) => fetchBlockStatus(props.studentId ?? 0, 'Video', { signal }),
  enabled,
})

const transactionsQuery = useQuery({
  queryKey: ['payments', 'transactions', computed(() => props.studentId), transactionsPage],
  queryFn: ({ signal }) =>
    fetchStudentTransactions(
      props.studentId ?? 0,
      { page: transactionsPage.value, pageSize: TRANSACTIONS_PAGE_SIZE },
      { signal },
    ),
  enabled,
})

const account = computed(() => accountQuery.data.value ?? null)
const months = computed(() => account.value?.months ?? [])
const transactions = computed(() => transactionsQuery.data.value?.items ?? [])
const block = computed(() => blockQuery.data.value ?? null)

const errorMessage = computed(() =>
  accountQuery.error.value !== null ? toUserMessage(accountQuery.error.value) : null,
)

const student = computed(() => {
  const data = account.value
  return data === null ? null : { id: data.studentId, name: data.fullName }
})

const exemptMutation = useMutation({
  mutationFn: (exempt: boolean) => setStudentExempt(props.studentId ?? 0, { exempt }),
  onSuccess: () => {
    actionError.value = null
    void queryClient.invalidateQueries({ queryKey: ['payments'] })
  },
  onError: (error: Error) => {
    actionError.value = toUserMessage(error)
  },
})

function toggleExempt(): void {
  const current = account.value
  if (current === null || exemptMutation.isPending.value) return
  exemptMutation.mutate(!current.exempt)
}

/**
 * Kechirish tugmasi qachon o'chiriladi.
 *
 * `Paid` — server 409 beradi ("To'langan oyni kechirib bo'lmaydi").
 * `Waived` — server TO'SMAYDI va IKKINCHI kechirim yozuvini yozib qo'yadi;
 * jurnal ikki marta ko'rinib, hisob chalkashardi. Shuning uchun to'siq shu yerda.
 */
function canWaive(month: PaymentDto): boolean {
  return month.status !== 'Paid' && month.status !== 'Waived'
}
</script>

<template>
  <BaseModal
    :open="props.open"
    wide
    :title="account?.fullName ?? 'O‘quvchi hisobi'"
    @close="emit('close')"
  >
    <div
      v-if="accountQuery.isPending.value"
      class="flex justify-center py-10"
    >
      <BaseSpinner size="lg" />
    </div>

    <div
      v-else-if="errorMessage !== null"
      class="rounded-xl border border-rose-500/25 bg-rose-500/10 px-5 py-6 text-center"
      role="alert"
    >
      <p
        class="text-sm text-rose-200"
        v-text="errorMessage"
      />
      <BaseButton
        class="mt-4"
        size="sm"
        variant="secondary"
        @click="accountQuery.refetch()"
      >
        Qayta urinish
      </BaseButton>
    </div>

    <div v-else-if="account !== null">
      <!-- ------------------------------------------------------ xulosa -->
      <dl class="grid grid-cols-2 gap-2.5 sm:grid-cols-4">
        <div class="rounded-lg border border-line bg-ink-800 p-3">
          <dd
            class="text-base font-bold tabular-nums"
            :class="account.debt > 0 ? 'text-rose-400' : 'text-green-400'"
            v-text="formatMoney(account.debt)"
          />
          <dt class="mt-0.5 text-[11px] text-slate-400">
            Joriy qarz
          </dt>
        </div>
        <div class="rounded-lg border border-line bg-ink-800 p-3">
          <dd
            class="text-base font-bold tabular-nums text-brand-400"
            v-text="formatMoney(account.balance)"
          />
          <dt class="mt-0.5 text-[11px] text-slate-400">
            Balans
          </dt>
        </div>
        <div class="rounded-lg border border-line bg-ink-800 p-3">
          <dd
            class="text-base font-bold tabular-nums text-green-400"
            v-text="formatMoney(account.paid)"
          />
          <dt class="mt-0.5 text-[11px] text-slate-400">
            Jami to‘langan
          </dt>
        </div>
        <div class="rounded-lg border border-line bg-ink-800 p-3">
          <dd
            class="text-base font-bold tabular-nums text-slate-100"
            v-text="account.openMonths"
          />
          <dt class="mt-0.5 text-[11px] text-slate-400">
            Ochiq oy
          </dt>
        </div>
      </dl>

      <!-- ------------------------------------------------------- amallar -->
      <div class="mt-3 flex flex-wrap items-center gap-2">
        <BaseButton
          size="sm"
          :disabled="student === null"
          @click="student !== null && emit('record', student)"
        >
          <template #icon>
            <AppIcon
              name="plus"
              :size="14"
            />
          </template>
          To‘lov kiritish
        </BaseButton>
        <BaseButton
          size="sm"
          variant="secondary"
          :disabled="student === null"
          @click="student !== null && emit('reverse', student)"
        >
          <template #icon>
            <AppIcon
              name="arrow-left"
              :size="14"
            />
          </template>
          Pulni qaytarish
        </BaseButton>
        <BaseButton
          size="sm"
          variant="ghost"
          :loading="exemptMutation.isPending.value"
          @click="toggleExempt"
        >
          <template #icon>
            <AppIcon
              name="lock"
              :size="14"
            />
          </template>
          {{ account.exempt ? 'Istisnoni olib tashlash' : 'Blokdan istisno qilish' }}
        </BaseButton>
      </div>

      <!-- --------------------------------------------------- blok holati -->
      <p
        v-if="block !== null"
        class="mt-3 rounded-lg border p-3 text-xs leading-relaxed"
        :class="
          block.blocked
            ? 'border-rose-500/30 bg-rose-500/10 text-rose-200'
            : 'border-line bg-ink-800 text-slate-400'
        "
      >
        <template v-if="block.blocked">
          {{ block.reason ?? 'O‘quvchi qarzi sababli bloklangan.' }}
        </template>
        <template v-else-if="account.exempt">
          Bloklashdan istisno qilingan — qarzi bo‘lsa ham platforma yopilmaydi.
        </template>
        <template v-else-if="!block.enforced">
          Blok rejimi o‘chiq: qarz hisoblanadi va ko‘rsatiladi, lekin hech kim bloklanmaydi.
        </template>
        <template v-else>
          Bloklanmagan. Chegara — {{ formatSum(block.threshold) }}.
        </template>
      </p>

      <p
        v-if="actionError !== null"
        class="mt-2 text-xs text-rose-400"
        role="alert"
        v-text="actionError"
      />

      <!-- ------------------------------------------------ oylar tarixi -->
      <h3 class="mb-2 mt-5 text-[13px] font-semibold text-slate-200">
        Oylar tarixi
      </h3>
      <p
        v-if="months.length === 0"
        class="rounded-lg border border-line bg-ink-800 px-3.5 py-4 text-xs text-slate-400"
      >
        Hali oylik yozuv ochilmagan.
      </p>
      <div
        v-else
        class="scroll-x-safe scrollbar-slim rounded-lg border border-line"
      >
        <table class="zn-table">
          <thead>
            <tr>
              <th>Oy</th>
              <th>Guruh</th>
              <th>Summa</th>
              <th>To‘langan</th>
              <th>Qarz</th>
              <th>Holat</th>
              <th />
            </tr>
          </thead>
          <tbody>
            <tr
              v-for="month in months"
              :key="month.id"
            >
              <td
                class="font-medium text-slate-100"
                v-text="periodLabel(month.period)"
              />
              <td
                class="text-slate-400"
                v-text="month.groupName"
              />
              <td class="tabular-nums text-slate-300">
                {{ formatMoney(month.amount) }}
                <span
                  v-if="month.discountAmount > 0"
                  class="text-[11px] text-dim"
                >(−{{ formatMoney(month.discountAmount) }})</span>
              </td>
              <td
                class="tabular-nums text-slate-400"
                v-text="formatMoney(month.paidAmount)"
              />
              <td
                class="tabular-nums"
                :class="debtAmount(month) > 0 ? 'text-rose-400' : 'text-slate-500'"
                v-text="formatMoney(debtAmount(month))"
              />
              <td>
                <BaseBadge :tone="paymentStatusTone(month.status)">
                  {{ paymentStatusLabel(month.status) }}
                </BaseBadge>
              </td>
              <td>
                <div class="flex justify-end">
                  <BaseButton
                    v-if="canWaive(month)"
                    size="sm"
                    variant="ghost"
                    @click="emit('waive', month)"
                  >
                    Kechirish
                  </BaseButton>
                </div>
              </td>
            </tr>
          </tbody>
        </table>
      </div>

      <!-- ------------------------------------------------------- jurnal -->
      <h3 class="mb-2 mt-5 text-[13px] font-semibold text-slate-200">
        Tranzaksiyalar jurnali
      </h3>
      <div
        v-if="transactionsQuery.isPending.value"
        class="flex justify-center py-6"
      >
        <BaseSpinner />
      </div>
      <p
        v-else-if="transactions.length === 0"
        class="rounded-lg border border-line bg-ink-800 px-3.5 py-4 text-xs text-slate-400"
      >
        Tranzaksiyalar tarixi topilmadi.
      </p>
      <div
        v-else
        class="rounded-lg border border-line"
      >
        <div class="scroll-x-safe scrollbar-slim">
          <table class="zn-table">
            <thead>
              <tr>
                <th>Vaqt</th>
                <th>Guruh</th>
                <th>Amal</th>
                <th>Summa</th>
                <th>Usul</th>
                <th>Kvitansiya</th>
                <th>Kiritgan mas’ul</th>
                <th>Izoh</th>
              </tr>
            </thead>
            <tbody>
              <tr
                v-for="item in transactions"
                :key="item.id"
              >
                <td
                  class="tabular-nums text-slate-400"
                  v-text="formatDateTime(item.createdAt)"
                />
                <td
                  class="text-slate-400"
                  v-text="item.groupName ?? '—'"
                />
                <td>
                  <BaseBadge :tone="transactionKindTone(item.kind)">
                    {{ transactionKindLabel(item.kind) }}
                  </BaseBadge>
                </td>
                <td
                  class="font-medium tabular-nums"
                  :class="isOutgoingTransaction(item.kind) ? 'text-rose-400' : 'text-green-400'"
                >
                  {{ isOutgoingTransaction(item.kind) ? '−' : '+' }}{{ formatMoney(item.amount) }}
                </td>
                <td
                  class="text-slate-400"
                  v-text="paymentMethodLabel(item.method)"
                />
                <td
                  class="font-mono text-[11px] text-slate-400"
                  v-text="item.receiptNo ?? '—'"
                />
                <td
                  class="text-slate-400"
                  v-text="item.actorName ?? 'Tizim'"
                />
                <td
                  class="max-w-48 truncate text-slate-400"
                  :title="item.note ?? ''"
                  v-text="item.note ?? '—'"
                />
              </tr>
            </tbody>
          </table>
        </div>
        <PaginationBar
          :page="transactionsPage"
          :total-pages="transactionsQuery.data.value?.totalPages ?? 1"
          :total="transactionsQuery.data.value?.total ?? 0"
          @update:page="transactionsPage = $event"
        />
      </div>
    </div>

    <template #footer>
      <BaseButton
        variant="secondary"
        @click="emit('close')"
      >
        Yopish
      </BaseButton>
    </template>
  </BaseModal>
</template>
