<script setup lang="ts">
import { useMutation, useQuery } from '@tanstack/vue-query'
import { computed, ref, watch } from 'vue'

import { fetchCourses } from '@/entities/course'
import { fetchGroupCategories, groupCategoryOptionLabel } from '@/entities/group-category'
import {
  createGroup,
  fetchCuratorCandidates,
  fetchGroup,
  fetchGroups,
  updateGroup,
  weekdayLabel,
} from '@/entities/group'
import { fetchUsers } from '@/entities/user'
import { toUserMessage } from '@/shared/api'
import { useConfirm } from '@/shared/lib/useConfirm'
import type {
  DayOfWeekName,
  GroupDto,
  GroupTypeName,
  ScheduleChangeSummary,
} from '@/shared/types'
import { BaseButton, BaseCard, BaseDrawer, BaseField, SectionLoader } from '@/shared/ui'

import VideoStartLessonPicker from './VideoStartLessonPicker.vue'
import type { GroupSectionForms, GroupSectionKey } from '../model/group-sections'
import {
  buildPayload,
  changedFieldLabels,
  formsForSectionSave,
  formsFrom,
  GROUP_SECTION_TITLES,
  scheduleRuleChanged,
  sectionIsDirty,
  sectionValidationError,
} from '../model/group-sections'

/**
 * GURUHNI BO'LIMLAR BO'YICHA TAHRIRLASH (o'ngdan chiquvchi 85% panel).
 *
 * Eski `GroupFormDialog.vue` (bitta `BaseModal` + bitta "Saqlash") o'rnini
 * bosadi. Talab: har bo'lim ALOHIDA saqlanadi va o'z loaderiga ega.
 *
 * ══════════════════════════════════════════════════════════════════════
 *  🔴 `PUT /groups/{id}` = TO'LIQ ALMASHTIRISH — shu komponentning butun
 *  mantig'i shundan kelib chiqadi
 * ══════════════════════════════════════════════════════════════════════
 *
 *  1. Panel ochilganda `GET /groups/{id}` bilan YANGI ma'lumot olinadi,
 *     ro'yxatning keshidan EMAS. Ro'yxat 30 sekundlik eski javob bo'lishi
 *     mumkin va u payload asosiga tushsa, boshqa xodimning o'zgarishini
 *     bekor qilardi.
 *  2. Bo'lim saqlanganda payload UCHALA bo'limdan yig'iladi
 *     (`buildPayload`): shu bo'lim — foydalanuvchi tahriridan, qolgan
 *     ikkitasi — SERVER snapshot'idan. Bitta maydonni yuborish qolgan
 *     hammasini `null` ga tushiradi (bu xato bir marta bo'lgan: kurs uzilib
 *     butun guruhda gating `NotInCourse` bo'lgan).
 *  3. Javobdagi `group` bilan uchala bo'limning lokal holati yangilanadi.
 *  4. 🔴 OPTIMISTIK QULF: saqlashdan OLDIN `GET` qaytariladi va `updatedAt`
 *     panel ochilganda olingani bilan solishtiriladi. Farq bo'lsa saqlash
 *     TO'XTATILADI. Aks holda ikki xodim ikki bo'limni parallel saqlab,
 *     biri ikkinchisining ishini yo'q qilardi.
 *
 * ★ LOADER: uchala tugma bitta `saveMutation` ustida ishlaydi va qaysi
 * bo'lim saqlanayotgani `variables` dan olinadi (`isSaving`). Uch alohida
 * mutatsiya yozilsa uchta bir xil `onError` bloki paydo bo'lardi.
 */
const props = defineProps<{
  open: boolean
  /** Tahrirlanadigan guruh Id'si. `null` — YANGI guruh rejimi. */
  groupId: number | null
}>()

const emit = defineEmits<{ close: []; saved: [] }>()

const confirm = useConfirm()

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

const SECTION_KEYS: readonly GroupSectionKey[] = ['basic', 'schedule', 'course']

const isEdit = computed(() => props.groupId !== null)

/* ------------------------------------------------------------- holat */

/** Tahrirlanayotgan qiymatlar (uchala bo'lim). */
const forms = ref<GroupSectionForms>(formsFrom(null))
/** Oxirgi SERVER holati — payload asosi va "o'zgardimi" solishtiruvi. */
const server = ref<GroupDto | null>(null)
/** Optimistik qulf uchun asos. */
const baselineUpdatedAt = ref<string | null>(null)

const loading = ref(false)
const loadError = ref<string | null>(null)
/** Boshqa joyda o'zgargani aniqlandi — saqlash to'sib qo'yiladi. */
const conflict = ref(false)

const sectionError = ref<Record<GroupSectionKey, string | null>>({
  basic: null,
  schedule: null,
  course: null,
})
const sectionNote = ref<Record<GroupSectionKey, string | null>>({
  basic: null,
  schedule: null,
  course: null,
})

