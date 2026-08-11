<script setup lang="ts">
import { useMutation } from '@tanstack/vue-query'
import { computed, ref, watch } from 'vue'

import {
  deleteLesson,
  deleteModule,
  lessonAssetSummary,
  lessonDurationLabel,
  lessonKindLabel,
  moduleLessonSummary,
  reorderLessons,
  reorderModules,
} from '@/entities/course'
import { toUserMessage } from '@/shared/api'
import type { CourseLessonDto, CourseModuleDto, CourseTreeDto } from '@/shared/types'
import { AppIcon, BaseBadge, BaseButton, ConfirmDeleteDialog, EmptyState } from '@/shared/ui'

import LessonEditDrawer from './LessonEditDrawer.vue'
import ModuleFormDialog from './ModuleFormDialog.vue'

/**
 * Kurs daraxti tahrirlagichi: modul -> dars CRUD va TARTIB.
 *
 * TARTIB HAQIDA: server "shu elementni yuqoriga surish" so'rovini qabul
 * qilmaydi — u TO'LIQ tartiblangan Id ro'yxatini kutadi va yetishmasa 400
 * beradi. Shuning uchun har surishda joriy ro'yxatdan yangi ketma-ketlik
 * quriladi va butunligicha yuboriladi.
 *
 * DARAXT TARTIBI serverdan gating hisoblagan ketma-ketlikda keladi — bu yerda
 * QAYTA SARALANMAYDI, aks holda ekrandagi tartib bilan gating ketma-ketligi
 * ajralib ketardi.
 */
const props = defineProps<{ course: CourseTreeDto }>()

const emit = defineEmits<{ changed: [] }>()

const modules = computed<CourseModuleDto[]>(() => props.course.modules ?? [])

/** Tartib/o'chirish amallari xatosi (validatsiya emas — umumiy banner). */
const actionError = ref<string | null>(null)

/* ------------------------------------------------------------ yig'ish holati */

const collapsed = ref(new Set<number>())

function toggleModule(moduleId: number): void {
  const next = new Set(collapsed.value)
  if (next.has(moduleId)) next.delete(moduleId)
  else next.add(moduleId)
  collapsed.value = next
}

function isOpen(moduleId: number): boolean {
  return !collapsed.value.has(moduleId)
}

/* ------------------------------------------------------------------- tartib */

function swapped(ids: number[], index: number, delta: number): number[] | null {
  const target = index + delta
  if (target < 0 || target >= ids.length) return null
  const next = [...ids]
  const moved = next[index]
  const replaced = next[target]
  if (moved === undefined || replaced === undefined) return null
  next[index] = replaced
  next[target] = moved
  return next
}

const moduleOrderMutation = useMutation({
  mutationFn: (orderedIds: number[]) => reorderModules(props.course.id, orderedIds),
  onSuccess: () => emit('changed'),
  onError: (error: Error) => {
    actionError.value = toUserMessage(error)
  },
})

const lessonOrderMutation = useMutation({
  mutationFn: (input: { moduleId: number; orderedIds: number[] }) =>
    reorderLessons(props.course.id, input.moduleId, input.orderedIds),
  onSuccess: () => emit('changed'),
  onError: (error: Error) => {
    actionError.value = toUserMessage(error)
  },
})

const reorderPending = computed(
  () => moduleOrderMutation.isPending.value || lessonOrderMutation.isPending.value,
)

function moveModule(index: number, delta: number): void {
  const next = swapped(
    modules.value.map((item) => item.id),
    index,
    delta,
  )
  if (next === null) return
  actionError.value = null
  moduleOrderMutation.mutate(next)
}

function moveLesson(module: CourseModuleDto, index: number, delta: number): void {
  const lessons = module.lessons ?? []
  const next = swapped(
    lessons.map((item) => item.id),
    index,
    delta,
  )
  if (next === null) return
  actionError.value = null
  lessonOrderMutation.mutate({ moduleId: module.id, orderedIds: next })
}

/* ------------------------------------------------------- modul/dars formalari */

const moduleDialogOpen = ref(false)
const editingModule = ref<CourseModuleDto | null>(null)

function openModuleCreate(): void {
  editingModule.value = null
  moduleDialogOpen.value = true
}

function openModuleEdit(module: CourseModuleDto): void {
  editingModule.value = module
  moduleDialogOpen.value = true
}

