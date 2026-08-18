<script setup lang="ts">
import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { computed, ref, watch } from 'vue'

import { currentPeriod, isValidPeriod, periodLabel } from '@/entities/payment'
import {
  PENALTY_KIND_OPTIONS,
  PENALTY_STATUS_OPTIONS,
  approvePenalty,
  cancelPenalty,
  fetchPenalties,
  fetchPenaltiesByUser,
  fetchPenaltyCategories,
  fetchPenaltySummary,
  penaltyKindLabel,
  penaltyKindTone,
  penaltyStatusLabel,
  penaltyStatusTone,
  staffRoleLabel,
} from '@/entities/penalty'
import { useAuthStore } from '@/features/auth/model/auth.store'
import { ManualPenaltyDialog, PenaltyReportDrawer } from '@/features/penalty-manage'
import { toUserMessage } from '@/shared/api'
import { formatDateTimeNumeric } from '@/shared/lib/datetime'
import { useDebounced } from '@/shared/lib/debounce'
import { formatMoney } from '@/shared/lib/money'
import { useConfirm } from '@/shared/lib/useConfirm'
import type { PenaltyKindName, PenaltyRowDto, PenaltyStatusName } from '@/shared/types'
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
 * ════════════════════════════════════════════════════════════════════════
 *  JARIMALAR — ustoz va kuratorlar (2026-08-18)
 * ════════════════════════════════════════════════════════════════════════
 *
 * Uch manba: kech boshlangan dars va o'tilmagan dars — AVTOMATIK,
 * uchinchisi — qo'lda kiritilgan.
 *
 * ★ IKKI BOSQICHLI OQIM: jarima "Kutilmoqda" bo'lib tug'iladi va oylikka
 * TEGMAYDI. Faqat tasdiqlangach oylikka manfiy tuzatma yaratiladi.
 *
 * ★ "KUTILMOQDA" SUMMASI ALOHIDA KARTADA: u hali PUL EMAS. Tasdiqlangan
 * summa bilan bitta raqamga qo'shilsa, "qancha ushlandi" degan savolga
 * noto'g'ri javob berardi.
 *
 * ★ TARIFLAR BU YERDA EMAS: ular "Sozlamalar → Jarimalar" bo'limida
 * (loyiha egasi, 2026-08-18). Tarif kundalik ish emas, bir marta
 * sozlanadigan qoida.
 */
const auth = useAuthStore()
const queryClient = useQueryClient()
const confirm = useConfirm()

/**
 * KIM TASDIQLAY OLADI — server bilan AYNI qoida (2026-08-18):
 *   • TIZIM yozgan jarima (kechikish, o'tilmagan dars) — o'quv bo'limi ham;
 *   • QO'LDA yozilgan — faqat admin, chunki uni o'quv bo'limining O'ZI
 *     kiritadi va bitta odam ham yozib, ham pulga aylantira olmasin.
 */
function canReview(row: PenaltyRowDto): boolean {
  return auth.role === 'Admin' || row.kind !== 'Manual'
}

const SECTIONS = [
  { key: 'list', label: 'Jarimalar', icon: 'clipboard' },
  { key: 'user', label: 'Xodimlar kesimi', icon: 'users' },
] as const

const activeTab = ref<(typeof SECTIONS)[number]['key']>('list')

/* ------------------------------------------------------------ filtrlar */

const period = ref(currentPeriod())
const occurredOn = ref('')
const search = ref('')
const debouncedSearch = useDebounced(search)
const categoryFilter = ref<number | ''>('')
const kindFilter = ref<'' | PenaltyKindName>('')
const statusFilter = ref<'' | PenaltyStatusName>('')

/**
 * Tariflar ro'yxati — filtr uchun BARCHASI (arxivlangani ham): o'tgan
 * oyning jarimasi arxivlangan tarif bo'yicha yozilgan bo'lishi mumkin
 * va uni filtrlab ko'ra olmaslik mantiqsiz bo'lardi.
 */
const categoriesQuery = useQuery({
  queryKey: ['penalty-categories', 'all'],
  queryFn: ({ signal }) => fetchPenaltyCategories(false, { signal }),
})

const categories = computed(() => categoriesQuery.data.value ?? [])

