<script setup lang="ts">
import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { computed, ref, watch } from 'vue'
import { useRouter } from 'vue-router'

import {
  COURSE_SEARCH_MIN,
  courseContentSummary,
  courseLooksDeletable,
  fetchCourses,
  reorderCourses,
} from '@/entities/course'
import CourseFormDialog from '@/features/course-form/ui/CourseFormDialog.vue'
import { toUserMessage } from '@/shared/api'
import { useDebounced } from '@/shared/lib/debounce'
import type { CourseDto } from '@/shared/types'
import {
  AppIcon,
  BaseBadge,
  BaseButton,
  BaseCard,
  DataStatus,
  PageHeader,
  PaginationBar,
} from '@/shared/ui'

/** Kurslar boshqaruvi (Academic/Admin): ro'yxat, qidiruv, yaratish/tahrirlash, tartib. */
const router = useRouter()
const queryClient = useQueryClient()

const search = ref('')
const debouncedSearch = useDebounced(search)
const activeFilter = ref<'' | 'true' | 'false'>('')
const page = ref(1)

const PAGE_SIZE = 20

/*
  ★ Server qidiruvda minimal uzunlik talab qiladi
  (`CourseService.MinSearchLength = 2`), qisqa satrda 400 qaytadi va jadval
  o'rniga xato ekrani chiqadi. Shuning uchun qisqa satr yuborilmaydi.
*/
const searchTerm = computed(() => debouncedSearch.value.trim())
const searchTooShort = computed(
  () => searchTerm.value.length > 0 && searchTerm.value.length < COURSE_SEARCH_MIN,
)
const effectiveSearch = computed(() =>
  searchTerm.value.length >= COURSE_SEARCH_MIN ? searchTerm.value : undefined,
)

watch([effectiveSearch, activeFilter], () => {
  page.value = 1
})

const coursesQuery = useQuery({
  queryKey: ['courses', 'manage', effectiveSearch, activeFilter, page],
  queryFn: ({ signal }) =>
    fetchCourses(
      {
        search: effectiveSearch.value,
        isActive: activeFilter.value === '' ? undefined : activeFilter.value === 'true',
        page: page.value,
        pageSize: PAGE_SIZE,
      },
      { signal },
    ),
})

const courses = computed(() => coursesQuery.data.value?.items ?? [])
const total = computed(() => coursesQuery.data.value?.total ?? 0)
const totalPages = computed(() => coursesQuery.data.value?.totalPages ?? 1)

const errorMessage = computed(() =>
  coursesQuery.error.value !== null ? toUserMessage(coursesQuery.error.value) : null,
)

const actionError = ref<string | null>(null)

function refresh(): void {
  void queryClient.invalidateQueries({ queryKey: ['courses'] })
}

/* -------------------------------------------------------------------- tartib */

/*
  ★ NIMA UCHUN TARTIB HAR DOIM MUMKIN EMAS: `POST /courses/reorder` BARCHA
  kurslarning Id'sini kutadi va yetishmasa 400 qaytaradi. Qidiruv/filtr yoqilgan
  yoki ro'yxat bir necha sahifaga bo'lingan bo'lsa, ekranda kurslarning FAQAT
  bir qismi turadi — o'sha qismni yuborish serverda yarim tartibga olib kelardi.
  Shu holatda tugmalar ko'rsatilmaydi va sabab yozib qo'yiladi.
*/
const canReorder = computed(
  () =>
    // AMALDA qo'llangan filtr muhim: hali qisqa (yuborilmagan) qidiruv satri
    // ro'yxatni kesmaydi, demak tartiblashga to'sqinlik ham qilmaydi.
    effectiveSearch.value === undefined && activeFilter.value === '' && totalPages.value <= 1,
)

const orderMutation = useMutation({
  mutationFn: (orderedIds: number[]) => reorderCourses(orderedIds),
  onSuccess: refresh,
  onError: (error: Error) => {
    actionError.value = toUserMessage(error)
  },
})

function move(index: number, delta: number): void {
  const target = index + delta
  const ids = courses.value.map((item) => item.id)
  if (target < 0 || target >= ids.length) return

  const next = [...ids]
  const moved = next[index]
  const replaced = next[target]
  if (moved === undefined || replaced === undefined) return
  next[index] = replaced
  next[target] = moved

  actionError.value = null
  orderMutation.mutate(next)
}

/* --------------------------------------------------------------------- forma */

const dialogOpen = ref(false)
const editing = ref<CourseDto | null>(null)

function openCreate(): void {
  editing.value = null
  dialogOpen.value = true
}

function openEdit(course: CourseDto): void {
  editing.value = course
  dialogOpen.value = true
}

function openContent(courseId: number): void {
  void router.push({ name: 'manage-course', params: { courseId: String(courseId) } })
}
</script>

