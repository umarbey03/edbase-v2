<script setup lang="ts">
import { useQuery, useQueryClient } from '@tanstack/vue-query'
import { computed, ref, watch } from 'vue'

import { fetchGroups } from '@/entities/group'
import {
  currentPeriod,
  debtAmount,
  fetchPayments,
  isValidPeriod,
  paymentStatusLabel,
  paymentStatusTone,
  periodLabel,
} from '@/entities/payment'
import StudentDiscountsCard from '@/features/discount-manage/ui/StudentDiscountsCard.vue'
import FinanceDashboard from '@/features/finance-dashboard/ui/FinanceDashboard.vue'
import BlockSettingsCard from '@/features/finance-settings/ui/BlockSettingsCard.vue'
import RecordPaymentDialog from '@/features/payment-actions/ui/RecordPaymentDialog.vue'
import ReversePaymentDialog from '@/features/payment-actions/ui/ReversePaymentDialog.vue'
import WaivePaymentDialog from '@/features/payment-actions/ui/WaivePaymentDialog.vue'
import StudentAccountDialog from '@/features/student-account/ui/StudentAccountDialog.vue'
import TariffsCard from '@/features/tariff-manage/ui/TariffsCard.vue'
import { toUserMessage } from '@/shared/api'
import { formatMoney, sumMoney } from '@/shared/lib/money'
import type { PaymentDto } from '@/shared/types'
import {
  AppIcon,
  BaseBadge,
  BaseButton,
  BaseCard,
  BaseSpinner,
  PaginationBar,
} from '@/shared/ui'

/**
 * MOLIYA (eski ilovadagi "Moliya" bo'limi).
 *
 * ★★ DASHBOARD VA SOZLAMA — BITTA SAHIFADA, ESKI TARTIBDA.
 *
 * Eski ilovada `#finance` bo'limi YAGONA uzun sahifa edi va shu ketma-ketlikda
 * o'qilardi (`academic.html`, 853–933):
 *     KPI -> Qarz yoshi -> Oxirgi 12 oy -> Guruh/usul kesimlari
 *     -> Tariflar -> Chegirmalar -> O'zgarishlar izi.
 * O'quv bo'limi xodimi ayni shu tartibga o'rgangan.
 *
 * NEGA ALOHIDA SAHIFA YOKI TAB EMAS:
 *  • alohida marshrut yangi menyu bandini talab qilardi
 *    (`entities/user/model/navigation.ts`), ya'ni menyu tartibi o'zgarardi —
 *    u esa eski `academic.html` menyusidan AYNAN ko'chirilgan;
 *  • tab eski ilovada UMUMAN bo'lmagan tushuncha: xodim bugun raqamlarni ham,
 *    tarifni ham bitta ekranda skroll qilib ko'radi. Tab qo'shsak, "chegirma
 *    berdim — yig'ilish foizi qanday o'zgardi?" degan oddiy ish ikki bosishga
 *    aylanardi.
 * Shu sababli dashboard sahifaning TEPASIGA qo'yildi, boshqaruv vositalari
 * (sozlama, tarif, chegirma, qarzdorlar) esa ostida qoldi.
 *
 * ★ Sahifada BIZNES MANTIQ YO'Q: dashboard butunlay
 * `features/finance-dashboard` ichida (davr filtri, so'rov, CSV eksport ham
 * o'sha yerda), sahifa faqat bloklarni yig'adi.
 *
 * ★ Sahifa sarlavhasi ("Moliya" + "Tushum, qarz va chegirmalar bo'yicha
 * umumiy manzara" — eski 857–858-qatorlar) `FinanceDashboard` ichida
 * chiziladi: davr filtri va "Excel" tugmasi eski dizaynda h1 bilan bir
 * qatorda turadi va ular o'sha komponentning holatiga tayanadi.
 *
 * ★ HALI KO'CHIRILMAGAN YAGONA BLOK — "O'zgarishlar izi" (audit): audit
 * yoziladi, lekin uni O'QIYDIGAN endpoint hali ochilmagan.
 */
const queryClient = useQueryClient()

const PAGE_SIZE = 25

const period = ref(currentPeriod())
const groupId = ref<number | null>(null)
const page = ref(1)

watch([period, groupId], () => {
  page.value = 1
})

const effectivePeriod = computed(() =>
  period.value.length > 0 && isValidPeriod(period.value) ? period.value : undefined,
)

const debtorParams = computed(() => ({
  period: effectivePeriod.value,
  groupId: groupId.value ?? undefined,
  onlyDebt: true,
}))

const debtorsQuery = useQuery({
  queryKey: ['payments', 'debtors', debtorParams, page],
  queryFn: ({ signal }) =>
    fetchPayments({ ...debtorParams.value, page: page.value, pageSize: PAGE_SIZE }, { signal }),
})

