<script setup lang="ts">
import { useQuery, useQueryClient } from '@tanstack/vue-query'
import { computed, ref, watch } from 'vue'

import {
  answerFormatsLabel,
  assignmentTitle,
  fetchAssignmentGroupsOverview,
  fetchAssignments,
  fetchSubmissions,
  fetchSubmissionsOverview,
  submissionStatusLabel,
  submissionStatusTone,
} from '@/entities/assignment'
import { fetchGroups, GROUP_SEARCH_MIN, groupDisplayName, groupTypeLabel, groupTypeTone } from '@/entities/group'
import { fetchUsers, USER_SEARCH_MIN } from '@/entities/user'
import AssignmentFormDialog from '@/features/assignment-form/ui/AssignmentFormDialog.vue'
import GradeDialog from '@/features/grading/ui/GradeDialog.vue'
import { toUserMessage } from '@/shared/api'
import { formatDateTimeNumeric } from '@/shared/lib/datetime'
import { useDebounced } from '@/shared/lib/debounce'
import { useBreakpoint } from '@/shared/lib/useBreakpoint'
import type {
  AssignmentDto,
  AssignmentGroupOverviewDto,
  GroupTypeName,
  SubmissionDto,
  SubmissionOverviewDto,
  SubmissionStatusName,
} from '@/shared/types'
import {
  AppIcon,
  BaseBadge,
  BaseButton,
  BaseCard,
  DataStatus,
  PageHeader,
  PaginationBar,
  SearchSelect,
} from '@/shared/ui'

/**
 * Uy vazifalari (o'quv bo'limi/admin).
 *
 * NEGA ALOHIDA SAHIFA, ustozning "Baholash" sahifasi yetmaydimi: KURS
 * vazifasini (dars nishoni) faqat o'quv bo'limi biriktiradi
 * (`AssignmentService.EnsureCanCreateAsync`), ustoz sahifasi esa `roles: STAFF`
 * bilan yopiq. Ya'ni bu sahifa bo'lmasa, endpoint'ning yarmi umuman
 * chaqirilmasdi.
 *
 * ════════════════════════════════════════════════════════════════════════
 * IKKI BO'LIM (2026-08-15 talabi)
 * ════════════════════════════════════════════════════════════════════════
 * "Vazifalar" — vazifa SHARTINI tuzish/tahrirlash (eski, o'zgarmagan xatti-
 * harakat). "Umumiy ko'rinish" — YANGI: "bir ko'rganda qaysi guruhdagi
 * vazifalar tekshirilmagani, nechtasi tekshirilgani/tekshirilmagani, javob
 * qachon yuborilgani/tekshirilgani, kim tekshirishi kerakligi, javob va
 * baho — hammasi ko'rinib turishi kerak" (ustoz/guruh turi/guruh filtri va
 * qidiruv bilan).
 *
 * ★ IKKI BO'LIM ALOHIDA, BITTAGA QO'SHILMAGAN: "shart tuzish" va "javoblarni
 * nazorat qilish" boshqa-boshqa vazifa — birinchisi kamdan-kam (yangi vazifa
 * berilganda), ikkinchisi kundalik. Ularni bitta jadvalga qo'shish "vazifa
 * qatorimi, javob qatorimi" degan chalkashlik keltirardi.
 */
const queryClient = useQueryClient()

/*
  Kartochka ↔ jadval: CSS emas, `v-if` — `hidden lg:block` IKKALA daraxtni
  ham quradi (telefonda ko'rinmas jadval ham mount bo'lib, ma'lumot olardi).
  ★ Chegara `lg` (1024px), `md` EMAS: yon menyu ham AYNI shu yerda ochiladi,
  ya'ni iPad tik holati (768px) kartochka bo'lib qoladi — `style.css` dagi
  "md va lg haqidagi asosiy qaror" izohiga qarang.
*/
const { isDesktop } = useBreakpoint()

