<script setup lang="ts">
import { computed, ref, watch } from 'vue'

import { useStudentCourse } from '@/features/student-course/model/useStudentCourse'
import CourseLessonPath from '@/features/student-course/ui/CourseLessonPath.vue'
import LessonSheet from '@/features/student-course/ui/LessonSheet.vue'
import type { CourseLessonDto } from '@/shared/types'
import { AppIcon, BaseButton } from '@/shared/ui'

/**
 * O'QUV — eski `#learn` bo'limi.
 *
 * Tuzilishi: kurs jarayoni doirasi -> modullar akkordeoni (ichida "ilon izi"
 * dars yo'lakchasi) -> vazifa/test bo'limlariga o'tish.
 *
 * ★ VAZIFALAR VA TESTLAR SHU TABDA: eski ilovada ular alohida tab EMAS,
 *   dars ichidagi "Vazifa" va "Test" segmentlari edi — ya'ni o'quvchi ularni
 *   "O'quv" ostida qidiradi. v2 da esa server ro'yxatlarni dars bilan
 *   bog'lamasdan beradi (`/assignments/mine`, `/tests/available`), shuning
 *   uchun ular shu tabning pastki sahifalari qilindi. Oltinchi tab qo'shish
 *   variantidan VOZ KECHILDI: pastki panelda tab soni va tartibi o'zgarsa
 *   o'quvchi qayta o'rganishga majbur bo'lardi.
 */
const course = useStudentCourse()

/** Ochiq modul id'si. Boshida — hozirgi dars turgan modul. */
const openModuleId = ref<number | null>(null)

const selectedLesson = ref<CourseLessonDto | null>(null)

const selectedModuleName = computed(() => {
  const lessonId = selectedLesson.value?.id
  if (lessonId === undefined) return ''
  const owner = course.modules.value.find((module) =>
    (module.lessons ?? []).some((lesson) => lesson.id === lessonId),
  )
  return owner?.name ?? ''
})

/** Doira uzunligi: 2 × π × 26 (eski `c-ring` radiusi). */
const RING_CIRCUMFERENCE = 163.4

const ringOffset = computed(
  () => RING_CIRCUMFERENCE * (1 - course.unlockedPercent.value / 100),
)

// Daraxt kelgach hozirgi dars turgan modulni ochamiz (eski `openMod` mantiqi).
watch(
  () => [course.modules.value, course.nextLessonId.value] as const,
  ([modules, currentId]) => {
    if (openModuleId.value !== null || modules.length === 0) return
    const owner = modules.find((module) =>
      (module.lessons ?? []).some((lesson) => lesson.id === currentId),
    )
    openModuleId.value = owner?.id ?? modules[0]?.id ?? null
  },
  { immediate: true },
)

function toggleModule(moduleId: number): void {
  openModuleId.value = openModuleId.value === moduleId ? null : moduleId
}

function doneCount(lessons: CourseLessonDto[]): number {
  return lessons.filter((lesson) => lesson.unlocked).length
}
</script>

