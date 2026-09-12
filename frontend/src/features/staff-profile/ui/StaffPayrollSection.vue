<script setup lang="ts">
import { useQuery, useQueryClient } from '@tanstack/vue-query'
import { computed, ref } from 'vue'
import { useRouter } from 'vue-router'

import {
  currentPayrollPeriod,
  fetchPayrollRules,
  fetchPayrollSummary,
  isPercentKind,
  payrollPeriodLabel,
  payrollRuleKindLabel,
  ruleScopeLabel,
} from '@/entities/payroll'
import PayrollRuleFormDialog from '@/features/payroll-manage/ui/PayrollRuleFormDialog.vue'
import { toUserMessage } from '@/shared/api'
import { formatDateWithYear } from '@/shared/lib/datetime'
import { formatMoney } from '@/shared/lib/money'
import type { PayrollRuleDto, UserRoleName } from '@/shared/types'
import { AppIcon, BaseBadge, BaseButton, BaseCard, DataStatus } from '@/shared/ui'

/**
 * USTOZ PROFILIDAGI OYLIK BO'LIMI (2026-09-09, loyiha egasi: "ustozlarni
 * profili orqali ham ularga stavka biriktirish imkoni bo'lishi kerak").
 *
 * ★ FAQAT ADMIN: `GET /payroll/*` endpointlari `[Authorize(Roles="Admin")]`.
 *   Ota komponent bo'limni `isAdminRole` bilan chizadi — bu yerda qayta
 *   tekshirilmaydi (o'quv bo'limi 403 olib o'tirmasin).
 *
 * ★ MA'LUMOT «Oylik» sahifasi bilan AYNI so'rovlardan (`['payroll','rules']`,
 *   `['payroll','summary']`): alohida endpoint yo'q, shuning uchun qoida
 *   shu yerdan qo'shilsa u sahifada ham darhol ko'rinadi (`['payroll']`
 *   invalidatsiya qilinadi).
 *
 * ★ IKKI RO'YXAT: xodimga SHAXSAN bog'langan qoidalar (tahrirlanadi) va
 *   uning roliga tegishli UMUMIY qoidalar (faqat ma'lumot uchun — ular
 *   boshqa xodimlarga ham tegishli, bu yerdan tahrirlash "men faqat
 *   Bekzodni o'zgartirdim" degan yolg'on taassurot berardi).
 */
const props = defineProps<{
  userId: number
  userName: string
  role: UserRoleName
}>()

const queryClient = useQueryClient()
const router = useRouter()

const period = currentPayrollPeriod()

const rulesQuery = useQuery({
  queryKey: ['payroll', 'rules'],
  queryFn: ({ signal }) => fetchPayrollRules({ signal }),
})

const summaryQuery = useQuery({
  queryKey: ['payroll', 'summary', period],
  queryFn: ({ signal }) => fetchPayrollSummary({ period }, { signal }),
})

const personalRules = computed(() =>
  (rulesQuery.data.value ?? []).filter((rule) => rule.userId === props.userId),
)

/** Rolga tegishli umumiy qoidalar — faqat faollari, ma'lumot uchun. */
const generalRules = computed(() =>
  (rulesQuery.data.value ?? []).filter(
    (rule) => rule.userId === null && rule.role === props.role && rule.isActive,
  ),
)

const summaryRow = computed(
  () => summaryQuery.data.value?.rows.find((row) => row.userId === props.userId) ?? null,
)

const rulesError = computed(() =>
  rulesQuery.error.value !== null ? toUserMessage(rulesQuery.error.value) : null,
)

function ruleValue(rule: PayrollRuleDto): string {
  if (isPercentKind(rule.kind)) return `${rule.amount}%`
  if (rule.kind === 'TieredByAttendance' && rule.tiers.length > 0) {
    const first = rule.tiers[0]
    const last = rule.tiers[rule.tiers.length - 1]
    if (first !== undefined && last !== undefined) {
      return `${formatMoney(first.amount)} … ${formatMoney(last.amount)}`
    }
  }
  return formatMoney(rule.amount)
}

function rulePeriod(rule: PayrollRuleDto): string {
  const from = formatDateWithYear(rule.activeFrom)
  return rule.activeTo === null ? `${from} dan` : `${from} — ${formatDateWithYear(rule.activeTo)}`
}

/* ------------------------------------------------------------- forma --- */

const formOpen = ref(false)
const editing = ref<PayrollRuleDto | null>(null)

function openCreate(): void {
  editing.value = null
  formOpen.value = true
}

function openEdit(rule: PayrollRuleDto): void {
  editing.value = rule
  formOpen.value = true
}

function onSaved(): void {
  void queryClient.invalidateQueries({ queryKey: ['payroll'] })
}

function openPayrollPage(): void {
  void router.push({ name: 'manage-payroll' })
}
</script>

