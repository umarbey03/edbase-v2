<script setup lang="ts">
import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { computed, ref } from 'vue'

import {
  deletePayrollRule,
  fetchPayrollRules,
  isPercentKind,
  payrollRuleKindHint,
  payrollRuleKindLabel,
  ruleScopeLabel,
  usesAcademicHour,
} from '@/entities/payroll'
import { toUserMessage } from '@/shared/api'
import { formatDateWithYear } from '@/shared/lib/datetime'
import { formatMoney } from '@/shared/lib/money'
import type { PayrollRuleDto, PayrollRuleKindName } from '@/shared/types'
import {
  AppIcon,
  BaseBadge,
  BaseButton,
  BaseCard,
  BaseSpinner,
  ConfirmDeleteDialog,
} from '@/shared/ui'

import PayrollKindGuideDrawer from './PayrollKindGuideDrawer.vue'
import PayrollRuleFormDialog from './PayrollRuleFormDialog.vue'

/**
 * OYLIK QOIDALARI ro'yxati — `TariffsCard` bilan AYNI naqsh.
 *
 * ★ TURI BO'YICHA GURUHLANGAN: ro'yxat aralash bo'lsa admin "bu ustozga
 * qaysi dars stavkasi tushadi?" degan savolga javob topolmasdi — bir xil
 * turdagi qoidalar orasidan FAQAT BITTASI ishlaydi va ularni yonma-yon
 * ko'rish shu tanlovni ko'rsatadi.
 */
const queryClient = useQueryClient()

const formOpen = ref(false)
const editing = ref<PayrollRuleDto | null>(null)
const deleting = ref<PayrollRuleDto | null>(null)
const deleteError = ref<string | null>(null)

/* Qo'llanma drawer'i — `guideKind` berilsa o'sha turga o'tadi. */
const guideOpen = ref(false)
const guideKind = ref<PayrollRuleKindName | null>(null)

function openGuide(kind: PayrollRuleKindName | null = null): void {
  guideKind.value = kind
  guideOpen.value = true
}

const rulesQuery = useQuery({
  queryKey: ['payroll', 'rules'],
  queryFn: ({ signal }) => fetchPayrollRules({ signal }),
})

const rules = computed(() => rulesQuery.data.value ?? [])

/** Turi bo'yicha guruhlar — tartib backenddagi enum tartibi bilan bir xil. */
const groupedRules = computed(() => {
  const buckets = new Map<PayrollRuleKindName, PayrollRuleDto[]>()

  for (const rule of rules.value) {
    const key = rule.kind
    const bucket = buckets.get(key)
    if (bucket === undefined) buckets.set(key, [rule])
    else bucket.push(rule)
  }

  return [...buckets.entries()].map(([kind, items]) => ({
    kind,
    label: payrollRuleKindLabel(kind),
    items,
  }))
})

const errorMessage = computed(() =>
  rulesQuery.error.value !== null ? toUserMessage(rulesQuery.error.value) : null,
)

/** Qoidaning qiymati — foiz turida `%`, qolganida so'm. */
function ruleValueLabel(rule: PayrollRuleDto): string {
  if (isPercentKind(rule.kind)) {
    return rule.planAmount !== null && rule.planReachedPercent !== null
      ? `${rule.amount}% → ${rule.planReachedPercent}%`
      : `${rule.amount}%`
  }

  if (rule.kind === 'TieredByAttendance' && rule.tiers.length > 0) {
    const lowest = rule.tiers[0]
    const highest = rule.tiers[rule.tiers.length - 1]
    return lowest === undefined || highest === undefined
      ? formatMoney(rule.amount)
      : `${formatMoney(lowest.amount)} … ${formatMoney(highest.amount)}`
  }

  return formatMoney(rule.amount)
}

/** Muddat: "01.09.2026 dan" yoki "01.09.2026 – 31.12.2026". */
function rulePeriodLabel(rule: PayrollRuleDto): string {
  const from = formatDateWithYear(rule.activeFrom)
  return rule.activeTo === null ? `${from} dan` : `${from} – ${formatDateWithYear(rule.activeTo)}`
}

function openCreate(): void {
  editing.value = null
  formOpen.value = true
}

function openEdit(rule: PayrollRuleDto): void {
  editing.value = rule
  formOpen.value = true
}

function refresh(): void {
  void queryClient.invalidateQueries({ queryKey: ['payroll'] })
}

const deleteMutation = useMutation({
  mutationFn: (id: number) => deletePayrollRule(id),
  onSuccess: () => {
    deleting.value = null
    deleteError.value = null
    refresh()
  },
  onError: (error: Error) => {
    deleteError.value = toUserMessage(error)
  },
})

function confirmDelete(): void {
  const rule = deleting.value
  if (rule === null) return
  deleteError.value = null
  deleteMutation.mutate(rule.id)
}

/**
 * ★ O'CHIRISH OGOHLANTIRISHI qoidaga QARAB o'zgaradi: unga qo'lda
 * bog'langan guruhlar bo'lsa, ular avtomatik "Avtomatik" rejimga qaytadi —
 * buni aytmaslik admin uchun kutilmagan yon ta'sir bo'lardi.
 */
