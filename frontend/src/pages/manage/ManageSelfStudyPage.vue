<script setup lang="ts">
import { keepPreviousData, useQuery } from '@tanstack/vue-query'
import { computed, ref, watch } from 'vue'

import {
  SELF_STUDY_PANEL_KEYS,
  SUBSCRIPTION_STATE_LABELS,
  SUBSCRIPTION_STATE_TONES,
  fetchCuratorPool,
  fetchSelfStudyChats,
  fetchSelfStudyCurators,
  fetchSelfStudyStudents,
  fetchSelfStudySubmissions,
  fetchSelfStudySummary,
  type SelfStudyChatRowDto,
  type SelfStudyStudentRowDto,
  type SelfStudySubscriptionStateName,
  type SubmissionStatusName,
} from '@/entities/self-study-panel'
import { AssignCuratorDialog, ChatOversightDrawer, CuratorPoolCard } from '@/features/self-study-panel'
import StudentProfileDrawer from '@/features/student-profile/ui/StudentProfileDrawer.vue'
import { toUserMessage } from '@/shared/api'
import { formatDateNumeric, formatDateTimeNumeric } from '@/shared/lib/datetime'
import { useDebounced } from '@/shared/lib/debounce'
import { formatPhone } from '@/shared/lib/phone'
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
 *  MUSTAQIL TARIF PANELI (2026-11-19)
 * ════════════════════════════════════════════════════════════════════════
 *
 * O'quv bo'limi va administrator uchun bitta joy: O'quvchilar, Kuratorlar,
 * Vazifalar, Chatlar. Kurator shu yerda biriktiriladi (bitta yoki bir
 * nechta o'quvchiga birdan).
 *
 * ★ "Mustaqil o'quvchi" ta'rifi SERVERDA (obuna bor + faol guruhda emas).
 * ★ Chatlar FAQAT O'QISH — har ochish qayd etiladi (loyiha egasining qarori).
 * ★ Kuratorlar tabidagi tugmalar boshqa tablarni SHU kurator bo'yicha
 *   filtrlab ochadi — alohida "kurator sahifasi" o'rniga mavjud ro'yxatlar.
 */

const SECTIONS = [
  { key: 'students', label: 'O‘quvchilar', icon: 'users' },
  { key: 'curators', label: 'Kuratorlar', icon: 'user-check' },
  { key: 'submissions', label: 'Vazifalar', icon: 'clipboard' },
  { key: 'chats', label: 'Chatlar', icon: 'chat' },
] as const

type SectionKey = (typeof SECTIONS)[number]['key']

const activeTab = ref<SectionKey>('students')
const PAGE_SIZE_OPTIONS = [10, 20, 50, 100] as const

/* ------------------------------------------------------------ umumiy */

const summaryQuery = useQuery({
  queryKey: SELF_STUDY_PANEL_KEYS.summary,
  queryFn: ({ signal }) => fetchSelfStudySummary({ signal }),
})
const summary = computed(() => summaryQuery.data.value ?? null)

const curatorsQuery = useQuery({
  queryKey: SELF_STUDY_PANEL_KEYS.curators,
  queryFn: ({ signal }) => fetchSelfStudyCurators({ signal }),
})
const curators = computed(() => curatorsQuery.data.value ?? [])
/* Avtomatik biriktirish ro'yxati (2026-11-20) — eslatma banneri ham shundan. */
const poolQuery = useQuery({
  queryKey: SELF_STUDY_PANEL_KEYS.pool,
  queryFn: ({ signal }) => fetchCuratorPool({ signal }),
  refetchInterval: 60_000,
})
const pool = computed(() => poolQuery.data.value ?? null)

const curatorsWithStudents = computed(() =>
  curators.value.filter((c) => c.primaryStudents + c.backupStudents > 0 || c.role === 'Assistant'))

/** Qidiruv kamida 3 belgi (server qoidasi) — qisqasi so'rovga umuman ketmaydi. */
function searchTerm(value: string): string | undefined {
  const term = value.trim()
  return term.length >= 3 ? term : undefined
}

function errorText(error: unknown): string | null {
  return error ? toUserMessage(error) : null
}

/* ------------------------------------------------------------ 1. O'quvchilar */

const studentSearch = ref('')
const studentSearchDebounced = useDebounced(studentSearch)
const studentState = ref<'' | SelfStudySubscriptionStateName>('')
const studentCurator = ref<number | ''>('')
const unassignedOnly = ref(false)
const studentPage = ref(1)
const studentPageSize = ref(20)

const studentFilters = computed(() => ({
  search: searchTerm(studentSearchDebounced.value),
  state: studentState.value === '' ? undefined : studentState.value,
  curatorId: studentCurator.value === '' ? undefined : studentCurator.value,
  unassignedOnly: unassignedOnly.value,
  page: studentPage.value,
  pageSize: studentPageSize.value,
}))

watch([studentSearchDebounced, studentState, studentCurator, unassignedOnly, studentPageSize], () => {
  studentPage.value = 1
})

const studentsQuery = useQuery({
  queryKey: computed(() => SELF_STUDY_PANEL_KEYS.students(studentFilters.value)),
  queryFn: ({ signal }) => fetchSelfStudyStudents(studentFilters.value, { signal }),
  enabled: computed(() => activeTab.value === 'students'),
  placeholderData: keepPreviousData,
})
const students = computed(() => studentsQuery.data.value?.items ?? [])

const studentFiltersActive = computed(() =>
  studentSearch.value !== '' || studentState.value !== '' || studentCurator.value !== '' || unassignedOnly.value)

function resetStudentFilters(): void {
  studentSearch.value = ''
  studentState.value = ''
  studentCurator.value = ''
  unassignedOnly.value = false
}

/* Tanlash — ommaviy biriktirish uchun. Sahifa almashganda ham saqlanadi. */
const selected = ref(new Map<number, SelfStudyStudentRowDto>())
const allOnPageSelected = computed(() =>
  students.value.length > 0 && students.value.every((s) => selected.value.has(s.studentId)))

function toggle(row: SelfStudyStudentRowDto): void {
  const next = new Map(selected.value)
  if (next.has(row.studentId)) next.delete(row.studentId)
  else next.set(row.studentId, row)
  selected.value = next
}

function togglePage(): void {
  const next = new Map(selected.value)
  if (allOnPageSelected.value) students.value.forEach((s) => next.delete(s.studentId))
  else students.value.forEach((s) => next.set(s.studentId, s))
  selected.value = next
}

function progressPercent(row: SelfStudyStudentRowDto): number {
  return row.totalLessons === 0 ? 0 : Math.round((row.masteredLessons / row.totalLessons) * 100)
}

/* Kurator biriktirish oynasi */
const assignOpen = ref(false)
const assignTargets = ref<{ id: number, name: string, curatorId: number | null, backupCuratorId: number | null }[]>([])

function openAssign(rows: SelfStudyStudentRowDto[]): void {
  assignTargets.value = rows.map((r) => ({
    id: r.studentId,
    name: r.fullName,
    curatorId: r.curatorId,
    backupCuratorId: r.backupCuratorId,
  }))
  assignOpen.value = true
}

function closeAssign(): void {
  assignOpen.value = false
  if (assignTargets.value.length > 1) selected.value = new Map()
}

/* O'quvchi profili */
const profileId = ref<number | null>(null)
const profileName = ref<string | undefined>(undefined)

function openProfile(row: { studentId: number, fullName?: string, studentName?: string }): void {
  profileId.value = row.studentId
  profileName.value = row.fullName ?? row.studentName
}

/* ------------------------------------------------------------ 2. Kuratorlar */

function showCuratorStudents(curatorId: number): void {
  resetStudentFilters()
  studentCurator.value = curatorId
  activeTab.value = 'students'
}

function showCuratorSubmissions(curatorId: number): void {
  submissionCurator.value = curatorId
  submissionStatus.value = 'Submitted'
  activeTab.value = 'submissions'
}

function showCuratorChats(curatorId: number): void {
  chatCurator.value = curatorId
  activeTab.value = 'chats'
}

function showUnassigned(): void {
  resetStudentFilters()
  unassignedOnly.value = true
  studentState.value = 'Active'
  activeTab.value = 'students'
}

/* ------------------------------------------------------------ 3. Vazifalar */

const submissionSearch = ref('')
const submissionSearchDebounced = useDebounced(submissionSearch)
const submissionStatus = ref<'' | SubmissionStatusName>('Submitted')
const submissionCurator = ref<number | ''>('')
const submissionPage = ref(1)
const submissionPageSize = ref(20)

const submissionFilters = computed(() => ({
  search: searchTerm(submissionSearchDebounced.value),
  status: submissionStatus.value === '' ? undefined : submissionStatus.value,
  curatorId: submissionCurator.value === '' ? undefined : submissionCurator.value,
  page: submissionPage.value,
  pageSize: submissionPageSize.value,
}))

watch([submissionSearchDebounced, submissionStatus, submissionCurator, submissionPageSize], () => {
  submissionPage.value = 1
})

const submissionsQuery = useQuery({
  queryKey: computed(() => SELF_STUDY_PANEL_KEYS.submissions(submissionFilters.value)),
  queryFn: ({ signal }) => fetchSelfStudySubmissions(submissionFilters.value, { signal }),
  enabled: computed(() => activeTab.value === 'submissions'),
  placeholderData: keepPreviousData,
})
const submissions = computed(() => submissionsQuery.data.value?.items ?? [])

/** Tekshiruvni kutish vaqti — 24 soatdan oshsa ogohlantiriladi. */
function waitingHours(iso: string): number {
  return Math.floor((Date.now() - new Date(iso).getTime()) / 3_600_000)
}

function waitingLabel(iso: string): string {
  const hours = waitingHours(iso)
  if (hours < 1) return 'hozirgina'
  if (hours < 24) return `${hours} soat`
  return `${Math.floor(hours / 24)} kun`
}

/* ------------------------------------------------------------ 4. Chatlar */

const chatSearch = ref('')
const chatSearchDebounced = useDebounced(chatSearch)
const chatCurator = ref<number | ''>('')
const waitingOnly = ref(false)
const chatPage = ref(1)
const chatPageSize = ref(20)

const chatFilters = computed(() => ({
  search: searchTerm(chatSearchDebounced.value),
  curatorId: chatCurator.value === '' ? undefined : chatCurator.value,
  waitingOnly: waitingOnly.value,
  page: chatPage.value,
  pageSize: chatPageSize.value,
}))

watch([chatSearchDebounced, chatCurator, waitingOnly, chatPageSize], () => {
  chatPage.value = 1
})

const chatsQuery = useQuery({
  queryKey: computed(() => SELF_STUDY_PANEL_KEYS.chats(chatFilters.value)),
  queryFn: ({ signal }) => fetchSelfStudyChats(chatFilters.value, { signal }),
  enabled: computed(() => activeTab.value === 'chats'),
  placeholderData: keepPreviousData,
})
const chats = computed(() => chatsQuery.data.value?.items ?? [])

const openChat = ref<SelfStudyChatRowDto | null>(null)
</script>

<template>
  <div>
    <PageHeader
      title="Mustaqil tarif"
      subtitle="Obunali o‘quvchilar, ularning kuratorlari, vazifalari va kurator bilan yozishmalari."
    />

    <!--
      ★ ESLATMA (loyiha egasi, 2026-11-20): kuratorlar to'lgan va o'quvchilar
      navbatda — har tabda ko'rinadi, bosilsa Kuratorlar tabi ochiladi.
      Xodimlar Telegram guruhi sozlangan bo'lsa, u yerga ham kuniga bir marta boradi.
    -->
    <button
      v-if="pool !== null && (pool.needsNewCurator || (pool.members.length === 0 && pool.waitingStudents > 0))"
      type="button"
      class="mb-4 flex w-full items-start gap-2.5 rounded-xl border border-rose-500/40 bg-rose-500/10 px-4 py-3 text-left text-sm text-rose-100 transition-colors hover:bg-rose-500/15"
      role="alert"
      @click="activeTab = 'curators'"
    >
      <AppIcon
        name="user-check"
        :size="18"
        class="mt-0.5 shrink-0 text-rose-300"
      />
      <span>
        <span class="font-semibold">
          {{ pool.members.length === 0 ? 'Mustaqil kuratori qo‘shilmagan' : 'Kuratorlar to‘ldi' }}
          — {{ pool.waitingStudents }} ta o‘quvchi kuratorsiz kutmoqda.
        </span>
        Yangi kurator qo‘shing: qo‘shilishi bilan navbatdagi o‘quvchilar unga o‘zi biriktiriladi.
      </span>
    </button>

    <!-- ═════════════════════ YIG'MA ═════════════════════ -->
    <div
      v-if="summary !== null"
      class="mb-4 grid grid-cols-2 gap-2.5 sm:grid-cols-3 lg:grid-cols-5"
    >
      <div class="rounded-xl border border-line bg-ink-900 p-3.5">
        <p
          class="text-lg font-bold tabular-nums text-slate-100"
          v-text="summary.students"
        />
        <p class="mt-0.5 text-[11px] text-slate-400">
          Mustaqil o‘quvchilar
        </p>
      </div>
      <div class="rounded-xl border border-line border-l-[3px] border-l-emerald-500 bg-ink-900 p-3.5">
        <p
          class="text-lg font-bold tabular-nums text-emerald-400"
          v-text="summary.activeSubscriptions"
        />
        <p class="mt-0.5 text-[11px] text-slate-400">
          Faol obuna
        </p>
      </div>
      <button
        type="button"
        class="rounded-xl border border-line border-l-[3px] bg-ink-900 p-3.5 text-left transition-colors hover:bg-ink-800"
        :class="summary.unassigned > 0 ? 'border-l-rose-500' : 'border-l-slate-600'"
        @click="showUnassigned"
      >
        <p
          class="text-lg font-bold tabular-nums"
          :class="summary.unassigned > 0 ? 'text-rose-400' : 'text-slate-300'"
          v-text="summary.unassigned"
        />
        <p class="mt-0.5 text-[11px] text-slate-400">
          Kuratorsiz (faol obunali)
        </p>
      </button>
      <button
        type="button"
        class="rounded-xl border border-line border-l-[3px] border-l-amber-500 bg-ink-900 p-3.5 text-left transition-colors hover:bg-ink-800"
        @click="submissionStatus = 'Submitted'; submissionCurator = ''; activeTab = 'submissions'"
      >
        <p
          class="text-lg font-bold tabular-nums text-amber-400"
          v-text="summary.pendingSubmissions"
        />
        <p class="mt-0.5 text-[11px] text-slate-400">
          Tekshiruv kutayotgan vazifa
        </p>
      </button>
      <button
        type="button"
        class="rounded-xl border border-line border-l-[3px] border-l-sky-500 bg-ink-900 p-3.5 text-left transition-colors hover:bg-ink-800"
        @click="waitingOnly = true; chatCurator = ''; activeTab = 'chats'"
      >
        <p
          class="text-lg font-bold tabular-nums text-sky-400"
          v-text="summary.waitingChats"
        />
        <p class="mt-0.5 text-[11px] text-slate-400">
          Javob kutayotgan chat
        </p>
      </button>
    </div>

    <!-- ═════════════════════ BO'LIMLAR ═════════════════════ -->
    <div
      class="mb-4 inline-flex flex-wrap gap-1 rounded-2xl border border-line bg-ink-900 p-1"
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

    <!-- ═════════════════════ 1. O'QUVCHILAR ═════════════════════ -->
    <template v-if="activeTab === 'students'">
      <div class="mb-4 grid gap-2.5 sm:grid-cols-2 lg:grid-cols-4">
        <div class="relative">
          <span class="pointer-events-none absolute inset-y-0 left-3 flex items-center text-slate-500">
            <AppIcon
              name="search"
              :size="16"
            />
          </span>
          <input
            v-model="studentSearch"
            class="zn-input pl-9"
            placeholder="Ism yoki telefon (kamida 3 belgi)"
          >
        </div>
        <select
          v-model="studentState"
          class="zn-input"
          aria-label="Obuna holati"
        >
          <option value="">
            Barcha obunalar
          </option>
          <option value="Active">
            Faol
          </option>
          <option value="Upcoming">
            Boshlanmagan
          </option>
          <option value="Expired">
            Tugagan
          </option>
        </select>
        <select
          v-model="studentCurator"
          class="zn-input"
          aria-label="Kurator bo‘yicha"
        >
          <option value="">
            Barcha kuratorlar
          </option>
          <option
            v-for="c in curatorsWithStudents"
            :key="c.id"
            :value="c.id"
          >
            {{ c.fullName }}
          </option>
        </select>
        <div class="flex items-center gap-3">
          <label class="flex cursor-pointer items-center gap-2 text-sm text-slate-300">
            <input
              v-model="unassignedOnly"
              type="checkbox"
              class="size-4 accent-brand-500"
            >
            Faqat kuratorsizlar
          </label>
          <button
            v-if="studentFiltersActive"
            type="button"
            class="flex items-center gap-1 rounded-lg px-2 py-1 text-xs font-semibold text-slate-400 hover:bg-ink-800 hover:text-slate-100"
            @click="resetStudentFilters"
          >
            <AppIcon
              name="close"
              :size="13"
            />
            Tozalash
          </button>
        </div>
      </div>

      <div
        v-if="selected.size > 0"
        class="mb-3 flex flex-wrap items-center gap-2 rounded-xl border border-brand-500/30 bg-brand-500/10 px-3 py-2 text-sm text-slate-100"
      >
        <span class="font-semibold">{{ selected.size }} ta o‘quvchi tanlandi</span>
        <BaseButton
          size="sm"
          @click="openAssign([...selected.values()])"
        >
          Kuratorga biriktirish
        </BaseButton>
        <BaseButton
          size="sm"
          variant="ghost"
          @click="selected = new Map()"
        >
          Tanlovni bekor qilish
        </BaseButton>
      </div>

      <DataStatus
        :pending="studentsQuery.isPending.value"
        :error="errorText(studentsQuery.error.value)"
        :empty="students.length === 0"
        :retrying="studentsQuery.isFetching.value"
        :skeleton-rows="5"
        empty-icon="users"
        empty-title="O‘quvchi topilmadi"
        :empty-text="studentFiltersActive ? 'Filtr shartlarini o‘zgartirib ko‘ring.' : 'Hali Mustaqil obunali o‘quvchi yo‘q.'"
        @retry="studentsQuery.refetch()"
      >
        <BaseCard flush>
          <div class="scroll-x-safe scrollbar-slim">
            <table class="zn-table">
              <thead>
                <tr>
                  <th class="w-10">
                    <input
                      type="checkbox"
                      class="size-4 accent-brand-500"
                      :checked="allOnPageSelected"
                      aria-label="Sahifadagilarni tanlash"
                      @change="togglePage"
                    >
                  </th>
                  <th>O‘quvchi</th>
                  <th>Obuna</th>
                  <th>Kurator</th>
                  <th>Progress</th>
                  <th>Vazifa</th>
                  <th>Chat</th>
                  <th class="w-44" />
                </tr>
              </thead>
              <tbody>
                <tr
                  v-for="row in students"
                  :key="row.studentId"
                >
                  <td>
                    <input
                      type="checkbox"
                      class="size-4 accent-brand-500"
                      :checked="selected.has(row.studentId)"
                      :aria-label="`${row.fullName} — tanlash`"
                      @change="toggle(row)"
                    >
                  </td>
                  <td class="font-medium text-slate-100">
                    <button
                      type="button"
                      class="text-left hover:text-brand-300"
                      @click="openProfile(row)"
                    >
                      {{ row.fullName }}
                    </button>
                    <span
                      v-if="row.phone !== null"
                      class="block text-xs font-normal text-dim"
                      v-text="formatPhone(row.phone)"
                    />
                    <BaseBadge
                      v-if="!row.isActive"
                      tone="danger"
                    >
                      Nofaol
                    </BaseBadge>
                  </td>
                  <td>
                    <BaseBadge :tone="SUBSCRIPTION_STATE_TONES[row.state]">
                      {{ SUBSCRIPTION_STATE_LABELS[row.state] }}
                    </BaseBadge>
                    <span
                      class="mt-1 block text-xs text-dim"
                      v-text="[row.planName, row.subscriptionEndsAt ? `${formatDateNumeric(row.subscriptionEndsAt)} gacha` : null].filter(Boolean).join(' · ')"
                    />
                  </td>
                  <td>
                    <template v-if="row.curatorName !== null">
                      <span
                        class="text-slate-100"
                        v-text="row.curatorName"
                      />
                      <span
                        v-if="row.backupCuratorName !== null"
                        class="block text-xs text-dim"
                        v-text="`Zaxira: ${row.backupCuratorName}`"
                      />
                    </template>
                    <BaseBadge
                      v-else
                      :tone="row.state === 'Active' ? 'danger' : 'neutral'"
                    >
                      Biriktirilmagan
                    </BaseBadge>
                  </td>
                  <td class="min-w-32">
                    <div class="flex items-center gap-2">
                      <div class="h-1.5 w-20 overflow-hidden rounded-full bg-ink-800">
                        <div
                          class="h-full rounded-full bg-emerald-500"
                          :style="{ width: `${progressPercent(row)}%` }"
                        />
                      </div>
                      <span
                        class="text-xs tabular-nums text-slate-300"
                        v-text="`${row.masteredLessons}/${row.totalLessons}`"
                      />
                    </div>
                  </td>
                  <td>
                    <BaseBadge
                      v-if="row.pendingSubmissions > 0"
                      tone="warning"
                    >
                      {{ row.pendingSubmissions }} kutmoqda
                    </BaseBadge>
                    <span
                      v-else
                      class="text-xs text-dim"
                    >—</span>
                  </td>
                  <td>
                    <BaseBadge
                      v-if="row.waitingSince !== null"
                      tone="warning"
                    >
                      Javob kutmoqda · {{ waitingLabel(row.waitingSince) }}
                    </BaseBadge>
                    <span
                      v-else-if="row.lastMessageAt !== null"
                      class="text-xs text-dim"
                      v-text="formatDateTimeNumeric(row.lastMessageAt)"
                    />
                    <span
                      v-else
                      class="text-xs text-dim"
                    >—</span>
                  </td>
                  <td>
                    <div class="flex justify-end gap-1.5">
                      <BaseButton
                        size="sm"
                        variant="secondary"
                        @click="openAssign([row])"
                      >
                        {{ row.curatorId === null ? 'Biriktirish' : 'Kurator' }}
                      </BaseButton>
                      <RouterLink
                        :to="{ name: 'teacher-self-study-student', params: { studentId: row.studentId } }"
                        class="inline-flex items-center rounded-lg border border-line px-2.5 text-xs font-semibold text-slate-300 hover:bg-ink-800"
                      >
                        Darslar
                      </RouterLink>
                    </div>
                  </td>
                </tr>
              </tbody>
            </table>
          </div>
          <PaginationBar
            :page="studentPage"
            :total-pages="studentsQuery.data.value?.totalPages ?? 0"
            :total="studentsQuery.data.value?.total ?? 0"
            :page-size="studentPageSize"
            :page-size-options="PAGE_SIZE_OPTIONS"
            @update:page="studentPage = $event"
            @update:page-size="studentPageSize = $event"
          />
        </BaseCard>
      </DataStatus>
    </template>

    <!-- ═════════════════════ 2. KURATORLAR ═════════════════════ -->
    <template v-else-if="activeTab === 'curators'">
      <CuratorPoolCard
        :pool="pool"
        :candidates="curators"
      />
      <DataStatus
        :pending="curatorsQuery.isPending.value"
        :error="errorText(curatorsQuery.error.value)"
        :empty="curators.length === 0"
        :retrying="curatorsQuery.isFetching.value"
        :skeleton-rows="4"
        empty-icon="user-check"
        empty-title="Kurator yo‘q"
        empty-text="«Foydalanuvchilar» bo‘limida Kurator rolida xodim qo‘shing."
        @retry="curatorsQuery.refetch()"
      >
        <BaseCard flush>
          <div class="scroll-x-safe scrollbar-slim">
            <table class="zn-table">
              <thead>
                <tr>
                  <th>Xodim</th>
                  <th>Asosiy</th>
                  <th>Zaxira</th>
                  <th>Vazifa kutmoqda</th>
                  <th>Javobsiz chat</th>
                  <th>Oxirgi javobi</th>
                  <th class="w-72" />
                </tr>
              </thead>
              <tbody>
                <tr
                  v-for="row in curators"
                  :key="row.id"
                >
                  <td class="font-medium text-slate-100">
                    {{ row.fullName }}
                    <span
                      class="block text-xs font-normal text-dim"
                      v-text="row.role === 'Assistant' ? 'Kurator' : 'Ustoz'"
                    />
                  </td>
                  <td
                    class="tabular-nums"
                    v-text="row.primaryStudents"
                  />
                  <td
                    class="tabular-nums text-slate-400"
                    v-text="row.backupStudents"
                  />
                  <td>
                    <BaseBadge
                      v-if="row.pendingSubmissions > 0"
                      tone="warning"
                    >
                      {{ row.pendingSubmissions }}
                    </BaseBadge>
                    <span
                      v-else
                      class="text-xs text-dim"
                    >0</span>
                  </td>
                  <td>
                    <BaseBadge
                      v-if="row.waitingChats > 0"
                      tone="warning"
                    >
                      {{ row.waitingChats }}
                    </BaseBadge>
                    <span
                      v-else
                      class="text-xs text-dim"
                    >0</span>
                  </td>
                  <td
                    class="text-xs text-dim"
                    v-text="row.lastReplyAt !== null ? formatDateTimeNumeric(row.lastReplyAt) : '—'"
                  />
                  <td>
                    <div class="flex flex-wrap justify-end gap-1.5">
                      <BaseButton
                        size="sm"
                        variant="secondary"
                        @click="showCuratorStudents(row.id)"
                      >
                        O‘quvchilari
                      </BaseButton>
                      <BaseButton
                        size="sm"
                        variant="ghost"
                        @click="showCuratorSubmissions(row.id)"
                      >
                        Vazifalari
                      </BaseButton>
                      <BaseButton
                        size="sm"
                        variant="ghost"
                        @click="showCuratorChats(row.id)"
                      >
                        Chatlari
                      </BaseButton>
                    </div>
                  </td>
                </tr>
              </tbody>
            </table>
          </div>
        </BaseCard>
        <p class="mt-2 text-xs text-dim">
          Qo‘lda biriktirish ham mumkin: «O‘quvchilar» tabida o‘quvchilarni belgilang → «Kuratorga biriktirish».
        </p>
      </DataStatus>
    </template>

    <!-- ═════════════════════ 3. VAZIFALAR ═════════════════════ -->
    <template v-else-if="activeTab === 'submissions'">
      <div class="mb-4 grid gap-2.5 sm:grid-cols-3">
        <div class="relative">
          <span class="pointer-events-none absolute inset-y-0 left-3 flex items-center text-slate-500">
            <AppIcon
              name="search"
              :size="16"
            />
          </span>
          <input
            v-model="submissionSearch"
            class="zn-input pl-9"
            placeholder="O‘quvchi ismi yoki telefoni"
          >
        </div>
        <select
          v-model="submissionStatus"
          class="zn-input"
          aria-label="Holat"
        >
          <option value="">
            Barcha holatlar
          </option>
          <option value="Submitted">
            Tekshiruv kutmoqda
          </option>
          <option value="Graded">
            Tekshirilgan
          </option>
        </select>
        <select
          v-model="submissionCurator"
          class="zn-input"
          aria-label="Kurator bo‘yicha"
        >
          <option value="">
            Barcha kuratorlar
          </option>
          <option
            v-for="c in curatorsWithStudents"
            :key="c.id"
            :value="c.id"
          >
            {{ c.fullName }}
          </option>
        </select>
      </div>

      <DataStatus
        :pending="submissionsQuery.isPending.value"
        :error="errorText(submissionsQuery.error.value)"
        :empty="submissions.length === 0"
        :retrying="submissionsQuery.isFetching.value"
        :skeleton-rows="5"
        empty-icon="clipboard"
        empty-title="Vazifa yo‘q"
        empty-text="Bu filtr bo‘yicha javob topilmadi."
        @retry="submissionsQuery.refetch()"
      >
        <BaseCard flush>
          <div class="scroll-x-safe scrollbar-slim">
            <table class="zn-table">
              <thead>
                <tr>
                  <th>O‘quvchi</th>
                  <th>Vazifa</th>
                  <th>Kurator</th>
                  <th>Holat</th>
                  <th>Topshirilgan</th>
                  <th class="w-28" />
                </tr>
              </thead>
              <tbody>
                <tr
                  v-for="row in submissions"
                  :key="row.submissionId"
                >
                  <td class="font-medium text-slate-100">
                    <button
                      type="button"
                      class="text-left hover:text-brand-300"
                      @click="openProfile(row)"
                    >
                      {{ row.studentName }}
                    </button>
                  </td>
                  <td>
                    {{ row.assignmentTitle }}
                    <span
                      v-if="row.lessonName !== null"
                      class="block text-xs text-dim"
                      v-text="row.lessonName"
                    />
                  </td>
                  <td
                    class="text-sm"
                    v-text="row.curatorName ?? '—'"
                  />
                  <td>
                    <template v-if="row.status === 'Submitted'">
                      <BaseBadge :tone="waitingHours(row.submittedAt) >= 24 ? 'danger' : 'warning'">
                        Kutmoqda · {{ waitingLabel(row.submittedAt) }}
                      </BaseBadge>
                    </template>
                    <template v-else>
                      <BaseBadge tone="success">
                        {{ row.score ?? '—' }}/{{ row.maxScore }}
                      </BaseBadge>
                      <span
                        v-if="row.gradedByName !== null"
                        class="block text-xs text-dim"
                        v-text="row.gradedByName"
                      />
                    </template>
                    <span
                      v-if="row.attemptNumber > 1"
                      class="block text-xs text-dim"
                      v-text="`${row.attemptNumber}-urinish`"
                    />
                  </td>
                  <td
                    class="text-xs text-dim"
                    v-text="formatDateTimeNumeric(row.submittedAt)"
                  />
                  <td>
                    <RouterLink
                      :to="{ name: 'teacher-self-study-student', params: { studentId: row.studentId } }"
                      class="inline-flex items-center rounded-lg border border-line px-2.5 py-1 text-xs font-semibold text-slate-300 hover:bg-ink-800"
                    >
                      Ochish
                    </RouterLink>
                  </td>
                </tr>
              </tbody>
            </table>
          </div>
          <PaginationBar
            :page="submissionPage"
            :total-pages="submissionsQuery.data.value?.totalPages ?? 0"
            :total="submissionsQuery.data.value?.total ?? 0"
            :page-size="submissionPageSize"
            :page-size-options="PAGE_SIZE_OPTIONS"
            @update:page="submissionPage = $event"
            @update:page-size="submissionPageSize = $event"
          />
        </BaseCard>
      </DataStatus>
    </template>

    <!-- ═════════════════════ 4. CHATLAR ═════════════════════ -->
    <template v-else>
      <div class="mb-4 grid gap-2.5 sm:grid-cols-3">
        <div class="relative">
          <span class="pointer-events-none absolute inset-y-0 left-3 flex items-center text-slate-500">
            <AppIcon
              name="search"
              :size="16"
            />
          </span>
          <input
            v-model="chatSearch"
            class="zn-input pl-9"
            placeholder="O‘quvchi ismi yoki telefoni"
          >
        </div>
        <select
          v-model="chatCurator"
          class="zn-input"
          aria-label="Kurator bo‘yicha"
        >
          <option value="">
            Barcha kuratorlar
          </option>
          <option
            v-for="c in curatorsWithStudents"
            :key="c.id"
            :value="c.id"
          >
            {{ c.fullName }}
          </option>
        </select>
        <label class="flex cursor-pointer items-center gap-2 text-sm text-slate-300">
          <input
            v-model="waitingOnly"
            type="checkbox"
            class="size-4 accent-brand-500"
          >
          Faqat javob kutayotganlar
        </label>
      </div>

      <p class="mb-3 flex items-center gap-1.5 text-xs text-dim">
        <AppIcon
          name="lock"
          :size="13"
        />
        Yozishmalar faqat o‘qish uchun ochiladi; har bir ochish qayd etiladi.
      </p>

      <DataStatus
        :pending="chatsQuery.isPending.value"
        :error="errorText(chatsQuery.error.value)"
        :empty="chats.length === 0"
        :retrying="chatsQuery.isFetching.value"
        :skeleton-rows="5"
        empty-icon="chat"
        empty-title="Yozishma yo‘q"
        empty-text="Bu filtr bo‘yicha yozishma topilmadi."
        @retry="chatsQuery.refetch()"
      >
        <BaseCard flush>
          <div class="scroll-x-safe scrollbar-slim">
            <table class="zn-table">
              <thead>
                <tr>
                  <th>O‘quvchi</th>
                  <th>Kurator</th>
                  <th>Oxirgi xabar</th>
                  <th>Holat</th>
                  <th class="w-28" />
                </tr>
              </thead>
              <tbody>
                <tr
                  v-for="row in chats"
                  :key="`${row.studentId}-${row.staffId}`"
                >
                  <td class="font-medium text-slate-100">
                    {{ row.studentName }}
                    <span
                      class="block text-xs font-normal text-dim"
                      v-text="`${row.messageCount} ta xabar`"
                    />
                  </td>
                  <td>
                    {{ row.staffName }}
                    <span
                      v-if="!row.isCurrentCurator"
                      class="block text-xs text-dim"
                    >hozir kurator emas</span>
                  </td>
                  <td class="max-w-72">
                    <span
                      class="block truncate text-sm text-slate-300"
                      :title="row.lastMessagePreview"
                    >
                      <span class="text-dim">{{ row.lastFromStudent ? 'O‘quvchi:' : 'Kurator:' }}</span>
                      {{ row.lastMessagePreview }}
                    </span>
                    <span
                      class="block text-xs text-dim"
                      v-text="formatDateTimeNumeric(row.lastMessageAt)"
                    />
                  </td>
                  <td>
                    <BaseBadge
                      v-if="row.waitingSince !== null"
                      :tone="waitingHours(row.waitingSince) >= 24 ? 'danger' : 'warning'"
                    >
                      Javob kutmoqda · {{ waitingLabel(row.waitingSince) }}
                    </BaseBadge>
                    <BaseBadge
                      v-else
                      tone="success"
                    >
                      Javob berilgan
                    </BaseBadge>
                  </td>
                  <td>
                    <BaseButton
                      size="sm"
                      variant="secondary"
                      @click="openChat = row"
                    >
                      Ochish
                    </BaseButton>
                  </td>
                </tr>
              </tbody>
            </table>
          </div>
          <PaginationBar
            :page="chatPage"
            :total-pages="chatsQuery.data.value?.totalPages ?? 0"
            :total="chatsQuery.data.value?.total ?? 0"
            :page-size="chatPageSize"
            :page-size-options="PAGE_SIZE_OPTIONS"
            @update:page="chatPage = $event"
            @update:page-size="chatPageSize = $event"
          />
        </BaseCard>
      </DataStatus>
    </template>

    <AssignCuratorDialog
      :open="assignOpen"
      :students="assignTargets"
      :curators="curators"
      @close="closeAssign"
    />

    <ChatOversightDrawer
      :open="openChat !== null"
      :chat="openChat"
      @close="openChat = null"
    />

    <StudentProfileDrawer
      :open="profileId !== null"
      :user-id="profileId"
      :fallback-name="profileName"
      @close="profileId = null"
    />
  </div>
</template>