<template>
  <BaseCard title="Oylik va stavkalar">
    <template #actions>
      <BaseButton
        size="sm"
        @click="openCreate"
      >
        <template #icon>
          <AppIcon
            name="plus"
            :size="14"
          />
        </template>
        Stavka qo‘shish
      </BaseButton>
    </template>

    <!-- Joriy oy xulosasi — «Oylik» sahifasidagi qator bilan AYNI son. -->
    <button
      type="button"
      class="mb-3 flex w-full items-center justify-between gap-3 rounded-xl border border-line bg-ink-800 p-3 text-left transition-colors hover:border-line-strong"
      :title="`${payrollPeriodLabel(period)} — batafsil «Oylik» sahifasida`"
      @click="openPayrollPage"
    >
      <div class="min-w-0">
        <p class="text-[11px] uppercase tracking-wide text-slate-500">
          {{ payrollPeriodLabel(period) }}
        </p>
        <p
          v-if="summaryRow !== null"
          class="mt-0.5 text-xs text-slate-400"
        >
          {{ summaryRow.sessionCount }} dars · {{ summaryRow.totalStudentsAttended }} o‘quvchi-dars
          <span
            v-if="summaryRow.sessionsWithoutRule > 0"
            class="text-amber-300"
          >
            · {{ summaryRow.sessionsWithoutRule }} darsda qoida topilmadi
          </span>
        </p>
        <p
          v-else-if="summaryQuery.isPending.value"
          class="mt-0.5 text-xs text-slate-500"
        >
          Yuklanmoqda…
        </p>
        <p
          v-else
          class="mt-0.5 text-xs text-slate-500"
        >
          Bu oyda hali hisoblangan dars yo‘q.
        </p>
      </div>
      <p class="shrink-0 text-right text-base font-semibold tabular-nums text-slate-100">
        {{ summaryRow !== null ? formatMoney(summaryRow.total) : '—' }}
        <span class="block text-[11px] font-normal text-slate-500">so‘m</span>
      </p>
    </button>

    <DataStatus
      :pending="rulesQuery.isPending.value"
      :error="rulesError"
      :empty="false"
      :retrying="rulesQuery.isFetching.value"
      :skeleton-rows="2"
      @retry="rulesQuery.refetch()"
    >
      <!-- Shaxsiy qoidalar -->
      <p
        v-if="personalRules.length === 0"
        class="rounded-xl border border-dashed border-line p-3 text-xs leading-relaxed text-slate-400"
      >
        Shaxsiy stavka yo‘q — oylik faqat umumiy (rol bo‘yicha) qoidalar
        bilan hisoblanadi. «Stavka qo‘shish» orqali aynan shu xodimga
        qoida biriktiring.
      </p>
      <ul
        v-else
        class="divide-y divide-line rounded-xl border border-line"
      >
        <li
          v-for="rule in personalRules"
          :key="rule.id"
        >
          <button
            type="button"
            class="flex w-full items-start justify-between gap-3 p-3 text-left transition-colors hover:bg-ink-800"
            :title="`${rule.name}: tahrirlash`"
            @click="openEdit(rule)"
          >
            <div class="min-w-0">
              <p class="flex flex-wrap items-center gap-1.5">
                <span
                  class="truncate text-sm font-medium text-slate-100"
                  v-text="rule.name"
                />
                <BaseBadge
                  v-if="!rule.isActive"
                  tone="neutral"
                >
                  O‘chirilgan
                </BaseBadge>
              </p>
              <p class="mt-0.5 text-xs text-slate-400">
                {{ payrollRuleKindLabel(rule.kind) }} · {{ ruleScopeLabel(rule) }}
              </p>
              <p class="mt-0.5 text-[11px] text-slate-500">
                {{ rulePeriod(rule) }}
              </p>
            </div>
            <p class="shrink-0 text-right text-sm font-semibold tabular-nums text-slate-100">
              {{ ruleValue(rule) }}
              <span
                v-if="rule.kind === 'PerStudentAcademicHour' || rule.kind === 'PerAcademicHour'"
                class="block text-[11px] font-normal text-slate-400"
              >
                / {{ rule.academicHourMinutes }} daqiqa
              </span>
            </p>
          </button>
        </li>
      </ul>

      <!-- Umumiy qoidalar — ma'lumot uchun -->
      <div
        v-if="generalRules.length > 0"
        class="mt-3"
      >
        <p class="mb-1.5 text-[11px] uppercase tracking-wide text-slate-500">
          Rol bo‘yicha umumiy qoidalar
        </p>
        <ul class="space-y-1">
          <li
            v-for="rule in generalRules"
            :key="rule.id"
            class="flex items-center justify-between gap-2 text-xs text-slate-400"
          >
            <span class="truncate">
              {{ rule.name }} · {{ payrollRuleKindLabel(rule.kind) }}
            </span>
            <span
              class="shrink-0 tabular-nums"
              v-text="ruleValue(rule)"
            />
          </li>
        </ul>
        <p class="mt-1.5 text-[11px] leading-relaxed text-slate-500">
          Bir xil turdagi qoidalardan xodimga bog‘langani umumiysidan ustun.
          Umumiy qoidalar «Oylik» sahifasida tahrirlanadi.
        </p>
      </div>
    </DataStatus>
  </BaseCard>

  <PayrollRuleFormDialog
    v-if="formOpen"
    :open="formOpen"
    :rule="editing"
    :preset-user-id="props.userId"
    :preset-role="props.role"
    @close="formOpen = false"
    @saved="onSaved"
  />
</template>
