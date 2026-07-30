<script setup lang="ts">
import { useMutation, useQuery } from '@tanstack/vue-query'
import { computed, ref, watch } from 'vue'

import { fetchCourses } from '@/entities/course'
import {
  createGroup,
  fetchCuratorCandidates,
  fetchGroups,
  updateGroup,
  weekdayLabel,
} from '@/entities/group'
import { fetchUsers } from '@/entities/user'
import { toUserMessage } from '@/shared/api'
import { formatClock } from '@/shared/lib/datetime'
import type { DayOfWeekName, GroupDto, GroupTypeName, GroupWriteRequest } from '@/shared/types'
import { BaseButton, BaseField, BaseModal } from '@/shared/ui'

/**
 * Guruh yaratish/tahrirlash.
 *
 * DIQQAT: jadval maydonlari (hafta kunlari, vaqt, davomiylik) o'zgarsa server
 * dars jadvalini QAYTA generatsiya qiladi va nechta dars yaratilgani/o'chirilgani
 * javobda keladi — buni foydalanuvchiga ko'rsatamiz, aks holda 69 ta darsning
 * jimgina o'chib ketishi kutilmagan bo'ladi.
 *
 * ★ `PUT /groups/{id}` TO'LIQ ALMASHTIRISH semantikasi: yuborilmagan maydon
 * `null` ga tushadi va server uni to'g'ridan-to'g'ri yozadi
 * (`GroupService.UpdateAsync`: `group.CourseId = request.CourseId`). Shu sababli
 * `courseId` va `curatorGroupId` MAYDON SIFATIDA shu formada turishi SHART:
 * ilgari ular yuborilmagani uchun har tahrirlash kursni jimgina uzib qo'yardi —
 * natijada guruhning barcha o'quvchilarida gating `NotInCourse` bo'lib, butun
 * kurs qulflanardi (vazifa va testlar ham ko'rinmay qolardi).
 */
const props = defineProps<{
  open: boolean
  /** `null` — yangi guruh rejimi. */
  group: GroupDto | null
}>()

const emit = defineEmits<{ close: []; saved: [] }>()

const WEEKDAYS: DayOfWeekName[] = [
  'Monday',
  'Tuesday',
  'Wednesday',
  'Thursday',
  'Friday',
  'Saturday',
  'Sunday',
]

const GROUP_TYPES: ReadonlyArray<{ value: GroupTypeName; label: string }> = [
  { value: 'Group', label: 'Guruh' },
  { value: 'Individual', label: 'Individual' },
  { value: 'Curator', label: 'Kurator guruhi' },
]

const name = ref('')
const type = ref<GroupTypeName>('Group')
const startDate = ref('')
const startTime = ref('10:00')
const weekdays = ref<DayOfWeekName[]>([])
const durationMinutes = ref(80)
const courseMonths = ref(8)
const teacherId = ref<number | null>(null)
const assistantId = ref<number | null>(null)
const courseId = ref<number | null>(null)
const curatorGroupId = ref<number | null>(null)
const recordEnabled = ref(false)
const isActive = ref(true)
const errorMessage = ref<string | null>(null)
const scheduleNote = ref<string | null>(null)

const isEdit = computed(() => props.group !== null)

function todayIso(): string {
  return new Date().toISOString().slice(0, 10)
}

function resetForm(): void {
  const group = props.group
  name.value = group?.name ?? ''
  type.value = group?.type ?? 'Group'
  startDate.value = group?.startDate ?? todayIso()
  startTime.value = group !== null ? formatClock(group.startTime) : '10:00'
  weekdays.value = [...(group?.weekdays ?? [])]
  durationMinutes.value = group?.durationMinutes ?? 80
  courseMonths.value = group?.courseMonths ?? 8
  teacherId.value = group?.teacherId ?? null
  assistantId.value = group?.assistantId ?? null
  courseId.value = group?.courseId ?? null
  curatorGroupId.value = group?.curatorGroupId ?? null
  recordEnabled.value = group?.recordEnabled ?? false
  isActive.value = group?.isActive ?? true
  errorMessage.value = null
  scheduleNote.value = null
}

watch(() => [props.open, props.group], resetForm, { immediate: true })

/* --------------------- ustoz/kurator tanlash ro'yxatlari -------------------- */
// Faqat oyna ochiq bo'lganda yuklanadi — CRM sahifasi ochilishi sekinlashmasin.
const staffEnabled = computed(() => props.open)