const deleteMessage = computed(() => {
  const rule = deleting.value
  if (rule === null) return ''

  const pinned =
    rule.pinnedGroupCount > 0
      ? ` Bu qoida ${rule.pinnedGroupCount} ta guruhga qo‘lda tayinlangan — ular “Avtomatik” rejimga qaytadi.`
      : ''

  return (
    `“${rule.name}” qoidasi o‘chirilsinmi? O‘tgan oy hisobotlari o‘zgarmaydi — ` +
    `ular hisoblanganda natijani nusxa qilib olgan.${pinned}` +
    ' Tarixni saqlash uchun o‘chirish o‘rniga tugash sanasini qo‘yish tavsiya etiladi.'
  )
})
</script>

<template>
  <BaseCard
    title="Oylik qoidalari"
    subtitle="Har dars yakunlanganda O‘SHA PAYTDAGI qoida bilan hisoblanib qotib qoladi — qoidani keyin tahrirlash o‘tgan oy hisobotini o‘zgartirmaydi. Bir xil turdagi qoidalardan faqat ENG ANIQI ishlaydi, turli turlar esa qo‘shiladi."
  >
    <template #actions>
      <BaseButton
        size="sm"
        variant="secondary"
        @click="openGuide()"
      >
        <template #icon>
          <AppIcon
            name="book"
            :size="14"
          />
        </template>
        Qanday hisoblanadi?
      </BaseButton>
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
        Yangi
      </BaseButton>
    </template>

    <div
      v-if="rulesQuery.isPending.value"
      class="flex justify-center py-6"
    >
      <BaseSpinner />
    </div>

    <p
      v-else-if="errorMessage !== null"
      class="text-xs text-rose-400"
      role="alert"
      v-text="errorMessage"
    />

    <p
      v-else-if="rules.length === 0"
      class="text-xs text-slate-400"
    >
      Qoida qo‘shilmagan — hisobot hech kimga summa hisoblamaydi.
    </p>

    <div
      v-else
      class="space-y-4"
    >
      <section
        v-for="group in groupedRules"
        :key="group.kind"
      >
        <div class="mb-1.5 flex flex-wrap items-baseline gap-x-2 gap-y-0.5">
          <h3
            class="text-[11px] font-semibold uppercase tracking-wide text-slate-500"
            v-text="group.label"
          />
          <p
            class="text-[11px] text-slate-500"
            v-text="payrollRuleKindHint(group.kind)"
          />
          <button
            type="button"
            class="text-[11px] font-medium text-brand-400 hover:text-brand-300"
            @click="openGuide(group.kind)"
          >
            Batafsil
          </button>
        </div>

        <ul class="divide-y divide-line">
          <li
            v-for="rule in group.items"
            :key="rule.id"
            class="flex flex-wrap items-center gap-3 py-2.5 first:pt-0 last:pb-0"
            :class="rule.isActive ? '' : 'opacity-50'"
          >
            <div class="min-w-0 flex-1">
              <div class="flex flex-wrap items-center gap-2">
                <span
                  class="truncate text-sm font-medium text-slate-100"
                  v-text="rule.name"
                />
                <BaseBadge :tone="rule.isActive ? 'success' : 'neutral'">
                  {{ rule.isActive ? 'Faol' : 'Nofaol' }}
                </BaseBadge>
                <BaseBadge
                  v-if="rule.pinnedGroupCount > 0"
                  tone="warning"
                >
                  {{ rule.pinnedGroupCount }} guruhga tayinlangan
                </BaseBadge>
              </div>
              <p
                class="mt-0.5 truncate text-[11px] text-slate-400"
                v-text="ruleScopeLabel(rule)"
              />
              <p
                class="text-[11px] text-slate-500"
                v-text="rulePeriodLabel(rule)"
              />
            </div>

            <p class="shrink-0 text-right text-sm font-semibold tabular-nums text-slate-100">
              {{ ruleValueLabel(rule) }}
              <span
                v-if="usesAcademicHour(rule.kind)"
                class="block text-[11px] font-normal text-slate-400"
              >
                / {{ rule.academicHourMinutes }} daqiqa
              </span>
              <span
                v-if="rule.minStudents !== null || rule.maxStudents !== null || rule.minDurationMinutes !== null"
                class="block text-[11px] font-normal text-slate-400"
              >
                shartli
              </span>
              <span
                v-if="rule.weekendHolidayMultiplier !== null"
                class="block text-[11px] font-normal text-amber-400"
              >
                dam olish/bayram × {{ rule.weekendHolidayMultiplier }}
              </span>
            </p>

            <div class="flex shrink-0 items-center gap-2">
              <BaseButton
                size="sm"
                variant="secondary"
                @click="openEdit(rule)"
              >
                <template #icon>
                  <AppIcon
                    name="edit"
                    :size="13"
                  />
                </template>
                Tahrirlash
              </BaseButton>
              <button
                type="button"
                class="tap-target flex items-center justify-center rounded-lg text-slate-400 transition-colors hover:bg-ink-800 hover:text-rose-400"
                title="O‘chirish"
                @click="deleting = rule"
              >
                <AppIcon
                  name="trash"
                  :size="15"
                />
              </button>
            </div>
          </li>
        </ul>
      </section>
    </div>

    <PayrollRuleFormDialog
      :open="formOpen"
      :rule="editing"
      @close="formOpen = false"
      @saved="refresh"
    />

    <PayrollKindGuideDrawer
      :open="guideOpen"
      :focus-kind="guideKind"
      @close="guideOpen = false"
    />

    <ConfirmDeleteDialog
      :open="deleting !== null"
      title="Qoidani o‘chirish"
      :message="deleteMessage"
      :pending="deleteMutation.isPending.value"
      :error="deleteError"
      @close="deleting = null"
      @confirm="confirmDelete"
    />
  </BaseCard>
</template>
