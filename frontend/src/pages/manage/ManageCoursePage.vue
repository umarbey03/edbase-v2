<script setup lang="ts">
import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { computed, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'

import { deleteCourse, fetchCourseTree } from '@/entities/course'
import CourseFormDialog from '@/features/course-form/ui/CourseFormDialog.vue'
import CourseTreeEditor from '@/features/course-tree/ui/CourseTreeEditor.vue'
import { toUserMessage } from '@/shared/api'
import { formatDateTime } from '@/shared/lib/datetime'
import type { CourseDto } from '@/shared/types'
import {
  AppIcon,
  BaseBadge,
  BaseButton,
  BaseCard,
  ConfirmDeleteDialog,
  DataStatus,
  PageHeader,
} from '@/shared/ui'

/** Bitta kursning kontenti: modullar va darslar (o'quv bo'limi/admin). */
const route = useRoute()
const router = useRouter()
const queryClient = useQueryClient()

const rawId = route.params['courseId']
const courseId = Number(Array.isArray(rawId) ? rawId[0] : rawId)
const isValidId = Number.isInteger(courseId) && courseId > 0

const courseQuery = useQuery({
  queryKey: ['course', courseId],
  queryFn: ({ signal }) => fetchCourseTree(courseId, { signal }),
  enabled: isValidId,
})

const course = computed(() => courseQuery.data.value ?? null)

const errorMessage = computed(() => {
  if (!isValidId) return 'Kurs manzili noto‘g‘ri.'
  return courseQuery.error.value !== null ? toUserMessage(courseQuery.error.value) : null
})

const moduleCount = computed(() => course.value?.modules?.length ?? 0)
const lessonCount = computed(() =>
  (course.value?.modules ?? []).reduce((sum, module) => sum + (module.lessons?.length ?? 0), 0),
)

function refreshTree(): void {
  void queryClient.invalidateQueries({ queryKey: ['course', courseId] })
  // Ro'yxatdagi modul/dars sanog'i ham eskiradi.
  void queryClient.invalidateQueries({ queryKey: ['courses'] })
}

/* --------------------------------------------------------------- tahrirlash */

const editOpen = ref(false)

/**
 * `CourseFormDialog` ro'yxat qatorini (`CourseDto`) kutadi, bu yerda esa daraxt
 * (`CourseTreeDto`) bor. Formaga faqat nom/tavsif/holat kerak — sanoqlar
 * daraxtdan hisoblanadi, shuning uchun qator shu joyda yig'iladi.
 */
const editableCourse = computed<CourseDto | null>(() => {
  const tree = course.value
  if (tree === null) return null
  return {
    id: tree.id,
    name: tree.name,
    description: tree.description,
    isActive: tree.isActive,
    position: tree.position,
    moduleCount: moduleCount.value,
    lessonCount: lessonCount.value,
    // Daraxt javobida guruh sanog'i yo'q — o'chirish tugmasi baribir serverga
    // tayanadi (409), shuning uchun 0 xavfsiz standart.
    groupCount: 0,
    createdAt: tree.createdAt,
    updatedAt: tree.updatedAt,
  }
})

/* ----------------------------------------------------------------- o'chirish */

const deleteOpen = ref(false)
const deleteError = ref<string | null>(null)

const deleteMutation = useMutation({
  mutationFn: () => deleteCourse(courseId),
  onSuccess: () => {
    deleteOpen.value = false
    void queryClient.invalidateQueries({ queryKey: ['courses'] })
    void router.push({ name: 'manage-courses' })
  },
  onError: (error: Error) => {
    // 409 sababi (biriktirilgan guruhlar / o'quvchi ishlari) oynada qoladi.
    deleteError.value = toUserMessage(error)
  },
})

function askDelete(): void {
  deleteError.value = null
  deleteOpen.value = true
}
</script>

<template>
  <div>
    <button
      type="button"
      class="mb-3 inline-flex min-h-11 items-center gap-1.5 rounded-lg pr-3 text-xs font-medium text-slate-400 transition-colors hover:text-slate-100"
      @click="router.push({ name: 'manage-courses' })"
    >
      <AppIcon
        name="arrow-left"
        :size="14"
      />
      Kurslar
    </button>

    <DataStatus
      :pending="courseQuery.isPending.value && isValidId"
      :error="errorMessage"
      :empty="false"
      :retrying="courseQuery.isFetching.value"
      :skeleton-rows="3"
      @retry="courseQuery.refetch()"
    >
      <template v-if="course !== null">
        <PageHeader
          :title="course.name ?? 'Kurs'"
          :subtitle="`${moduleCount} modul · ${lessonCount} dars`"
        >
          <template #actions>
            <BaseBadge :tone="course.isActive ? 'success' : 'neutral'">
              {{ course.isActive ? 'Faol' : 'Arxiv' }}
            </BaseBadge>
            <BaseButton
              size="sm"
              variant="secondary"
              @click="editOpen = true"
            >
              <template #icon>
                <AppIcon
                  name="edit"
                  :size="13"
                />
              </template>
              Tahrirlash
            </BaseButton>
            <BaseButton
              size="sm"
              variant="danger"
              @click="askDelete"
            >
              <template #icon>
                <AppIcon
                  name="trash"
                  :size="13"
                />
              </template>
              O‘chirish
            </BaseButton>
          </template>
        </PageHeader>

        <BaseCard
          v-if="(course.description ?? '').length > 0"
          class="mb-4"
        >
          <p
            class="whitespace-pre-line text-sm text-slate-300"
            v-text="course.description"
          />
          <p
            v-if="course.updatedAt !== null"
            class="mt-2 text-[11px] text-dim"
          >
            Oxirgi o‘zgarish: {{ formatDateTime(course.updatedAt) }}
          </p>
        </BaseCard>

        <CourseTreeEditor
          :course="course"
          @changed="refreshTree"
        />
      </template>
    </DataStatus>

    <CourseFormDialog
      :open="editOpen"
      :course="editableCourse"
      @close="editOpen = false"
      @saved="refreshTree"
    />

    <ConfirmDeleteDialog
      :open="deleteOpen"
      title="Kursni o‘chirish"
      :message="`“${course?.name ?? 'Kurs'}” kursi butun kontenti bilan o‘chiriladi. Guruh biriktirilgan yoki o‘quvchi ishi bo‘lsa server ruxsat bermaydi.`"
      :pending="deleteMutation.isPending.value"
      :error="deleteError"
      @close="deleteOpen = false"
      @confirm="deleteMutation.mutate()"
    />
  </div>
</template>