const lessonDrawerOpen = ref(false)
const lessonModuleId = ref<number>(0)

/**
 * Tahrirlanayotgan dars — OB'EKT emas, ID.
 *
 * 🔴 SABAB: drawer ochiq turganda daraxt QAYTA SO'RALADI (fayl yuklandi,
 * vazifa saqlandi...) va `useQuery` yangi ob'ektlar qaytaradi. Agar bu yerda
 * darsning O'ZI saqlansa, u ESKI daraxtdagi surat bo'lib qolardi va drawer
 * yangilangan fayllar ro'yxatini KO'RMASDI. ID bo'yicha izlash esa har
 * render'da joriy ma'lumotni beradi.
 */
const editingLessonId = ref<number | null>(null)

const editingLesson = computed<CourseLessonDto | null>(() => {
  const id = editingLessonId.value
  if (id === null) return null
  for (const module of modules.value) {
    const found = (module.lessons ?? []).find((lesson) => lesson.id === id)
    if (found !== undefined) return found
  }
  return null
})

/** Drawer sarlavhasi ostidagi qator — xodim qaysi modulda ishlayotganini bilsin. */
const editingModuleName = computed<string>(
  () => modules.value.find((module) => module.id === lessonModuleId.value)?.name ?? '',
)

function openLessonCreate(module: CourseModuleDto): void {
  lessonModuleId.value = module.id
  editingLessonId.value = null
  lessonDrawerOpen.value = true
}

function openLessonEdit(module: CourseModuleDto, lesson: CourseLessonDto): void {
  lessonModuleId.value = module.id
  editingLessonId.value = lesson.id
  lessonDrawerOpen.value = true
}

/* ------------------------------------------------------------------ o'chirish */

/**
 * O'chirish nishoni. Modul va dars bitta oyna bilan tasdiqlanadi — matn va
 * chaqiriladigan API farq qiladi, oynaning o'zi bir xil.
 */
interface DeleteTarget {
  kind: 'module' | 'lesson'
  title: string
  message: string
  moduleId: number
  lessonId: number | null
}

const deleteTarget = ref<DeleteTarget | null>(null)
const deleteError = ref<string | null>(null)

function askDeleteModule(module: CourseModuleDto): void {
  const count = module.lessons?.length ?? 0
  deleteError.value = null
  deleteTarget.value = {
    kind: 'module',
    title: 'Modulni o‘chirish',
    message:
      count > 0
        ? `“${module.name ?? 'Modul'}” moduli va uning ${count} ta darsi o‘chiriladi. Bu amalni qaytarib bo‘lmaydi.`
        : `“${module.name ?? 'Modul'}” moduli o‘chiriladi.`,
    moduleId: module.id,
    lessonId: null,
  }
}

function askDeleteLesson(module: CourseModuleDto, lesson: CourseLessonDto): void {
  deleteError.value = null
  deleteTarget.value = {
    kind: 'lesson',
    title: 'Darsni o‘chirish',
    message: `“${lesson.name ?? 'Dars'}” darsi o‘chiriladi. Bu amalni qaytarib bo‘lmaydi.`,
    moduleId: module.id,
    lessonId: lesson.id,
  }
}

const deleteMutation = useMutation({
  mutationFn: (target: DeleteTarget) =>
    target.lessonId !== null
      ? deleteLesson(props.course.id, target.moduleId, target.lessonId)
      : deleteModule(props.course.id, target.moduleId),
  onSuccess: () => {
    deleteTarget.value = null
    emit('changed')
  },
  onError: (error: Error) => {
    // Oyna OCHIQ qoladi: 409 sababi (o'quvchi javoblari, test urinishlari)
    // aynan shu yerda o'qilishi kerak.
    deleteError.value = toUserMessage(error)
  },
})

function confirmDelete(): void {
  const target = deleteTarget.value
  if (target === null) return
  deleteError.value = null
  deleteMutation.mutate(target)
}

/** Kurs almashsa oldingi kursning ochiq oynalari yopilib ketsin. */
watch(
  () => props.course.id,
  () => {
    moduleDialogOpen.value = false
    lessonDrawerOpen.value = false
    editingLessonId.value = null
    deleteTarget.value = null
    actionError.value = null
    collapsed.value = new Set<number>()
  },
)
</script>

