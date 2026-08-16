<script setup lang="ts">
import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { computed, ref } from 'vue'

import PayrollDetailDialog from '@/features/payroll-manage/ui/PayrollDetailDialog.vue'
import TeacherRatesCard from '@/features/payroll-manage/ui/TeacherRatesCard.vue'
import {
  approvePayrollPeriod,
  currentPayrollPeriod,
  fetchPayrollSummary,
  isValidPayrollPeriod,
  markPayrollPeriodPaid,
  payrollApprovalStatusLabel,
  payrollApprovalStatusTone,
  payrollRoleLabel,
} from '@/entities/payroll'
import { toUserMessage } from '@/shared/api'
import { useConfirm } from '@/shared/lib/useConfirm'
import { formatMoney } from '@/shared/lib/money'
import { useBreakpoint } from '@/shared/lib/useBreakpoint'
import {
  AppIcon,
  BaseBadge,
  BaseButton,
  BaseCard,
  DataStatus,
  PageHeader,
} from '@/shared/ui'
import type { IconName } from '@/shared/ui'
import type { PayrollSummaryRowDto } from '@/shared/types'

/**
 * OYLIK HISOBLASH (Bosqich 4 → 2026-08-16 upgrade) — ustoz/kurator haqi.
 * FAQAT Admin.
 *
 * ★ 2026-08-16 — TASDIQLASH/TO'LOV OQIMI qo'shildi (Tutorbase/GetCourse
 * uslubidagi Draft → Approved → Paid): har qator endi "Holat" ustuniga va
 * mos amal tugmasiga ega. Baza oylik/KPI bonusi (kurator uchun) va qo'lda
 * tuzatishlar summasi "Jami"ga kiradi, tafsiloti `PayrollDetailDialog`da.
 *
 * `ManageAcademicSettingsPage` dagi tab naqshi bilan AYNI: "Hisobot" va
 * "Stavkalar" — ikkinchisi CRUD, birinchisi faqat o'qish (+ tasdiqlash amali).
 */
interface Section {
  key: string
  label: string
  icon: IconName
}

const SECTIONS: Section[] = [
  { key: 'summary', label: 'Hisobot', icon: 'chart' },
  { key: 'rates', label: 'Stavkalar', icon: 'wallet' },
]

const active = ref<string>(SECTIONS[0]!.key)

const { isDesktop } = useBreakpoint()
const queryClient = useQueryClient()
const confirm = useConfirm()

const period = ref(currentPayrollPeriod())

const effectivePeriod = computed(() =>
  period.value.length > 0 && isValidPayrollPeriod(period.value) ? period.value : undefined,
)

const periodInvalid = computed(() => period.value.length > 0 && !isValidPayrollPeriod(period.value))

const summaryQuery = useQuery({
  queryKey: ['payroll', 'summary', effectivePeriod],
  queryFn: ({ signal }) => fetchPayrollSummary({ period: effectivePeriod.value }, { signal }),
})

const rows = computed(() => summaryQuery.data.value?.rows ?? [])
const grandTotal = computed(() => summaryQuery.data.value?.grandTotal ?? 0)

const errorMessage = computed(() =>
  summaryQuery.error.value !== null ? toUserMessage(summaryQuery.error.value) : null,
)

const detailOpen = ref(false)
const detailUserId = ref<number | null>(null)

function openDetail(userId: number): void {
  detailUserId.value = userId
  detailOpen.value = true
}

/* ------------------------------------------------------- tasdiqlash/to'lov */

const actionError = ref<string | null>(null)
const actingUserId = ref<number | null>(null)

function invalidateSummary(): void {
  void queryClient.invalidateQueries({ queryKey: ['payroll', 'summary'] })
  void queryClient.invalidateQueries({ queryKey: ['payroll', 'detail'] })
}

const approveMutation = useMutation({
  mutationFn: (row: PayrollSummaryRowDto) =>
    approvePayrollPeriod({ userId: row.userId, period: effectivePeriod.value ?? currentPayrollPeriod() }),
  onSuccess: () => {
    actionError.value = null
    invalidateSummary()
  },
  onError: (error: unknown) => {
    actionError.value = toUserMessage(error)
  },
  onSettled: () => {
    actingUserId.value = null
  },
})

const markPaidMutation = useMutation({
  mutationFn: (row: PayrollSummaryRowDto) =>
    markPayrollPeriodPaid({ userId: row.userId, period: effectivePeriod.value ?? currentPayrollPeriod() }),
  onSuccess: () => {
    actionError.value = null
    invalidateSummary()
  },
  onError: (error: unknown) => {
    actionError.value = toUserMessage(error)
  },
  onSettled: () => {
    actingUserId.value = null
  },
})