const teachersQuery = useQuery({
  queryKey: ['users', 'role', 'Teacher'],
  queryFn: ({ signal }) => fetchUsers({ role: 'Teacher', pageSize: 100 }, { signal }),
  enabled: staffEnabled,
})

const assistantsQuery = useQuery({
  queryKey: ['users', 'role', 'Assistant'],
  queryFn: ({ signal }) => fetchUsers({ role: 'Assistant', pageSize: 100 }, { signal }),
  enabled: staffEnabled,
})

const teachers = computed(() => teachersQuery.data.value?.items ?? [])
const assistants = computed(() => assistantsQuery.data.value?.items ?? [])

/*
  Kurslar: faqat FAOL kurslar tanlanadi (arxivlangan kursni yangi guruhga
  biriktirish mantiqsiz). Guruhda arxivlangan kurs turgan bo'lsa u ro'yxatga
  qo'shiladi — aks holda tahrirlashda select bo'sh qolib, saqlashda kurs
  jimgina uzilardi. AYNAN shu xatoni tuzatyapmiz.
*/
const coursesQuery = useQuery({
  queryKey: ['courses', 'active', 'options'],
  queryFn: ({ signal }) => fetchCourses({ isActive: true, pageSize: 100 }, { signal }),
  enabled: staffEnabled,
})

const courses = computed(() => coursesQuery.data.value?.items ?? [])

/** Guruhdagi kurs ro'yxatda yo'q (arxivlangan) — uni tushib qolmasin uchun ko'rsatamiz. */
const missingCourseOption = computed(() => {
  const group = props.group
  if (group?.courseId == null) return null
  if (courses.value.some((item) => item.id === group.courseId)) return null
  return { id: group.courseId, name: `${group.courseName ?? 'Kurs'} (arxiv)` }
})

/*
  Kurator guruhi nomzodlari.

  Tahrirlashda SERVER filtridan foydalanamiz (`curator-candidates`): u o'zini,
  zanjir yasovchi va nofaol guruhlarni chiqarib tashlaydi. Yaratishda esa guruh
  Id'si hali yo'q — shuning uchun faol kurator guruhlari ro'yxati olinadi.
*/
interface CuratorOption {
  id: number
  name: string | null
  /** Shu kurator guruhiga allaqachon nechta guruh bog'langan (yaratishda 0). */
  linkedGroupCount: number
}

const isCuratorGroup = computed(() => type.value === 'Curator')
const curatorPickerEnabled = computed(() => props.open && !isCuratorGroup.value)
const editedGroupId = computed(() => props.group?.id ?? null)

const curatorCandidatesQuery = useQuery({
  queryKey: ['groups', 'curator-candidates', editedGroupId],
  queryFn: async ({ signal }): Promise<CuratorOption[]> => {
    const groupId = editedGroupId.value
    if (groupId !== null) {
      const candidates = await fetchCuratorCandidates(groupId, { signal })
      return candidates.map((item) => ({
        id: item.id,
        name: item.name,
        linkedGroupCount: item.linkedGroupCount,
      }))
    }

    const paged = await fetchGroups({ type: 'Curator', isActive: true, pageSize: 100 }, { signal })
    return (paged.items ?? []).map((item) => ({
      id: item.id,
      name: item.name,
      linkedGroupCount: 0,
    }))
  },
  enabled: curatorPickerEnabled,
})

const curatorCandidates = computed(() => curatorCandidatesQuery.data.value ?? [])

/** Bog'langan kurator guruhi nomzodlar orasida bo'lmasa ham tanlovda qolsin. */
const missingCuratorOption = computed(() => {
  const group = props.group
  if (group?.curatorGroupId == null) return null
  if (curatorCandidates.value.some((item) => item.id === group.curatorGroupId)) return null
  return { id: group.curatorGroupId, name: group.curatorGroupName ?? 'Bog‘langan kurator guruhi' }
})

/*
  Kurator guruhining O'ZI boshqa kuratorga bog'lanmaydi (Domain qoidasi) —
  tur "Kurator guruhi" ga o'zgarganda bog'lanish tozalanadi, aks holda server
  400 qaytarardi.
*/
watch(isCuratorGroup, (isCurator) => {
  if (isCurator) curatorGroupId.value = null
})

