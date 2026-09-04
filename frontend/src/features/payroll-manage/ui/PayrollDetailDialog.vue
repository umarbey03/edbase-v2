<script setup lang="ts">
import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { computed, ref } from 'vue'

import {
  deletePayrollAdjustment,
  fetchPayrollDetail,
  payrollApprovalStatusLabel,
  payrollApprovalStatusTone,
  payrollPeriodLabel,
  payrollRoleLabel,
  setPayrollStudentCoefficient,
} from '@/entities/payroll'
import { toUserMessage } from '@/shared/api'
import { formatDateTime } from '@/shared/lib/datetime'
import { useConfirm } from '@/shared/lib/useConfirm'
import { formatMoney } from '@/shared/lib/money'
import { AppIcon, BaseBadge, BaseButton, BaseModal, BaseSpinner } from '@/shared/ui'

import PayrollAdjustmentDialog from './PayrollAdjustmentDialog.vue'

/**
 * Bitta xodimning dars-dars tafsiloti — hisobot qatorini bosganda ochiladi.
 *
 * ★ 2026-08-16 — kengaytirildi: baza oylik/KPI (kurator), qo'lda tuzatishlar
 * (qo'shish/o'chirish — faqat Draft davrda) va tasdiqlash/to'lov holati.
 * Sessiyalar RO'YXATI bo'sh bo'lsa ham (masalan hali darsi yo'q yangi
 * kurator) baza oylik/KPI/tuzatishlar bo'limi ko'rsatiladi — shu sabab
 * "bo'sh" holat endi FAQAT sessiyalar jadvaliga tegishli, butun dialogga
 * emas.
 */
const props = defineProps<{ open: boolean; userId: number | null; period: string }>()

const emit = defineEmits<{ close: [] }>()

const queryClient = useQueryClient()
const confirm = useConfirm()

const enabled = computed(() => props.open && props.userId !== null)
const userId = computed(() => props.userId)
const period = computed(() => props.period)

const detailQuery = useQuery({
  queryKey: ['payroll', 'detail', userId, period],
  queryFn: ({ signal }) => {
    const id = userId.value
    if (id === null) throw new Error('userId yo‘q.')
    return fetchPayrollDetail(id, { period: period.value }, { signal })
  },
  enabled,
})

const detail = computed(() => detailQuery.data.value ?? null)

const errorMessage = computed(() =>
  detailQuery.error.value !== null ? toUserMessage(detailQuery.error.value) : null,
)

const isDraft = computed(() => detail.value?.approvalStatus === 'Draft')

/* ------------------------------------------------------ hisob tafsiloti */

/**
 * Ochilgan dars qatorlari.
 *
 * ★ HOLAT SET'DA, DTO'DA EMAS: so'rov qayta yuklanganda (tuzatish
 * qo'shilgach) ochiq qatorlar YOPILMASLIGI kerak — admin qayerga
 * qaraganini yo'qotardi.
 */
const expanded = ref<Set<number>>(new Set())

function toggleSession(sessionId: number): void {
  const next = new Set(expanded.value)
  if (next.has(sessionId)) next.delete(sessionId)
  else next.add(sessionId)
  expanded.value = next
}

/* --------------------------------------------------------- koeffitsient */

/**
 * ★ BO'LIM FAQAT KERAK BO'LGANDA KO'RINADI: koeffitsient faqat "oylik —
 * har faol o'quvchi uchun" qoidasiga ta'sir qiladi. Bunday qoidasi yo'q
 * xodimda uni ko'rsatish adminni hech narsani o'zgartirmaydigan jadvalni
 * to'ldirishga undardi.
 */
const showCoefficients = computed(
  () => detail.value?.periodLines.some((line) => line.kind === 'MonthlyPerActiveStudent') ?? false,
)

const coefficientError = ref<string | null>(null)
const savingStudentId = ref<number | null>(null)

const coefficientMutation = useMutation({
  mutationFn: (payload: { studentId: number; percent: number }) => {
    const id = userId.value
    if (id === null) throw new Error('userId yo‘q.')

    return setPayrollStudentCoefficient({
      userId: id,
      studentId: payload.studentId,
      period: period.value,
      percent: payload.percent,
      note: null,
    })
  },
  onSuccess: () => {
    coefficientError.value = null
    refreshDetail()
  },
  onError: (error: unknown) => {
    coefficientError.value = toUserMessage(error)
  },
  onSettled: () => {
    savingStudentId.value = null
  },
})

/**
 * Kiritilgan foizni saqlaydi. Noto'g'ri qiymat SAQLANMAYDI va xato
 * ko'rsatiladi — serverga yuborib 400 kutish adminni javobni kutishga
 * majbur qilardi.
 */