const SECTIONS = [
  { key: 'list', label: 'Vazifalar', icon: 'clipboard' },
  { key: 'overview', label: 'Umumiy ko‘rinish', icon: 'chart' },
] as const

const activeTab = ref<(typeof SECTIONS)[number]['key']>('list')

/* ============================================================================
   BO'LIM 1 — VAZIFALAR (shart tuzish/tahrirlash). Xatti-harakat o'zgarmagan.
   ============================================================================ */

const PAGE_SIZE = 20

const page = ref(1)

const assignmentsQuery = useQuery({
  queryKey: ['assignments', 'manage', page],
  queryFn: ({ signal }) => fetchAssignments({ page: page.value, pageSize: PAGE_SIZE }, { signal }),
  enabled: computed(() => activeTab.value === 'list'),
})

const assignments = computed(() => assignmentsQuery.data.value?.items ?? [])
const total = computed(() => assignmentsQuery.data.value?.total ?? 0)
const totalPages = computed(() => assignmentsQuery.data.value?.totalPages ?? 1)

const errorMessage = computed(() =>
  assignmentsQuery.error.value !== null ? toUserMessage(assignmentsQuery.error.value) : null,
)

/** Nishon ustuni: guruh vazifasimi yoki kurs darsimi. */
function targetName(assignment: AssignmentDto): string {
  if (assignment.groupName !== null && assignment.groupName.length > 0) return assignment.groupName
  if (assignment.moduleLessonName !== null && assignment.moduleLessonName.length > 0) {
    return assignment.moduleLessonName
  }
  return '—'
}

function isCourseAssignment(assignment: AssignmentDto): boolean {
  return assignment.moduleLessonId !== null
}

const formOpen = ref(false)
const editing = ref<AssignmentDto | null>(null)

function openCreate(): void {
  editing.value = null
  formOpen.value = true
}

function openEdit(assignment: AssignmentDto): void {
  editing.value = assignment
  formOpen.value = true
}

function refresh(): void {
  // ★ PREFIKS BO'YICHA: `['assignments', 'overview', ...]` ham shu kalitdan
  // boshlanadi, ya'ni vazifa yaratilgan/tahrirlangan/baholanganidan keyin
  // "Umumiy ko'rinish" bo'limi ham avtomatik eskiradi — ikkinchi
  // invalidatsiya kerak emas.
  void queryClient.invalidateQueries({ queryKey: ['assignments'] })
}

/* ============================================================================
   BO'LIM 2 — UMUMIY KO'RINISH (2026-08-15). Filtr + guruh xulosasi + javoblar.
   ============================================================================ */

const overviewSearch = ref('')
const debouncedOverviewSearch = useDebounced(overviewSearch)
const overviewGroupType = ref<GroupTypeName | ''>('')
const overviewStatus = ref<SubmissionStatusName | ''>('')

/* --------------------------------------------------- ustoz qidiruvi/tanlovi */

const teacherSearch = ref('')
const debouncedTeacherSearch = useDebounced(teacherSearch)
const teacherTerm = computed(() => debouncedTeacherSearch.value.trim())
const teacherSearchTooShort = computed(
  () => teacherTerm.value.length > 0 && teacherTerm.value.length < USER_SEARCH_MIN,
)
const effectiveTeacherSearch = computed(() =>
  teacherTerm.value.length >= USER_SEARCH_MIN ? teacherTerm.value : undefined,
)

/*
  ★ NAQSH `ManageUsersPage`dagi guruh tanlagichi bilan AYNI: to'liq ro'yxat
  yuklanmaydi (o'nlab ustoz bo'lishi mumkin), qidiruvsiz holatda faqat
  birinchi 25 tasi ko'rinadi va tanlangan ustoz qidiruv natijasidan
  chiqib ketsa ham ro'yxatda QOLADI (nom saqlanadi).
*/
const teachersQuery = useQuery({
  queryKey: ['users', 'assignment-teacher-filter', effectiveTeacherSearch],
  queryFn: ({ signal }) =>
    fetchUsers({ role: 'Teacher', search: effectiveTeacherSearch.value, pageSize: 25 }, { signal }),
  enabled: computed(() => activeTab.value === 'overview'),
})