function toggleWeekday(day: DayOfWeekName): void {
  const index = weekdays.value.indexOf(day)
  if (index >= 0) weekdays.value.splice(index, 1)
  else weekdays.value.push(day)
}

function buildPayload(): GroupWriteRequest {
  return {
    name: name.value.trim(),
    startDate: startDate.value,
    weekdays: weekdays.value,
    // Backend `TimeOnly` kutadi — `<input type="time">` faqat `HH:mm` beradi.
    startTime: `${startTime.value}:00`,
    type: type.value,
    durationMinutes: durationMinutes.value,
    courseMonths: courseMonths.value,
    teacherId: teacherId.value,
    assistantId: assistantId.value,
    // ★ Bu ikkisi HAR DOIM yuboriladi — yo'qligi kursni/bog'lanishni o'chirardi.
    courseId: courseId.value,
    curatorGroupId: curatorGroupId.value,
    recordEnabled: recordEnabled.value,
    isActive: isActive.value,
  }
}

const createMutation = useMutation({
  mutationFn: () => createGroup(buildPayload()),
  onSuccess: (response) => {
    emit('saved')
    scheduleNote.value = `Guruh yaratildi. ${response.sessionsCreated} ta dars jadvalga qo‘shildi.`
  },
  onError: (error: Error) => {
    errorMessage.value = toUserMessage(error)
  },
})

const updateMutation = useMutation({
  mutationFn: (id: number) => updateGroup(id, buildPayload()),
  onSuccess: (response) => {
    emit('saved')
    const summary = response.schedule
    scheduleNote.value = summary.scheduleTouched
      ? `Saqlandi. Jadval yangilandi: +${summary.created} / −${summary.deleted}, saqlab qolindi ${summary.preserved}.`
      : 'Saqlandi. Dars jadvaliga tegilmadi.'
  },
  onError: (error: Error) => {
    errorMessage.value = toUserMessage(error)
  },
})

const isPending = computed(() => createMutation.isPending.value || updateMutation.isPending.value)

const canSubmit = computed(
  () =>
    name.value.trim().length > 0 &&
    startDate.value.length > 0 &&
    weekdays.value.length > 0 &&
    !isPending.value,
)

function handleSubmit(): void {
  if (!canSubmit.value) return
  errorMessage.value = null
  const group = props.group
  if (group !== null) updateMutation.mutate(group.id)
  else createMutation.mutate()
}
</script>