function saveCoefficient(studentId: number, raw: string, current: number): void {
  const text = raw.trim()
  const value = text.length === 0 ? 100 : Number(text)

  if (!Number.isFinite(value) || value < 0 || value > 100) {
    coefficientError.value = 'Koeffitsient 0..100 oralig‘ida bo‘lishi kerak.'
    return
  }

  if (value === current) return

  coefficientError.value = null
  savingStudentId.value = studentId
  coefficientMutation.mutate({ studentId, percent: value })
}

/* ------------------------------------------------------------ tuzatish */

const adjustmentFormOpen = ref(false)
const deletingAdjustmentId = ref<number | null>(null)
const adjustmentError = ref<string | null>(null)

function refreshDetail(): void {
  void queryClient.invalidateQueries({ queryKey: ['payroll', 'detail', userId, period] })
  void queryClient.invalidateQueries({ queryKey: ['payroll', 'summary'] })
}

const deleteAdjustmentMutation = useMutation({
  mutationFn: (id: number) => deletePayrollAdjustment(id),
  onSuccess: () => {
    adjustmentError.value = null
    refreshDetail()
  },
  onError: (error: unknown) => {
    adjustmentError.value = toUserMessage(error)
  },
  onSettled: () => {
    deletingAdjustmentId.value = null
  },
})

async function askDeleteAdjustment(id: number, reason: string): Promise<void> {
  adjustmentError.value = null

  const ok = await confirm({
    title: 'Tuzatishni o‘chirish',
    message: `“${reason}” tuzatishi o‘chirilsinmi?`,
    confirmLabel: 'O‘chirish',
    tone: 'danger',
  })
  if (!ok) return

  deletingAdjustmentId.value = id
  deleteAdjustmentMutation.mutate(id)
}
</script>

