<script setup lang="ts">
import { useQuery, useQueryClient } from '@tanstack/vue-query'
import { computed, ref, watch } from 'vue'

import { fetchGroups } from '@/entities/group'
import {
  currentPeriod,
  debtAmount,
  fetchPayments,
  isValidPeriod,
  PAYMENT_STATUS_OPTIONS,
  paymentStatusLabel,
  paymentStatusTone,
  periodLabel,
} from '@/entities/payment'
import OpenPeriodDialog from '@/features/payment-actions/ui/OpenPeriodDialog.vue'
import RecordPaymentDialog from '@/features/payment-actions/ui/RecordPaymentDialog.vue'
import ReversePaymentDialog from '@/features/payment-actions/ui/ReversePaymentDialog.vue'
import WaivePaymentDialog from '@/features/payment-actions/ui/WaivePaymentDialog.vue'
import StudentAccountDialog from '@/features/student-account/ui/StudentAccountDialog.vue'
import { toUserMessage } from '@/shared/api'
import { formatMoney, sumMoney } from '@/shared/lib/money'
import type { PaymentDto, PaymentStatusName } from '@/shared/types'
import {
  AppIcon,
  BaseBadge,
  BaseButton,
  BaseCard,
  DataStatus,
  PageHeader,
  PaginationBar,
} from '@/shared/ui'

/**
 * OYLIK TO'LOVLAR (eski ilovadagi "Oylik to'lovlar" bo'limi).
 *
 * Eskisidan farqlar va SABABLARI:
 *
 *  • Eskisida sahifa guruh kartochkalaridan iborat edi, jadval esa tortma
 *    ichida ochilardi. Yangi backend `GET /payments` da o'quvchi × guruh × OY
 *    yozuvlarini sahifalab beradi; guruh bo'yicha yig'ma raqamlar (nechta
 *    qarzdor, guruh qarzi) uchun endpoint YO'Q va ularni mijozda hisoblash
 *    uchun barcha sahifalarni yuklab olish kerak bo'lardi. Shuning uchun
 *    ro'yxat TEKIS jadval, guruh esa FILTR.
 *
 *  • Jadval ustunlari eskisidagidek: O'quvchi / Holat / Tafsilot / Summa /
 *    Amallar. "Tafsilot" ga guruh va oy joylashtirilgan.
 *
 *  • "Jami qarz (so'm)" tile'i eskisida BUTUN markaz bo'yicha edi. Server
 *    bunday yig'indini bermaydi, shuning uchun bu yerda AYNAN nima
 *    hisoblanayotgani yozib qo'yilgan ("shu sahifada") — chalg'ituvchi
 *    "jami" raqamidan ko'ra kamroq, lekin to'g'ri.
 */
const queryClient = useQueryClient()

const PAGE_SIZE = 25

const period = ref(currentPeriod())
const groupId = ref<number | null>(null)
const status = ref<PaymentStatusName | ''>('')
const onlyDebt = ref(false)
const page = ref(1)

watch([period, groupId, status, onlyDebt], () => {
  page.value = 1
})

/*
  Buzuq oy serverga YUBORILMAYDI: `GET /payments?period=bad` 400 beradi va
  jadval o'rniga xato ekrani chiqardi. `<input type="month">` tozalanganda
  bo'sh satr qoladi — bunda filtr shunchaki qo'llanmaydi.
*/
const effectivePeriod = computed(() =>
  period.value.length > 0 && isValidPeriod(period.value) ? period.value : undefined,
)

const periodInvalid = computed(() => period.value.length > 0 && !isValidPeriod(period.value))

const listParams = computed(() => ({
  period: effectivePeriod.value,
  groupId: groupId.value ?? undefined,
  status: status.value === '' ? undefined : status.value,
  onlyDebt: onlyDebt.value ? true : undefined,
}))

const paymentsQuery = useQuery({
  queryKey: ['payments', 'list', listParams, page],
  queryFn: ({ signal }) =>
    fetchPayments({ ...listParams.value, page: page.value, pageSize: PAGE_SIZE }, { signal }),
})

/*
  Qarzdorlar SONI alohida so'rov bilan olinadi (`pageSize: 1` — faqat `total`
  kerak). Joriy sahifadagi qatorlardan sanash MUMKIN EMAS: 25 tadan iborat
  sahifa butun markazni ifodalamaydi va raqam sahifa almashganda o'zgarardi.
*/
const debtParams = computed(() => ({
  period: effectivePeriod.value,
  groupId: groupId.value ?? undefined,
  onlyDebt: true,
}))