async function askApprove(row: PayrollSummaryRowDto): Promise<void> {
  actionError.value = null

  const ok = await confirm({
    title: 'Davrni tasdiqlash',
    message: `${row.fullName} uchun ${formatMoney(row.total)} summasi tasdiqlanadi va suratga olinadi.`,
    confirmLabel: 'Tasdiqlash',
    tone: 'warning',
    details: ['Tasdiqlangandan keyin bu davrga tuzatish qo‘shib/o‘chirib bo‘lmaydi.'],
  })
  if (!ok) return

  actingUserId.value = row.userId
  approveMutation.mutate(row)
}

async function askMarkPaid(row: PayrollSummaryRowDto): Promise<void> {
  actionError.value = null

  const ok = await confirm({
    title: 'To‘landi deb belgilash',
    message: `${row.fullName} uchun ${formatMoney(row.total)} summasi to‘langan deb belgilanadi.`,
    confirmLabel: 'To‘landi',
  })
  if (!ok) return

  actingUserId.value = row.userId
  markPaidMutation.mutate(row)
}
</script>

<template>
  <div>
    <PageHeader
      title="Oylik hisoblash"
      subtitle="Ustoz va kurator haqi — yakunlangan darslar, baza oylik/KPI va tuzatishlar asosida. Faqat Admin ko‘radi."
    />

    <div
      class="mb-5 inline-flex gap-1 rounded-2xl border border-line bg-ink-900 p-1"
      role="tablist"
    >
      <button
        v-for="section in SECTIONS"
        :key="section.key"
        type="button"
        role="tab"
        :aria-selected="active === section.key"
        class="flex items-center gap-1.5 rounded-xl px-4 py-2 text-sm font-semibold transition-colors"
        :class="
          active === section.key
            ? 'bg-brand-500 text-on-brand'
            : 'text-slate-400 hover:bg-ink-800 hover:text-slate-100'
        "
        @click="active = section.key"
      >
        <AppIcon
          :name="section.icon"
          :size="15"
        />
        {{ section.label }}
      </button>
    </div>

    <div v-if="active === 'summary'">
      <div class="mb-4 grid gap-2.5 sm:grid-cols-[220px_1fr]">
        <div>
          <input
            v-model="period"
            class="zn-input"
            type="month"
            aria-label="Davr"
          >
          <p
            v-if="periodInvalid"
            class="mt-1 text-[11px] text-rose-400"
          >
            Oy YYYY-MM ko‘rinishida bo‘lishi kerak.
          </p>
        </div>
        <div class="flex items-center rounded-xl border border-line bg-ink-900 px-3.5 py-2.5">
          <p class="text-sm text-slate-300">
            Jami:
            <span class="font-semibold tabular-nums text-slate-100">
              {{ formatMoney(grandTotal) }}
            </span>
          </p>
        </div>
      </div>

      <p
        v-if="actionError !== null"
        class="mb-3 rounded-lg border border-rose-500/25 bg-rose-500/10 p-2.5 text-xs text-rose-200"
        role="alert"
        v-text="actionError"
      />

      <DataStatus
        :pending="summaryQuery.isPending.value"
        :error="errorMessage"
        :empty="rows.length === 0"
        :retrying="summaryQuery.isFetching.value"
        :skeleton-rows="5"
        empty-icon="star"
        empty-title="Yakunlangan dars topilmadi"
        empty-text="Tanlangan oyda hech kimga dars hisoblanmagan — davrni almashtirib ko‘ring."
        @retry="summaryQuery.refetch()"
      >
        <BaseCard flush>
          <!-- Telefon/planshet: kartochka -->
          <ul
            v-if="!isDesktop"
            class="divide-y divide-line"
          >
            <li
              v-for="row in rows"
              :key="row.userId"
              class="p-3.5"
            >
              <div class="flex items-start justify-between gap-2">
                <button
                  type="button"
                  class="min-w-0 flex-1 truncate text-left text-sm font-medium text-slate-100 underline-offset-2 hover:underline"
                  @click="openDetail(row.userId)"
                >
                  {{ row.fullName }}
                </button>
                <BaseBadge tone="accent">
                  {{ payrollRoleLabel(row.role) }}
                </BaseBadge>
              </div>
              <p class="mt-1 text-xs text-slate-400">
                {{ row.sessionCount }} dars · {{ row.totalStudentsAttended }} qatnashgan
                <span v-if="row.activeStudentCount > 0">· {{ row.activeStudentCount }} faol o‘quvchi (KPI)</span>
              </p>
              <div class="mt-1 flex items-center justify-between gap-2">
                <p class="text-sm font-semibold tabular-nums text-slate-100">
                  {{ formatMoney(row.total) }}
                  <span
                    v-if="row.sessionsWithoutRate > 0"
                    class="ml-1.5 text-xs font-normal text-amber-400"
                  >· {{ row.sessionsWithoutRate }} darsga stavka topilmadi</span>
                </p>
                <BaseBadge :tone="payrollApprovalStatusTone(row.approvalStatus)">
                  {{ payrollApprovalStatusLabel(row.approvalStatus) }}
                </BaseBadge>
              </div>
              <div
                v-if="row.approvalStatus !== 'Paid'"
                class="mt-2 flex justify-end"
              >
                <BaseButton
                  size="sm"
                  variant="secondary"
                  :loading="actingUserId === row.userId && (approveMutation.isPending.value || markPaidMutation.isPending.value)"
                  @click="row.approvalStatus === 'Draft' ? askApprove(row) : askMarkPaid(row)"
                >
                  {{ row.approvalStatus === 'Draft' ? 'Tasdiqlash' : 'To‘landi deb belgilash' }}
                </BaseButton>
              </div>
            </li>
          </ul>

          <!-- Desktop -->
          <div
            v-else
            class="scroll-x-safe scrollbar-slim"
          >
            <table class="zn-table">
              <thead>
                <tr>
                  <th>Xodim</th>
                  <th>Rol</th>
                  <th>Dars</th>
                  <th>Qatnashgan</th>
                  <th>Asosiy</th>
                  <th>Bonus</th>
                  <th
                    title="Baza oylik + KPI bonusi (kurator)"
                  >
                    Baza/KPI
                  </th>
                  <th
                    title="Qo'lda qo'shilgan bonus/ushlab qolish"
                  >
                    Tuzatish
                  </th>
                  <th>Jami</th>
                  <th>Holat</th>
                  <th />
                </tr>
              </thead>
              <tbody>
                <tr
                  v-for="row in rows"
                  :key="row.userId"
                >
                  <td>
                    <button
                      type="button"
                      class="font-medium text-slate-100 underline-offset-2 hover:underline"
                      title="Dars-dars tafsilotni ko‘rish"
                      @click="openDetail(row.userId)"
                    >
                      {{ row.fullName }}
                    </button>
                  </td>
                  <td>
                    <BaseBadge tone="accent">
                      {{ payrollRoleLabel(row.role) }}
                    </BaseBadge>
                  </td>
                  <td class="tabular-nums text-slate-300">
                    {{ row.sessionCount }}
                  </td>
                  <td class="tabular-nums text-slate-300">
                    {{ row.totalStudentsAttended }}
                  </td>
                  <td class="tabular-nums text-slate-300">
                    {{ formatMoney(row.baseAmount) }}
                  </td>
                  <td class="tabular-nums text-slate-300">
                    {{ formatMoney(row.bonusAmount) }}
                  </td>
                  <td
                    class="tabular-nums text-slate-300"
                    :title="`Baza oylik: ${formatMoney(row.baseSalaryAmount)} · KPI (${row.activeStudentCount} faol o‘quvchi): ${formatMoney(row.kpiBonusAmount)}`"
                  >
                    {{ formatMoney(row.baseSalaryAmount + row.kpiBonusAmount) }}
                  </td>
                  <td
                    class="tabular-nums"
                    :class="row.adjustmentAmount < 0 ? 'text-rose-400' : 'text-slate-300'"
                  >
                    {{ row.adjustmentAmount === 0 ? '—' : formatMoney(row.adjustmentAmount) }}
                  </td>
                  <td class="tabular-nums font-semibold text-slate-100">
                    {{ formatMoney(row.total) }}
                    <span
                      v-if="row.sessionsWithoutRate > 0"
                      class="ml-1.5 text-xs font-normal text-amber-400"
                      :title="`${row.sessionsWithoutRate} darsga stavka topilmadi`"
                    >
                      <AppIcon
                        name="alert"
                        :size="13"
                      />
                    </span>
                  </td>
                  <td>
                    <BaseBadge :tone="payrollApprovalStatusTone(row.approvalStatus)">
                      {{ payrollApprovalStatusLabel(row.approvalStatus) }}
                    </BaseBadge>
                  </td>
                  <td>
                    <BaseButton
                      v-if="row.approvalStatus !== 'Paid'"
                      size="sm"
                      variant="secondary"
                      :loading="actingUserId === row.userId && (approveMutation.isPending.value || markPaidMutation.isPending.value)"
                      @click="row.approvalStatus === 'Draft' ? askApprove(row) : askMarkPaid(row)"
                    >
                      {{ row.approvalStatus === 'Draft' ? 'Tasdiqlash' : 'To‘landi' }}
                    </BaseButton>
                  </td>
                </tr>
              </tbody>
            </table>
          </div>
        </BaseCard>
      </DataStatus>
    </div>

    <TeacherRatesCard v-if="active === 'rates'" />

    <PayrollDetailDialog
      :open="detailOpen"
      :user-id="detailUserId"
      :period="effectivePeriod ?? currentPayrollPeriod()"
      @close="detailOpen = false"
    />
  </div>
</template>