<template>
  <BaseModal
    :open="props.open"
    wide
    :title="detail === null ? 'Dars-dars tafsilot' : `${detail.fullName} — ${payrollPeriodLabel(detail.period)}`"
    @close="emit('close')"
  >
    <div
      v-if="detailQuery.isPending.value"
      class="flex justify-center py-8"
    >
      <BaseSpinner />
    </div>

    <p
      v-else-if="errorMessage !== null"
      class="text-xs text-rose-400"
      role="alert"
      v-text="errorMessage"
    />

    <template v-else-if="detail !== null">
      <div class="mb-3 flex flex-wrap items-center justify-between gap-2">
        <p class="text-xs text-slate-400">
          {{ payrollRoleLabel(detail.role) }} · {{ detail.sessions.length }} dars · jami
          <span class="font-semibold text-slate-200">{{ formatMoney(detail.grandTotal) }}</span>
        </p>
        <BaseBadge :tone="payrollApprovalStatusTone(detail.approvalStatus)">
          {{ payrollApprovalStatusLabel(detail.approvalStatus) }}
        </BaseBadge>
      </div>

      <!--
        DAVR QOIDALARI — oklad, tushumdan foiz, oylik o'quvchi bonusi.
        Sessiyaga BOG'LIQ EMAS, shuning uchun alohida blok. Ro'yxat
        dinamik: yangi hisoblash turi qo'shilsa bu shablon o'zgarmaydi.
      -->
      <div
        v-if="detail.periodLines.length > 0"
        class="mb-3 rounded-lg border border-line bg-ink-950 p-3"
      >
        <p class="mb-2 text-[11px] font-semibold uppercase tracking-wide text-slate-500">
          Oylik qismi — darsga bog‘liq emas
        </p>
        <ul class="space-y-1.5">
          <li
            v-for="(line, index) in detail.periodLines"
            :key="`${line.ruleId ?? 'x'}-${index}`"
            class="flex flex-wrap items-baseline justify-between gap-2 text-xs"
          >
            <span class="min-w-0 text-slate-300">
              {{ line.ruleName }}
              <span
                v-if="line.basis !== null"
                class="text-slate-500"
              >
                · {{ line.basis }}
              </span>
            </span>
            <b class="shrink-0 tabular-nums text-slate-100">{{ formatMoney(line.amount) }}</b>
          </li>
        </ul>
        <p
          v-if="detail.weightedStudentUnits !== detail.activeStudentCount"
          class="mt-2 text-[11px] text-amber-400"
        >
          {{ detail.activeStudentCount }} faol o‘quvchi, koeffitsientlardan keyin
          {{ detail.weightedStudentUnits }} ulush.
        </p>
      </div>

      <p
        v-if="detail.sessions.length === 0"
        class="text-xs text-slate-400"
      >
        Shu davrda yakunlangan dars yo‘q.
      </p>

      <div
        v-else
        class="scroll-x-safe scrollbar-slim"
      >
        <table class="zn-table">
          <thead>
            <tr>
              <th>Sana</th>
              <th>Guruh</th>
              <th>Qatnashgan</th>
              <th>Stavka</th>
              <th>Bonus</th>
              <th>Jami</th>
            </tr>
          </thead>
          <tbody>
            <template
              v-for="session in detail.sessions"
              :key="session.sessionId"
            >
              <tr :class="session.excluded ? 'opacity-50' : ''">
                <td class="text-slate-300">
                  <!--
                    ★ QATORNI OCHISH — "nega bu dars shuncha?" savoliga
                      javob: qoidama-qoida tafsilot pastda ochiladi.
                      Tugma FAQAT tafsilot bo'lganda chiziladi, aks holda
                      bosilganda hech narsa ochilmasdi.
                  -->
                  <button
                    v-if="session.lines.length > 0"
                    type="button"
                    class="mr-1 align-middle text-slate-500 transition-colors hover:text-slate-200"
                    :title="expanded.has(session.sessionId) ? 'Yopish' : 'Hisob tafsiloti'"
                    @click="toggleSession(session.sessionId)"
                  >
                    <AppIcon
                      :name="expanded.has(session.sessionId) ? 'chevron-down' : 'chevron-right'"
                      :size="13"
                    />
                  </button>
                  {{ formatDateTime(session.scheduledStart) }}
                </td>
                <td class="text-slate-400">
                  {{ session.groupName }}
                </td>
                <td class="tabular-nums text-slate-300">
                  {{ session.attendedStudents }}
                </td>
                <td class="tabular-nums text-slate-300">
                  {{ formatMoney(session.sessionRate) }}
                  <span
                    v-if="session.premiumMultiplierApplied !== 1"
                    class="ml-1 text-[11px] font-normal text-amber-400"
                    :title="`Dam olish/bayram ustamasi qo'llangan: ×${session.premiumMultiplierApplied}`"
                  >
                    ×{{ session.premiumMultiplierApplied }}
                  </span>
                </td>
                <td class="tabular-nums text-slate-300">
                  {{ formatMoney(session.bonusAmount) }}
                </td>
                <td class="tabular-nums font-semibold text-slate-100">
                  <!--
                    ★ UCH XIL NOL — UCH XIL SABAB, va ular ARALASHTIRILMAYDI:
                      "qoida yo'q" = SOZLASH kerak (xato),
                      "oklad ichida" = ONGLI qaror,
                      "bepul dars" = darsning o'zi bepul.
                      Bittasini boshqasi bilan ko'rsatish adminni har oy
                      bor-yo'q muammoni qidirishga majbur qilardi.
                  -->
                  <span
                    v-if="session.ruleMissing"
                    class="inline-flex items-center gap-1"
                  >
                    <AppIcon
                      name="alert"
                      :size="13"
                      class="text-amber-400"
                    />
                    <BaseBadge tone="warning">
                      Qoida yo‘q
                    </BaseBadge>
                  </span>
                  <span
                    v-else-if="session.excluded"
                    class="text-xs font-normal text-dim"
                  >
                    bepul, haq yo‘q
                  </span>
                  <span
                    v-else-if="session.includedInSalary"
                    class="text-xs font-normal text-dim"
                  >
                    oklad ichida
                  </span>
                  <span v-else>{{ formatMoney(session.total) }}</span>
                </td>
              </tr>

              <tr v-if="expanded.has(session.sessionId) && session.lines.length > 0">
                <td
                  colspan="6"
                  class="bg-ink-950"
                >
                  <ul class="space-y-1 py-1">
                    <li
                      v-for="(line, index) in session.lines"
                      :key="`${line.ruleId ?? 'x'}-${index}`"
                      class="flex flex-wrap items-baseline justify-between gap-2 text-[11px]"
                    >
                      <span class="min-w-0 text-slate-400">
                        {{ line.ruleName }}
                        <span
                          v-if="line.basis !== null"
                          class="text-slate-500"
                        >
                          · {{ line.basis }}
                        </span>
                      </span>
                      <b class="shrink-0 tabular-nums text-slate-200">
                        {{ formatMoney(line.amount) }}
                      </b>
                    </li>
                  </ul>
                </td>
              </tr>
            </template>
          </tbody>
        </table>
      </div>

      <!--
        O'QUVCHI KOEFFITSIENTLARI — HolliHop'dagi «Коэффициент» ustuni.
        Oy o'rtasida qo'shilgan yoki boshqa kuratorga o'tgan o'quvchi
        uchun ulushni kamaytirish (masalan 50%).
      -->
      <div
        v-if="showCoefficients"
        class="mt-4"
      >
        <p class="mb-2 text-xs font-semibold uppercase tracking-[0.5px] text-slate-400">
          O‘quvchi koeffitsientlari
        </p>

        <p class="mb-2 text-[11px] text-slate-500">
          100% — to‘liq oy. Oy o‘rtasida qo‘shilgan o‘quvchi uchun kamaytiring.
          <template v-if="!isDraft">
            Davr tasdiqlangan — o‘zgartirib bo‘lmaydi.
          </template>
        </p>

        <p
          v-if="coefficientError !== null"
          class="mb-2 text-xs text-rose-400"
          role="alert"
          v-text="coefficientError"
        />

        <p
          v-if="detail.students.length === 0"
          class="text-xs text-slate-400"
        >
          Bu xodimning faol o‘quvchisi yo‘q.
        </p>

        <ul
          v-else
          class="divide-y divide-line rounded-lg border border-line"
        >
          <li
            v-for="student in detail.students"
            :key="student.studentId"
            class="flex items-center gap-2 p-2.5"
          >
            <span
              class="min-w-0 flex-1 truncate text-sm text-slate-200"
              v-text="student.studentName"
            />
            <div class="flex shrink-0 items-center gap-1">
              <input
                class="zn-input w-20 tabular-nums text-right"
                type="text"
                inputmode="numeric"
                autocomplete="off"
                :value="student.percent"
                :disabled="!isDraft || savingStudentId === student.studentId"
                @change="saveCoefficient(student.studentId, ($event.target as HTMLInputElement).value, student.percent)"
              >
              <span class="text-xs text-slate-500">%</span>
            </div>
          </li>
        </ul>
      </div>

      <!-- Qo'lda tuzatishlar. -->
      <div class="mt-4">
        <div class="mb-2 flex items-center justify-between">
          <p class="text-xs font-semibold uppercase tracking-[0.5px] text-slate-400">
            Qo‘lda tuzatishlar
          </p>
          <BaseButton
            v-if="isDraft"
            size="sm"
            variant="secondary"
            @click="adjustmentFormOpen = true"
          >
            <template #icon>
              <AppIcon
                name="plus"
                :size="13"
              />
            </template>
            Qo‘shish
          </BaseButton>
        </div>

        <p
          v-if="adjustmentError !== null"
          class="mb-2 text-xs text-rose-400"
          role="alert"
          v-text="adjustmentError"
        />

        <p
          v-if="detail.adjustments.length === 0"
          class="text-xs text-slate-400"
        >
          Tuzatish yo‘q.
        </p>

        <ul
          v-else
          class="divide-y divide-line rounded-lg border border-line"
        >
          <li
            v-for="adjustment in detail.adjustments"
            :key="adjustment.id"
            class="flex flex-wrap items-center gap-2 p-2.5"
          >
            <div class="min-w-0 flex-1">
              <p class="truncate text-sm text-slate-200">
                {{ adjustment.reason }}
              </p>
              <p class="text-[11px] text-dim">
                {{ adjustment.createdByName ?? 'Admin' }} · {{ formatDateTime(adjustment.createdAt) }}
                <!--
                  ★ Qatordan "nega o'chirish tugmasi yo'q?" degan savol
                    tug'ilmasin: manba va yechim shu yerda aytiladi.
                -->
                <template v-if="adjustment.fromPenalty">
                  · Jarimadan — bekor qilish “Jarimalar” panelida
                </template>
              </p>
            </div>
            <span
              class="shrink-0 text-sm font-semibold tabular-nums"
              :class="adjustment.amount < 0 ? 'text-rose-400' : 'text-emerald-400'"
            >
              {{ adjustment.amount > 0 ? '+' : '' }}{{ formatMoney(adjustment.amount) }}
            </span>
            <!--
              ★ `fromPenalty` — TUGMA UMUMAN CHIZILMAYDI: server bunday
                tuzatmani o'chirmaydi (jarima unga `Restrict` bilan havola
                qiladi) va ilgari bosilganda "Serverda kutilmagan xato"
                chiqardi. Qoida serverda ham bor — bu faqat birinchi
                qatlam (`PayrollAdjustmentDto.fromPenalty` izohi).
            -->
            <button
              v-if="isDraft && !adjustment.fromPenalty"
              type="button"
              class="tap-target flex shrink-0 items-center justify-center rounded-lg text-slate-400 transition-colors hover:bg-ink-800 hover:text-rose-400"
              title="O‘chirish"
              :disabled="deletingAdjustmentId === adjustment.id && deleteAdjustmentMutation.isPending.value"
              @click="askDeleteAdjustment(adjustment.id, adjustment.reason)"
            >
              <AppIcon
                name="trash"
                :size="14"
              />
            </button>
          </li>
        </ul>
      </div>
    </template>

    <template #footer>
      <BaseButton
        variant="secondary"
        @click="emit('close')"
      >
        Yopish
      </BaseButton>
    </template>
  </BaseModal>

  <PayrollAdjustmentDialog
    :open="adjustmentFormOpen"
    :user-id="props.userId"
    :period="props.period"
    @close="adjustmentFormOpen = false"
    @saved="refreshDetail"
  />
</template>
