<script setup lang="ts">
import { useQuery } from '@tanstack/vue-query'
import { computed, ref, watch } from 'vue'

import {
  EVENT_KIND_OPTIONS,
  TRIAL_LESSON_COUNT,
  eventKindLabel,
  eventKindTone,
  fetchAttrition,
  fetchAttritionByGroup,
  fetchAttritionByTeacher,
  fetchAttritionSummary,
  trialLabel,
  trialTone,
} from '@/entities/attrition'
import { RANGE_PRESETS, daysAgoIso, rangeError, todayIso } from '@/entities/teacher-availability'
import { GroupAttritionModal, TeacherGroupsBreakdown } from '@/features/attrition'
import { toUserMessage } from '@/shared/api'
import { useDebounced } from '@/shared/lib/debounce'
import { formatDateTimeNumeric } from '@/shared/lib/datetime'
import { useBreakpoint } from '@/shared/lib/useBreakpoint'
import type { AttritionRowDto, AttritionSortName, MembershipEventKindName } from '@/shared/types'
import { AppIcon, BaseBadge, BaseCard, DataStatus, PageHeader, PaginationBar } from '@/shared/ui'

/**
 * ════════════════════════════════════════════════════════════════════════
 *  TO'KILISHLAR (2026-08-17)
 * ════════════════════════════════════════════════════════════════════════
 *
 * Loyiha egasi talabi: *"aktiv probniy to'kilishlarni ko'rsatib turuvchi
 * panel ... qachon qaysi guruhdan nima sababdan qaysi ustozdan to'kilgan,
 * muzlatilgan, to'xtatilgan, ko'chirilgan barcha ma'lumotlar bo'lishi
 * kerak"*.
 *
 * ★ MANBA — o'chmaydigan hodisa jurnali (`GroupMembershipEvent`), a'zolik
 * qatorining o'zi EMAS: u faqat oxirgi holatni saqlaydi va o'quvchi
 * qaytsa tozalanadi (sabab backend entity izohida).
 *
 * ★ "PROBNIY" — HISOBLANADIGAN qiymat: 8 darsdan kam o'tab ketgan
 * o'quvchi sinov davrida ketgan hisoblanadi. Ikkisi markaz uchun boshqa
 * ma'no: probniy — "moslashuv/sotuv" muammosi, aktiv — "ushlab qolish"
 * muammosi. Shuning uchun panelda ular ALOHIDA ko'rsatiladi.
 *
 * ★ STANDART KESIM — OXIRGI 30 KUN (bugun emas): to'kilish kundalik
 * hodisa emas, bir kunlik oynada panel deyarli doim bo'sh ko'rinardi.
 */
const { isDesktop } = useBreakpoint()

const SECTIONS = [
  { key: 'list', label: 'Hodisalar', icon: 'clipboard' },
  { key: 'teacher', label: 'Ustozlar kesimi', icon: 'graduation' },
  { key: 'group', label: 'Guruhlar kesimi', icon: 'grid' },
] as const

const activeTab = ref<(typeof SECTIONS)[number]['key']>('list')

/* ------------------------------------------------------------ filtrlar */

const search = ref('')
const debouncedSearch = useDebounced(search)
const kindFilter = ref<'' | MembershipEventKindName>('')
const trialFilter = ref<'' | 'true' | 'false'>('')

const from = ref(daysAgoIso(29))
const to = ref(todayIso())

const sort = ref<AttritionSortName>('Date')
const desc = ref(true)

const page = ref(1)
const pageSize = ref(20)
const PAGE_SIZE_OPTIONS = [10, 20, 50, 100] as const

const dateError = computed(() => rangeError(from.value, to.value))

const effectiveSearch = computed(() => {
  const term = debouncedSearch.value.trim()
  return term.length > 0 ? term : undefined
})

const filters = computed(() => ({
  search: effectiveSearch.value,
  kind: kindFilter.value === '' ? undefined : kindFilter.value,
  trial: trialFilter.value === '' ? undefined : trialFilter.value === 'true',
  from: from.value.length > 0 ? from.value : undefined,
  to: to.value.length > 0 ? to.value : undefined,
}))

const defaultFrom = daysAgoIso(29)