/** Kurs almashtirilganda boshlanish darsi tozalanganini AYTIB qo'yamiz. */
const courseResetNote = ref<string | null>(null)
/** Yaratish rejimi natijasi (eski oynadagidek: forma o'rniga xulosa). */
const createdNote = ref<string | null>(null)
/**
 * Yaratishdagi server xatosi ALOHIDA saqlanadi: yaratish rejimida bo'lim
 * futerlari chizilmaydi, ya'ni `sectionError` ko'rinmas joyga tushardi.
 */
const createError = ref<string | null>(null)

function clearMessages(): void {
  loadError.value = null
  conflict.value = false
  courseResetNote.value = null
  createdNote.value = null
  createError.value = null
  sectionError.value = { basic: null, schedule: null, course: null }
  sectionNote.value = { basic: null, schedule: null, course: null }
}

function applyServer(group: GroupDto): void {
  server.value = group
  baselineUpdatedAt.value = group.updatedAt
  forms.value = formsFrom(group)
}

/*
  ★ `loadToken` — "eskirgan javob" himoyasi: panel bir guruhga ochilib,
  yopilib, boshqa guruhga ochilsa birinchi `GET` javobi KEYIN kelib formani
  begona guruh ma'lumoti bilan to'ldirishi mumkin. Token mos kelmasa javob
  tashlab yuboriladi.
*/
let loadToken = 0

async function loadGroup(id: number): Promise<void> {
  const token = ++loadToken
  loading.value = true
  clearMessages()
  try {
    const fresh = await fetchGroup(id)
    if (token !== loadToken) return
    applyServer(fresh)
  } catch (error) {
    if (token !== loadToken) return
    loadError.value = toUserMessage(error as Error)
  } finally {
    if (token === loadToken) loading.value = false
  }
}

/*
  Panel ochilganda (yoki boshqa guruhga qayta ochilganda) holat tiklanadi.
  Yopilganda TOZALANMAYDI: yopilish animatsiyasi davomida forma ko'rinib
  turadi va tozalash "sakrash" bo'lib ko'rinardi.
*/
watch(
  () => [props.open, props.groupId] as const,
  ([open, groupId]) => {
    if (!open) return
    loadToken += 1
    if (groupId === null) {
      loading.value = false
      server.value = null
      baselineUpdatedAt.value = null
      forms.value = formsFrom(null)
      clearMessages()
      return
    }
    void loadGroup(groupId)
  },
  { immediate: true },
)

/* --------------------- ustoz/kurator/kurs ro'yxatlari -------------------- */
// Faqat panel ochiq bo'lganda yuklanadi — CRM sahifasi sekinlashmasin.
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
  qo'shiladi — aks holda select bo'sh qolib, saqlashda kurs jimgina uzilardi.
*/
const coursesQuery = useQuery({
  queryKey: ['courses', 'active', 'options'],
  queryFn: ({ signal }) => fetchCourses({ isActive: true, pageSize: 100 }, { signal }),
  enabled: staffEnabled,
})

const courses = computed(() => coursesQuery.data.value?.items ?? [])

/** Guruhdagi kurs ro'yxatda yo'q (arxivlangan) — tushib qolmasin. */
const missingCourseOption = computed(() => {
  const current = server.value
  if (current?.courseId == null) return null
  if (courses.value.some((item) => item.id === current.courseId)) return null
  return { id: current.courseId, name: `${current.courseName ?? 'Kurs'} (arxiv)` }
})

/* ===== R21b · O'QUV YO'NALISHLARI (kategoriyalar) =====

   Kurslar bilan AYNI naqsh va AYNI sabab: tanlagichda faqat FAOL
   kategoriyalar turadi, lekin guruhda ARXIVLANGAN kategoriya bo'lsa u
   ro'yxatga qaytariladi — aks holda `PUT` (to'liq almashtirish) saqlashda
   yorliqni JIMGINA uzib yuborardi.

   ★ Kesh kaliti `['group-categories', 'active']` — boshqaruv paneli
   (`GroupCategoryManagerDrawer`) `['group-categories']` prefiksi bo'yicha
   invalidatsiya qiladi, ya'ni yangi kategoriya qo'shilishi bilan bu
   tanlagich ham yangilanadi. */
const categoriesQuery = useQuery({
  queryKey: ['group-categories', 'active'],
  queryFn: ({ signal }) => fetchGroupCategories({ isActive: true }, { signal }),
  enabled: staffEnabled,
})

const categories = computed(() => categoriesQuery.data.value ?? [])

/** Guruhdagi kategoriya ro'yxatda yo'q (arxivlangan) — tushib qolmasin. */
const missingCategoryOption = computed(() => {
  const current = server.value
  if (current?.categoryId == null) return null
  if (categories.value.some((item) => item.id === current.categoryId)) return null
  return { id: current.categoryId, name: `${current.categoryName ?? 'Yo‘nalish'} (arxiv)` }
})

/*
  Kurator guruhi nomzodlari. Tahrirlashda SERVER filtri (`curator-candidates`)
  o'zini, zanjir yasovchini va nofaol guruhlarni chiqarib tashlaydi; yaratishda
  guruh Id'si hali yo'q, shuning uchun faol kurator guruhlari ro'yxati olinadi.
*/
interface CuratorOption {
  id: number
  name: string | null
  linkedGroupCount: number
}

