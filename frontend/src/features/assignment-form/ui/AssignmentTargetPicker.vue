<script setup lang="ts">
import { useQuery } from '@tanstack/vue-query'
import { computed, ref, watch } from 'vue'

import { COURSE_SEARCH_MIN, fetchCourses, fetchCourseTree } from '@/entities/course'
import { fetchGroups, GROUP_SEARCH_MIN } from '@/entities/group'
import { toUserMessage } from '@/shared/api'
import { useDebounced } from '@/shared/lib/debounce'
import { BaseField } from '@/shared/ui'

import type { AssignmentTarget, AssignmentTargetKind } from '../model/target'

/**
 * Vazifa NISHONINI tanlash — FAQAT yaratishda.
 *
 * Ikki rejim serverdagi ikki xil vazifaga mos keladi:
 *  • GURUH vazifasi — ustoz/kurator o'z guruhiga beradi;
 *  • KURS darsi vazifasi — o'quv bo'limi butun kursga biriktiradi.
 *
 * Ro'yxatlar SERVER QIDIRUVI orqali keladi, "hammasini yuklash" YO'Q:
 * bazada yuzlab guruh bor va ularni bitta `select` ga solish telefonda
 * ochilmaydigan ro'yxat berardi. Har qidiruvning MINIMAL uzunligi server
 * shartnomasidan olinadi (`GROUP_SEARCH_MIN` / `COURSE_SEARCH_MIN`) — qisqa
 * satr yuborilsa server 400 qaytaradi.
 */
const props = defineProps<{
  modelValue: AssignmentTarget
  /**
   * Kurs darsi varianti ko'rsatiladimi. Faqat o'quv bo'limi/admin uchun:
   * ustoz kurs vazifasini yarata olmaydi va serverdan 403 olardi
   * (`AssignmentService.EnsureCanCreateAsync`).
   */
  allowCourseTarget: boolean
  /** Forma ochiqmi — yopiq oynada so'rov yuborilmasin. */
  enabled: boolean
}>()

const emit = defineEmits<{ 'update:modelValue': [value: AssignmentTarget] }>()

function patch(changes: Partial<AssignmentTarget>): void {
  emit('update:modelValue', { ...props.modelValue, ...changes })
}

function selectKind(kind: AssignmentTargetKind): void {
  // Tur almashganda ikkinchi tomonning tanlovi TOZALANADI: aks holda
  // "guruh + dars" birgalikda yuborilib, server 409 berardi.
  emit('update:modelValue', { kind, groupId: null, lessonId: null })
}

/* --------------------------------------------------------------- guruh */

const groupSearch = ref('')
const debouncedGroupSearch = useDebounced(groupSearch)

const groupTerm = computed(() => debouncedGroupSearch.value.trim())
const groupSearchTooShort = computed(
  () => groupTerm.value.length > 0 && groupTerm.value.length < GROUP_SEARCH_MIN,
)
const effectiveGroupSearch = computed(() =>
  groupTerm.value.length >= GROUP_SEARCH_MIN ? groupTerm.value : undefined,
)

const groupsQuery = useQuery({
  queryKey: ['groups', 'assignment-target', effectiveGroupSearch],
  queryFn: ({ signal }) =>
    fetchGroups({ search: effectiveGroupSearch.value, isActive: true, pageSize: 25 }, { signal }),
  // Ro'yxat SERVERDA filtrlanadi: ustoz faqat o'z guruhlarini oladi.
  enabled: computed(() => props.enabled && props.modelValue.kind === 'group'),
})

const groups = computed(() => groupsQuery.data.value?.items ?? [])

/* ---------------------------------------------------------------- kurs */

const courseSearch = ref('')
const debouncedCourseSearch = useDebounced(courseSearch)

const courseTerm = computed(() => debouncedCourseSearch.value.trim())
const courseSearchTooShort = computed(
  () => courseTerm.value.length > 0 && courseTerm.value.length < COURSE_SEARCH_MIN,
)
const effectiveCourseSearch = computed(() =>
  courseTerm.value.length >= COURSE_SEARCH_MIN ? courseTerm.value : undefined,
)

const coursesQuery = useQuery({
  queryKey: ['courses', 'assignment-target', effectiveCourseSearch],
  queryFn: ({ signal }) =>
    fetchCourses({ search: effectiveCourseSearch.value, isActive: true, pageSize: 25 }, { signal }),
  enabled: computed(() => props.enabled && props.modelValue.kind === 'lesson'),
})

const courses = computed(() => coursesQuery.data.value?.items ?? [])

/** Tanlangan kurs — darslar faqat shundan keyin so'raladi (daraxt og'ir). */
const courseId = ref<number | null>(null)