<template>
  <div>
    <PageHeader
      title="Kurs kontenti"
      :subtitle="`Jami: ${total} ta kurs`"
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

    <div class="mb-4 grid gap-2.5 sm:grid-cols-3">
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
          placeholder="Kurs nomi bo‘yicha qidirish"
        >
        <p
          v-if="searchTooShort"
          class="mt-1 text-[11px] text-dim"
        >
          Qidirish uchun kamida {{ COURSE_SEARCH_MIN }} belgi kiriting.
        </p>
      </div>
      <select
        v-model="activeFilter"
        class="zn-input"
        aria-label="Holat bo‘yicha filtr"
      >
        <option value="">
          Barcha holatlar
        </option>
        <option value="true">
          Faol
        </option>
        <option value="false">
          Arxiv
        </option>
      </select>
    </div>

    <div
      v-if="actionError !== null"
      class="mb-3 rounded-lg border border-rose-500/25 bg-rose-500/10 p-3 text-xs text-rose-200"
      role="alert"
      v-text="actionError"
    />

    <DataStatus
      :pending="coursesQuery.isPending.value"
      :error="errorMessage"
      :empty="courses.length === 0"
      :retrying="coursesQuery.isFetching.value"
      :skeleton-rows="4"
      empty-icon="file-text"
      empty-title="Kurs topilmadi"
      empty-text="Qidiruv shartlarini o‘zgartiring yoki yangi kurs yarating."
      @retry="coursesQuery.refetch()"
    >
      <BaseCard flush>
        <!-- Telefon: kartochka -->
        <ul class="divide-y divide-line md:hidden">
          <li
            v-for="(course, index) in courses"
            :key="course.id"
            class="p-3.5"
          >
            <div class="flex items-start justify-between gap-2">
              <p
                class="min-w-0 flex-1 truncate text-sm font-medium text-slate-100"
                v-text="course.name"
              />
              <BaseBadge :tone="course.isActive ? 'success' : 'neutral'">
                {{ course.isActive ? 'Faol' : 'Arxiv' }}
              </BaseBadge>
            </div>
            <p
              class="mt-1 text-xs text-slate-400"
              v-text="courseContentSummary(course)"
            />
            <p class="text-xs text-dim">
              {{ course.groupCount }} guruh biriktirilgan
            </p>
            <div class="mt-2.5 flex flex-wrap items-center gap-2">
              <template v-if="canReorder">
                <button
                  type="button"
                  class="tap-target flex items-center justify-center rounded-lg border border-line text-slate-300 disabled:opacity-30"
                  title="Yuqoriga"
                  :disabled="index === 0 || orderMutation.isPending.value"
                  @click="move(index, -1)"
                >
                  <AppIcon
                    name="arrow-up"
                    :size="15"
                  />
                </button>
                <button
                  type="button"
                  class="tap-target flex items-center justify-center rounded-lg border border-line text-slate-300 disabled:opacity-30"
                  title="Pastga"
                  :disabled="index === courses.length - 1 || orderMutation.isPending.value"
                  @click="move(index, 1)"
                >
                  <AppIcon
                    name="arrow-down"
                    :size="15"
                  />
                </button>
              </template>
              <span class="flex-1" />
              <BaseButton
                size="sm"
                variant="secondary"
                @click="openContent(course.id)"
              >
                Kontent
              </BaseButton>
              <BaseButton
                size="sm"
                @click="openEdit(course)"
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

        <!-- Desktop: jadval -->
        <div class="scroll-x-safe scrollbar-slim hidden md:block">
          <table class="zn-table">
            <thead>
              <tr>
                <th class="w-16">
                  №
                </th>
                <th>Nomi</th>
                <th>Kontent</th>
                <th>Guruh</th>
                <th>Holat</th>
                <th />
              </tr>
            </thead>
            <tbody>
              <tr
                v-for="(course, index) in courses"
                :key="course.id"
              >
                <td class="tabular-nums text-dim">
                  {{ course.position + 1 }}
                </td>
                <td class="font-medium text-slate-100">
                  <p
                    class="truncate"
                    v-text="course.name"
                  />
                  <p
                    v-if="(course.description ?? '').length > 0"
                    class="mt-0.5 max-w-80 truncate text-xs font-normal text-dim"
                    v-text="course.description"
                  />
                </td>
                <td
                  class="text-slate-400"
                  v-text="courseContentSummary(course)"
                />
                <td class="tabular-nums text-slate-400">
                  {{ course.groupCount }}
                  <span
                    v-if="!courseLooksDeletable(course)"
                    class="text-dim"
                  >· bog‘langan</span>
                </td>
                <td>
                  <BaseBadge :tone="course.isActive ? 'success' : 'neutral'">
                    {{ course.isActive ? 'Faol' : 'Arxiv' }}
                  </BaseBadge>
                </td>
                <td>
                  <div class="flex items-center justify-end gap-1.5">
                    <template v-if="canReorder">
                      <button
                        type="button"
                        class="tap-target flex items-center justify-center rounded-lg text-slate-400 transition-colors hover:bg-ink-800 hover:text-slate-100 disabled:opacity-30"
                        title="Yuqoriga"
                        :disabled="index === 0 || orderMutation.isPending.value"
                        @click="move(index, -1)"
                      >
                        <AppIcon
                          name="arrow-up"
                          :size="15"
                        />
                      </button>
                      <button
                        type="button"
                        class="tap-target flex items-center justify-center rounded-lg text-slate-400 transition-colors hover:bg-ink-800 hover:text-slate-100 disabled:opacity-30"
                        title="Pastga"
                        :disabled="index === courses.length - 1 || orderMutation.isPending.value"
                        @click="move(index, 1)"
                      >
                        <AppIcon
                          name="arrow-down"
                          :size="15"
                        />
                      </button>
                    </template>
                    <BaseButton
                      size="sm"
                      variant="secondary"
                      @click="openContent(course.id)"
                    >
                      Kontent
                    </BaseButton>
                    <BaseButton
                      size="sm"
                      @click="openEdit(course)"
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

      <p
        v-if="!canReorder"
        class="mt-2 text-[11px] text-dim"
      >
        Tartibni o‘zgartirish uchun qidiruv va filtrlarni tozalang — server tartiblashda
        barcha kurslar ro‘yxatini talab qiladi.
      </p>
    </DataStatus>

    <CourseFormDialog
      :open="dialogOpen"
      :course="editing"
      @close="dialogOpen = false"
      @saved="refresh"
    />
  </div>
</template>