const teacherFilter = ref<{ id: number; name: string } | null>(null)

const teacherOptions = computed(() => {
  const list = (teachersQuery.data.value?.items ?? []).map((user) => ({
    id: user.id,
    name: user.fullName ?? `Foydalanuvchi #${user.id}`,
  }))
  const picked = teacherFilter.value
  if (picked !== null && !list.some((option) => option.id === picked.id)) {
    return [picked, ...list]
  }
  return list
})

/* ---------------------------------------------------- guruh qidiruvi/tanlovi */

const overviewGroupSearch = ref('')
const debouncedOverviewGroupSearch = useDebounced(overviewGroupSearch)
const overviewGroupTerm = computed(() => debouncedOverviewGroupSearch.value.trim())
const overviewGroupSearchTooShort = computed(
  () => overviewGroupTerm.value.length > 0 && overviewGroupTerm.value.length < GROUP_SEARCH_MIN,
)
const effectiveOverviewGroupSearch = computed(() =>
  overviewGroupTerm.value.length >= GROUP_SEARCH_MIN ? overviewGroupTerm.value : undefined,
)

const overviewGroupsQuery = useQuery({
  queryKey: ['groups', 'assignment-overview-filter', effectiveOverviewGroupSearch],
  queryFn: ({ signal }) =>
    fetchGroups({ search: effectiveOverviewGroupSearch.value, pageSize: 25 }, { signal }),
  enabled: computed(() => activeTab.value === 'overview'),
})

const overviewGroupFilter = ref<{ id: number; name: string } | null>(null)

const overviewGroupOptions = computed(() => {
  const list = (overviewGroupsQuery.data.value?.items ?? []).map((group) => ({
    id: group.id,
    name: groupDisplayName(group),
  }))
  const picked = overviewGroupFilter.value
  if (picked !== null && !list.some((option) => option.id === picked.id)) {
    return [picked, ...list]
  }
  return list
})

/** Guruh xulosasidagi qatorga bosilsa — javoblar ro'yxati o'sha guruhga toraytiriladi. */
function drillIntoGroup(row: AssignmentGroupOverviewDto): void {
  if (row.groupId === null) return
  overviewGroupFilter.value = { id: row.groupId, name: row.groupName }
}

/* -------------------------------------------------------------- so'rovlar */

const overviewFilter = computed(() => ({
  teacherId: teacherFilter.value?.id,
  groupType: overviewGroupType.value === '' ? undefined : overviewGroupType.value,
  groupId: overviewGroupFilter.value?.id,
  search: debouncedOverviewSearch.value.trim().length > 0
    ? debouncedOverviewSearch.value.trim()
    : undefined,
}))

const overviewPage = ref(1)
const OVERVIEW_PAGE_SIZE = 20

watch([overviewFilter, overviewStatus], () => {
  overviewPage.value = 1
})

const groupsOverviewQuery = useQuery({
  queryKey: ['assignments', 'overview', 'groups', overviewFilter],
  queryFn: ({ signal }) => fetchAssignmentGroupsOverview(overviewFilter.value, { signal }),
  enabled: computed(() => activeTab.value === 'overview'),
})

const groupRows = computed(() => groupsOverviewQuery.data.value ?? [])
const groupsOverviewError = computed(() =>
  groupsOverviewQuery.error.value !== null ? toUserMessage(groupsOverviewQuery.error.value) : null,
)

const submissionsOverviewQuery = useQuery({
  queryKey: ['assignments', 'overview', 'submissions', overviewFilter, overviewStatus, overviewPage],
  queryFn: ({ signal }) =>
    fetchSubmissionsOverview(
      {
        ...overviewFilter.value,
        status: overviewStatus.value === '' ? undefined : overviewStatus.value,
        page: overviewPage.value,
        pageSize: OVERVIEW_PAGE_SIZE,
      },
      { signal },
    ),
  enabled: computed(() => activeTab.value === 'overview'),
})