watch(courses, (list) => {
  // Qidiruv natijasi o'zgarsa va tanlangan kurs ro'yxatdan chiqib ketsa,
  // dars ro'yxati eski kursnikida qolib ketmasin.
  if (courseId.value === null) return
  if (!list.some((course) => course.id === courseId.value)) {
    courseId.value = null
    patch({ lessonId: null })
  }
})

const treeQuery = useQuery({
  queryKey: ['course', 'assignment-target', courseId],
  queryFn: ({ signal }) => fetchCourseTree(courseId.value ?? 0, { signal }),
  enabled: computed(() => props.enabled && props.modelValue.kind === 'lesson' && courseId.value !== null),
})

const modules = computed(() => treeQuery.data.value?.modules ?? [])

const hasLessons = computed(() =>
  modules.value.some((module) => (module.lessons ?? []).length > 0),
)

/* ---------------------------------------------------------------- xato */

const listError = computed(() => {
  const error =
    props.modelValue.kind === 'group'
      ? groupsQuery.error.value
      : (coursesQuery.error.value ?? treeQuery.error.value)
  return error !== null && error !== undefined ? toUserMessage(error) : null
})

function onGroupChange(event: Event): void {
  const value = (event.target as HTMLSelectElement).value
  patch({ groupId: value.length > 0 ? Number(value) : null })
}

function onCourseChange(event: Event): void {
  const value = (event.target as HTMLSelectElement).value
  courseId.value = value.length > 0 ? Number(value) : null
  patch({ lessonId: null })
}

function onLessonChange(event: Event): void {
  const value = (event.target as HTMLSelectElement).value
  patch({ lessonId: value.length > 0 ? Number(value) : null })
}
</script>

<template>
  <div>
    <!-- Nishon turi. Bitta variant qolsa tanlov ko'rsatilmaydi (ustoz uchun). -->
    <div
      v-if="props.allowCourseTarget"
      class="mb-3 flex gap-2"
      role="group"
      aria-label="Vazifa nishoni"
    >
      <button
        type="button"
        class="tap-target flex-1 rounded-lg border px-3 text-xs font-medium transition-colors"
        :class="
          props.modelValue.kind === 'group'
            ? 'border-brand-500 bg-brand-500/16 text-brand-400'
            : 'border-line bg-ink-800 text-slate-300 hover:bg-ink-750'
        "
        @click="selectKind('group')"
      >
        Guruh vazifasi
      </button>
      <button
        type="button"
        class="tap-target flex-1 rounded-lg border px-3 text-xs font-medium transition-colors"
        :class="
          props.modelValue.kind === 'lesson'
            ? 'border-brand-500 bg-brand-500/16 text-brand-400'
            : 'border-line bg-ink-800 text-slate-300 hover:bg-ink-750'
        "
        @click="selectKind('lesson')"
      >
        Kurs darsi
      </button>
    </div>

    <p
      v-if="props.modelValue.kind === 'lesson'"
      class="mb-3 rounded-lg border border-line bg-ink-950 px-3 py-2 text-[11px] leading-relaxed text-slate-400"
    >
      Kurs darsiga biriktirilgan vazifa shu kursdagi BARCHA guruhlarga tegishli bo‘ladi va
      o‘quvchiga faqat darsi ochilgach ko‘rinadi.
    </p>

    <!-- GURUH -->
    <template v-if="props.modelValue.kind === 'group'">
      <BaseField
        label="Guruhni qidirish"
        :hint="groupSearchTooShort ? `Kamida ${GROUP_SEARCH_MIN} belgi kiriting.` : 'Guruh nomi'"
      >
        <input
          v-model="groupSearch"
          class="zn-input"
          placeholder="Masalan: ATF-1"
        >
      </BaseField>

      <div class="mt-3">
        <BaseField
          label="Guruh"
          :hint="groups.length === 0 && !groupsQuery.isFetching.value ? 'Guruh topilmadi.' : ''"
        >
          <select
            class="zn-input"
            :value="props.modelValue.groupId ?? ''"
            @change="onGroupChange"
          >
            <option value="">
              Tanlanmagan
            </option>
            <option
              v-for="group in groups"
              :key="group.id"
              :value="group.id"
            >
              {{ group.name ?? `Guruh #${group.id}` }}
            </option>
          </select>
        </BaseField>
      </div>
    </template>

    <!-- KURS DARSI -->
    <template v-else>
      <BaseField
        label="Kursni qidirish"
        :hint="courseSearchTooShort ? `Kamida ${COURSE_SEARCH_MIN} belgi kiriting.` : 'Kurs nomi'"
      >
        <input
          v-model="courseSearch"
          class="zn-input"
          placeholder="Masalan: ATF"
        >
      </BaseField>

      <div class="mt-3">
        <BaseField label="Kurs">
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
            :value="props.modelValue.lessonId ?? ''"
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
    </template>

    <p
      v-if="listError !== null"
      class="mt-2 text-[11px] text-rose-400"
      role="alert"
      v-text="listError"
    />
  </div>
</template>
