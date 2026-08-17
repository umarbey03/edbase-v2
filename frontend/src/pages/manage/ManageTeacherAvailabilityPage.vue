<script setup lang="ts">
import { useQuery } from '@tanstack/vue-query'
import { computed, ref, watch } from 'vue'

import {
  CHECKIN_STATUS_OPTIONS,
  RANGE_PRESETS,
  checkinStatusLabel,
  checkinStatusTone,
  coverageLabel,
  fetchTeacherAvailability,
  fetchTeacherAvailabilitySummary,
  rangeError,
  todayIso,
} from '@/entities/teacher-availability'
import { AvailabilityDetailDrawer } from '@/features/teacher-availability'
import { toUserMessage } from '@/shared/api'
import { useDebounced } from '@/shared/lib/debounce'
import { formatDateNumeric, formatTime } from '@/shared/lib/datetime'
import { useBreakpoint } from '@/shared/lib/useBreakpoint'
import type {
  TeacherAvailabilityRowDto,
  TeacherAvailabilitySortName,
  TeacherCheckinStatusName,
} from '@/shared/types'
import { AppIcon, BaseBadge, BaseCard, DataStatus, PageHeader, PaginationBar } from '@/shared/ui'

/**
 * ════════════════════════════════════════════════════════════════════════
 *  USTOZLAR HOLATI (2026-08-17) — kunlik "darsga o'ta olasizmi?" tasdiqlash
 *  + o'rinbosar tizimining o'quv bo'limi paneli.
 * ════════════════════════════════════════════════════════════════════════
 *
 * Suhbatning O'ZI (savol/javob, dars tanlash, sabab, o'rinbosar qidirish
 * va taklif) BUTUNLAY Telegram bot orqali ketadi — bu sahifa faqat
 * KUZATUV: barcha yozuvlar, sana kesimi, filtr/qidiruv/saralash.
 *
 * ★ POLLING FAQAT "BUGUN" KO'RINISHIDA (pastdagi `isLiveView`): tarixiy
 * oraliqda ma'lumot O'ZGARMAYDI, har 20 sekundda so'rov yuborish esa
 * bekorga server yuki va "sahifa o'zi yangilanib ketdi" hissi edi.
 *
 * ★ YIG'MA ALOHIDA SO'ROVDA: ro'yxat sahifalangan, yig'ma esa BUTUN
 * filtrga mos to'plamni sanaydi (backend'da ham shu qaror izohlangan).
 */
const { isDesktop } = useBreakpoint()

/* ------------------------------------------------------------ filtrlar */

const search = ref('')
const debouncedSearch = useDebounced(search)
const statusFilter = ref<'' | TeacherCheckinStatusName>('')
const onlyUncovered = ref(false)

// Standart ko'rinish — BUGUN (eng ko'p kerak bo'ladigan kesim).
const from = ref(todayIso())
const to = ref(todayIso())

const sort = ref<TeacherAvailabilitySortName>('Date')
const desc = ref(true)

const page = ref(1)
const pageSize = ref(20)
const PAGE_SIZE_OPTIONS = [10, 20, 50, 100] as const

const dateError = computed(() => rangeError(from.value, to.value))

/** Bo'sh satr YUBORILMAYDI — `undefined` "bu parametr yo'q" degani. */
const effectiveSearch = computed(() => {
  const term = debouncedSearch.value.trim()
  return term.length > 0 ? term : undefined
})

const filters = computed(() => ({
  search: effectiveSearch.value,
  status: statusFilter.value === '' ? undefined : statusFilter.value,
  from: from.value.length > 0 ? from.value : undefined,
  to: to.value.length > 0 ? to.value : undefined,
  onlyUncovered: onlyUncovered.value ? true : undefined,
}))

const filtersActive = computed(
  () =>
    effectiveSearch.value !== undefined
    || statusFilter.value !== ''
    || onlyUncovered.value
    || from.value !== todayIso()
    || to.value !== todayIso(),
)

/** Faqat bugungi kesim ochiq bo'lsa jonli yangilanadi (izoh sarlavhada). */
const isLiveView = computed(() => from.value === todayIso() && to.value === todayIso())