const groupsQuery = useQuery({
  queryKey: ['groups', 'active', 'options'],
  queryFn: ({ signal }) => fetchGroups({ isActive: true, pageSize: 100 }, { signal }),
})

const groups = computed(() => groupsQuery.data.value?.items ?? [])
const debtors = computed(() => debtorsQuery.data.value?.items ?? [])
const total = computed(() => debtorsQuery.data.value?.total ?? 0)
const totalPages = computed(() => debtorsQuery.data.value?.totalPages ?? 1)

/**
 * ★ Faqat SHU SAHIFADAGI qarz.
 *
 * "Butun markaz qarzi" ni ko'rsatish uchun barcha sahifalarni yuklab, ularni
 * qo'shish kerak bo'lardi — u raqam yuklab bo'lgunicha eskirardi. Yorliqda
 * qamrov aniq yozilgan, chunki noto'g'ri "jami" raqami hisobotga tushib
 * ketishi mumkin edi. Qo'shish `sumMoney` orqali, tiyinda bajariladi.
 */
const pageDebt = computed(() => sumMoney(debtors.value.map(debtAmount)))

const errorMessage = computed(() =>
  debtorsQuery.error.value !== null ? toUserMessage(debtorsQuery.error.value) : null,
)

/* ------------------------------------------------------------- oynalar --- */

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

function openWaive(payment: PaymentDto): void {
  activePayment.value = payment
  waiveOpen.value = true
}

function openAccount(studentId: number): void {
  accountStudentId.value = studentId
  accountOpen.value = true
}

function recordFromAccount(student: { id: number; name: string }): void {
  activeStudent.value = student
  activeGroupId.value = null
  recordOpen.value = true
}

function reverseFromAccount(student: { id: number; name: string }): void {
  activeStudent.value = student
  reverseOpen.value = true
}

/** `Waived` da server ikkinchi kechirim yozuvini yozardi — to'siq mijozda. */
function canWaive(payment: PaymentDto): boolean {
  return payment.status !== 'Paid' && payment.status !== 'Waived'
}
</script>

<template>
  <div>
    <!-- Eski `#finance` bo'limining tepa qismi: KPI, Qarz yoshi,
         Oxirgi 12 oy, guruh va usul kesimlari + davr filtri va eksport. -->
    <FinanceDashboard />

    <!-- Ostida — eskisidagidek boshqaruv vositalari (Tariflar, Chegirmalar). -->
    <div class="mt-4 grid gap-4 xl:grid-cols-2">
      <BlockSettingsCard />
      <TariffsCard />
      <StudentDiscountsCard class="xl:col-span-2" />
    </div>

    <!-- ---------------------------------------------------- qarzdorlar -->
    <BaseCard
      class="mt-4"
      title="Qarzdorlar"
      :subtitle="`Filtrga mos ${total} ta ochiq qarz yozuvi.`"
    >
      <template #actions>
        <span class="text-xs text-slate-400">
          Shu sahifadagi qarz:
          <span
            class="font-semibold tabular-nums text-rose-400"
            v-text="formatMoney(pageDebt)"
          />
          so‘m
        </span>
      </template>

      <div class="mb-3.5 grid gap-2.5 sm:grid-cols-2">
        <input
          v-model="period"
          class="zn-input"
          type="month"
          aria-label="Hisob oyi"
        >
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
      </div>

      <div
        v-if="debtorsQuery.isPending.value"
        class="flex justify-center py-8"
      >
        <BaseSpinner size="lg" />
      </div>

      <p
        v-else-if="errorMessage !== null"
        class="rounded-lg border border-rose-500/25 bg-rose-500/10 p-3.5 text-xs text-rose-200"
        role="alert"
        v-text="errorMessage"
      />

      <p
        v-else-if="debtors.length === 0"
        class="rounded-lg border border-green-500/25 bg-green-500/10 p-3.5 text-xs text-green-200"
      >
        Qarzdor yo‘q.
      </p>

      <template v-else>
        <!-- Telefon: kartochka -->
        <ul class="divide-y divide-line md:hidden">
          <li
            v-for="payment in debtors"
            :key="payment.id"
            class="py-3 first:pt-0"
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
            <p class="text-xs font-semibold tabular-nums text-rose-400">
              qarz {{ formatMoney(debtAmount(payment)) }}
            </p>
            <div class="mt-2.5 flex flex-wrap items-center justify-end gap-2">
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

        <!-- Desktop: jadval -->
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
                v-for="payment in debtors"
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
                  <span class="font-semibold text-rose-400">
                    {{ formatMoney(debtAmount(payment)) }}
                  </span>
                  <span class="ml-1.5 text-dim">
                    / {{ formatMoney(payment.amount) }}
                  </span>
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
      </template>
    </BaseCard>

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