const page = ref(1)
const pageSize = ref(20)
const PAGE_SIZE_OPTIONS = [10, 20, 50, 100] as const

const periodInvalid = computed(() => period.value.length > 0 && !isValidPeriod(period.value))

const effectiveSearch = computed(() => {
  const term = debouncedSearch.value.trim()
  return term.length > 0 ? term : undefined
})

const filters = computed(() => ({
  period: period.value.length > 0 && isValidPeriod(period.value) ? period.value : undefined,
  occurredOn: occurredOn.value.length > 0 ? occurredOn.value : undefined,
  search: effectiveSearch.value,
  categoryId: categoryFilter.value === '' ? undefined : categoryFilter.value,
  kind: kindFilter.value === '' ? undefined : kindFilter.value,
  status: statusFilter.value === '' ? undefined : statusFilter.value,
}))

const filtersActive = computed(
  () =>
    effectiveSearch.value !== undefined
    || occurredOn.value !== ''
    || categoryFilter.value !== ''
    || kindFilter.value !== ''
    || statusFilter.value !== ''
    || period.value !== currentPeriod(),
)

function resetFilters(): void {
  period.value = currentPeriod()
  occurredOn.value = ''
  search.value = ''
  categoryFilter.value = ''
  kindFilter.value = ''
  statusFilter.value = ''
}

watch([effectiveSearch, categoryFilter, kindFilter, statusFilter, period, occurredOn], () => {
  page.value = 1
})

watch(pageSize, () => {
  page.value = 1
})

/* ------------------------------------------------------------ so'rovlar */

const enabled = computed(() => !periodInvalid.value)

const listQuery = useQuery({
  queryKey: ['penalties', 'list', filters, page, pageSize],
  queryFn: ({ signal }) =>
    fetchPenalties({ ...filters.value, page: page.value, pageSize: pageSize.value }, { signal }),
  enabled: computed(() => enabled.value && activeTab.value === 'list'),
})

const summaryQuery = useQuery({
  queryKey: ['penalties', 'summary', filters],
  queryFn: ({ signal }) => fetchPenaltySummary(filters.value, { signal }),
  enabled,
})

const byUserQuery = useQuery({
  queryKey: ['penalties', 'by-user', filters],
  queryFn: ({ signal }) => fetchPenaltiesByUser(filters.value, { signal }),
  enabled: computed(() => enabled.value && activeTab.value === 'user'),
})

const rows = computed<PenaltyRowDto[]>(() => listQuery.data.value?.items ?? [])
const total = computed(() => listQuery.data.value?.total ?? 0)
const totalPages = computed(() => listQuery.data.value?.totalPages ?? 1)
const effectivePageSize = computed(() => listQuery.data.value?.pageSize ?? pageSize.value)

const summary = computed(() => summaryQuery.data.value ?? null)
const userRows = computed(() => byUserQuery.data.value ?? [])

const listError = computed(() =>
  listQuery.error.value !== null ? toUserMessage(listQuery.error.value) : null,
)
const userError = computed(() =>
  byUserQuery.error.value !== null ? toUserMessage(byUserQuery.error.value) : null,
)

/* ------------------------------------------------------------ amallar */

const actionError = ref<string | null>(null)
const busyId = ref<number | null>(null)

function refresh(): void {
  void queryClient.invalidateQueries({ queryKey: ['penalties'] })

  // 🔴 OYLIK HAM ESKIRADI: tasdiqlangan jarima `PayrollAdjustment`
  // yaratadi — ochiq "Oylik hisoblash" sahifasi eski summani
  // ko'rsatib qolmasin.
  void queryClient.invalidateQueries({ queryKey: ['payroll'] })
}

const approveMutation = useMutation({
  mutationFn: (id: number) => approvePenalty(id),
  onSuccess: refresh,
  onError: (error: Error) => {
    actionError.value = toUserMessage(error)
  },
  onSettled: () => {
    busyId.value = null
  },
})

const cancelMutation = useMutation({
  mutationFn: (input: { id: number; reason: string }) =>
    cancelPenalty(input.id, { reason: input.reason }),
  onSuccess: refresh,
  onError: (error: Error) => {
    actionError.value = toUserMessage(error)
  },
  onSettled: () => {
    busyId.value = null
  },
})