function applyPreset(key: string): void {
  const preset = RANGE_PRESETS.find((item) => item.key === key)
  if (preset === undefined) return

  from.value = preset.from()
  to.value = preset.to()
}

function activePreset(): string | null {
  const match = RANGE_PRESETS.find(
    (preset) => preset.from() === from.value && preset.to() === to.value,
  )
  return match?.key ?? null
}

function resetFilters(): void {
  search.value = ''
  statusFilter.value = ''
  onlyUncovered.value = false
  from.value = todayIso()
  to.value = todayIso()
  sort.value = 'Date'
  desc.value = true
}

/**
 * Filtr o'zgarsa 1-sahifaga qaytamiz — aks holda "10-sahifada natija yo'q"
 * holati chiqardi (`ManageUsersPage` dagi AYNI qoida va AYNI sabab).
 */
watch([effectiveSearch, statusFilter, onlyUncovered, from, to, sort, desc], () => {
  page.value = 1
})

watch(pageSize, () => {
  page.value = 1
})

/* ------------------------------------------------------------ so'rovlar */

const listQuery = useQuery({
  queryKey: ['teacher-availability', 'list', filters, sort, desc, page, pageSize],
  queryFn: ({ signal }) =>
    fetchTeacherAvailability(
      { ...filters.value, sort: sort.value, desc: desc.value, page: page.value, pageSize: pageSize.value },
      { signal },
    ),
  enabled: computed(() => dateError.value === null),
  refetchInterval: computed(() => (isLiveView.value ? 20_000 : false)),
})

const summaryQuery = useQuery({
  queryKey: ['teacher-availability', 'summary', filters],
  queryFn: ({ signal }) => fetchTeacherAvailabilitySummary(filters.value, { signal }),
  enabled: computed(() => dateError.value === null),
  refetchInterval: computed(() => (isLiveView.value ? 20_000 : false)),
})

const rows = computed<TeacherAvailabilityRowDto[]>(() => listQuery.data.value?.items ?? [])
const total = computed(() => listQuery.data.value?.total ?? 0)
const totalPages = computed(() => listQuery.data.value?.totalPages ?? 1)

/** Qator raqami GLOBAL — server echo qilgan `pageSize` bo'yicha (u chegaralanishi mumkin). */
const effectivePageSize = computed(() => listQuery.data.value?.pageSize ?? pageSize.value)

const summary = computed(() => summaryQuery.data.value ?? null)

const errorMessage = computed(() =>
  listQuery.error.value !== null ? toUserMessage(listQuery.error.value) : null,
)

/* ------------------------------------------------------------ saralash */

/**
 * Ustun sarlavhasi bosilganda: AYNI ustun bo'lsa yo'nalish almashadi,
 * boshqa ustun bo'lsa unga o'tib standart yo'nalish (kamayish) olinadi.
 */
function toggleSort(column: TeacherAvailabilitySortName): void {
  if (sort.value === column) {
    desc.value = !desc.value
    return
  }

  sort.value = column
  desc.value = true
}

function sortIcon(column: TeacherAvailabilitySortName): 'arrow-down' | 'arrow-up' | null {
  if (sort.value !== column) return null
  return desc.value ? 'arrow-down' : 'arrow-up'
}

/* ------------------------------------------------------------ tafsilot */

const selectedCheckinId = ref<number | null>(null)
</script>