const debtCountQuery = useQuery({
  queryKey: ['payments', 'debt-count', debtParams],
  queryFn: ({ signal }) => fetchPayments({ ...debtParams.value, pageSize: 1 }, { signal }),
})

const groupsQuery = useQuery({
  queryKey: ['groups', 'active', 'options'],
  queryFn: ({ signal }) => fetchGroups({ isActive: true, pageSize: 100 }, { signal }),
})

const groups = computed(() => groupsQuery.data.value?.items ?? [])
const payments = computed(() => paymentsQuery.data.value?.items ?? [])
const total = computed(() => paymentsQuery.data.value?.total ?? 0)
const totalPages = computed(() => paymentsQuery.data.value?.totalPages ?? 1)
const debtCount = computed(() => debtCountQuery.data.value?.total ?? 0)

/** ★ `sumMoney` — tiyinda, butun sonda qo'shadi (izoh: `shared/lib/money.ts`). */
const pageDebt = computed(() => sumMoney(payments.value.map(debtAmount)))

const errorMessage = computed(() =>
  paymentsQuery.error.value !== null ? toUserMessage(paymentsQuery.error.value) : null,
)

/* ------------------------------------------------------------- oynalar --- */

const openPeriodOpen = ref(false)
const recordOpen = ref(false)
const reverseOpen = ref(false)
const waiveOpen = ref(false)
const accountOpen = ref(false)

const activeStudent = ref<{ id: number; name: string } | null>(null)
const activeGroupId = ref<number | null>(null)
const activePayment = ref<PaymentDto | null>(null)
const accountStudentId = ref<number | null>(null)

function refresh(): void {
  void queryClient.invalidateQueries({ queryKey: ['payments'] })
}

function openRecordFor(payment: PaymentDto): void {
  activeStudent.value = { id: payment.studentId, name: payment.studentName }
  activeGroupId.value = payment.groupId
  recordOpen.value = true
}

function openRecordBlank(): void {
  activeStudent.value = null
  activeGroupId.value = null
  recordOpen.value = true
}

function openWaive(payment: PaymentDto): void {
  activePayment.value = payment
  waiveOpen.value = true
}

function openAccount(studentId: number): void {
  accountStudentId.value = studentId
  accountOpen.value = true
}

/*
  Hisob oynasidan chaqiriladigan amallar. Oynalar SAHIFA darajasida yagona
  nusxada turadi: `StudentAccountDialog` ichida ham nusxasi bo'lsa, bitta
  amal uchun ikkita komponent holati saqlanardi va ular ajralib ketardi.
*/
function recordFromAccount(student: { id: number; name: string }): void {
  activeStudent.value = student
  activeGroupId.value = null
  recordOpen.value = true
}

function reverseFromAccount(student: { id: number; name: string }): void {
  activeStudent.value = student
  reverseOpen.value = true
}

/**
 * Kechirish tugmasi ko'rinishi.
 *
 * `Paid` — server 409 beradi ("To'langan oyni kechirib bo'lmaydi").
 * `Waived` — server TO'SMAYDI va IKKINCHI kechirim yozuvini yozadi; jurnalda
 * ikki marta ko'rinib, hisob chalkashardi. Shuning uchun to'siq mijozda.
 */
function canWaive(payment: PaymentDto): boolean {
  return payment.status !== 'Paid' && payment.status !== 'Waived'
}
</script>