async function askApprove(row: PenaltyRowDto): Promise<void> {
  actionError.value = null

  const ok = await confirm({
    title: 'Jarimani tasdiqlash',
    message: `${row.userName} — ${formatMoney(row.amount)} so‘m.`,
    confirmLabel: 'Tasdiqlash',
    tone: 'danger',
    details: [
      'Summa oylikdan USHLAB QOLINADI (manfiy tuzatma yaratiladi).',
      'Tasdiqlangan jarimani keyin bekor qilib bo‘lmaydi.',
    ],
  })

  if (!ok) return

  busyId.value = row.id
  approveMutation.mutate(row.id)
}

async function askCancel(row: PenaltyRowDto): Promise<void> {
  actionError.value = null

  const ok = await confirm({
    title: 'Jarimani bekor qilish',
    message: `${row.userName} — ${formatMoney(row.amount)} so‘m.`,
    confirmLabel: 'Bekor qilish',
    tone: 'warning',
    details: [
      'Jarima oylikka TUSHMAYDI.',
      'Yozuv o‘chirilmaydi — holati “Bekor qilingan” bo‘ladi.',
    ],
  })

  if (!ok) return

  busyId.value = row.id
  cancelMutation.mutate({ id: row.id, reason: '' })
}

const manualOpen = ref(false)
const reportOpen = ref(false)

/**
 * Jarimaning turi — TARIF NOMI ustunroq.
 *
 * ★ NEGA TARIF NOMI ENUM YORLIG'IDAN USTUN: "Kech boshlagan" — tizim
 * ichki tasnifi, "Darsga kechikish · 15 daqiqa" esa operator o'zi
 * kiritgan tarif. Ikkinchisi savolga to'liqroq javob beradi; tarif
 * yo'q bo'lgan eski yozuvlarda enum yorlig'i zaxira bo'lib qoladi.
 */
function typeLabel(row: PenaltyRowDto): string {
  return row.categoryLabel ?? penaltyKindLabel(row.kind)
}

/** "15 daqiqa" — faqat songa qarab hisoblangan jarimada. */
function quantityLabel(row: PenaltyRowDto): string | null {
  if (row.quantity === null) return null

  return `${row.quantity} ${row.unitLabel ?? 'dona'}`
}

/** Kechikish jarimasida dalil: reja va haqiqiy vaqt. */
function proofTitle(row: PenaltyRowDto): string {
  if (row.sessionScheduledStart === null || row.sessionActualStart === null) return ''

  return `Reja: ${formatDateTimeNumeric(row.sessionScheduledStart)}`
    + ` · Boshlandi: ${formatDateTimeNumeric(row.sessionActualStart)}`
}
</script>

