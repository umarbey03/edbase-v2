<script setup lang="ts">
import { useQuery } from '@tanstack/vue-query'
import { computed, ref, watch } from 'vue'

import { COURSE_SEARCH_MIN, fetchCourses, fetchCourseTree } from '@/entities/course'
import { toUserMessage } from '@/shared/api'
import { useDebounced } from '@/shared/lib/debounce'
import { BaseField } from '@/shared/ui'

/**
 * Dars testining NISHONI — kurs darsi.
 *
 * Ro'yxatlar SERVER QIDIRUVI orqali keladi, "hammasini yuklash" YO'Q: bazada
 * o'nlab kurs va yuzlab dars bor. Qidiruvning MINIMAL uzunligi server
 * shartnomasidan olinadi (`COURSE_SEARCH_MIN` = `CourseService.MinSearchLength`)
 * — qisqa satr yuborilsa server 400 qaytaradi va jadval o'rniga xato ekrani
 * chiqardi. Naqsh `AssignmentTargetPicker` bilan bir xil.
 *
 * DARSLAR faqat kurs tanlangach so'raladi: kurs daraxti og'ir javob.
 */
const props = defineProps<{
  /** Tanlangan dars (`null` — tanlanmagan). */
  modelValue: number | null
  /** Forma ochiqmi — yopiq oynada so'rov yuborilmasin. */
  enabled: boolean
}>()

const emit = defineEmits<{ 'update:modelValue': [value: number | null] }>()

const search = ref('')
const debouncedSearch = useDebounced(search)

const term = computed(() => debouncedSearch.value.trim())
const searchTooShort = computed(() => term.value.length > 0 && term.value.length < COURSE_SEARCH_MIN)
const effectiveSearch = computed(() =>
  term.value.length >= COURSE_SEARCH_MIN ? term.value : undefined,
)

const coursesQuery = useQuery({
  queryKey: ['courses', 'test-target', effectiveSearch],
  queryFn: ({ signal }) =>
    fetchCourses({ search: effectiveSearch.value, isActive: true, pageSize: 25 }, { signal }),
  enabled: computed(() => props.enabled),
})

const courses = computed(() => coursesQuery.data.value?.items ?? [])

const courseId = ref<number | null>(null)

watch(courses, (list) => {
  // Qidiruv natijasi o'zgarib, tanlangan kurs ro'yxatdan chiqib ketsa —
  // dars ro'yxati eski kursnikida qolib ketmasin.
  if (courseId.value === null) return
  if (!list.some((course) => course.id === courseId.value)) {
    courseId.value = null
    emit('update:modelValue', null)
  }
})

const treeQuery = useQuery({
  queryKey: ['course', 'test-target', courseId],
  queryFn: ({ signal }) => fetchCourseTree(courseId.value ?? 0, { signal }),
  enabled: computed(() => props.enabled && courseId.value !== null),
})

const modules = computed(() => treeQuery.data.value?.modules ?? [])

const hasLessons = computed(() => modules.value.some((module) => (module.lessons ?? []).length > 0))

const listError = computed(() => {
  const error = coursesQuery.error.value ?? treeQuery.error.value
  return error !== null && error !== undefined ? toUserMessage(error) : null
})

function onCourseChange(event: Event): void {
  const value = (event.target as HTMLSelectElement).value
  courseId.value = value.length > 0 ? Number(value) : null
  emit('update:modelValue', null)
}

function onLessonChange(event: Event): void {
  const value = (event.target as HTMLSelectElement).value
  emit('update:modelValue', value.length > 0 ? Number(value) : null)
}
</script>

<template>
  <div>
    <p class="mb-3 rounded-lg border border-line bg-ink-950 px-3 py-2 text-[11px] leading-relaxed text-slate-400">
      Dars testi shu kursdagi BARCHA guruhlarga tegishli bo‘ladi va o‘quvchiga
      faqat darsi ochilgach ko‘rinadi (sur‘at nazorati).
    </p>

    <BaseField
      label="Kursni qidirish"
      :hint="searchTooShort ? `Kamida ${COURSE_SEARCH_MIN} belgi kiriting.` : 'Kurs nomi'"
    >
      <input
        v-model="search"
        class="zn-input"
        placeholder="Masalan: ATF"
      >
    </BaseField>

    <div class="mt-3">
      <BaseField
        label="Kurs"
        :hint="courses.length === 0 && !coursesQuery.isFetching.value ? 'Kurs topilmadi.' : ''"
      >
        <select
          class="zn-input"
          :value="courseId ?? ''"
          @change="onCourseChange"
        >
          <option value="">
            Tanlanmagan
          </option>
          <option
            v-for="course in courses"
            :key="course.id"
            :value="course.id"
          >
            {{ course.name ?? `Kurs #${course.id}` }}
          </option>
        </select>
      </BaseField>
    </div>

    <div
      v-if="courseId !== null"
      class="mt-3"
    >
      <BaseField
        label="Dars"
        :hint="
          !hasLessons && !treeQuery.isFetching.value
            ? 'Bu kursda hali dars yo‘q — avval kurs kontentini to‘ldiring.'
            : 'Modul bo‘yicha guruhlangan'
        "
      >
        <select
          class="zn-input"
          :value="props.modelValue ?? ''"
          @change="onLessonChange"
        >
          <option value="">
            Tanlanmagan
          </option>
          <optgroup
            v-for="module in modules"
            :key="module.id"
            :label="module.name ?? `Modul #${module.id}`"
          >
            <option
              v-for="lesson in module.lessons ?? []"
              :key="lesson.id"
              :value="lesson.id"
            >
              {{ lesson.name ?? `Dars #${lesson.id}` }}
            </option>
          </optgroup>
        </select>
      </BaseField>
    </div>

    <p
      v-if="listError !== null"
      class="mt-2 text-[11px] text-rose-400"
      role="alert"
      v-text="listError"
    />
  </div>
</template>