<template>
  <section>
    <header class="mb-3 flex flex-wrap items-center justify-between gap-2">
      <div class="min-w-0">
        <h2 class="text-sm font-semibold text-slate-200">
          Kurs kontenti
        </h2>
        <p class="mt-0.5 text-[11px] text-dim">
          Darslar tartibi gating ketma-ketligini belgilaydi: keyingi dars oldingisi
          tugatilgandan keyin ochiladi.
        </p>
      </div>
      <BaseButton
        size="sm"
        @click="openModuleCreate"
      >
        <template #icon>
          <AppIcon
            name="plus"
            :size="14"
          />
        </template>
        Modul qo‘shish
      </BaseButton>
    </header>

    <div
      v-if="actionError !== null"
      class="mb-3 rounded-lg border border-rose-500/25 bg-rose-500/10 p-3 text-xs text-rose-200"
      role="alert"
      v-text="actionError"
    />

    <EmptyState
      v-if="modules.length === 0"
      icon="file-text"
      title="Modul yo‘q"
      text="Kurs kontentini modul qo‘shishdan boshlang."
    />

    <ul
      v-else
      class="space-y-2.5"
    >
      <li
        v-for="(module, moduleIndex) in modules"
        :key="module.id"
        class="overflow-hidden rounded-xl border border-line bg-ink-900"
      >
        <!-- Modul sarlavhasi -->
        <div class="flex items-start gap-2 p-3 sm:items-center">
          <button
            type="button"
            class="tap-target flex shrink-0 items-center justify-center rounded-lg text-slate-400 transition-colors hover:bg-ink-800 hover:text-slate-100"
            :aria-expanded="isOpen(module.id)"
            :title="isOpen(module.id) ? 'Yig‘ish' : 'Ochish'"
            @click="toggleModule(module.id)"
          >
            <AppIcon
              :name="isOpen(module.id) ? 'chevron-down' : 'chevron-right'"
              :size="16"
            />
          </button>

          <div class="min-w-0 flex-1">
            <p class="truncate text-sm font-semibold text-slate-100">
              <span class="mr-1.5 tabular-nums text-dim">{{ moduleIndex + 1 }}.</span>
              {{ module.name }}
            </p>
            <p
              class="mt-0.5 text-[11px] text-dim"
              v-text="moduleLessonSummary(module)"
            />
          </div>

          <div class="flex shrink-0 items-center gap-1">
            <button
              type="button"
              class="tap-target flex items-center justify-center rounded-lg text-slate-400 transition-colors hover:bg-ink-800 hover:text-slate-100 disabled:opacity-30"
              title="Yuqoriga"
              :disabled="moduleIndex === 0 || reorderPending"
              @click="moveModule(moduleIndex, -1)"
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
              :disabled="moduleIndex === modules.length - 1 || reorderPending"
              @click="moveModule(moduleIndex, 1)"
            >
              <AppIcon
                name="arrow-down"
                :size="15"
              />
            </button>
            <button
              type="button"
              class="tap-target flex items-center justify-center rounded-lg text-slate-400 transition-colors hover:bg-ink-800 hover:text-slate-100"
              title="Modulni tahrirlash"
              @click="openModuleEdit(module)"
            >
              <AppIcon
                name="edit"
                :size="15"
              />
            </button>
            <button
              type="button"
              class="tap-target flex items-center justify-center rounded-lg text-slate-400 transition-colors hover:bg-rose-500/10 hover:text-rose-300"
              title="Modulni o‘chirish"
              @click="askDeleteModule(module)"
            >
              <AppIcon
                name="trash"
                :size="15"
              />
            </button>
          </div>
        </div>

        <!-- Darslar -->
        <div
          v-if="isOpen(module.id)"
          class="border-t border-line"
        >
          <ul
            v-if="(module.lessons?.length ?? 0) > 0"
            class="divide-y divide-line"
          >
            <li
              v-for="(lesson, lessonIndex) in module.lessons ?? []"
              :key="lesson.id"
              class="flex items-start gap-2 px-3 py-2.5 pl-6 sm:items-center"
            >
              <div class="min-w-0 flex-1">
                <p class="truncate text-[13px] text-slate-200">
                  <span class="mr-1.5 tabular-nums text-dim">
                    {{ moduleIndex + 1 }}.{{ lessonIndex + 1 }}
                  </span>
                  {{ lesson.name }}
                </p>
                <div class="mt-1 flex flex-wrap items-center gap-1.5">
                  <span
                    class="text-[11px] tabular-nums text-dim"
                    v-text="lessonDurationLabel(lesson)"
                  />
                  <!--
                    Dars TURI faqat imtihonda ko'rsatiladi: "Odatiy" nishoni
                    har qatorda takrorlanib, ro'yxatni shovqinga to'ldirardi
                    (odatiy dars — standart holat).
                  -->
                  <BaseBadge
                    v-if="lesson.kind === 'Exam'"
                    tone="warning"
                  >
                    {{ lessonKindLabel(lesson.kind) }}
                  </BaseBadge>
                  <BaseBadge
                    v-if="lessonAssetSummary(lesson) !== null"
                    tone="neutral"
                  >
                    {{ lessonAssetSummary(lesson) }}
                  </BaseBadge>
                  <BaseBadge
                    v-if="lesson.hasAssignment"
                    tone="accent"
                  >
                    Vazifa
                  </BaseBadge>
                  <BaseBadge
                    v-if="lesson.hasTest"
                    tone="assistant"
                  >
                    Test
                  </BaseBadge>
                  <BaseBadge
                    v-if="(lesson.description ?? '').length === 0"
                    tone="warning"
                  >
                    Tavsif yo‘q
                  </BaseBadge>
                </div>
              </div>

              <div class="flex shrink-0 items-center gap-1">
                <button
                  type="button"
                  class="tap-target flex items-center justify-center rounded-lg text-slate-400 transition-colors hover:bg-ink-800 hover:text-slate-100 disabled:opacity-30"
                  title="Yuqoriga"
                  :disabled="lessonIndex === 0 || reorderPending"
                  @click="moveLesson(module, lessonIndex, -1)"
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
                  :disabled="lessonIndex === (module.lessons?.length ?? 0) - 1 || reorderPending"
                  @click="moveLesson(module, lessonIndex, 1)"
                >
                  <AppIcon
                    name="arrow-down"
                    :size="15"
                  />
                </button>
                <button
                  type="button"
                  class="tap-target flex items-center justify-center rounded-lg text-slate-400 transition-colors hover:bg-ink-800 hover:text-slate-100"
                  title="Darsni tahrirlash"
                  @click="openLessonEdit(module, lesson)"
                >
                  <AppIcon
                    name="edit"
                    :size="15"
                  />
                </button>
                <button
                  type="button"
                  class="tap-target flex items-center justify-center rounded-lg text-slate-400 transition-colors hover:bg-rose-500/10 hover:text-rose-300"
                  title="Darsni o‘chirish"
                  @click="askDeleteLesson(module, lesson)"
                >
                  <AppIcon
                    name="trash"
                    :size="15"
                  />
                </button>
              </div>
            </li>
          </ul>

          <p
            v-else
            class="px-6 py-3 text-xs text-dim"
          >
            Bu modulda dars yo‘q.
          </p>

          <div class="border-t border-line px-3 py-2.5 pl-6">
            <BaseButton
              size="sm"
              variant="ghost"
              @click="openLessonCreate(module)"
            >
              <template #icon>
                <AppIcon
                  name="plus"
                  :size="14"
                />
              </template>
              Dars qo‘shish
            </BaseButton>
          </div>
        </div>
      </li>
    </ul>

    <ModuleFormDialog
      :open="moduleDialogOpen"
      :course-id="props.course.id"
      :module="editingModule"
      @close="moduleDialogOpen = false"
      @saved="emit('changed')"
    />

    <!--
      Dars DRAWER'da tahrirlanadi (o'ngdan 85%): ichida to'rt bo'lim bor —
      ma'lumotlar, tur, media (video qismlari / rasmlar) va uy vazifasi.
    -->
    <LessonEditDrawer
      :open="lessonDrawerOpen"
      :course-id="props.course.id"
      :module-id="lessonModuleId"
      :lesson="editingLesson"
      :module-name="editingModuleName"
      @close="lessonDrawerOpen = false"
      @saved="emit('changed')"
    />

    <ConfirmDeleteDialog
      :open="deleteTarget !== null"
      :title="deleteTarget?.title ?? ''"
      :message="deleteTarget?.message ?? ''"
      :pending="deleteMutation.isPending.value"
      :error="deleteError"
      @close="deleteTarget = null"
      @confirm="confirmDelete"
    />
  </section>
</template>
