<script setup lang="ts">
import { useQuery, useQueryClient } from '@tanstack/vue-query'
import { computed, ref } from 'vue'

import {
  answerFormatsLabel,
  assignmentTitle,
  fetchAssignments,
} from '@/entities/assignment'
import AssignmentFormDialog from '@/features/assignment-form/ui/AssignmentFormDialog.vue'
import { toUserMessage } from '@/shared/api'
import { formatDateTime } from '@/shared/lib/datetime'
import { useBreakpoint } from '@/shared/lib/useBreakpoint'
import type { AssignmentDto } from '@/shared/types'
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
 * Uy vazifalari (o'quv bo'limi/admin).
 *
 * NEGA ALOHIDA SAHIFA, ustozning "Baholash" sahifasi yetmaydimi: KURS
 * vazifasini (dars nishoni) faqat o'quv bo'limi biriktiradi
 * (`AssignmentService.EnsureCanCreateAsync`), ustoz sahifasi esa `roles: STAFF`
 * bilan yopiq. Ya'ni bu sahifa bo'lmasa, endpoint'ning yarmi umuman
 * chaqirilmasdi.
 *
 * Filtrlar ATAYLAB yo'q: server ro'yxatni `groupId`/`moduleLessonId` bo'yicha
 * filtrlashni qo'llaydi, lekin ikkalasi ham ID talab qiladi (nom bo'yicha
 * qidiruv YO'Q) — ya'ni filtr uchun yana bir tanlagich kerak bo'lardi.
 * Hozircha sahifalash yetarli.
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

const PAGE_SIZE = 20

const page = ref(1)

const assignmentsQuery = useQuery({
  queryKey: ['assignments', 'manage', page],
  queryFn: ({ signal }) => fetchAssignments({ page: page.value, pageSize: PAGE_SIZE }, { signal }),
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
  void queryClient.invalidateQueries({ queryKey: ['assignments'] })
}
</script>

<template>
  <div>
    <PageHeader
      title="Uy vazifalari"
      :subtitle="`Jami: ${total} ta vazifa`"
    >
      <template #actions>
        <BaseButton @click="openCreate">
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
              Muddat: {{ formatDateTime(assignment.dueAt) }}
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
                  {{ assignment.dueAt === null ? 'Muddatsiz' : formatDateTime(assignment.dueAt) }}
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
  </div>
</template>