<template>
  <div>
    <h2
      class="mb-3 ml-1 mt-2 flex items-center gap-[7px] text-xs font-bold uppercase tracking-[1.4px] text-slate-400"
    >
      <AppIcon
        name="book"
        :size="15"
      />
      O‘quv
    </h2>

    <div
      v-if="course.isPending.value"
      class="space-y-3"
    >
      <div
        v-for="index in 3"
        :key="index"
        class="h-24 animate-pulse rounded-xl border border-line bg-ink-900"
      />
    </div>

    <div
      v-else-if="course.error.value !== null"
      class="rounded-xl border border-rose-500/25 bg-rose-500/10 px-5 py-6 text-center"
      role="alert"
    >
      <p
        class="text-sm text-rose-200"
        v-text="course.error.value"
      />
      <BaseButton
        class="mt-4"
        size="sm"
        variant="secondary"
        :loading="course.isFetching.value"
        @click="course.refetch()"
      >
        Qayta urinish
      </BaseButton>
    </div>

    <!--
      Kurs biriktirilmagan — matn eski ilovadan AYNAN
      (`renderCourseUI()` dagi `COURSE.no_course` shoxi).
    -->
    <div
      v-else-if="course.hasNoCourse.value || course.lessonCount.value === 0"
      class="rounded-xl border border-line bg-ink-900 px-[18px] py-[26px] text-center"
    >
      <p class="text-[15px] font-bold">
        Kurs hali biriktirilmagan
      </p>
      <p class="mt-[7px] text-[13px] leading-relaxed text-slate-400">
        Guruhingizga video kurs ulanmagan. O‘quv bo‘limi ulagach shu yerda paydo bo‘ladi.
      </p>
    </div>

    <template v-else>
      <!-- Kurs jarayoni (eski `.c-hero`) -->
      <section class="mb-3.5 mt-3 flex items-center gap-4 rounded-[18px] border border-line bg-ink-900 p-4">
        <div class="relative size-[62px] shrink-0">
          <svg
            width="62"
            height="62"
            viewBox="0 0 62 62"
            class="-rotate-90"
            aria-hidden="true"
          >
            <circle
              cx="31"
              cy="31"
              r="26"
              fill="none"
              stroke="rgb(255 255 255 / 0.1)"
              stroke-width="5"
            />
            <circle
              cx="31"
              cy="31"
              r="26"
              fill="none"
              stroke="var(--color-brand-500)"
              stroke-width="5"
              stroke-linecap="round"
              :stroke-dasharray="RING_CIRCUMFERENCE"
              :stroke-dashoffset="ringOffset"
            />
          </svg>
          <i class="absolute inset-0 flex items-center justify-center text-sm font-extrabold not-italic">
            {{ course.unlockedPercent.value }}%
          </i>
        </div>

        <div class="min-w-0 flex-1">
          <p class="text-[17px] font-extrabold leading-tight">
            Kurs davomi
          </p>
          <!--
            Eski ilovada bu qator "N / M dars tugatilgan" edi. Server v2 da
            darsning TUGATILGANINI bermaydi (kurs daraxtida `completed` maydoni
            yo'q), shuning uchun bizda bor yagona halol o'lchov — OCHILGAN
            darslar soni. Matn shunga qarab o'zgartirildi.
          -->
          <p class="mt-1 text-[12.5px] text-slate-400">
            {{ course.unlockedCount.value }} / {{ course.lessonCount.value }} dars ochilgan
          </p>
          <div class="mt-2 h-[5px] overflow-hidden rounded-sm bg-white/10">
            <span
              class="block h-full rounded-sm bg-brand-500 transition-[width] duration-500"
              :style="{ width: `${course.unlockedPercent.value}%` }"
            />
          </div>
        </div>
      </section>

      <!-- Modullar (eski `.c-mod`) -->
      <section
        v-for="(module, moduleIndex) in course.modules.value"
        :key="module.id"
        class="mb-2.5 overflow-hidden rounded-xl border border-line bg-ink-900"
      >
        <button
          type="button"
          class="flex min-h-11 w-full select-none items-center gap-3 px-[15px] py-3.5 text-left"
          :aria-expanded="openModuleId === module.id"
          @click="toggleModule(module.id)"
        >
          <span
            class="flex size-[30px] shrink-0 items-center justify-center rounded-[9px] bg-brand-500/15 text-[12.5px] font-extrabold text-brand-500"
            aria-hidden="true"
          >
            {{ moduleIndex + 1 }}
          </span>
          <span class="min-w-0 flex-1">
            <span
              class="block text-[14.5px] font-bold leading-snug"
              v-text="module.name"
            />
            <span class="mt-0.5 block text-[11.5px] font-medium text-slate-400">
              {{ doneCount(module.lessons ?? []) }}/{{ (module.lessons ?? []).length }} dars ochilgan
            </span>
          </span>
          <AppIcon
            name="chevron-right"
            :size="18"
            class="shrink-0 text-slate-400 transition-transform duration-200"
            :class="openModuleId === module.id ? 'rotate-90' : ''"
          />
        </button>

        <div
          v-if="openModuleId === module.id"
          class="border-t border-line"
        >
          <CourseLessonPath
            :lessons="module.lessons ?? []"
            :current-lesson-id="course.nextLessonId.value"
            @open="selectedLesson = $event"
          />
        </div>
      </section>
    </template>

    <!-- ==================== Vazifalar va testlar ==================== -->
    <h2
      class="mb-3 ml-1 mt-6 flex items-center gap-[7px] text-xs font-bold uppercase tracking-[1.4px] text-slate-400"
    >
      <AppIcon
        name="clipboard"
        :size="15"
      />
      Vazifa va testlar
    </h2>

    <div class="flex flex-col gap-2.5">
      <RouterLink
        :to="{ name: 'student-assignments' }"
        class="flex min-h-11 items-center gap-3 rounded-[15px] border border-line bg-ink-900 p-[15px]"
      >
        <span
          class="flex size-[42px] shrink-0 items-center justify-center rounded-xl bg-brand-500/15 text-brand-400"
          aria-hidden="true"
        >
          <AppIcon
            name="clipboard"
            :size="20"
          />
        </span>
        <span class="min-w-0 flex-1">
          <b class="block text-base">Vazifalarim</b>
          <span class="block text-xs text-dim">Topshirish va baholar</span>
        </span>
        <AppIcon
          name="chevron-right"
          :size="20"
          class="shrink-0 text-dim"
        />
      </RouterLink>

      <RouterLink
        :to="{ name: 'student-tests' }"
        class="flex min-h-11 items-center gap-3 rounded-[15px] border border-line bg-ink-900 p-[15px]"
      >
        <span
          class="flex size-[42px] shrink-0 items-center justify-center rounded-xl"
          style="background: rgb(34 211 238 / 0.17); color: #67e8f9"
          aria-hidden="true"
        >
          <AppIcon
            name="award"
            :size="20"
          />
        </span>
        <span class="min-w-0 flex-1">
          <b class="block text-base">Testlarim</b>
          <span class="block text-xs text-dim">Ochiq testlar va natijalar</span>
        </span>
        <AppIcon
          name="chevron-right"
          :size="20"
          class="shrink-0 text-dim"
        />
      </RouterLink>
    </div>

    <LessonSheet
      :lesson="selectedLesson"
      :module-name="selectedModuleName"
      @close="selectedLesson = null"
    />
  </div>
</template>