const isCuratorGroup = computed(() => forms.value.basic.type === 'Curator')
const curatorPickerEnabled = computed(() => props.open && !isCuratorGroup.value)

const curatorCandidatesQuery = useQuery({
  queryKey: ['groups', 'curator-candidates', computed(() => props.groupId)],
  queryFn: async ({ signal }): Promise<CuratorOption[]> => {
    const groupId = props.groupId
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
  const current = server.value
  if (current?.curatorGroupId == null) return null
  if (curatorCandidates.value.some((item) => item.id === current.curatorGroupId)) return null
  return {
    id: current.curatorGroupId,
    name: current.curatorGroupName ?? 'Bog‘langan kurator guruhi',
  }
})

/* ------------------------------------------------------- maydon amallari */

function toggleWeekday(day: DayOfWeekName): void {
  const days = forms.value.schedule.weekdays
  const index = days.indexOf(day)
  if (index >= 0) days.splice(index, 1)
  else days.push(day)
}

/*
  Tur o'zgarishi `<select>` ning `@change` ida ushlanadi, `watch` da EMAS:
  `watch` server javobi bilan formani tiklaganda ham ishga tushib, haqiqiy
  bog'lanishni jimgina tozalab yuborardi.
*/
function onTypeChange(event: Event): void {
  const value = (event.target as HTMLSelectElement).value as GroupTypeName
  forms.value.basic.type = value
  // Kurator guruhining O'ZI boshqa kuratorga bog'lanmaydi (Domain qoidasi).
  if (value === 'Curator') forms.value.basic.curatorGroupId = null
}

/**
 * 🔴 KURS ALMASHTIRILGANDA VIDEO BOSHLANISH DARSI TOZALANADI.
 *
 * Eski kursning darsi yuborilsa server 400 beradi ("Tanlangan dars guruhning
 * kursiga tegishli emas"). Tozalash JIMGINA bo'lmaydi — foydalanuvchiga
 * aytiladi, aks holda u "sozlagan edim, yo'qolib qoldi" deb o'ylardi.
 */
function onCourseChange(event: Event): void {
  const raw = (event.target as HTMLSelectElement).value
  const next = raw.length > 0 ? Number(raw) : null
  const previous = forms.value.course.courseId
  forms.value.course.courseId = next

  if (previous === next) return
  if (forms.value.course.videoStartLessonId === null) {
    courseResetNote.value = null
    return
  }
  forms.value.course.videoStartLessonId = null
  courseResetNote.value =
    'Kurs almashtirildi — video darslar boshlanish nuqtasi tozalandi. Yangi kursdan qaytadan tanlang.'
}

/* --------------------------------------------------------- saqlash oqimi */

/*
  🔴 INTEGRATSIYADA TOPILDI (brauzerda o'lchangan): serverning UCHTA yo'li bor,
  bu funksiya esa faqat IKKITASINI ajratardi.

  Faqat guruh NOMINI o'zgartirib saqlashda server quyidagini qaytaradi:
      scheduleTouched: true, regenerated: false,
      created: 0, deleted: 0, preserved: 0, titlesUpdated: 50
  ya'ni darslar O'RNIDA tahrirlangan (Id, xona nomi, davomat, chat SAQLANGAN).
  Eski matn esa aynan shu holatda «Jadval yangilandi: +0 / −0, saqlab qolindi 0»
  deb yozardi — bu HAQIQATNING TESKARISI: xodim 50 ta dars o'chib ketgan va
  hech nima saqlanmagan deb o'ylardi.

  `created`/`deleted`/`preserved` FAQAT `regenerated: true` da ma'noga ega;
  o'rnida tahrirlash `hostsUpdated`/`titlesUpdated` bilan o'lchanadi.
*/
function scheduleSummaryNote(summary: ScheduleChangeSummary): string {
  if (!summary.scheduleTouched) return 'Saqlandi. Dars jadvaliga tegilmadi.'

  if (summary.regenerated) {
    return (
      `Saqlandi. Jadval qayta tuzildi: +${summary.created} / −${summary.deleted}, ` +
      `saqlab qolindi ${summary.preserved}.`
    )
  }

  // O'rnida tahrirlash: dars o'chirilmadi, faqat nom/host yangilandi.
  const parts: string[] = []
  if (summary.titlesUpdated > 0) parts.push(`${summary.titlesUpdated} ta dars nomi`)
  if (summary.hostsUpdated > 0) parts.push(`${summary.hostsUpdated} ta dars hosti`)
  const what = parts.length > 0 ? parts.join(' va ') : 'mavjud darslar'
  return `Saqlandi. Darslar o‘chirilmadi — ${what} o‘rnida yangilandi.`
}

const saveMutation = useMutation({
  mutationFn: async (section: GroupSectionKey) => {
    const current = server.value
    if (current === null) throw new Error('Guruh ma’lumoti yuklanmagan.')

    /*
      🔴 OPTIMISTIK QULF. `GET` saqlashdan OLDIN takrorlanadi: panel ochiq
      turgan vaqtda (bir necha daqiqa bo'lishi mumkin) boshqa xodim guruhni
      o'zgartirgan bo'lsa, bizning TO'LIQ payload uning ishini bekor qilardi.
    */
    const fresh = await fetchGroup(current.id)
    if (fresh.updatedAt !== baselineUpdatedAt.value) {
      conflict.value = true
      throw new Error(
        'Guruh boshqa joyda o‘zgardi. Saqlash to‘xtatildi — ma’lumotni qayta yuklab, o‘zgarishni takrorlang.',
      )
    }

    const payload = buildPayload(formsForSectionSave(fresh, section, forms.value))
    return updateGroup(fresh.id, payload)
  },
  onSuccess: (response, section) => {
    applyServer(response.group)
    courseResetNote.value = null
    sectionError.value[section] = null
    sectionNote.value[section] = scheduleSummaryNote(response.schedule)
    emit('saved')
  },
  onError: (error: Error, section) => {
    sectionError.value[section] = toUserMessage(error)
  },
})

function isSaving(section: GroupSectionKey): boolean {
  return saveMutation.isPending.value && saveMutation.variables.value === section
}

const anyPending = computed(() => saveMutation.isPending.value)

async function saveSection(section: GroupSectionKey): Promise<void> {
  const current = server.value
  if (current === null || conflict.value) return

  sectionError.value[section] = null
  sectionNote.value[section] = null

  const invalid = sectionValidationError(section, forms.value)
  if (invalid !== null) {
    sectionError.value[section] = invalid
    return
  }

  const changed = changedFieldLabels(section, forms.value, current)
  if (changed.length === 0) {
    sectionNote.value[section] = 'O‘zgarish yo‘q — saqlash kerak emas.'
    return
  }

  // Bo'lim payload'i (qolgan ikkisi server holatidan) jadvalga tegadimi?
  const payloadForms = formsForSectionSave(current, section, forms.value)
  const regenerates = scheduleRuleChanged(payloadForms, current)

  const details = changed.map((label) => `O‘zgardi: ${label}`)

  /*
    ★ Boshqa kartadagi SAQLANMAGAN tahrir haqida ochiq ogohlantirish: saqlash
    javobidan keyin uchala bo'lim server holatiga qaytariladi (C2.3 qoidasi),
    ya'ni u tahrir yo'qoladi. Jimgina yo'qotish eng yomon variant bo'lardi.
  */
  const dirtyOthers = SECTION_KEYS.filter(
    (key) => key !== section && sectionIsDirty(key, forms.value, current),
  )
  if (dirtyOthers.length > 0) {
    const titles = dirtyOthers.map((key) => `«${GROUP_SECTION_TITLES[key]}»`).join(', ')
    details.push(`${titles} bo‘limidagi saqlanmagan o‘zgarishlar bekor qilinadi.`)
  }

  if (regenerates) {
    details.push('Boshlanmagan darslar o‘chirilib qayta yaratiladi, o‘tgan darslar saqlanadi.')
    details.push('Nechta dars o‘zgargani saqlangandan keyin ko‘rsatiladi.')
  }

  const ok = await confirm({
    title: `${GROUP_SECTION_TITLES[section]} — saqlash`,
    message: regenerates
      ? 'Dars jadvali qayta generatsiya qilinadi. Davom etilsinmi?'
      : 'Guruh ma’lumoti yangilanadi. Davom etilsinmi?',
    confirmLabel: 'Saqlash',
    tone: regenerates ? 'warning' : 'primary',
    details,
  })
  if (!ok) return

  saveMutation.mutate(section)
}

/* --------------------------------------------------------- yaratish rejimi */

const createMutation = useMutation({
  mutationFn: () => createGroup(buildPayload(forms.value)),
  onSuccess: (response) => {
    createdNote.value = `Guruh yaratildi. ${response.sessionsCreated} ta dars jadvalga qo‘shildi.`
    emit('saved')
  },
  onError: (error: Error) => {
    createError.value = toUserMessage(error)
  },
})

/** Yaratishda uchala bo'lim ham to'g'ri bo'lishi shart (bo'lak-bo'lak saqlash yo'q). */
const createValidationError = computed(
  () =>
    sectionValidationError('basic', forms.value) ??
    sectionValidationError('schedule', forms.value) ??
    sectionValidationError('course', forms.value),
)

async function createGroupNow(): Promise<void> {
  createError.value = null

  const invalid = createValidationError.value
  if (invalid !== null) {
    createError.value = invalid
    return
  }

  const ok = await confirm({
    title: 'Yangi guruh',
    message: 'Guruh yaratiladi va butun kurs davri uchun dars jadvali generatsiya qilinadi.',
    confirmLabel: 'Yaratish',
    tone: 'primary',
    details: [
      `Nomi: ${forms.value.basic.name.trim()}`,
      `Boshlanish: ${forms.value.schedule.startDate}, ${forms.value.schedule.startTime}`,
      `Dars kunlari: ${forms.value.schedule.weekdays.map(weekdayLabel).join(', ')}`,
    ],
  })
  if (!ok) return

  createMutation.mutate()
}

/* ------------------------------------------------------------- yopilish */

const isDirty = computed(() => {
  const current = server.value
  if (current === null) {
    /*
      Yaratish rejimi: server holati yo'q, shuning uchun "biror narsa
      kiritilganmi" bo'yicha o'lchanadi. Bo'sh formani yopish savol
      so'ramaydi (aks holda tasodifan ochilgan panel ham to'sardi).
    */
    return (
      forms.value.basic.name.trim().length > 0 ||
      forms.value.schedule.weekdays.length > 0 ||
      forms.value.basic.teacherId !== null ||
      forms.value.basic.assistantId !== null ||
      forms.value.basic.curatorGroupId !== null ||
      forms.value.course.courseId !== null
    )
  }
  return SECTION_KEYS.some((key) => sectionIsDirty(key, forms.value, current))
})

/** Tasdiq ochiq turganda ikkinchi marta so'ralmasin (ESC ikki marta bosilishi). */
let closeAsking = false

async function requestClose(): Promise<void> {
  if (anyPending.value || createMutation.isPending.value) return
  if (closeAsking) return

  if (createdNote.value === null && isDirty.value) {
    closeAsking = true
    try {
      const ok = await confirm({
        title: 'Saqlanmagan o‘zgarishlar',
        message: 'Panel yopiladi va saqlanmagan o‘zgarishlar yo‘q bo‘ladi.',
        confirmLabel: 'Yopish',
        cancelLabel: 'Tahrirni davom ettirish',
        tone: 'warning',
      })
      if (!ok) return
    } finally {
      closeAsking = false
    }
  }

  emit('close')
}

const drawerSubtitle = computed(() => {
  if (!isEdit.value) return 'Bo‘limlar to‘ldirilgach guruh bir marta yaratiladi'
  const current = server.value
  if (current === null) return 'Ma’lumot yuklanmoqda…'
  return `${current.name ?? `Guruh #${current.id}`} · har bo‘lim alohida saqlanadi`
})

/** Server bergan "Modul · Dars" nomi — faqat SAQLANGAN qiymat uchun. */
const serverVideoStartLabel = computed(() => {
  const current = server.value
  if (current === null) return ''
  if (current.videoStartLessonId !== forms.value.course.videoStartLessonId) return ''
  const lesson = current.videoStartLessonName
  if (lesson === null) return ''
  const module = current.videoStartModuleName
  return module === null ? lesson : `${module} · ${lesson}`
})
</script>

<template>
  <BaseDrawer
    :open="props.open"
    :title="isEdit ? 'Guruhni tahrirlash' : 'Yangi guruh'"
    :subtitle="drawerSubtitle"
    @close="requestClose"
  >
    <!-- Panel ochilishida `GET /groups/{id}` — bo'lim skeleti bilan. -->
    <SectionLoader
      v-if="loading"
      variant="form"
      :rows="6"
      label="Guruh ma’lumoti yuklanmoqda"
    />

    <div
      v-else-if="loadError !== null"
      class="rounded-xl border border-rose-500/25 bg-rose-500/10 p-4 text-sm text-rose-200"
      role="alert"
    >
      <p v-text="loadError" />
      <BaseButton
        class="mt-3"
        size="sm"
        variant="secondary"
        @click="props.groupId !== null && loadGroup(props.groupId)"
      >
        Qayta urinish
      </BaseButton>
    </div>

    <!-- Yaratish natijasi: forma o'rniga xulosa (ikki marta yaratish mumkin emas). -->
    <div
      v-else-if="createdNote !== null"
      class="rounded-xl border border-brand-500/30 bg-brand-500/10 p-4 text-sm text-brand-200"
      v-text="createdNote"
    />

    <div
      v-else
      class="space-y-4"
    >
      <!--
        Ziddiyat banneri: `PUT` to'liq almashtirish bo'lgani uchun eski
        snapshot ustiga saqlash boshqa xodimning ishini yo'q qilardi.
      -->
      <div
        v-if="conflict"
        class="rounded-xl border border-amber-500/30 bg-amber-500/10 p-4 text-sm text-amber-200"
        role="alert"
      >
        <p>Guruh boshqa joyda o‘zgardi. Saqlash to‘xtatildi.</p>
        <p class="mt-1 text-[11px]">
          Qayta yuklash panelni serverdagi holatga qaytaradi — kiritilgan
          o‘zgarishlaringiz yo‘qoladi, ularni takrorlash kerak bo‘ladi.
        </p>
        <BaseButton
          class="mt-3"
          size="sm"
          variant="warning"
          :loading="loading"
          @click="props.groupId !== null && loadGroup(props.groupId)"
        >
          Qayta yuklash
        </BaseButton>
      </div>

      <!-- Yaratishdagi server xatosi (bo'lim futerlari bu rejimda yo'q). -->
      <div
        v-if="createError !== null"
        class="rounded-xl border border-rose-500/25 bg-rose-500/10 p-4 text-sm text-rose-200"
        role="alert"
        v-text="createError"
      />

      <!-- ═══════════════════ 1. ASOSIY MA'LUMOTLAR ═══════════════════ -->
      <BaseCard :title="GROUP_SECTION_TITLES.basic">
        <BaseField label="Guruh nomi">
          <input
            v-model="forms.basic.name"
            class="zn-input"
            required
          >
        </BaseField>

        <div class="mt-3 grid gap-3 sm:grid-cols-2">
          <BaseField
            label="Guruh turi"
            hint="Tur o‘zgarsa dars jadvali qayta tuziladi."
          >
            <select
              class="zn-input"
              :value="forms.basic.type"
              @change="onTypeChange"
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
          <BaseField
            label="Kurator guruhi"
            :hint="
              isCuratorGroup
                ? 'Kurator guruhi boshqa kurator guruhiga bog‘lanmaydi.'
                : 'Davomat va vazifalar shu kurator guruhi orqali hisoblanadi.'
            "
          >
            <select
              v-model="forms.basic.curatorGroupId"
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

        <!--
          R21b · O'QUV YO'NALISHI.
          ⚠️ "Kurs" bo'limidagi tanlagich BILAN ARALASHTIRILMASIN: kurs
          KONTENT beradi (modullar, darslar, gating), bu esa faqat YORLIQ.
          Hint aynan shuni aytadi — aks holda xodim ikkalasini bir xil
          narsa deb o'ylab, bittasini to'ldirmay ketardi.
        -->
        <div class="mt-3 grid gap-3 sm:grid-cols-2">
          <BaseField
            label="Yo‘nalish (kategoriya)"
            hint="Guruhni saralash uchun yorliq: ATF, Grammatika, CEFR, IELTS. Kurs kontentiga ta’sir qilmaydi."
          >
            <select
              v-model="forms.basic.categoryId"
              class="zn-input"
            >
              <option :value="null">
                Tanlanmagan
              </option>
              <option
                v-if="missingCategoryOption !== null"
                :value="missingCategoryOption.id"
              >
                {{ missingCategoryOption.name }}
              </option>
              <option
                v-for="item in categories"
                :key="item.id"
                :value="item.id"
              >
                {{ groupCategoryOptionLabel(item) }}
              </option>
            </select>
          </BaseField>
        </div>

        <div class="mt-3 grid gap-3 sm:grid-cols-2">
          <BaseField label="Ustoz">
            <select
              v-model="forms.basic.teacherId"
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
              v-model="forms.basic.assistantId"
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

        <!--
          ============================================================
          R33 + R40 — KIM MAS'UL (o'quv bo'limi tanlaydi)
          ============================================================

          Loyiha egasi ikki marta AYNI narsani so'radi: *"vazifalarni
          tekshirishni dynamic qilish kerak, o'quv bo'limi tanlaydi kurator
          yoki teacher tekshirishi kerakligini"* (R33) va *"javob berish
          dostupi dynamic bo'lsin, o'quv bo'limi tarafidan tayinlanishi
          kerak"* (R40).

          ★ IKKI TANLOV, BITTA QATOR — va ular ATAYLAB ustma-ust turadi:
            markaz ularni birga o'ylaydi ("kuratorga savollarni beraylik,
            baholashni ustozda qoldiraylik"). Turli bo'limlarga bo'linsa
            bu bog'liqlik ko'rinmasdi.

          ★ HAR IKKALASINING STANDARTI — BUGUNGI XATTI-HARAKAT, lekin ular
            HAR XIL: baholashda ustoz ham, kurator ham; savollarda faqat
            kurator. Shuning uchun bitta kalitga birlashtirilmadi.

          ⚠️ BO'SH O'RINDIQNI TANLASH SERVERDA 400 BERADI (guruhga kurator
            biriktirilmagan bo'lsa "Kurator" ni tanlab bo'lmaydi) — xato
            shu kartochka ustida ko'rinadi.
        -->
        <div class="mt-3 grid gap-3 sm:grid-cols-2">
          <BaseField
            label="Vazifalarni kim tekshiradi"
            hint="O‘quv bo‘limi va admin har doim tekshira oladi."
          >
            <select
              v-model="forms.basic.assignmentGraderRole"
              class="zn-input"
            >
              <option value="Both">
                Ustoz va kurator
              </option>
              <option value="Teacher">
                Faqat ustoz
              </option>
              <option value="Assistant">
                Faqat kurator
              </option>
            </select>
          </BaseField>
          <!--
            🔴 "Ikkalasi ham" TANLANSA O'QUVCHIDA IKKI SUHBAT bo'ladi
            (ustoz bilan va kurator bilan) — bu ONGLI natija, tasodif emas.
            Ularning yozishmalari bir-biridan YOPIQ: suhbat kaliti
            `(o'quvchi, xodim)` juftligi, ya'ni ustoz kuratorning
            yozishmasini ko'ra olmaydi.
          -->
          <BaseField
            label="Savollarga kim javob beradi"
            hint="“Ustoz va kurator” — o‘quvchida ikkita alohida suhbat bo‘ladi."
          >
            <select
              v-model="forms.basic.questionResponderRole"
              class="zn-input"
            >
              <option value="Assistant">
                Faqat kurator
              </option>
              <option value="Teacher">
                Faqat ustoz
              </option>
              <option value="Both">
                Ustoz va kurator
              </option>
            </select>
          </BaseField>
        </div>

        <div class="mt-3 flex flex-wrap gap-x-6">
          <label class="flex min-h-11 items-center gap-2.5 text-sm text-slate-300">
            <input
              v-model="forms.basic.recordEnabled"
              type="checkbox"
              class="size-4 accent-brand-500"
            >
            Darslarni yozib olish
          </label>
          <!--
            R5. ⚠️ YUQORIDAGI KALITDAN ALOHIDA VA BU ATAYLAB: "yozib olish"
            o'chirilsa fayl UMUMAN yaratilmaydi, bu esa faqat o'quvchidan
            yashiradi — yozuv olinadi va o'quv bo'limi uni ko'raveradi.
            Ikkalasini bitta kalitga birlashtirish arxivni yo'q qilardi.
          -->
          <label
            class="flex min-h-11 items-center gap-2.5 text-sm text-slate-300"
            title="O‘chirilsa yozuvlar baribir olinadi, lekin o‘quvchilar ularni ko‘rmaydi."
          >
            <input
              v-model="forms.basic.recordingsVisibleToStudents"
              type="checkbox"
              class="size-4 accent-brand-500"
            >
            Yozuvlar o‘quvchilarga ochiq
          </label>
          <label class="flex min-h-11 items-center gap-2.5 text-sm text-slate-300">
            <input
              v-model="forms.basic.isActive"
              type="checkbox"
              class="size-4 accent-brand-500"
            >
            Faol guruh
          </label>
        </div>

        <!--
          ============================================================
          YOZIB OLISH USULI (yozuv quvuri v2)
          ============================================================

          ★ AYNAN `recordEnabled` YONIDA: bu o'sha kalitning DAVOMI —
            "yozilsinmi" dan keyingi "qanday yozilsin". Boshqa kartaga
            qo'yilsa bog'liqlik ko'rinmasdi.

          🔴 YOZUV O'CHIQ BO'LSA TANLAGICH HAM O'CHIQ: yozilmaydigan
             guruhda usul tanlash hech narsa qilmaydi va xodim uni
             "yoqdim" deb tushunardi. Saqlangan qiymat esa YO'QOLMAYDI —
             `buildPayload` uni baribir qaytaradi (izoh o'sha faylda).

          🔴 GLOBAL KALIT USTUNROQ: `recordings.track_pipeline_enabled`
             sozlamasi o'chiq bo'lsa bu tanlov e'tiborga olinmaydi va
             guruh standart yo'lda qolaveradi. Shuning uchun izoh matni
             buni ochiq aytadi — aks holda "tanladim, lekin ishlamadi"
             degan savol paydo bo'lardi. Sozlamaning O'ZI bu yerdan
             o'qilmaydi: u "Sozlamalar" sahifasida turadi va uni har
             guruh panelida so'rash ortiqcha so'rov bo'lardi.
        -->
        <div class="mt-3 grid gap-3 sm:grid-cols-2">
          <BaseField
            label="Yozib olish usuli"
            :hint="
              forms.basic.recordEnabled
                ? '“Tungi montaj” sifatliroq: video kechasi tayyorlanadi va ertalab ochiladi. Global sozlama o‘chiq bo‘lsa standart usul ishlaydi.'
                : 'Avval “Darslarni yozib olish”ni yoqing.'
            "
          >
            <select
              v-model="forms.basic.recordingPipeline"
              class="zn-input"
              :disabled="!forms.basic.recordEnabled"
            >
              <option value="RoomComposite">
                Standart (jonli montaj)
              </option>
              <option value="TrackComposition">
                Tungi montaj (sifatliroq)
              </option>
            </select>
          </BaseField>
        </div>

        <div
          v-if="isEdit"
          class="mt-4 flex flex-wrap items-center justify-end gap-3 border-t border-line pt-3.5"
        >
          <p
            v-if="sectionError.basic !== null"
            class="mr-auto text-[11px] text-rose-400"
            role="alert"
            v-text="sectionError.basic"
          />
          <p
            v-else-if="sectionNote.basic !== null"
            class="mr-auto text-[11px] text-brand-500"
            v-text="sectionNote.basic"
          />
          <BaseButton
            size="sm"
            :loading="isSaving('basic')"
            :disabled="conflict || anyPending"
            @click="saveSection('basic')"
          >
            Saqlash
          </BaseButton>
        </div>
      </BaseCard>

      <!-- ═══════════════════════ 2. DARS JADVALI ═══════════════════════ -->
      <BaseCard :title="GROUP_SECTION_TITLES.schedule">
        <div class="grid gap-3 sm:grid-cols-2">
          <BaseField label="Boshlanish sanasi">
            <input
              v-model="forms.schedule.startDate"
              class="zn-input"
              type="date"
              required
            >
          </BaseField>
          <BaseField label="Boshlanish vaqti">
            <input
              v-model="forms.schedule.startTime"
              class="zn-input"
              type="time"
              required
            >
          </BaseField>
        </div>

        <div class="mt-3">
          <BaseField
            label="Dars kunlari"
            :error="forms.schedule.weekdays.length === 0 ? 'Kamida bitta kun tanlang.' : null"
          >
            <div class="flex flex-wrap gap-2">
              <button
                v-for="day in WEEKDAYS"
                :key="day"
                type="button"
                class="min-h-11 min-w-11 rounded-lg px-3 text-xs font-medium transition-colors"
                :class="
                  forms.schedule.weekdays.includes(day)
                    ? 'bg-brand-500 text-on-brand'
                    : 'bg-ink-800 text-slate-300 hover:bg-ink-750'
                "
                @click="toggleWeekday(day)"
              >
                {{ weekdayLabel(day) }}
              </button>
            </div>
          </BaseField>
        </div>

        <div class="mt-3 grid gap-3 sm:grid-cols-2">
          <BaseField label="Dars davomiyligi (daq.)">
            <input
              v-model.number="forms.schedule.durationMinutes"
              class="zn-input"
              type="number"
              min="10"
              max="300"
            >
          </BaseField>
          <BaseField label="Kurs davomiyligi (oy)">
            <input
              v-model.number="forms.schedule.courseMonths"
              class="zn-input"
              type="number"
              min="1"
              max="24"
            >
          </BaseField>
        </div>

        <p class="mt-3 rounded-lg border border-line bg-ink-950 px-3 py-2 text-[11px] leading-relaxed text-slate-400">
          Bu bo‘lim saqlanganda dars jadvali qayta generatsiya qilinadi:
          boshlanmagan darslar o‘chirilib qayta yaratiladi, o‘tgan va
          yakunlangan darslar saqlanadi.
        </p>

        <div
          v-if="isEdit"
          class="mt-4 flex flex-wrap items-center justify-end gap-3 border-t border-line pt-3.5"
        >
          <p
            v-if="sectionError.schedule !== null"
            class="mr-auto text-[11px] text-rose-400"
            role="alert"
            v-text="sectionError.schedule"
          />
          <p
            v-else-if="sectionNote.schedule !== null"
            class="mr-auto text-[11px] text-brand-500"
            v-text="sectionNote.schedule"
          />
          <BaseButton
            size="sm"
            :loading="isSaving('schedule')"
            :disabled="conflict || anyPending"
            @click="saveSection('schedule')"
          >
            Saqlash
          </BaseButton>
        </div>
      </BaseCard>

      <!-- ══════════════════════════ 3. KURS ══════════════════════════ -->
      <BaseCard :title="GROUP_SECTION_TITLES.course">
        <BaseField
          label="Kurs"
          hint="Kurssiz guruhda o‘quvchilar uchun barcha darslar qulflanadi."
        >
          <select
            class="zn-input"
            :value="forms.course.courseId ?? ''"
            @change="onCourseChange"
          >
            <option value="">
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

        <div class="mt-3">
          <VideoStartLessonPicker
            v-model="forms.course.videoStartLessonId"
            :course-id="forms.course.courseId"
            :enabled="props.open"
            :selected-label="serverVideoStartLabel"
          />
        </div>

        <p
          v-if="courseResetNote !== null"
          class="mt-3 rounded-lg border border-amber-500/30 bg-amber-500/10 px-3 py-2 text-[11px] leading-relaxed text-amber-200"
          role="status"
          v-text="courseResetNote"
        />

        <div
          v-if="isEdit"
          class="mt-4 flex flex-wrap items-center justify-end gap-3 border-t border-line pt-3.5"
        >
          <p
            v-if="sectionError.course !== null"
            class="mr-auto text-[11px] text-rose-400"
            role="alert"
            v-text="sectionError.course"
          />
          <p
            v-else-if="sectionNote.course !== null"
            class="mr-auto text-[11px] text-brand-500"
            v-text="sectionNote.course"
          />
          <BaseButton
            size="sm"
            :loading="isSaving('course')"
            :disabled="conflict || anyPending"
            @click="saveSection('course')"
          >
            Saqlash
          </BaseButton>
        </div>
      </BaseCard>
    </div>

    <template #footer>
      <!--
        YARATISH rejimida BITTA tugma: guruh hali yo'q, bo'lak-bo'lak saqlash
        mumkin emas. Tahrirlashda futerda faqat "Yopish" — saqlash tugmalari
        bo'limlar ichida.
      -->
      <template v-if="isEdit || createdNote !== null">
        <BaseButton
          variant="secondary"
          @click="requestClose"
        >
          Yopish
        </BaseButton>
      </template>
      <template v-else>
        <!--
          Nima uchun tugma o'chiq turgani AYTILADI: sababsiz o'chiq tugma
          foydalanuvchini "ilova buzuq" degan xulosaga olib boradi.
        -->
        <p
          v-if="createValidationError !== null"
          class="text-[11px] text-slate-400 sm:mr-auto sm:self-center"
          v-text="createValidationError"
        />
        <BaseButton
          variant="secondary"
          :disabled="createMutation.isPending.value"
          @click="requestClose"
        >
          Bekor qilish
        </BaseButton>
        <BaseButton
          :disabled="createValidationError !== null"
          :loading="createMutation.isPending.value"
          @click="createGroupNow"
        >
          Yaratish
        </BaseButton>
      </template>
    </template>
  </BaseDrawer>
</template>