<template>
  <div>
    <PageHeader
      title="Ustozlar holati"
      subtitle="Kunlik ‘darsga o‘ta olasizmi?’ tasdiqlash va o‘rinbosar qidiruvi. Suhbat Telegram bot orqali ketadi."
    />

    <!-- ═════════════════════ SANA KESIMI ═════════════════════ -->
    <div class="mb-3 flex flex-wrap items-center gap-2">
      <button
        v-for="preset in RANGE_PRESETS"
        :key="preset.key"
        type="button"
        class="inline-flex min-h-11 shrink-0 items-center rounded-[20px] border px-[15px] text-[13px] transition-colors"
        :class="
          activePreset() === preset.key
            ? 'border-brand-500 bg-brand-500/14 font-semibold text-brand-500'
            : 'border-line bg-ink-900 font-medium text-slate-400 hover:border-line-strong hover:bg-ink-800 hover:text-slate-100'
        "
        @click="applyPreset(preset.key)"
      >
        {{ preset.label }}
      </button>

      <span class="mx-1 hidden h-6 w-px bg-line sm:block" />

      <input
        v-model="from"
        class="zn-input w-[calc(50%-0.25rem)] flex-none sm:w-[9.5rem]"
        type="date"
        aria-label="Davr boshi"
      >
      <input
        v-model="to"
        class="zn-input w-[calc(50%-0.25rem)] flex-none sm:w-[9.5rem]"
        type="date"
        aria-label="Davr oxiri"
      >

      <button
        v-if="filtersActive"
        type="button"
        class="tap-target flex items-center gap-1 rounded-lg px-2 text-xs font-semibold text-slate-400 transition-colors hover:bg-ink-800 hover:text-slate-100"
        @click="resetFilters"
      >
        <AppIcon
          name="close"
          :size="13"
        />
        Tozalash
      </button>
    </div>

    <p
      v-if="dateError !== null"
      class="mb-3.5 rounded-lg border border-amber-500/25 bg-amber-500/10 px-3.5 py-2.5 text-xs text-amber-200"
      role="alert"
      v-text="dateError"
    />

    <!-- ═════════════════════ FILTRLAR ═════════════════════ -->
    <div class="mb-4 grid gap-2.5 sm:grid-cols-2 lg:grid-cols-4">
      <div class="relative sm:col-span-2">
        <span class="pointer-events-none absolute inset-y-0 left-3 flex items-center text-slate-500">
          <AppIcon
            name="search"
            :size="16"
          />
        </span>
        <input
          v-model="search"
          class="zn-input pl-9"
          placeholder="Ustoz ismi yoki sabab matni bo‘yicha qidirish"
        >
      </div>

      <select
        v-model="statusFilter"
        class="zn-input"
        aria-label="Holat bo‘yicha filtr"
      >
        <option value="">
          Barcha holatlar
        </option>
        <option
          v-for="option in CHECKIN_STATUS_OPTIONS"
          :key="option.value"
          :value="option.value"
        >
          {{ option.label }}
        </option>
      </select>

      <label class="flex min-h-11 items-center gap-2.5 text-sm text-slate-300">
        <input
          v-model="onlyUncovered"
          type="checkbox"
          class="size-4 accent-brand-500"
        >
        Faqat o‘rinbosarsizlar
      </label>
    </div>

    <!-- ═════════════════════ YIG'MA ═════════════════════ -->
    <div
      v-if="summary !== null"
      class="mb-4 grid grid-cols-2 gap-2.5 sm:grid-cols-3 lg:grid-cols-5"
    >
      <div class="rounded-xl border border-line bg-ink-900 p-3.5">
        <p
          class="text-lg font-bold tabular-nums text-slate-100"
          v-text="summary.total"
        />
        <p class="mt-0.5 text-[11px] text-slate-400">
          Filtrga mos yozuv
        </p>
      </div>
      <div class="rounded-xl border border-line bg-ink-900 p-3.5">
        <p
          class="text-lg font-bold tabular-nums text-green-400"
          v-text="summary.confirmed"
        />
        <p class="mt-0.5 text-[11px] text-slate-400">
          Tasdiqlagan
        </p>
      </div>
      <div class="rounded-xl border border-line bg-ink-900 p-3.5">
        <p
          class="text-lg font-bold tabular-nums"
          :class="summary.declined > 0 ? 'text-rose-400' : 'text-slate-100'"
          v-text="summary.declined"
        />
        <p class="mt-0.5 text-[11px] text-slate-400">
          O‘ta olmagan
        </p>
      </div>
      <div class="rounded-xl border border-line bg-ink-900 p-3.5">
        <p
          class="text-lg font-bold tabular-nums"
          :class="summary.pending + summary.inProgress > 0 ? 'text-amber-400' : 'text-slate-100'"
          v-text="summary.pending + summary.inProgress"
        />
        <p class="mt-0.5 text-[11px] text-slate-400">
          Javobsiz / yarim qolgan
        </p>
      </div>
      <div class="col-span-2 rounded-xl border border-line bg-ink-900 p-3.5 sm:col-span-1">
        <p class="text-lg font-bold tabular-nums text-slate-100">
          <span :class="summary.coverageResolved > 0 ? 'text-green-400' : ''">{{ summary.coverageResolved }}</span>
          <span class="text-dim"> / {{ summary.affectedSessions }}</span>
        </p>
        <p class="mt-0.5 text-[11px] text-slate-400">
          O‘rinbosar topilgan dars
        </p>
      </div>
    </div>

    <!-- ═════════════════════ RO'YXAT ═════════════════════ -->
    <DataStatus
      :pending="listQuery.isPending.value"
      :error="errorMessage"
      :empty="rows.length === 0"
      :retrying="listQuery.isFetching.value"
      :skeleton-rows="4"
      empty-icon="user-check"
      empty-title="Yozuv topilmadi"
      :empty-text="
        filtersActive
          ? 'Filtr shartlarini o‘zgartirib ko‘ring.'
          : 'Bugun darsi bor ustozlarga savol ertalab avtomatik yuboriladi (07:00–08:00).'
      "
      @retry="listQuery.refetch()"
    >
      <BaseCard flush>
        <!-- ─────────── TELEFON: KARTOCHKA RO'YXATI ─────────── -->
        <ul
          v-if="!isDesktop"
          class="divide-y divide-line"
        >
          <li
            v-for="row in rows"
            :key="row.checkinId"
            class="cursor-pointer p-3.5 hover:bg-ink-800"
            role="button"
            tabindex="0"
            :aria-label="`${row.teacherName} tafsilotini ochish`"
            @click="selectedCheckinId = row.checkinId"
            @keydown.enter.prevent="selectedCheckinId = row.checkinId"
          >
            <div class="flex flex-wrap items-center gap-2">
              <span
                class="tabular-nums text-xs text-slate-400"
                v-text="formatDateNumeric(row.checkinDate)"
              />
              <span
                class="min-w-0 flex-1 truncate text-sm font-semibold text-slate-100"
                v-text="row.teacherName"
              />
              <BaseBadge :tone="checkinStatusTone(row.status)">
                {{ checkinStatusLabel(row.status) }}
              </BaseBadge>
            </div>

            <p
              v-if="row.declineReason !== null"
              class="mt-1.5 text-xs text-slate-400"
            >
              {{ row.declineReason }}
              <span v-if="row.unavailableDays !== null && row.unavailableDays > 1">
                ({{ row.unavailableDays }} kunga)
              </span>
            </p>

            <p
              v-for="session in row.affectedSessions"
              :key="session.sessionId"
              class="mt-1.5 rounded-lg bg-ink-800 px-2.5 py-1.5 text-xs text-slate-400"
            >
              <span
                class="tabular-nums"
                v-text="formatTime(session.scheduledStart)"
              />
              <span class="font-medium text-slate-200"> {{ session.groupName }}</span>
              <br>
              <span v-text="row.teacherName" />
              <span> o‘tolmaydi</span>
              <template v-if="session.substituteTeacherName !== null">
                <span class="text-slate-500"> → </span>
                <span
                  class="font-semibold text-emerald-400"
                  v-text="`${session.substituteTeacherName} o‘tib beradi`"
                />
              </template>
              <span
                v-else
                class="text-amber-400"
              > — {{ coverageLabel(session.status) }}</span>
            </p>
          </li>
        </ul>

        <!-- ─────────── DESKTOP: JADVAL (saralanadigan) ─────────── -->
        <div
          v-else
          class="scroll-x-safe scrollbar-slim"
        >
          <table class="zn-table">
            <thead>
              <tr>
                <th class="w-10">
                  #
                </th>
                <!--
                  ★ SARALANADIGAN SARLAVHA — KODBAZADA BIRINCHI MARTA.
                  `zn-table th` da interaktiv uslub yo'q, shuning uchun
                  `<button>` ichkariga qo'yiladi: klaviatura bilan ham
                  ishlaydi va `aria-sort` skrinriderga yo'nalishni aytadi.
                -->
                <th :aria-sort="sort === 'Date' ? (desc ? 'descending' : 'ascending') : 'none'">
                  <button
                    type="button"
                    class="inline-flex items-center gap-1 uppercase tracking-[0.06em] transition-colors hover:text-slate-100"
                    @click="toggleSort('Date')"
                  >
                    Sana
                    <AppIcon
                      v-if="sortIcon('Date') !== null"
                      :name="sortIcon('Date')!"
                      :size="12"
                    />
                  </button>
                </th>
                <th :aria-sort="sort === 'Teacher' ? (desc ? 'descending' : 'ascending') : 'none'">
                  <button
                    type="button"
                    class="inline-flex items-center gap-1 uppercase tracking-[0.06em] transition-colors hover:text-slate-100"
                    @click="toggleSort('Teacher')"
                  >
                    Ustoz
                    <AppIcon
                      v-if="sortIcon('Teacher') !== null"
                      :name="sortIcon('Teacher')!"
                      :size="12"
                    />
                  </button>
                </th>
                <th :aria-sort="sort === 'Status' ? (desc ? 'descending' : 'ascending') : 'none'">
                  <button
                    type="button"
                    class="inline-flex items-center gap-1 uppercase tracking-[0.06em] transition-colors hover:text-slate-100"
                    @click="toggleSort('Status')"
                  >
                    Holat
                    <AppIcon
                      v-if="sortIcon('Status') !== null"
                      :name="sortIcon('Status')!"
                      :size="12"
                    />
                  </button>
                </th>
                <th>Sabab</th>
                <th>Ta'sirlangan dars / o‘rinbosar</th>
              </tr>
            </thead>
            <tbody>
              <tr
                v-for="(row, index) in rows"
                :key="row.checkinId"
                class="cursor-pointer"
                role="button"
                tabindex="0"
                :aria-label="`${row.teacherName} tafsilotini ochish`"
                @click="selectedCheckinId = row.checkinId"
                @keydown.enter.prevent="selectedCheckinId = row.checkinId"
              >
                <td
                  class="tabular-nums text-dim"
                  v-text="(page - 1) * effectivePageSize + index + 1"
                />
                <td
                  class="tabular-nums text-slate-400"
                  v-text="formatDateNumeric(row.checkinDate)"
                />
                <td
                  class="font-medium text-slate-100"
                  v-text="row.teacherName"
                />
                <td>
                  <BaseBadge :tone="checkinStatusTone(row.status)">
                    {{ checkinStatusLabel(row.status) }}
                  </BaseBadge>
                </td>
                <td class="max-w-56">
                  <span
                    v-if="row.declineReason !== null"
                    class="block truncate text-slate-300"
                    :title="row.declineReason"
                  >{{ row.declineReason }}<template v-if="row.unavailableDays !== null && row.unavailableDays > 1"> ({{ row.unavailableDays }} kun)</template></span>
                  <span
                    v-else
                    class="text-dim"
                  >—</span>
                </td>
                <td class="max-w-80 whitespace-normal">
                  <span
                    v-if="row.affectedSessions.length === 0"
                    class="text-dim"
                  >—</span>
                  <div
                    v-for="session in row.affectedSessions"
                    v-else
                    :key="session.sessionId"
                    class="mb-1 last:mb-0"
                  >
                    <span
                      class="tabular-nums text-slate-400"
                      v-text="formatTime(session.scheduledStart)"
                    />
                    <span
                      class="font-medium text-slate-200"
                      v-text="` ${session.groupName} — `"
                    />
                    <span
                      v-if="session.substituteTeacherName !== null"
                      class="font-semibold text-emerald-400"
                      v-text="`${session.substituteTeacherName} o‘tib beradi`"
                    />
                    <span
                      v-else
                      class="text-amber-400"
                      v-text="coverageLabel(session.status)"
                    />
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
          :page-size="pageSize"
          :page-size-options="PAGE_SIZE_OPTIONS"
          @update:page="page = $event"
          @update:page-size="pageSize = $event"
        />
      </BaseCard>
    </DataStatus>

    <AvailabilityDetailDrawer
      :checkin-id="selectedCheckinId"
      @close="selectedCheckinId = null"
    />
  </div>
</template>