<template>
  <BaseModal
    :open="props.open"
    wide
    :title="isEdit ? 'Guruhni tahrirlash' : 'Yangi guruh'"
    @close="emit('close')"
  >
    <div
      v-if="scheduleNote !== null"
      class="rounded-lg border border-brand-500/30 bg-brand-500/10 p-4 text-sm text-brand-200"
      v-text="scheduleNote"
    />

    <form
      v-else
      novalidate
      @submit.prevent="handleSubmit"
    >
      <BaseField label="Guruh nomi">
        <input
          v-model="name"
          class="zn-input"
          required
        >
      </BaseField>

      <div class="mt-3 grid gap-3 sm:grid-cols-2">
        <BaseField label="Turi">
          <select
            v-model="type"
            class="zn-input"
          >
            <option
              v-for="option in GROUP_TYPES"
              :key="option.value"
              :value="option.value"
            >
              {{ option.label }}
            </option>
          </select>
        </BaseField>
        <BaseField label="Boshlanish sanasi">
          <input
            v-model="startDate"
            class="zn-input"
            type="date"
            required
          >
        </BaseField>
      </div>

      <div class="mt-3">
        <BaseField
          label="Dars kunlari"
          :error="weekdays.length === 0 ? 'Kamida bitta kun tanlang.' : null"
        >
          <!-- Chip'lar (eski `.chip`): telefonda o'ralib ketadi, skroll shart emas. -->
          <div class="flex flex-wrap gap-2">
            <button
              v-for="day in WEEKDAYS"
              :key="day"
              type="button"
              class="min-h-11 min-w-11 rounded-lg px-3 text-xs font-medium transition-colors"
              :class="
                weekdays.includes(day)
                  ? 'bg-brand-500 text-white'
                  : 'bg-ink-800 text-slate-300 hover:bg-ink-750'
              "
              @click="toggleWeekday(day)"
            >
              {{ weekdayLabel(day) }}
            </button>
          </div>
        </BaseField>
      </div>

      <div class="mt-3 grid gap-3 sm:grid-cols-3">
        <BaseField label="Boshlanish vaqti">
          <input
            v-model="startTime"
            class="zn-input"
            type="time"
            required
          >
        </BaseField>
        <BaseField label="Dars davomiyligi (daq.)">
          <input
            v-model.number="durationMinutes"
            class="zn-input"
            type="number"
            min="10"
            max="300"
          >
        </BaseField>
        <BaseField label="Kurs davomiyligi (oy)">
          <input
            v-model.number="courseMonths"
            class="zn-input"
            type="number"
            min="1"
            max="24"
          >
        </BaseField>
      </div>

      <div class="mt-3 grid gap-3 sm:grid-cols-2">
        <BaseField label="Ustoz">
          <select
            v-model="teacherId"
            class="zn-input"
          >
            <option :value="null">
              Tanlanmagan
            </option>
            <option
              v-for="item in teachers"
              :key="item.id"
              :value="item.id"
            >
              {{ item.fullName ?? item.email }}
            </option>
          </select>
        </BaseField>
        <BaseField label="Kurator">
          <select
            v-model="assistantId"
            class="zn-input"
          >
            <option :value="null">
              Tanlanmagan
            </option>
            <option
              v-for="item in assistants"
              :key="item.id"
              :value="item.id"
            >
              {{ item.fullName ?? item.email }}
            </option>
          </select>
        </BaseField>
      </div>

      <div class="mt-3 grid gap-3 sm:grid-cols-2">
        <BaseField
          label="Kurs"
          hint="Kurssiz guruhda o‘quvchilar uchun barcha darslar qulflanadi."
        >
          <select
            v-model="courseId"
            class="zn-input"
          >
            <option :value="null">
              Biriktirilmagan
            </option>
            <option
              v-if="missingCourseOption !== null"
              :value="missingCourseOption.id"
            >
              {{ missingCourseOption.name }}
            </option>
            <option
              v-for="item in courses"
              :key="item.id"
              :value="item.id"
            >
              {{ item.name }}
            </option>
          </select>
        </BaseField>
        <BaseField
          label="Kurator guruhi"
          :hint="
            isCuratorGroup
              ? 'Kurator guruhi boshqa kurator guruhiga bog‘lanmaydi.'
              : 'Davomat va vazifalar shu kurator guruhi orqali hisoblanadi.'
          "
        >
          <select
            v-model="curatorGroupId"
            class="zn-input"
            :disabled="isCuratorGroup"
          >
            <option :value="null">
              Bog‘lanmagan
            </option>
            <option
              v-if="missingCuratorOption !== null"
              :value="missingCuratorOption.id"
            >
              {{ missingCuratorOption.name }}
            </option>
            <option
              v-for="item in curatorCandidates"
              :key="item.id"
              :value="item.id"
            >
              {{ item.name }}
            </option>
          </select>
        </BaseField>
      </div>

      <div class="mt-3 flex flex-wrap gap-x-6">
        <label class="flex min-h-11 items-center gap-2.5 text-sm text-slate-300">
          <input
            v-model="recordEnabled"
            type="checkbox"
            class="size-4 accent-brand-500"
          >
          Yozib olish yoqilsin
        </label>
        <label class="flex min-h-11 items-center gap-2.5 text-sm text-slate-300">
          <input
            v-model="isActive"
            type="checkbox"
            class="size-4 accent-brand-500"
          >
          Faol guruh
        </label>
      </div>

      <p
        v-if="errorMessage !== null"
        class="mt-3 text-xs text-rose-400"
        role="alert"
        v-text="errorMessage"
      />
    </form>

    <template #footer>
      <template v-if="scheduleNote !== null">
        <BaseButton @click="emit('close')">
          Yopish
        </BaseButton>
      </template>
      <template v-else>
        <BaseButton
          variant="secondary"
          @click="emit('close')"
        >
          Bekor qilish
        </BaseButton>
        <BaseButton
          :disabled="!canSubmit"
          :loading="isPending"
          @click="handleSubmit"
        >
          {{ isEdit ? 'Saqlash' : 'Yaratish' }}
        </BaseButton>
      </template>
    </template>
  </BaseModal>
</template>