const filtersActive = computed(
  () =>
    effectiveSearch.value !== undefined
    || kindFilter.value !== ''
    || trialFilter.value !== ''
    || from.value !== defaultFrom
    || to.value !== todayIso(),
)

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
  kindFilter.value = ''
  trialFilter.value = ''
  from.value = defaultFrom
  to.value = todayIso()
  sort.value = 'Date'
  desc.value = true
}

watch([effectiveSearch, kindFilter, trialFilter, from, to, sort, desc], () => {
  page.value = 1
})

watch(pageSize, () => {
  page.value = 1
})

/* ------------------------------------------------------------ so'rovlar */

const enabled = computed(() => dateError.value === null)

const listQuery = useQuery({
  queryKey: ['attrition', 'list', filters, sort, desc, page, pageSize],
  queryFn: ({ signal }) =>
    fetchAttrition(
      { ...filters.value, sort: sort.value, desc: desc.value, page: page.value, pageSize: pageSize.value },
      { signal },
    ),
  enabled: computed(() => enabled.value && activeTab.value === 'list'),
})

const summaryQuery = useQuery({
  queryKey: ['attrition', 'summary', filters],
  queryFn: ({ signal }) => fetchAttritionSummary(filters.value, { signal }),
  enabled,
})

const byTeacherQuery = useQuery({
  queryKey: ['attrition', 'by-teacher', filters],
  queryFn: ({ signal }) => fetchAttritionByTeacher(filters.value, { signal }),
  enabled: computed(() => enabled.value && activeTab.value === 'teacher'),
})

const byGroupQuery = useQuery({
  queryKey: ['attrition', 'by-group', filters],
  queryFn: ({ signal }) => fetchAttritionByGroup(filters.value, { signal }),
  enabled: computed(() => enabled.value && activeTab.value === 'group'),
})

const rows = computed<AttritionRowDto[]>(() => listQuery.data.value?.items ?? [])
const total = computed(() => listQuery.data.value?.total ?? 0)
const totalPages = computed(() => listQuery.data.value?.totalPages ?? 1)
const effectivePageSize = computed(() => listQuery.data.value?.pageSize ?? pageSize.value)

const summary = computed(() => summaryQuery.data.value ?? null)
const teacherRows = computed(() => byTeacherQuery.data.value ?? [])
const groupRows = computed(() => byGroupQuery.data.value ?? [])

const listError = computed(() =>
  listQuery.error.value !== null ? toUserMessage(listQuery.error.value) : null,
)
const teacherError = computed(() =>
  byTeacherQuery.error.value !== null ? toUserMessage(byTeacherQuery.error.value) : null,
)
const groupError = computed(() =>
  byGroupQuery.error.value !== null ? toUserMessage(byGroupQuery.error.value) : null,
)

/* ------------------------------------------------------------ saralash */

function toggleSort(column: AttritionSortName): void {
  if (sort.value === column) {
    desc.value = !desc.value
    return
  }

  sort.value = column
  desc.value = true
}

function sortIcon(column: AttritionSortName): 'arrow-down' | 'arrow-up' | null {
  if (sort.value !== column) return null
  return desc.value ? 'arrow-down' : 'arrow-up'
}

function ariaSort(column: AttritionSortName): 'ascending' | 'descending' | 'none' {
  if (sort.value !== column) return 'none'
  return desc.value ? 'descending' : 'ascending'
}

/* ------------------------------------------------------------ drill-down */

/**
 * Ochilgan ustoz qatori. `null` — hech biri ochiq emas.
 *
 * ⚠️ `teacherId` NING O'ZI `null` BO'LISHI MUMKIN ("ustoz tayinlanmagan"
 * guruhlar to'plami), shuning uchun "ochiq emas" holati alohida bayroq
 * bilan ajratiladi — aks holda o'sha qator hech qachon ochilmasdi.
 */
const expandedTeacherId = ref<number | null>(null)
const teacherExpanded = ref(false)

function isTeacherOpen(teacherId: number | null): boolean {
  return teacherExpanded.value && expandedTeacherId.value === teacherId
}

function toggleTeacher(teacherId: number | null): void {
  if (isTeacherOpen(teacherId)) {
    teacherExpanded.value = false
    expandedTeacherId.value = null
    return
  }

  teacherExpanded.value = true
  expandedTeacherId.value = teacherId
}

/** Ochilgan guruh modali. */
const groupModal = ref<{ groupId: number; groupName: string } | null>(null)

function openGroup(groupId: number, groupName: string): void {
  groupModal.value = { groupId, groupName }
}