const submissionRows = computed(() => submissionsOverviewQuery.data.value?.items ?? [])
const submissionsTotal = computed(() => submissionsOverviewQuery.data.value?.total ?? 0)
const submissionsTotalPages = computed(() => submissionsOverviewQuery.data.value?.totalPages ?? 1)
const submissionsOverviewError = computed(() =>
  submissionsOverviewQuery.error.value !== null
    ? toUserMessage(submissionsOverviewQuery.error.value)
    : null,
)

/* ------------------------------------------------------- shu yerdan baholash

   ★ NEGA QAYTA SO'RALADI: `SubmissionOverviewDto` ro'yxat kartochkasi uchun
   YENGIL — unda `GradeDialog` ko'rsatadigan o'quvchi FAYLLARI yo'q (bu
   ro'yxat o'nlab qatorni bitta so'rovda qaytaradi, har biriga fayl
   ro'yxatini qo'shish uni og'irlashtirardi). "Baholash" bosilganda esa
   AYNI vazifaning TO'LIQ javoblari (`fetchSubmissions`, xodim allaqachon
   ishlatadigan yo'l) so'raladi va kerakli javob shu ro'yxatdan topiladi —
   ikkinchi endpoint yozish shart emas.
*/
const gradingRow = ref<SubmissionOverviewDto | null>(null)
const gradingSubmission = ref<SubmissionDto | null>(null)
const gradingLoading = ref(false)
const gradingLoadError = ref<string | null>(null)

async function openGrade(row: SubmissionOverviewDto): Promise<void> {
  gradingRow.value = row
  gradingSubmission.value = null
  gradingLoadError.value = null
  gradingLoading.value = true
  try {
    const list = await fetchSubmissions(row.assignmentId)
    const found = list.find((item) => item.id === row.submissionId) ?? null
    if (found === null) {
      gradingLoadError.value = 'Javob topilmadi — ro‘yxat yangilangan bo‘lishi mumkin.'
      return
    }
    gradingSubmission.value = found
  } catch (cause) {
    gradingLoadError.value = toUserMessage(cause)
  } finally {
    gradingLoading.value = false
  }
}

function closeGrade(): void {
  gradingRow.value = null
  gradingSubmission.value = null
  gradingLoadError.value = null
}

function handleGraded(): void {
  closeGrade()
  refresh()
}
</script>