<template>
  <div>
    <PageHeader
      title="Oylik to‘lovlar"
      subtitle="Har oy uchun yozuv ochiladi; to‘lov eng eski qarzdan boshlab taqsimlanadi, ortig‘i balansga tushadi."
    >
      <template #actions>
        <BaseButton
          variant="secondary"
          @click="openRecordBlank"
        >
          <template #icon>
            <AppIcon
              name="plus"
              :size="16"
            />
          </template>
          To‘lov kiritish
        </BaseButton>
        <BaseButton @click="openPeriodOpen = true">
          <template #icon>
            <AppIcon
              name="calendar"
              :size="16"
            />
          </template>
          Joriy oy yozuvlarini yaratish
        </BaseButton>
      </template>
    </PageHeader>

    <!-- ---------------------------------------------------------- xulosa -->
    <div class="mb-4 grid grid-cols-2 gap-2.5 sm:grid-cols-3">
      <div class="rounded-xl border border-line bg-ink-900 p-3.5">
        <p
          class="text-lg font-bold tabular-nums text-slate-100"
          v-text="total"
        />
        <p class="mt-0.5 text-[11px] text-slate-400">
          Filtrga mos yozuv
        </p>
      </div>
      <div class="rounded-xl border border-line bg-ink-900 p-3.5">
        <p
          class="text-lg font-bold tabular-nums"
          :class="debtCount > 0 ? 'text-rose-400' : 'text-green-400'"
          v-text="debtCount"
        />
        <p class="mt-0.5 text-[11px] text-slate-400">
          Qarzdor yozuv
        </p>
      </div>
      <div class="col-span-2 rounded-xl border border-line bg-ink-900 p-3.5 sm:col-span-1">
        <p
          class="text-lg font-bold tabular-nums"
          :class="pageDebt > 0 ? 'text-rose-400' : 'text-slate-100'"
          v-text="formatMoney(pageDebt)"
        />
        <p class="mt-0.5 text-[11px] text-slate-400">
          Shu sahifadagi qarz (so‘m)
        </p>
      </div>
    </div>

    <!-- -------------------------------------------------------- filtrlar -->
    <div class="mb-4 grid gap-2.5 sm:grid-cols-2 lg:grid-cols-4">
      <div>
        <input
          v-model="period"
          class="zn-input"
          type="month"
          aria-label="Hisob oyi"
        >
        <p
          v-if="periodInvalid"
          class="mt-1 text-[11px] text-rose-400"
        >
          Oy YYYY-MM ko‘rinishida bo‘lishi kerak.
        </p>
      </div>
      <select
        v-model="groupId"
        class="zn-input"
        aria-label="Guruh bo‘yicha filtr"
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
      <select
        v-model="status"
        class="zn-input"
        aria-label="Holat bo‘yicha filtr"
      >
        <option value="">
          Barcha holatlar
        </option>
        <option
          v-for="option in PAYMENT_STATUS_OPTIONS"
          :key="option.value"
          :value="option.value"
        >
          {{ option.label }}
        </option>
      </select>
      <label class="flex min-h-11 items-center gap-2.5 text-sm text-slate-300">
        <input
          v-model="onlyDebt"
          type="checkbox"
          class="size-4 accent-brand-500"
        >
        Faqat qarzdorlar
      </label>
    </div>

    <DataStatus
      :pending="paymentsQuery.isPending.value"
      :error="errorMessage"
      :empty="payments.length === 0"
      :retrying="paymentsQuery.isFetching.value"
      :skeleton-rows="5"
      empty-icon="star"
      empty-title="Yozuv topilmadi"
      empty-text="Tanlangan oy uchun yozuvlar hali ochilmagan bo‘lishi mumkin — “Joriy oy yozuvlarini yaratish” tugmasini bosing."
      @retry="paymentsQuery.refetch()"
    >
      <BaseCard flush>
        <!-- Telefon: kartochka -->
        <ul class="divide-y divide-line md:hidden">
          <li
            v-for="payment in payments"
            :key="payment.id"
            class="p-3.5"
          >
            <div class="flex items-start justify-between gap-2">
              <button
                type="button"
                class="min-w-0 flex-1 truncate text-left text-sm font-medium text-slate-100 underline-offset-2 hover:underline"
                @click="openAccount(payment.studentId)"
              >
                {{ payment.studentName }}
              </button>
              <BaseBadge :tone="paymentStatusTone(payment.status)">
                {{ paymentStatusLabel(payment.status) }}
              </BaseBadge>
            </div>
            <p class="mt-1 text-xs text-slate-400">
              {{ periodLabel(payment.period) }} · {{ payment.groupName }}
            </p>
            <p class="text-xs tabular-nums text-slate-400">
              {{ formatMoney(payment.paidAmount) }} / {{ formatMoney(payment.amount) }}
              <span
                v-if="debtAmount(payment) > 0"
                class="font-semibold text-rose-400"
              >· qarz {{ formatMoney(debtAmount(payment)) }}</span>
            </p>
            <div class="mt-2.5 flex flex-wrap items-center justify-end gap-2">
              <BaseButton
                size="sm"
                variant="ghost"
                @click="openAccount(payment.studentId)"
              >
                Tarix
              </BaseButton>
              <BaseButton
                v-if="canWaive(payment)"
                size="sm"
                variant="secondary"
                @click="openWaive(payment)"
              >
                Kechirish
              </BaseButton>
              <BaseButton
                size="sm"
                @click="openRecordFor(payment)"
              >
                To‘landi
              </BaseButton>
            </div>
          </li>
        </ul>

        <!-- Desktop: jadval. Ustunlar eski ilovadagidek. -->
        <div class="scroll-x-safe scrollbar-slim hidden md:block">
          <table class="zn-table">
            <thead>
              <tr>
                <th>O‘quvchi</th>
                <th>Holat</th>
                <th>Tafsilot</th>
                <th>Summa</th>
                <th>Amallar</th>
              </tr>
            </thead>
            <tbody>
              <tr
                v-for="payment in payments"
                :key="payment.id"
              >
                <td>
                  <button
                    type="button"
                    class="font-medium text-slate-100 underline-offset-2 hover:underline"
                    title="To‘lovlar va tranzaksiyalar tarixini ko‘rish"
                    @click="openAccount(payment.studentId)"
                  >
                    {{ payment.studentName }}
                  </button>
                </td>
                <td>
                  <BaseBadge :tone="paymentStatusTone(payment.status)">
                    {{ paymentStatusLabel(payment.status) }}
                  </BaseBadge>
                </td>
                <td class="text-slate-400">
                  {{ periodLabel(payment.period) }}
                  <span class="text-dim">· {{ payment.groupName }}</span>
                </td>
                <td class="tabular-nums">
                  <span class="text-slate-300">
                    {{ formatMoney(payment.paidAmount) }} / {{ formatMoney(payment.amount) }}
                  </span>
                  <span
                    v-if="debtAmount(payment) > 0"
                    class="ml-1.5 font-semibold text-rose-400"
                  >qarz {{ formatMoney(debtAmount(payment)) }}</span>
                </td>
                <td>
                  <div class="flex items-center justify-end gap-2">
                    <BaseButton
                      size="sm"
                      variant="ghost"
                      @click="openAccount(payment.studentId)"
                    >
                      <template #icon>
                        <AppIcon
                          name="clock"
                          :size="13"
                        />
                      </template>
                      Tarix
                    </BaseButton>
                    <BaseButton
                      v-if="canWaive(payment)"
                      size="sm"
                      variant="secondary"
                      @click="openWaive(payment)"
                    >
                      Kechirish
                    </BaseButton>
                    <BaseButton
                      size="sm"
                      @click="openRecordFor(payment)"
                    >
                      To‘landi
                    </BaseButton>
                  </div>
                </td>
              </tr>
            </tbody>
          </table>
        </div>

        <PaginationBar
          :page="page"
          :total-pages="totalPages"
          :total="total"
          @update:page="page = $event"
        />
      </BaseCard>
    </DataStatus>

    <OpenPeriodDialog
      :open="openPeriodOpen"
      @close="openPeriodOpen = false"
      @done="refresh"
    />
    <!--
      ★ TARTIB MUHIM — O'ZGARTIRMANG.

      `BaseModal` `<Teleport to="body">` bilan chiziladi va HAMMASI bir xil
      `z-50` da turadi, ya'ni ustma-ust tushganda DOM TARTIBI hal qiladi.
      Teleport langarlari komponentlar E'LON QILINGAN tartibda yaratiladi
      (ochilish tartibida EMAS), shuning uchun hisob oynasi BIRINCHI turadi:
      undan chaqiriladigan amal oynalari (to'lov, kechirim, qaytarish) uning
      USTIGA chiqadi. Teskari tartibda ular hisob oynasi ORTIDA ochilib,
      bosib bo'lmasdi — bu brauzerda topilgan haqiqiy xato edi.
    -->
    <StudentAccountDialog
      :open="accountOpen"
      :student-id="accountStudentId"
      @close="accountOpen = false"
      @record="recordFromAccount"
      @reverse="reverseFromAccount"
      @waive="openWaive"
    />

    <RecordPaymentDialog
      :open="recordOpen"
      :student="activeStudent"
      :group-id="activeGroupId"
      @close="recordOpen = false"
      @saved="refresh"
    />
    <WaivePaymentDialog
      :open="waiveOpen"
      :payment="activePayment"
      @close="waiveOpen = false"
      @saved="refresh"
    />
    <ReversePaymentDialog
      :open="reverseOpen"
      :student="activeStudent"
      @close="reverseOpen = false"
      @saved="refresh"
    />
  </div>
</template>