/* Bo'lim almashsa ochilgan qator yopiladi — aks holda boshqa tabga
   qaytganda eski ochiq holat "yopishib" qolardi. */
watch(activeTab, () => {
  teacherExpanded.value = false
  expandedTeacherId.value = null
})
</script>

<template>
  <div>
    <PageHeader
      title="To‘kilishlar"
      :subtitle="`O‘quvchi qachon, qaysi guruhdan, qaysi ustozdan va nima sababdan ketgani. ${TRIAL_LESSON_COUNT} darsdan kam o‘tab ketgan — “probniy”.`"
    />

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
          placeholder="O‘quvchi, guruh yoki sabab bo‘yicha qidirish"
        >
      </div>

      <select
        v-model="kindFilter"
        class="zn-input"
        aria-label="Hodisa turi bo‘yicha filtr"
      >
        <option value="">
          Barcha hodisalar
        </option>
        <option
          v-for="option in EVENT_KIND_OPTIONS"
          :key="option.value"
          :value="option.value"
        >
          {{ option.label }}
        </option>
      </select>

      <select
        v-model="trialFilter"
        class="zn-input"
        aria-label="Probniy yoki aktiv bo‘yicha filtr"
      >
        <option value="">
          Probniy va aktiv
        </option>
        <option value="true">
          Faqat probniy ({{ TRIAL_LESSON_COUNT }} darsdan kam)
        </option>
        <option value="false">
          Faqat aktiv ({{ TRIAL_LESSON_COUNT }}+ dars)
        </option>
      </select>
    </div>

    <!-- ═════════════════════ YIG'MA ═════════════════════ -->
    <div
      v-if="summary !== null"
      class="mb-4 grid grid-cols-2 gap-2.5 sm:grid-cols-3 lg:grid-cols-6"
    >
      <div class="rounded-xl border border-line bg-ink-900 p-3.5">
        <p
          class="text-lg font-bold tabular-nums"
          :class="summary.stopped > 0 ? 'text-rose-400' : 'text-slate-100'"
          v-text="summary.stopped"
        />
        <p class="mt-0.5 text-[11px] text-slate-400">
          Chiqarilgan
        </p>
      </div>
      <div class="rounded-xl border border-line bg-ink-900 p-3.5">
        <p
          class="text-lg font-bold tabular-nums"
          :class="summary.paused > 0 ? 'text-amber-400' : 'text-slate-100'"
          v-text="summary.paused"
        />
        <p class="mt-0.5 text-[11px] text-slate-400">
          Muzlatilgan
        </p>
      </div>
      <div class="rounded-xl border border-line bg-ink-900 p-3.5">
        <p
          class="text-lg font-bold tabular-nums text-slate-100"
          v-text="summary.moved"
        />
        <p class="mt-0.5 text-[11px] text-slate-400">
          Ko‘chirilgan
        </p>
      </div>
      <div class="rounded-xl border border-line border-l-[3px] border-l-amber-500 bg-ink-900 p-3.5">
        <p
          class="text-lg font-bold tabular-nums text-amber-400"
          v-text="summary.trialLosses"
        />
        <p class="mt-0.5 text-[11px] text-slate-400">
          Probniy yo‘qotish
        </p>
      </div>
      <div class="rounded-xl border border-line border-l-[3px] border-l-brand-500 bg-ink-900 p-3.5">
        <p
          class="text-lg font-bold tabular-nums text-brand-400"
          v-text="summary.activeLosses"
        />
        <p class="mt-0.5 text-[11px] text-slate-400">
          Aktiv yo‘qotish
        </p>
      </div>
      <div class="col-span-2 rounded-xl border border-line bg-ink-900 p-3.5 sm:col-span-1">
        <p
          class="text-lg font-bold tabular-nums text-slate-100"
          v-text="summary.averageLessonsBeforeLeaving"
        />
        <p class="mt-0.5 text-[11px] text-slate-400">
          O‘rtacha dars (ketishdan oldin)
        </p>
      </div>
    </div>

    <!-- ═════════════════════ 1. HODISALAR ═════════════════════ -->
    <DataStatus
      v-if="activeTab === 'list'"
      :pending="listQuery.isPending.value"
      :error="listError"
      :empty="rows.length === 0"
      :retrying="listQuery.isFetching.value"
      :skeleton-rows="4"
      empty-icon="users"
      empty-title="Hodisa topilmadi"
      :empty-text="
        filtersActive
          ? 'Filtr shartlarini o‘zgartirib ko‘ring.'
          : 'Tanlangan davrda a’zolik o‘zgarishi bo‘lmagan.'
      "
      @retry="listQuery.refetch()"
    >
      <BaseCard flush>
        <!-- TELEFON: kartochka -->
        <ul
          v-if="!isDesktop"
          class="divide-y divide-line"
        >
          <li
            v-for="row in rows"
            :key="row.eventId"
            class="p-3.5"
          >
            <div class="flex flex-wrap items-center gap-2">
              <span
                class="min-w-0 flex-1 truncate text-sm font-semibold text-slate-100"
                v-text="row.studentName"
              />
              <BaseBadge :tone="eventKindTone(row.kind)">
                {{ eventKindLabel(row.kind) }}
              </BaseBadge>
              <BaseBadge
                size="xs"
                :tone="trialTone(row.isTrial)"
              >
                {{ trialLabel(row.isTrial) }}
              </BaseBadge>
            </div>

            <p class="mt-1 text-xs text-slate-400">
              <span
                class="tabular-nums"
                v-text="formatDateTimeNumeric(row.occurredAt)"
              />
              <span> · </span>
              <span
                class="font-medium text-slate-300"
                v-text="row.groupName"
              />
              <span v-if="row.teacherName !== null"> · ustoz: {{ row.teacherName }}</span>
            </p>

            <p class="mt-1 text-xs text-slate-400">
              <span class="text-dim">{{ row.lessonsCompleted }} dars o‘tagan</span>
              <template v-if="row.movedToGroupName !== null">
                <span> → </span>
                <span
                  class="text-slate-300"
                  v-text="row.movedToGroupName"
                />
              </template>
            </p>

            <p
              v-if="row.reason !== null"
              class="mt-1.5 rounded-lg bg-ink-800 px-2.5 py-1.5 text-xs text-slate-300"
              v-text="row.reason"
            />
          </li>
        </ul>

        <!-- DESKTOP: jadval -->
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
                <th :aria-sort="ariaSort('Date')">
                  <button
                    type="button"
                    class="inline-flex items-center gap-1 uppercase tracking-[0.06em] transition-colors hover:text-slate-100"
                    @click="toggleSort('Date')"
                  >
                    Qachon
                    <AppIcon
                      v-if="sortIcon('Date') !== null"
                      :name="sortIcon('Date')!"
                      :size="12"
                    />
                  </button>
                </th>
                <th :aria-sort="ariaSort('Student')">
                  <button
                    type="button"
                    class="inline-flex items-center gap-1 uppercase tracking-[0.06em] transition-colors hover:text-slate-100"
                    @click="toggleSort('Student')"
                  >
                    O‘quvchi
                    <AppIcon
                      v-if="sortIcon('Student') !== null"
                      :name="sortIcon('Student')!"
                      :size="12"
                    />
                  </button>
                </th>
                <th :aria-sort="ariaSort('Group')">
                  <button
                    type="button"
                    class="inline-flex items-center gap-1 uppercase tracking-[0.06em] transition-colors hover:text-slate-100"
                    @click="toggleSort('Group')"
                  >
                    Guruh
                    <AppIcon
                      v-if="sortIcon('Group') !== null"
                      :name="sortIcon('Group')!"
                      :size="12"
                    />
                  </button>
                </th>
                <th>Ustoz</th>
                <th>Nima bo‘ldi</th>
                <th :aria-sort="ariaSort('Lessons')">
                  <button
                    type="button"
                    class="inline-flex items-center gap-1 uppercase tracking-[0.06em] transition-colors hover:text-slate-100"
                    @click="toggleSort('Lessons')"
                  >
                    Dars
                    <AppIcon
                      v-if="sortIcon('Lessons') !== null"
                      :name="sortIcon('Lessons')!"
                      :size="12"
                    />
                  </button>
                </th>
                <th>Sabab</th>
                <th>Kim bajardi</th>
              </tr>
            </thead>
            <tbody>
              <tr
                v-for="(row, index) in rows"
                :key="row.eventId"
              >
                <td
                  class="tabular-nums text-dim"
                  v-text="(page - 1) * effectivePageSize + index + 1"
                />
                <td
                  class="tabular-nums text-slate-400"
                  v-text="formatDateTimeNumeric(row.occurredAt)"
                />
                <td
                  class="font-medium text-slate-100"
                  v-text="row.studentName"
                />
                <td class="max-w-40">
                  <span
                    class="block truncate text-slate-300"
                    :title="row.groupName"
                    v-text="row.groupName"
                  />
                  <span
                    v-if="row.movedToGroupName !== null"
                    class="block truncate text-xs text-dim"
                    :title="row.movedToGroupName"
                  >→ {{ row.movedToGroupName }}</span>
                </td>
                <td
                  class="text-slate-400"
                  v-text="row.teacherName ?? '—'"
                />
                <td>
                  <BaseBadge :tone="eventKindTone(row.kind)">
                    {{ eventKindLabel(row.kind) }}
                  </BaseBadge>
                </td>
                <td>
                  <span
                    class="tabular-nums text-slate-300"
                    v-text="row.lessonsCompleted"
                  />
                  <BaseBadge
                    size="xs"
                    :tone="trialTone(row.isTrial)"
                    class="ml-1.5"
                  >
                    {{ trialLabel(row.isTrial) }}
                  </BaseBadge>
                </td>
                <td class="max-w-56">
                  <span
                    v-if="row.reason !== null"
                    class="block truncate text-slate-300"
                    :title="row.reason"
                    v-text="row.reason"
                  />
                  <span
                    v-else
                    class="text-dim"
                  >—</span>
                </td>
                <td
                  class="text-slate-400"
                  v-text="row.actorName"
                />
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

    <!-- ═════════════════════ 2. USTOZLAR KESIMI ═════════════════════ -->
    <BaseCard
      v-else-if="activeTab === 'teacher'"
      title="Ustozlar kesimi"
      subtitle="Hodisa PAYTIDAGI ustoz bo‘yicha — ustoz keyin almashtirilgani hisobga ta’sir qilmaydi."
      flush
    >
      <DataStatus
        :pending="byTeacherQuery.isPending.value"
        :error="teacherError"
        :empty="teacherRows.length === 0"
        :retrying="byTeacherQuery.isFetching.value"
        :skeleton-rows="3"
        empty-icon="graduation"
        empty-title="Ma’lumot yo‘q"
        empty-text="Tanlangan davrda a’zolik o‘zgarishi bo‘lmagan."
        @retry="byTeacherQuery.refetch()"
      >
        <div class="scroll-x-safe scrollbar-slim">
          <table class="zn-table">
            <thead>
              <tr>
                <th class="w-10">
                  #
                </th>
                <th>Ustoz</th>
                <th>Chiqarilgan</th>
                <th>Muzlatilgan</th>
                <th>Ko‘chirilgan</th>
                <th>Probniy yo‘qotish</th>
              </tr>
            </thead>
            <tbody>
              <!--
                ★ OCHILADIGAN QATOR (loyiha egasi, 2026-08-17): ustoz ustiga
                bosilganda uning guruhlari bo'yicha bo'linish ochiladi,
                guruh ustiga bosilganda esa o'quvchilar modali. Uch qatlam
                jadval ICHIDA sig'masdi, shuning uchun uchinchisi modal.
              -->
              <template
                v-for="(row, index) in teacherRows"
                :key="row.teacherId ?? 0"
              >
                <tr
                  class="cursor-pointer"
                  role="button"
                  tabindex="0"
                  :aria-expanded="isTeacherOpen(row.teacherId)"
                  :aria-label="`${row.teacherName} guruhlarini ${isTeacherOpen(row.teacherId) ? 'yopish' : 'ochish'}`"
                  @click="toggleTeacher(row.teacherId)"
                  @keydown.enter.prevent="toggleTeacher(row.teacherId)"
                >
                  <td
                    class="tabular-nums text-dim"
                    v-text="index + 1"
                  />
                  <td class="font-medium text-slate-100">
                    <span class="inline-flex items-center gap-1.5">
                      <AppIcon
                        :name="isTeacherOpen(row.teacherId) ? 'chevron-down' : 'chevron-right'"
                        :size="14"
                        class="text-dim"
                      />
                      {{ row.teacherName }}
                    </span>
                  </td>
                  <td
                    class="tabular-nums"
                    :class="row.stopped > 0 ? 'font-semibold text-rose-400' : 'text-dim'"
                    v-text="row.stopped"
                  />
                  <td
                    class="tabular-nums text-amber-400"
                    v-text="row.paused"
                  />
                  <td
                    class="tabular-nums text-slate-400"
                    v-text="row.moved"
                  />
                  <td
                    class="tabular-nums text-slate-300"
                    v-text="row.trialLosses"
                  />
                </tr>

                <tr v-if="isTeacherOpen(row.teacherId)">
                  <td
                    colspan="6"
                    class="!p-0"
                  >
                    <TeacherGroupsBreakdown
                      :teacher-id="row.teacherId"
                      :params="filters"
                      @open-group="openGroup"
                    />
                  </td>
                </tr>
              </template>
            </tbody>
          </table>
        </div>
      </DataStatus>
    </BaseCard>

    <!-- ═════════════════════ 3. GURUHLAR KESIMI ═════════════════════ -->
    <BaseCard
      v-else
      title="Guruhlar kesimi"
      subtitle="Har guruhda nechta o‘quvchi yo‘qolgani va hozir nechta faol a’zo qolgani."
      flush
    >
      <DataStatus
        :pending="byGroupQuery.isPending.value"
        :error="groupError"
        :empty="groupRows.length === 0"
        :retrying="byGroupQuery.isFetching.value"
        :skeleton-rows="3"
        empty-icon="grid"
        empty-title="Ma’lumot yo‘q"
        empty-text="Tanlangan davrda a’zolik o‘zgarishi bo‘lmagan."
        @retry="byGroupQuery.refetch()"
      >
        <div class="scroll-x-safe scrollbar-slim">
          <table class="zn-table">
            <thead>
              <tr>
                <th class="w-10">
                  #
                </th>
                <th>Guruh</th>
                <th>Ustoz</th>
                <th>Chiqarilgan</th>
                <th>Muzlatilgan</th>
                <th>Ko‘chirilgan</th>
                <th>Probniy yo‘qotish</th>
                <th>Hozir faol</th>
              </tr>
            </thead>
            <tbody>
              <!--
                ★ QATOR BOSILADIGAN (loyiha egasi, 2026-08-17): guruh
                ustiga bosilganda to'liq ma'lumot modali ochiladi —
                guruh haqida (ustoz, boshlangan sana, qaysi darsga
                kelgani) va kim, nima sababdan ketgani.
              -->
              <tr
                v-for="(row, index) in groupRows"
                :key="row.groupId"
                class="cursor-pointer"
                role="button"
                tabindex="0"
                :aria-label="`${row.groupName} to‘kilishlarini ochish`"
                @click="openGroup(row.groupId, row.groupName)"
                @keydown.enter.prevent="openGroup(row.groupId, row.groupName)"
              >
                <td
                  class="tabular-nums text-dim"
                  v-text="index + 1"
                />
                <td class="font-medium text-slate-100">
                  <span class="inline-flex items-center gap-1.5">
                    {{ row.groupName }}
                    <AppIcon
                      name="chevron-right"
                      :size="13"
                      class="text-dim"
                    />
                  </span>
                </td>
                <td
                  class="text-slate-400"
                  v-text="row.teacherName ?? '—'"
                />
                <td
                  class="tabular-nums"
                  :class="row.stopped > 0 ? 'font-semibold text-rose-400' : 'text-dim'"
                  v-text="row.stopped"
                />
                <td
                  class="tabular-nums text-amber-400"
                  v-text="row.paused"
                />
                <td
                  class="tabular-nums text-slate-400"
                  v-text="row.moved"
                />
                <td
                  class="tabular-nums text-slate-300"
                  v-text="row.trialLosses"
                />
                <td
                  class="tabular-nums text-slate-300"
                  v-text="row.activeMembers"
                />
              </tr>
            </tbody>
          </table>
        </div>
      </DataStatus>
    </BaseCard>

    <!--
      Guruh tafsiloti modali — IKKI JOYDAN ochiladi: "Guruhlar kesimi"
      jadvalidan va "Ustozlar kesimi" dagi ochilgan guruh qatoridan.
      Bitta nusxa, bitta holat.
    -->
    <GroupAttritionModal
      :group-id="groupModal?.groupId ?? null"
      :group-name="groupModal?.groupName ?? ''"
      :params="filters"
      @close="groupModal = null"
    />
  </div>
</template>