<template>
  <div>
    <PageHeader
      title="Uy vazifalari"
      :subtitle="`Jami: ${total} ta vazifa`"
    >
      <template #actions>
        <BaseButton
          v-if="activeTab === 'list'"
          @click="openCreate"
        >
          <template #icon>
            <AppIcon
              name="plus"
              :size="16"
            />
          </template>
          Yangi
        </BaseButton>
      </template>
    </PageHeader>

    <div
      class="mb-5 inline-flex gap-1 rounded-2xl border border-line bg-ink-900 p-1"
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

    <!-- ═══════════════════════════════ BO'LIM 1 — VAZIFALAR ═══════════════════════════════ -->
    <template v-if="activeTab === 'list'">
      <DataStatus
        :pending="assignmentsQuery.isPending.value"
        :error="errorMessage"
        :empty="assignments.length === 0"
        :retrying="assignmentsQuery.isFetching.value"
        :skeleton-rows="4"
        empty-icon="clipboard"
        empty-title="Vazifa topilmadi"
        empty-text="Kurs darsiga yoki guruhga birinchi uy vazifasini biriktiring."
        @retry="assignmentsQuery.refetch()"
      >
        <template #empty-action>
          <BaseButton @click="openCreate">
            <template #icon>
              <AppIcon
                name="plus"
                :size="16"
              />
            </template>
            Yangi vazifa
          </BaseButton>
        </template>

        <BaseCard flush>
          <!-- Telefon/planshet: kartochka -->
          <ul
            v-if="!isDesktop"
            class="divide-y divide-line"
          >
            <li
              v-for="assignment in assignments"
              :key="assignment.id"
              class="p-3.5"
            >
              <div class="flex items-start justify-between gap-2">
                <p
                  class="min-w-0 flex-1 truncate text-sm font-medium text-slate-100"
                  v-text="assignmentTitle(assignment.title, assignment.id)"
                />
                <BaseBadge :tone="isCourseAssignment(assignment) ? 'accent' : 'neutral'">
                  {{ isCourseAssignment(assignment) ? 'Kurs darsi' : 'Guruh' }}
                </BaseBadge>
              </div>
              <p
                class="mt-1 truncate text-xs text-slate-400"
                v-text="targetName(assignment)"
              />
              <p class="text-xs text-dim">
                {{ assignment.maxScore }} ball ·
                {{ answerFormatsLabel(assignment.allowedFormats) }} ·
                {{ assignment.gradedCount }}/{{ assignment.submissionCount }} baholangan
              </p>
              <p
                v-if="assignment.dueAt !== null"
                class="text-xs tabular-nums text-dim"
              >
                Muddat: {{ formatDateTimeNumeric(assignment.dueAt) }}
              </p>
              <div class="mt-2.5 flex justify-end">
                <BaseButton
                  size="sm"
                  @click="openEdit(assignment)"
                >
                  <template #icon>
                    <AppIcon
                      name="edit"
                      :size="13"
                    />
                  </template>
                  Tahrirlash
                </BaseButton>
              </div>
            </li>
          </ul>

          <!-- Desktop (≥1024px): jadval -->
          <div
            v-else
            class="scroll-x-safe scrollbar-slim"
          >
            <table class="zn-table">
              <thead>
                <tr>
                  <th>Sarlavha</th>
                  <th>Nishon</th>
                  <th>Muddat</th>
                  <th>Ball</th>
                  <th>Javob turi</th>
                  <th>Baholangan</th>
                  <th />
                </tr>
              </thead>
              <tbody>
                <tr
                  v-for="assignment in assignments"
                  :key="assignment.id"
                >
                  <td class="font-medium text-slate-100">
                    <p
                      class="max-w-72 truncate"
                      v-text="assignmentTitle(assignment.title, assignment.id)"
                    />
                    <p
                      v-if="(assignment.description ?? '').length > 0"
                      class="mt-0.5 max-w-72 truncate text-xs font-normal text-dim"
                      v-text="assignment.description"
                    />
                  </td>
                  <td>
                    <BaseBadge :tone="isCourseAssignment(assignment) ? 'accent' : 'neutral'">
                      {{ isCourseAssignment(assignment) ? 'Kurs darsi' : 'Guruh' }}
                    </BaseBadge>
                    <span
                      class="ml-1.5 text-slate-400"
                      v-text="targetName(assignment)"
                    />
                  </td>
                  <td class="tabular-nums text-slate-400">
                    {{ assignment.dueAt === null ? 'Muddatsiz' : formatDateTimeNumeric(assignment.dueAt) }}
                  </td>
                  <td
                    class="tabular-nums text-slate-400"
                    v-text="assignment.maxScore"
                  />
                  <td
                    class="text-slate-400"
                    v-text="answerFormatsLabel(assignment.allowedFormats)"
                  />
                  <td class="tabular-nums text-slate-400">
                    {{ assignment.gradedCount }}/{{ assignment.submissionCount }}
                  </td>
                  <td class="text-right">
                    <BaseButton
                      size="sm"
                      variant="secondary"
                      @click="openEdit(assignment)"
                    >
                      <template #icon>
                        <AppIcon
                          name="edit"
                          :size="13"
                        />
                      </template>
                      Tahrirlash
                    </BaseButton>
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

      <!-- O'quv bo'limi kurs darsiga ham biriktira oladi. -->
      <AssignmentFormDialog
        :open="formOpen"
        :assignment="editing"
        allow-course-target
        @close="formOpen = false"
        @saved="refresh"
      />
    </template>

    <!-- ═══════════════════════════════ BO'LIM 2 — UMUMIY KO'RINISH ═══════════════════════════════ -->
    <template v-else>
      <!-- Filtrlar -->
      <div class="mb-4 grid gap-2.5 sm:grid-cols-2 lg:grid-cols-5">
        <div class="relative sm:col-span-2">
          <span class="pointer-events-none absolute inset-y-0 left-3 flex items-center text-slate-500">
            <AppIcon
              name="search"
              :size="16"
            />
          </span>
          <input
            v-model="overviewSearch"
            class="zn-input pl-9"
            placeholder="Vazifa, guruh yoki ustoz nomi"
          >
        </div>

        <div>
          <SearchSelect
            v-model="teacherFilter"
            :search="teacherSearch"
            :options="teacherOptions"
            :loading="teachersQuery.isFetching.value"
            placeholder="Ustozni qidirish"
            empty-label="Barcha ustozlar"
            label="Ustoz bo‘yicha filtr"
            @update:search="teacherSearch = $event"
          />
          <p
            v-if="teacherSearchTooShort"
            class="mt-1 text-[11px] text-dim"
          >
            Kamida {{ USER_SEARCH_MIN }} belgi kiriting.
          </p>
        </div>

        <select
          v-model="overviewGroupType"
          class="zn-input"
          aria-label="Guruh turi bo‘yicha filtr"
        >
          <option value="">
            Barcha guruh turlari
          </option>
          <option value="Group">
            Guruh
          </option>
          <option value="Individual">
            Individual
          </option>
          <option value="Curator">
            Kurator guruhi
          </option>
        </select>

        <div>
          <SearchSelect
            v-model="overviewGroupFilter"
            :search="overviewGroupSearch"
            :options="overviewGroupOptions"
            :loading="overviewGroupsQuery.isFetching.value"
            placeholder="Guruhni qidirish"
            empty-label="Barcha guruhlar"
            label="Guruh bo‘yicha filtr"
            @update:search="overviewGroupSearch = $event"
          />
          <p
            v-if="overviewGroupSearchTooShort"
            class="mt-1 text-[11px] text-dim"
          >
            Kamida {{ GROUP_SEARCH_MIN }} belgi kiriting.
          </p>
        </div>

        <select
          v-model="overviewStatus"
          class="zn-input"
          aria-label="Holat bo‘yicha filtr"
        >
          <option value="">
            Barcha holatlar
          </option>
          <option value="Submitted">
            Tekshirilmagan
          </option>
          <option value="Graded">
            Tekshirilgan
          </option>
        </select>
      </div>

      <!--
        ★ GURUH XULOSASI — "bir ko'rganda qaysi guruhda ko'p tekshirilmagan"
        talabining to'g'ridan-to'g'ri javobi. SAHIFALANMAYDI: server BUTUN
        filtrlangan to'plam bo'yicha aniq son beradi (`ListAsync` izohi).
        Guruh qatoriga bosilsa pastdagi javoblar ro'yxati o'sha guruhga
        toraytiriladi — "Kurs vazifalari" qatori bundan mustasno (bitta
        aniq guruhi yo'q).
      -->
      <BaseCard
        title="Guruhlar bo‘yicha xulosa"
        flush
        class="mb-5"
      >
        <DataStatus
          :pending="groupsOverviewQuery.isPending.value"
          :error="groupsOverviewError"
          :empty="groupRows.length === 0"
          :retrying="groupsOverviewQuery.isFetching.value"
          :skeleton-rows="3"
          empty-icon="chart"
          empty-title="Ma’lumot yo‘q"
          empty-text="Tanlangan filtrlarga mos vazifa topilmadi."
          @retry="groupsOverviewQuery.refetch()"
        >
          <div class="scroll-x-safe scrollbar-slim">
            <table class="zn-table">
              <thead>
                <tr>
                  <th>Guruh</th>
                  <th>Turi</th>
                  <th>Ustoz</th>
                  <th>Vazifalar</th>
                  <th>Javoblar</th>
                  <th>Tekshirilgan</th>
                  <th>Tekshirilmagan</th>
                  <th>Oxirgi javob</th>
                </tr>
              </thead>
              <tbody>
                <tr
                  v-for="row in groupRows"
                  :key="row.groupId ?? 'course'"
                  :class="row.groupId !== null ? 'cursor-pointer hover:bg-ink-800' : ''"
                  :role="row.groupId !== null ? 'button' : undefined"
                  :tabindex="row.groupId !== null ? 0 : undefined"
                  @click="drillIntoGroup(row)"
                  @keydown.enter.prevent="drillIntoGroup(row)"
                >
                  <td
                    class="font-medium text-slate-100"
                    v-text="row.groupName"
                  />
                  <td>
                    <BaseBadge
                      v-if="row.groupType !== null"
                      :tone="groupTypeTone(row.groupType)"
                    >
                      {{ groupTypeLabel(row.groupType) }}
                    </BaseBadge>
                    <span
                      v-else
                      class="text-dim"
                    >—</span>
                  </td>
                  <td
                    class="text-slate-400"
                    v-text="row.teacherName ?? '—'"
                  />
                  <td
                    class="tabular-nums text-slate-400"
                    v-text="row.assignmentCount"
                  />
                  <td
                    class="tabular-nums text-slate-400"
                    v-text="row.submissionCount"
                  />
                  <td class="tabular-nums text-emerald-400">
                    {{ row.gradedCount }}
                  </td>
                  <td
                    class="tabular-nums"
                    :class="row.ungradedCount > 0 ? 'font-semibold text-amber-400' : 'text-dim'"
                  >
                    {{ row.ungradedCount }}
                  </td>
                  <td class="tabular-nums text-slate-400">
                    {{ row.lastSubmittedAt === null ? '—' : formatDateTimeNumeric(row.lastSubmittedAt) }}
                  </td>
                </tr>
              </tbody>
            </table>
          </div>
        </DataStatus>
      </BaseCard>

      <!-- Javoblar — yassilangan, sahifalangan ro'yxat -->
      <BaseCard
        title="Javoblar"
        :subtitle="`Jami: ${submissionsTotal} ta javob`"
        flush
      >
        <DataStatus
          :pending="submissionsOverviewQuery.isPending.value"
          :error="submissionsOverviewError"
          :empty="submissionRows.length === 0"
          :retrying="submissionsOverviewQuery.isFetching.value"
          :skeleton-rows="4"
          empty-icon="clipboard"
          empty-title="Javob topilmadi"
          empty-text="Tanlangan filtrlarga mos topshirilgan ish yo‘q."
          @retry="submissionsOverviewQuery.refetch()"
        >
          <!-- Telefon/planshet: kartochka -->
          <ul
            v-if="!isDesktop"
            class="divide-y divide-line"
          >
            <li
              v-for="row in submissionRows"
              :key="row.submissionId"
              class="p-3.5"
            >
              <div class="flex items-start justify-between gap-2">
                <p
                  class="min-w-0 flex-1 truncate text-sm font-medium text-slate-100"
                  v-text="row.studentName ?? '—'"
                />
                <BaseBadge :tone="submissionStatusTone(row.status)">
                  {{ submissionStatusLabel(row.status) }}
                </BaseBadge>
              </div>
              <p
                class="mt-1 truncate text-xs text-slate-400"
                v-text="`${row.assignmentTitle ?? '—'} · ${row.groupName ?? 'Kurs vazifasi'}`"
              />
              <p class="text-xs tabular-nums text-dim">
                Yuborilgan: {{ formatDateTimeNumeric(row.submittedAt) }}
                <span
                  v-if="row.isLate"
                  class="text-amber-400"
                > · kechikkan</span>
              </p>
              <p
                v-if="row.gradedAt !== null"
                class="text-xs tabular-nums text-dim"
              >
                Tekshirilgan: {{ formatDateTimeNumeric(row.gradedAt) }}
                <span v-if="row.gradedByName !== null"> · {{ row.gradedByName }}</span>
              </p>
              <p
                v-else
                class="text-xs text-dim"
              >
                Tekshiruvchi: {{ row.graderLabel ?? '—' }}
              </p>
              <div class="mt-2 flex items-center justify-between gap-2">
                <span class="text-xs tabular-nums text-slate-300">
                  Ball: {{ row.score ?? '—' }} / {{ row.maxScore }}
                </span>
                <BaseButton
                  size="sm"
                  :loading="gradingLoading && gradingRow?.submissionId === row.submissionId"
                  @click="openGrade(row)"
                >
                  Baholash
                </BaseButton>
              </div>
            </li>
          </ul>

          <!-- Desktop (≥1024px): jadval -->
          <div
            v-else
            class="scroll-x-safe scrollbar-slim"
          >
            <table class="zn-table">
              <thead>
                <tr>
                  <th>O‘quvchi</th>
                  <th>Vazifa</th>
                  <th>Guruh</th>
                  <th>Yuborilgan</th>
                  <th>Holat</th>
                  <th>Ball</th>
                  <th>Tekshirilgan</th>
                  <th>Kim tekshiradi</th>
                  <th />
                </tr>
              </thead>
              <tbody>
                <tr
                  v-for="row in submissionRows"
                  :key="row.submissionId"
                >
                  <td
                    class="font-medium text-slate-100"
                    v-text="row.studentName ?? '—'"
                  />
                  <td>
                    <p
                      class="max-w-56 truncate text-slate-200"
                      v-text="row.assignmentTitle ?? '—'"
                    />
                  </td>
                  <td
                    class="text-slate-400"
                    v-text="row.groupName ?? 'Kurs vazifasi'"
                  />
                  <td class="tabular-nums text-slate-400">
                    {{ formatDateTimeNumeric(row.submittedAt) }}
                    <span
                      v-if="row.isLate"
                      class="text-amber-400"
                    >(kech)</span>
                  </td>
                  <td>
                    <BaseBadge :tone="submissionStatusTone(row.status)">
                      {{ submissionStatusLabel(row.status) }}
                    </BaseBadge>
                  </td>
                  <td class="tabular-nums text-slate-200">
                    {{ row.score ?? '—' }} / {{ row.maxScore }}
                  </td>
                  <td class="tabular-nums text-slate-400">
                    <template v-if="row.gradedAt !== null">
                      {{ formatDateTimeNumeric(row.gradedAt) }}
                      <p
                        v-if="row.gradedByName !== null"
                        class="text-xs text-dim"
                        v-text="row.gradedByName"
                      />
                    </template>
                    <span
                      v-else
                      class="text-dim"
                    >—</span>
                  </td>
                  <td
                    class="text-slate-400"
                    v-text="row.graderLabel ?? '—'"
                  />
                  <td class="text-right">
                    <BaseButton
                      size="sm"
                      variant="secondary"
                      :loading="gradingLoading && gradingRow?.submissionId === row.submissionId"
                      @click="openGrade(row)"
                    >
                      Baholash
                    </BaseButton>
                  </td>
                </tr>
              </tbody>
            </table>
          </div>

          <PaginationBar
            :page="overviewPage"
            :total-pages="submissionsTotalPages"
            :total="submissionsTotal"
            @update:page="overviewPage = $event"
          />
        </DataStatus>
      </BaseCard>

      <p
        v-if="gradingLoadError !== null"
        class="mt-3 rounded-xl border border-rose-500/25 bg-rose-500/10 px-4 py-3 text-xs text-rose-200"
        role="alert"
        v-text="gradingLoadError"
      />

      <GradeDialog
        :submission="gradingSubmission"
        :max-score="gradingRow?.maxScore ?? 0"
        @close="closeGrade"
        @graded="handleGraded"
      />
    </template>
  </div>
</template>