<template>
  <div>
    <PageHeader
      title="Jarimalar"
      subtitle="Ustoz va kuratorlar uchun. Kech boshlangan va o‘tilmagan darslar avtomatik aniqlanadi."
    >
      <template #actions>
        <BaseButton
          variant="secondary"
          :disabled="filters.period === undefined"
          @click="reportOpen = true"
        >
          <template #icon>
            <AppIcon
              name="clipboard"
              :size="15"
            />
          </template>
          Oylik hisobot
        </BaseButton>
        <BaseButton @click="manualOpen = true">
          <template #icon>
            <AppIcon
              name="plus"
              :size="15"
            />
          </template>
          Qo‘lda jarima
        </BaseButton>
      </template>
    </PageHeader>

    <!-- ═════════════════════ BO'LIMLAR ═════════════════════ -->
    <div
      class="mb-4 inline-flex gap-1 rounded-2xl border border-line bg-ink-900 p-1"
      role="tablist"
    >
      <button
        v-for="section in SECTIONS"
        :key="section.key"
        type="button"
        role="tab"
        :aria-selected="activeTab === section.key"
        class="flex items-center gap-1.5 rounded-xl px-4 py-2 text-sm font-semibold transition-colors"
        :class="
          activeTab === section.key
            ? 'bg-brand-500 text-on-brand'
            : 'text-slate-400 hover:bg-ink-800 hover:text-slate-100'
        "
        @click="activeTab = section.key"
      >
        <AppIcon
          :name="section.icon"
          :size="15"
        />
        {{ section.label }}
      </button>
    </div>

    <!-- ═════════════════════ FILTRLAR ═════════════════════ -->
    <div class="mb-4 grid gap-2.5 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-6">
      <div>
        <input
          v-model="period"
          class="zn-input"
          type="month"
          aria-label="Oylik davri"
        >
        <p
          v-if="periodInvalid"
          class="mt-1 text-[11px] text-rose-400"
        >
          Oy YYYY-MM ko‘rinishida bo‘lishi kerak.
        </p>
      </div>

      <!--
        ★ ANIQ SANA — OYDAN MUSTAQIL: "shu oyning hammasi" va "aynan
        12-avgust" ikki xil savol. Bahsda ko'pincha AYNAN kun so'raladi
        ("o'sha kuni nima bo'ldi?").
      -->
      <input
        v-model="occurredOn"
        class="zn-input"
        type="date"
        aria-label="Aniq sana"
      >

      <div class="relative sm:col-span-2 lg:col-span-1">
        <span class="pointer-events-none absolute inset-y-0 left-3 flex items-center text-slate-500">
          <AppIcon
            name="search"
            :size="16"
          />
        </span>
        <input
          v-model="search"
          class="zn-input pl-9"
          placeholder="Xodim, sabab yoki tarif"
        >
      </div>

      <select
        v-model="categoryFilter"
        class="zn-input"
        aria-label="Tarif bo‘yicha filtr"
      >
        <option value="">
          Barcha tariflar
        </option>
        <option
          v-for="option in categories"
          :key="option.id"
          :value="option.id"
        >
          {{ option.label }}
        </option>
      </select>

      <select
        v-model="kindFilter"
        class="zn-input"
        aria-label="Manba bo‘yicha filtr"
      >
        <option value="">
          Barcha manbalar
        </option>
        <option
          v-for="option in PENALTY_KIND_OPTIONS"
          :key="option.value"
          :value="option.value"
        >
          {{ option.label }}
        </option>
      </select>

      <div class="flex items-center gap-2">
        <select
          v-model="statusFilter"
          class="zn-input"
          aria-label="Holat bo‘yicha filtr"
        >
          <option value="">
            Barcha holatlar
          </option>
          <option
            v-for="option in PENALTY_STATUS_OPTIONS"
            :key="option.value"
            :value="option.value"
          >
            {{ option.label }}
          </option>
        </select>

        <button
          v-if="filtersActive"
          type="button"
          class="tap-target flex shrink-0 items-center gap-1 rounded-lg px-2 text-xs font-semibold text-slate-400 transition-colors hover:bg-ink-800 hover:text-slate-100"
          @click="resetFilters"
        >
          <AppIcon
            name="close"
            :size="13"
          />
          Tozalash
        </button>
      </div>
    </div>

    <!-- ═════════════════════ YIG'MA ═════════════════════ -->
    <div
      v-if="summary !== null"
      class="mb-4 grid grid-cols-2 gap-2.5 sm:grid-cols-4"
    >
      <div class="rounded-xl border border-line border-l-[3px] border-l-amber-500 bg-ink-900 p-3.5">
        <p
          class="text-lg font-bold tabular-nums text-amber-400"
          v-text="summary.pendingCount"
        />
        <p class="mt-0.5 text-[11px] text-slate-400">
          Kutilmoqda
        </p>
      </div>
      <div class="rounded-xl border border-line bg-ink-900 p-3.5">
        <p
          class="text-lg font-bold tabular-nums text-amber-400"
          v-text="formatMoney(summary.pendingAmount)"
        />
        <p class="mt-0.5 text-[11px] text-slate-400">
          Kutilmoqda (so‘m) — hali ushlanmagan
        </p>
      </div>
      <div class="rounded-xl border border-line border-l-[3px] border-l-rose-500 bg-ink-900 p-3.5">
        <p
          class="text-lg font-bold tabular-nums text-rose-400"
          v-text="summary.approvedCount"
        />
        <p class="mt-0.5 text-[11px] text-slate-400">
          Tasdiqlangan
        </p>
      </div>
      <div class="rounded-xl border border-line bg-ink-900 p-3.5">
        <p
          class="text-lg font-bold tabular-nums text-rose-400"
          v-text="formatMoney(summary.approvedAmount)"
        />
        <p class="mt-0.5 text-[11px] text-slate-400">
          Oylikdan ushlangan (so‘m)
        </p>
      </div>
    </div>

    <p
      v-if="actionError !== null"
      class="mb-3 rounded-lg border border-rose-500/25 bg-rose-500/10 px-3 py-2 text-xs text-rose-200"
      role="alert"
      v-text="actionError"
    />

    <!-- ═════════════════════ 1. JARIMALAR ═════════════════════ -->
    <DataStatus
      v-if="activeTab === 'list'"
      :pending="listQuery.isPending.value"
      :error="listError"
      :empty="rows.length === 0"
      :retrying="listQuery.isFetching.value"
      :skeleton-rows="4"
      empty-icon="clipboard"
      empty-title="Jarima yo‘q"
      :empty-text="
        filtersActive
          ? 'Filtr shartlarini o‘zgartirib ko‘ring.'
          : 'Bu davrda jarima yozilmagan — yaxshi natija.'
      "
      @retry="listQuery.refetch()"
    >
      <BaseCard flush>
        <div class="scroll-x-safe scrollbar-slim">
          <table class="zn-table">
            <thead>
              <tr>
                <th class="w-10">
                  #
                </th>
                <th>Xodim</th>
                <th>Sabab</th>
                <th>Dars</th>
                <th>Summa</th>
                <th>Holat</th>
                <th>Qachon</th>
                <!--
                  Ustun DOIM chiziladi: sahifaning o'ziga o'quv bo'limi
                  va admin kiradi, ikkalasi ham HECH BO'LMAGANDA tizim
                  yozgan jarimani ko'rib chiqa oladi.
                -->
                <th class="w-40" />
              </tr>
            </thead>
            <tbody>
              <tr
                v-for="(row, index) in rows"
                :key="row.id"
              >
                <td
                  class="tabular-nums text-dim"
                  v-text="(page - 1) * effectivePageSize + index + 1"
                />
                <td class="font-medium text-slate-100">
                  {{ row.userName }}
                  <span
                    class="block text-xs font-normal text-dim"
                    v-text="staffRoleLabel(row.userRole)"
                  />
                </td>
                <td class="max-w-56">
                  <span class="flex flex-wrap items-center gap-1">
                    <BaseBadge :tone="penaltyKindTone(row.kind)">
                      {{ typeLabel(row) }}
                    </BaseBadge>
                    <!--
                      ★ MIQDOR TARIF YONIDA: "Darsga kechikish · 15 daqiqa"
                      summaning QAYERDAN chiqqanini bir qarashda ko'rsatadi
                      — ustozga isbotlashda aynan shu so'raladi.
                    -->
                    <span
                      v-if="quantityLabel(row) !== null"
                      class="text-xs text-slate-400"
                      v-text="`· ${quantityLabel(row)}`"
                    />
                  </span>
                  <span
                    class="mt-1 block truncate text-xs text-slate-400"
                    :title="row.reason"
                    v-text="row.reason"
                  />
                </td>
                <td class="max-w-40">
                  <span
                    v-if="row.groupName !== null"
                    class="block truncate text-slate-300"
                    :title="proofTitle(row)"
                    v-text="row.groupName"
                  />
                  <span
                    v-else
                    class="text-dim"
                  >—</span>
                  <!--
                    ★ KECHIKISH DAQIQASI — ISBOT: bahsda "qancha kech"
                    degan savolga javob shu yerda, tooltipda esa aniq
                    reja/haqiqiy vaqt.
                  -->
                  <span
                    v-if="row.lateMinutes !== null"
                    class="mt-0.5 block text-xs font-semibold text-rose-300"
                    :title="proofTitle(row)"
                  >−{{ row.lateMinutes }} daq</span>
                </td>
                <td
                  class="font-semibold tabular-nums text-rose-300"
                  v-text="formatMoney(row.amount)"
                />
                <td>
                  <BaseBadge :tone="penaltyStatusTone(row.status)">
                    {{ penaltyStatusLabel(row.status) }}
                  </BaseBadge>
                  <span
                    v-if="row.reviewedByName !== null"
                    class="mt-0.5 block text-xs text-dim"
                    v-text="row.reviewedByName"
                  />
                </td>
                <td
                  class="tabular-nums text-slate-400"
                  v-text="formatDateTimeNumeric(row.occurredAt)"
                />
                <td>
                  <div
                    v-if="row.status === 'Pending' && canReview(row)"
                    class="flex gap-2"
                  >
                    <BaseButton
                      size="sm"
                      variant="danger"
                      :loading="busyId === row.id && approveMutation.isPending.value"
                      @click="askApprove(row)"
                    >
                      Tasdiqlash
                    </BaseButton>
                    <BaseButton
                      size="sm"
                      variant="secondary"
                      :loading="busyId === row.id && cancelMutation.isPending.value"
                      @click="askCancel(row)"
                    >
                      Bekor
                    </BaseButton>
                  </div>
                  <!--
                    ★ SABABI AYTILADI, jimgina bo'sh katak QOLDIRILMAYDI:
                    o'quv bo'limi xodimi o'zi kiritgan jarima yonida
                    tugma yo'qligini ko'rib "nosozlik" deb o'ylardi.
                  -->
                  <span
                    v-else-if="row.status === 'Pending'"
                    class="text-xs text-dim"
                  >Admin tasdiqlaydi</span>
                  <span
                    v-else
                    class="text-xs text-dim"
                  >—</span>
                </td>
              </tr>
            </tbody>
          </table>
        </div>

        <PaginationBar
          :page="page"
          :total-pages="totalPages"
          :total="total"
          :page-size="pageSize"
          :page-size-options="PAGE_SIZE_OPTIONS"
          @update:page="page = $event"
          @update:page-size="pageSize = $event"
        />
      </BaseCard>
    </DataStatus>

    <!-- ═════════════════════ 2. XODIMLAR KESIMI ═════════════════════ -->
    <BaseCard
      v-else
      title="Xodimlar kesimi"
      :subtitle="`Davr: ${filters.period !== undefined ? periodLabel(filters.period) : 'barcha'}`"
      flush
    >
      <DataStatus
        :pending="byUserQuery.isPending.value"
        :error="userError"
        :empty="userRows.length === 0"
        :retrying="byUserQuery.isFetching.value"
        :skeleton-rows="3"
        empty-icon="users"
        empty-title="Ma’lumot yo‘q"
        empty-text="Bu davrda jarima yozilmagan."
        @retry="byUserQuery.refetch()"
      >
        <div class="scroll-x-safe scrollbar-slim">
          <table class="zn-table">
            <thead>
              <tr>
                <th class="w-10">
                  #
                </th>
                <th>Xodim</th>
                <th>Kutilmoqda</th>
                <th>Tasdiqlangan</th>
                <th>Ushlangan (so‘m)</th>
                <th>Jami kechikish</th>
              </tr>
            </thead>
            <tbody>
              <tr
                v-for="(row, index) in userRows"
                :key="row.userId"
              >
                <td
                  class="tabular-nums text-dim"
                  v-text="index + 1"
                />
                <td class="font-medium text-slate-100">
                  {{ row.userName }}
                  <span
                    class="block text-xs font-normal text-dim"
                    v-text="staffRoleLabel(row.userRole)"
                  />
                </td>
                <td
                  class="tabular-nums"
                  :class="row.pendingCount > 0 ? 'font-semibold text-amber-400' : 'text-dim'"
                  v-text="row.pendingCount"
                />
                <td
                  class="tabular-nums text-slate-300"
                  v-text="row.approvedCount"
                />
                <td
                  class="font-semibold tabular-nums"
                  :class="row.approvedAmount > 0 ? 'text-rose-400' : 'text-dim'"
                  v-text="formatMoney(row.approvedAmount)"
                />
                <td
                  class="tabular-nums text-slate-400"
                  v-text="row.totalLateMinutes > 0 ? `${row.totalLateMinutes} daq` : '—'"
                />
              </tr>
            </tbody>
          </table>
        </div>
      </DataStatus>
    </BaseCard>

    <ManualPenaltyDialog
      :open="manualOpen"
      @close="manualOpen = false"
      @saved="refresh"
    />

    <PenaltyReportDrawer
      :open="reportOpen"
      :period="filters.period ?? ''"
      @close="reportOpen = false"
    />
  </div>
</template>
