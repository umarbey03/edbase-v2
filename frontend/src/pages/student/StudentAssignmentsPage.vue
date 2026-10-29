<script setup lang="ts">
import { useQuery, useQueryClient } from '@tanstack/vue-query'
import { computed, nextTick, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'

import {
  answerFormatsLabel,
  assignmentBucket,
  assignmentState,
  assignmentTitle,
  fetchMyAssignments,
} from '@/entities/assignment'
import type { AssignmentBucket } from '@/entities/assignment'
import SubmissionFeedbackFiles from '@/entities/assignment/ui/SubmissionFeedbackFiles.vue'
import SubmitAssignmentDialog from '@/features/assignment-submit/ui/SubmitAssignmentDialog.vue'
import { useStudentCourse } from '@/features/student-course/model/useStudentCourse'
import { toUserMessage } from '@/shared/api'
import { formatDateTime } from '@/shared/lib/datetime'
import type { StudentAssignmentDto } from '@/shared/types'
import { AppIcon, BaseBadge, BaseButton, BaseModal, DataStatus, EmptyState } from '@/shared/ui'
import type { IconName } from '@/shared/ui'
import StudentSubHeader from '@/widgets/student-shell/ui/StudentSubHeader.vue'

/**
 * O'quvchining vazifalari.
 *
 * MUHIM: qulflangan dars vazifasi ham RO'YXATDA QOLADI — o'quvchi nima
 * kutayotganini bilishi kerak. Topshirib bo'lmasligining SABABI har
 * kartochkada matn bilan yoziladi.
 *
 * "Topshirish" tugmasi SERVER qaroriga tayanadi (`canSubmit` -> `blockedReason`)
 * — sahifa gating va qayta topshirish qoidalarini o'zicha hisoblamaydi.
 */
const queryClient = useQueryClient()

const assignmentsQuery = useQuery({
  queryKey: ['assignments', 'mine'],
  queryFn: ({ signal }) => fetchMyAssignments({ signal }),
})

/*
  ★ DARS YORLIG'I «7-dars · «Ayn» va «G'ayn»» (2026-10-29, audit 2.5-a).

  DTO darsning NOMINI beradi (`moduleLessonName`), tartib raqamini esa yo'q.
  Raqam kurs daraxtidan olinadi (`useStudentCourse().lessonsById`) — u
  O'quv sahifasi bilan AYNI so'rov va 5 daqiqa keshda, ya'ni odatda
  qo'shimcha so'rov ketmaydi. Backend o'zgarishi kerak emas.

  ⚠️ Daraxt kelmasa (xato, kurs yo'q, guruhning kursdan tashqari vazifasi)
  kartochka faqat nom bilan qoladi — ro'yxat daraxtni KUTMAYDI.
*/
const course = useStudentCourse()

interface LessonLabel {
  text: string
  /** Faqat OCHIQ dars havola bo'ladi — qulflangan darsga olib borish befoyda. */
  lessonId: number | null
}

function lessonLabel(item: StudentAssignmentDto): LessonLabel | null {
  if (item.moduleLessonId === null) return null
  const entry = course.lessonsById.value.get(item.moduleLessonId)
  if (entry === undefined) {
    return item.moduleLessonName === null ? null : { text: item.moduleLessonName, lessonId: null }
  }
  const name = entry.lesson.name ?? item.moduleLessonName ?? ''
  return {
    text: name.length > 0 ? `${entry.number}-dars · ${name}` : `${entry.number}-dars`,
    lessonId: entry.lesson.unlocked ? entry.lesson.id : null,
  }
}

/** Holat bir marta hisoblanadi — shablonda `assignmentState()` takror chaqirilmasin. */
const rows = computed(() =>
  (assignmentsQuery.data.value ?? []).map((item) => ({
    item,
    state: assignmentState(item),
    bucket: assignmentBucket(item),
    lesson: lessonLabel(item),
    formats: answerFormatsLabel(item.allowedFormats),
    feedback: item.mySubmission?.feedback ?? null,
    fileCount: item.mySubmission?.files?.length ?? 0,
    /**
     * R37 · USTOZ tekshirishda biriktirgan fayllar.
     *
     * ★ SHU YERDA `?? []` bir marta bajariladi: shablonda har kartochka
     * uchun null-tekshiruv yozilmasin.
     */
    feedbackFiles: item.mySubmission?.feedbackFiles ?? [],
  })),
)

type Row = (typeof rows.value)[number]

/* ==========================================================================
   SEGMENTLAR: «Topshirish kerak» / «Tekshirilmoqda» / «Baholangan»
   (2026-10-29, audit 2.5-a)

   Qaysi vazifa qayerga tushishi — `assignmentBucket` (entity), sahifa
   faqat ko'rsatadi va tartiblaydi.

   ★ TANLOV URL'DA (`?holat=...`): Bosh sahifadagi «Vazifani topshirish»
   tugmasi yoki bildirishnoma kerakli segmentga to'g'ridan-to'g'ri olib
   bora oladi, sahifa yangilanganda tanlov yo'qolmaydi. `router.replace`,
   `push` EMAS — segmentlar orasida yurish tarixni to'ldirmasin, aks holda
   "orqaga" O'quv sahifasiga uch-to'rt bosishda yetardi.
   ========================================================================== */

interface SegmentDef {
  key: AssignmentBucket
  /** `?holat=` qiymati — o'zbekcha, havolada o'qiladigan. */
  query: string
  label: string
  emptyIcon: IconName
  emptyTitle: string
  emptyText: string
}

const SEGMENTS: readonly SegmentDef[] = [
  {
    key: 'todo',
    query: 'topshirish',
    label: 'Topshirish kerak',
    emptyIcon: 'check-square',
    emptyTitle: 'Topshiriladigan vazifa yo‘q',
    emptyText: 'Hammasi yuborilgan. Yangi vazifalar darslar bilan birga shu yerda paydo bo‘ladi.',
  },
  {
    key: 'review',
    query: 'tekshiruvda',
    label: 'Tekshirilmoqda',
    emptyIcon: 'clock',
    emptyTitle: 'Tekshirilayotgan ish yo‘q',
    emptyText: 'Javob yuborganingizdan so‘ng u baho qo‘yilguncha shu yerda turadi.',
  },
  {
    key: 'graded',
    query: 'baholangan',
    label: 'Baholangan',
    emptyIcon: 'award',
    emptyTitle: 'Hali baholangan ish yo‘q',
    emptyText: 'Ishingiz tekshirilgach, ball va izoh shu yerda ko‘rinadi.',
  },
]

/** Sanani solishtirish uchun; `null` muddat — ro'yxat OXIRIGA. */
function dueTime(item: StudentAssignmentDto): number {
  return item.dueAt === null ? Number.POSITIVE_INFINITY : Date.parse(item.dueAt)
}

function submittedTime(item: StudentAssignmentDto): number {
  return item.mySubmission === null ? 0 : Date.parse(item.mySubmission.submittedAt)
}

/*
  TARTIB:
  • «Topshirish kerak» — avval HOZIR topshirsa bo'ladiganlar (qulflangan
    dars oxirida: uni baribir bosib bo'lmaydi), so'ng muddati ENG YAQIN
    birinchi. Muddati o'tganlar shu qoida bilan tepaga chiqadi — ular eng
    shoshilinch. Muddatsiz vazifa — oxirida.
  • «Tekshirilmoqda» va «Baholangan» — eng YANGI yuborilgan birinchi.
    ⚠️ Baholangan ishda "baho qo'yilgan vaqt" bo'yicha tartiblash to'g'riroq
    bo'lardi, lekin `StudentSubmissionDto` da `gradedAt` YO'Q — yuborilgan
    vaqt deyarli har doim AYNI tartibni beradi. Kerak bo'lsa backendga bitta
    maydon qo'shish kifoya.
  Teng holatda `id` — tartib har yangilanishda sakramasin.
*/
function compareRows(bucket: AssignmentBucket, left: Row, right: Row): number {
  if (bucket === 'todo') {
    const actionable = Number(right.item.canSubmit) - Number(left.item.canSubmit)
    if (actionable !== 0) return actionable
    const due = dueTime(left.item) - dueTime(right.item)
    if (due !== 0 && !Number.isNaN(due)) return due
    return left.item.id - right.item.id
  }
  const submitted = submittedTime(right.item) - submittedTime(left.item)
  return submitted !== 0 ? submitted : right.item.id - left.item.id
}

const rowsByBucket = computed<Record<AssignmentBucket, Row[]>>(() => {
  const groups: Record<AssignmentBucket, Row[]> = { todo: [], review: [], graded: [] }
  for (const row of rows.value) groups[row.bucket].push(row)
  for (const key of Object.keys(groups) as AssignmentBucket[]) {
    groups[key].sort((left, right) => compareRows(key, left, right))
  }
  return groups
})

const route = useRoute()
const router = useRouter()

/** URL'da ANIQ tanlangan segment (noto'g'ri qiymat — e'tiborsiz). */
const querySegment = computed<AssignmentBucket | null>(() => {
  const raw = route.query['holat']
  return SEGMENTS.find((segment) => segment.query === raw)?.key ?? null
})

/*
  SUKUT: birinchi BO'SH BO'LMAGAN segment, «Topshirish kerak» ustun.
  Hammasi topshirilgan o'quvchi bo'sh «Topshirish kerak» ni emas, o'z
  ishlarining holatini ko'radi.

  ★ Sukut REAKTIV: o'quvchi segment tanlamagan bo'lsa va oxirgi vazifani
  topshirsa, ko'rinish «Tekshirilmoqda» ga o'tadi — u aynan hozir yuborgan
  ishini ko'radi. Tanlagan bo'lsa (URL'da `holat` bor) — joyida qoladi va
  bo'sh holat matni chiqadi.
*/
const activeSegment = computed<AssignmentBucket>(() => {
  if (querySegment.value !== null) return querySegment.value
  return SEGMENTS.find((segment) => rowsByBucket.value[segment.key].length > 0)?.key ?? 'todo'
})

const activeDef = computed(() => SEGMENTS.find((segment) => segment.key === activeSegment.value))

const visibleRows = computed(() => rowsByBucket.value[activeSegment.value])

function selectSegment(key: AssignmentBucket): void {
  const query = SEGMENTS.find((segment) => segment.key === key)?.query
  if (query === undefined || route.query['holat'] === query) return
  void router.replace({ query: { ...route.query, holat: query } })
}

/*
  KLAVIATURA — WAI-ARIA "Tabs" naqshi: faqat FAOL tab `tabindex=0`
  (Tab tugmasi segmentlar ichida uch marta to'xtamaydi), ←/→ qo'shni tabga,
  Home/End chekkasiga o'tadi va uni DARHOL tanlaydi (avtomatik faollashtirish:
  segment almashishi arzon — so'rov yo'q, faqat filtr).
*/
const tabRefs = ref<HTMLButtonElement[]>([])

function handleTabKeydown(event: KeyboardEvent, index: number): void {
  const last = SEGMENTS.length - 1
  let next: number
  if (event.key === 'ArrowRight') next = index === last ? 0 : index + 1
  else if (event.key === 'ArrowLeft') next = index === 0 ? last : index - 1
  else if (event.key === 'Home') next = 0
  else if (event.key === 'End') next = last
  else return

  event.preventDefault()
  const target = SEGMENTS[next]
  if (target === undefined) return
  selectSegment(target.key)
  void nextTick(() => tabRefs.value[next]?.focus())
}

/**
 * Kattalashtirilgan rasm (R37: *"tekshirishda rasmni katta ekranda ko'rish
 * mumkin bo'lsin"*).
 *
 * ★ TALAB IKKI TOMONGA TEGISHLI: ustoz o'quvchining rasmini
 * (`GradeDialog`), o'quvchi esa ustoz qo'ygan tuzatish rasmini katta
 * ko'rishi kerak. Ikkinchisi ilgari UMUMAN yo'q edi.
 */
const zoomUrl = ref<string | null>(null)

const errorMessage = computed(() =>
  assignmentsQuery.error.value !== null ? toUserMessage(assignmentsQuery.error.value) : null,
)

const submitting = ref<StudentAssignmentDto | null>(null)

function handleSubmitted(): void {
  // Javob topshirilishi gating'ni ham o'zgartiradi (keyingi dars ochilishi
  // mumkin) — shuning uchun butun ro'yxat qayta so'raladi.
  void queryClient.invalidateQueries({ queryKey: ['assignments', 'mine'] })
}
</script>

<template>
  <!--
    ★ `@container` SAHIFA ILDIZIDA (ilgari ro'yxat `<div>` ida edi).

    Sabab TEXNIK: element O'ZINI so'rovga sola olmaydi — `@container` va
    `@sm:`/`@2xl:` bitta tugunda tursa, so'rov YUQORIDAGI konteynerga
    murojaat qiladi, u esa yo'q edi. Ro'yxat `<div>` i ildiz bilan bir xil
    kenglikda (ikkalasi ham `<main>` ustunini to'ldiradi), shuning uchun
    o'lchov qiymati o'zgarmaydi — faqat endi setka ham, kartochka ichki
    bo'shlig'i ham SHU konteynerdan o'qiydi.
  -->
  <div class="@container">
    <!--
      `PageHeader` o'rniga `StudentSubHeader`: bu sahifa endi "O'quv" tabining
      ichida yashaydi va o'quvchiga u yerga qaytish yo'li ko'rinib turishi
      kerak (Mini App karkasida "orqaga" tugmasi yo'q).
    -->
    <StudentSubHeader
      title="Vazifalarim"
      subtitle="Topshirish kerak bo‘lgan va baholangan ishlar"
    />

    <DataStatus
      :pending="assignmentsQuery.isPending.value"
      :error="errorMessage"
      :empty="rows.length === 0"
      :retrying="assignmentsQuery.isFetching.value"
      empty-icon="clipboard"
      empty-title="Vazifa yo‘q"
      empty-text="Hozircha vazifa yo‘q. Darslar davomida berilgan vazifalar shu yerda ko‘rinadi."
      @retry="assignmentsQuery.refetch()"
    >
      <!--
        ★ `@container` + `@sm:` — ichki bo'shliq EKRANGA emas, USTUN
        kengligiga qarab o'sadi. Ilgari `sm:p-4` edi: u oyna 640px dan
        kengaygandagina yoqilardi, holbuki kartochka 520px lik ustunda
        yashaydi va uning kengligi oynadan mustaqil. Ya'ni "ustun keng
        bo'lsa nafas kengroq" degan niyat oyna kengligiga bog'lab qo'yilgan
        edi — panel desktopda kengaysa ham natija to'g'ri bo'lishi uchun
        o'lchov endi konteynerniki.

        ★ 2026-08-13: `space-y-3` O'RNIGA SETKA. Karkas ustuni 1600px
        bo'lgach bitta ustundagi vazifa kartochkasi ~1536px ga cho'zilardi:
        chap chekkada bitta sarlavha, o'ng chekkada bitta tugma, orada bir
        metr bo'sh joy. Bo'shliqni `gap-3` beradi (avvalgi `space-y-3` bilan
        AYNAN bir xil 12px), ya'ni bitta ustunda ko'rinish o'zgarmaydi.

        Chegaralar kartochkaning matn hajmidan: bu yerda tavsif, ustoz
        izohi va blok sababi bor, ya'ni eng kam qulay kenglik ~320px —
        2 ustun uchun 42rem, 3 ustun uchun 64rem. TO'RTINCHI USTUN
        ATAYLAB YO'Q: 1536px da 3 ustun ~505px beradi va 12px lik izoh
        matni uchun bu qulay o'lchov; 4 ustunda (~375px) uzun izohlar
        kartochkani cho'zib, satrlar notekis bo'lib ketardi.

        ★ Telefon: 42rem = 672px, karkas ustuni esa `lg` gacha 520px bilan
        qulflangan — birorta so'rov yonmaydi, ro'yxat bitta ustun.
      -->
      <!--
        ★ SEGMENTLAR (2026-10-29, audit 2.5-a). Shakl `LessonDashboard`
        dagi Vazifa/Test/Savol-javob tablari bilan AYNI (bitta ilovada ikki
        xil segment boshqaruvi bo'lmasin), faqat bu yerda kenglik bo'ylab
        TENG uch ustun (`grid-cols-3`): telefonda 358px ga uchta yorliq
        sig'ishi kerak, `flex-1` esa kengroq so'zga ko'proq joy berib,
        tugmalarni notekis qilardi.

        ★ `@lg:w-fit` — keng ustunda tugmalar 1600px ga cho'zilib ketmasin.
        Telefonda (`@lg` = 32rem, ustun 520px dan torroq) to'liq kenglik.

        Hisoblagich alohida "tabletka" va `flex-wrap`: 390px da uchdan bir
        kenglikka «Tekshirilmoqda · 1» sig'maydi — tugma o'z kengligidan
        chiqib, qo'shnisini surardi (birinchi skrinshotda shunday bo'ldi).
        Endi sig'masa raqam yorliq OSTIGA tushadi, yorliq esa butun qoladi.
      -->
      <div
        class="mb-3 grid grid-cols-3 gap-1 rounded-2xl border border-line bg-ink-900 p-1 @lg:flex @lg:w-fit"
        role="tablist"
        aria-label="Vazifalar holati"
      >
        <button
          v-for="(segment, index) in SEGMENTS"
          :id="`assignments-tab-${segment.key}`"
          :key="segment.key"
          ref="tabRefs"
          type="button"
          role="tab"
          :aria-selected="activeSegment === segment.key"
          :aria-controls="`assignments-panel-${segment.key}`"
          :tabindex="activeSegment === segment.key ? 0 : -1"
          class="flex min-h-11 min-w-0 flex-wrap items-center justify-center gap-x-1.5 gap-y-0.5 rounded-xl px-1.5 py-1.5 text-center text-[12.5px] font-semibold leading-tight transition-colors @lg:px-4 @lg:text-[13px]"
          :class="
            activeSegment === segment.key
              ? 'bg-brand-500 text-on-brand'
              : 'text-slate-400 hover:bg-ink-800 hover:text-slate-100'
          "
          @click="selectSegment(segment.key)"
          @keydown="handleTabKeydown($event, index)"
        >
          <span v-text="segment.label" />
          <span
            class="shrink-0 rounded-full px-1.5 py-px text-[11px] font-bold tabular-nums"
            :class="activeSegment === segment.key ? 'bg-white/20' : 'bg-ink-800'"
          >
            <span class="sr-only">·</span>{{ rowsByBucket[segment.key].length }}
          </span>
        </button>
      </div>

      <div
        :id="`assignments-panel-${activeSegment}`"
        role="tabpanel"
        :aria-labelledby="`assignments-tab-${activeSegment}`"
      >
        <EmptyState
          v-if="visibleRows.length === 0 && activeDef !== undefined"
          :icon="activeDef.emptyIcon"
          :title="activeDef.emptyTitle"
          :text="activeDef.emptyText"
        />

        <div
          v-else
          class="grid gap-3 @2xl:grid-cols-2 @5xl:grid-cols-3"
        >
          <!--
          ★ HOVER FAQAT CHEGARADA: kartochkaning o'zi bosilmaydi (harakat
          "Topshirish" tugmasida), fon o'zgarishi esa bosilishga yolg'on
          va'da berardi.
        -->
          <article
            v-for="row in visibleRows"
            :key="row.item.id"
            class="flex flex-col rounded-xl border border-line bg-ink-900 p-3.5 transition-colors hover:border-line-strong @sm:p-4"
          >
            <div class="flex flex-wrap items-start justify-between gap-2">
              <h3
                class="min-w-0 flex-1 text-sm font-semibold text-slate-100"
                v-text="assignmentTitle(row.item.title, row.item.id)"
              />
              <BaseBadge :tone="row.state.tone">
                {{ row.state.label }}
              </BaseBadge>
            </div>

            <p
              v-if="row.item.description !== null && row.item.description.length > 0"
              class="mt-1.5 text-xs text-slate-400"
              v-text="row.item.description"
            />

            <dl class="mt-2.5 flex flex-wrap gap-x-4 gap-y-1.5 text-xs text-slate-400">
              <!--
              Dars yorlig'i «7-dars · …» (audit 2.5-a). Ochiq dars — havola:
              o'quvchi vazifani o'qib, videoni qayta ko'rmoqchi bo'ladi.
              `max-w-full` + `truncate`: uzun nom kartochkani kengaytirmasin.
            -->
              <div
                v-if="row.lesson !== null"
                class="inline-flex min-w-0 max-w-full items-center gap-1.5"
              >
                <AppIcon
                  name="book"
                  :size="13"
                  class="shrink-0"
                />
                <RouterLink
                  v-if="row.lesson.lessonId !== null"
                  :to="{ name: 'student-lesson', params: { lessonId: String(row.lesson.lessonId) } }"
                  class="truncate font-medium text-brand-500 underline-offset-2 hover:underline"
                >
                  {{ row.lesson.text }}
                </RouterLink>
                <span
                  v-else
                  class="truncate"
                  v-text="row.lesson.text"
                />
              </div>
              <div
                v-if="row.item.groupName !== null"
                class="inline-flex min-w-0 items-center gap-1.5"
              >
                <AppIcon
                  name="users"
                  :size="13"
                />
                <span
                  class="truncate"
                  v-text="row.item.groupName"
                />
              </div>
              <div
                v-if="row.item.dueAt !== null"
                class="inline-flex items-center gap-1.5"
              >
                <AppIcon
                  name="clock"
                  :size="13"
                />
                <span
                  class="tabular-nums"
                  v-text="formatDateTime(row.item.dueAt)"
                />
              </div>
              <div class="inline-flex items-center gap-1.5">
                <AppIcon
                  name="star"
                  :size="13"
                />
                <span class="tabular-nums">{{ row.item.maxScore }} ball</span>
              </div>
              <div
                v-if="row.formats.length > 0"
                class="text-dim"
              >
                Javob turi: {{ row.formats }}
              </div>
            </dl>

            <!-- Yuborilgan javobning qisqacha holati. -->
            <p
              v-if="row.item.mySubmission !== null"
              class="mt-2.5 flex flex-wrap items-center gap-x-3 gap-y-1 text-xs text-slate-400"
            >
              <span class="tabular-nums">
                {{ row.item.mySubmission.attemptNumber }}-urinish ·
                {{ formatDateTime(row.item.mySubmission.submittedAt) }}
              </span>
              <span
                v-if="row.fileCount > 0"
                class="inline-flex items-center gap-1.5"
              >
                <AppIcon
                  name="paperclip"
                  :size="13"
                />
                {{ row.fileCount }} ta fayl
              </span>
              <span
                v-if="row.item.mySubmission.isLate"
                class="text-amber-400"
              >kechikkan</span>
            </p>

            <!-- Nega topshira olmaslik sababi — qulflangan darsda ENG muhim ma'lumot. -->
            <p
              v-if="row.state.blockedReason !== null"
              class="mt-3 flex items-start gap-2 rounded-lg bg-ink-800 px-3 py-2 text-xs text-slate-300"
            >
              <AppIcon
                :name="row.item.lessonUnlocked ? 'alert' : 'lock'"
                :size="14"
                class="mt-px"
              />
              <span v-text="row.state.blockedReason" />
            </p>

            <!-- Ustoz qayta topshirishga ruxsat bergan bo'lsa — sababi ko'rsatiladi. -->
            <p
              v-if="(row.item.mySubmission?.resubmitNote ?? '').length > 0"
              class="mt-2 rounded-lg border border-amber-500/25 bg-amber-500/10 px-3 py-2 text-xs text-amber-200"
            >
              <span class="font-semibold">Qayta yuborish so‘raldi: </span>
              <span v-text="row.item.mySubmission?.resubmitNote" />
            </p>

            <p
              v-if="row.feedback !== null && row.feedback.length > 0"
              class="mt-2 rounded-lg border border-line bg-ink-950 px-3 py-2 text-xs text-slate-300"
            >
              <span class="font-semibold text-slate-200">Ustoz izohi: </span>
              <span v-text="row.feedback" />
            </p>

            <!--
            ★ R37 · USTOZ BIRIKTIRGAN FAYLLAR (tuzatilgan varaq, namuna
            talaffuz, PDF sharh). O'chirish tugmasi YO'Q — bu ustozning
            sharhi, o'quvchining javobi emas.
          -->
            <div
              v-if="row.feedbackFiles.length > 0"
              class="mt-2"
            >
              <h3 class="mb-1.5 text-[11px] font-bold uppercase tracking-wide text-slate-400">
                Ustoz biriktirgan fayllar
              </h3>
              <SubmissionFeedbackFiles
                :files="row.feedbackFiles"
                @zoom="(url) => (zoomUrl = url)"
              />
            </div>

            <!--
            ★ `flex flex-col` + `@2xl:mt-auto`: ko'p ustunli setkada bir
            satrdagi kartochkalar teng balandlikka cho'ziladi, tugma esa
            matn tugagan joyda "osilib" qolardi — endi u kartochka
            TAGIGA yopishadi va tugmalar qatori bir chiziqda turadi.

            `mt-auto` KONTEYNER SO'ROVI OSTIDA: bitta ustunda kartochka
            balandligi kontent bo'yicha, ya'ni bo'sh joy YO'Q va `mt-auto`
            0 ga aylanib, hozirgi 12px lik oraliqni yeb qo'yardi. Telefon
            yo'lida esa 42rem lik so'rov hech qachon yonmaydi.
          -->
            <div
              v-if="row.state.blockedReason === null"
              class="mt-3 flex justify-end @2xl:mt-auto @2xl:pt-3"
            >
              <!--
              ★ `tap-expand`: `size="sm"` 36px baland, WCAG 2.5.5 esa 44px
              so'raydi. `BaseButton` o'lchov xaritasi butun ilovaniki —
              uni surish har panelda joylashuvni siljitardi. Shuning uchun
              faqat bosiladigan maydon kengaytiriladi (36 + 2×6 = 48px),
              ko'rinish o'zgarmaydi.
            -->
              <BaseButton
                class="tap-expand"
                size="sm"
                @click="submitting = row.item"
              >
                <template #icon>
                  <AppIcon
                    name="send"
                    :size="14"
                  />
                </template>
                {{ row.item.mySubmission !== null ? 'Qayta yuborish' : 'Topshirish' }}
              </BaseButton>
            </div>
          </article>
        </div>
      </div>
    </DataStatus>

    <SubmitAssignmentDialog
      :assignment="submitting"
      @close="submitting = null"
      @submitted="handleSubmitted"
    />

    <!--
      Kattalashtirilgan rasm — `GradingQueueOverlay` va `GradeDialog` dagi
      AYNI naqsh (`BaseModal wide` + `max-h-[75dvh] object-contain`).
    -->
    <BaseModal
      :open="zoomUrl !== null"
      title="Rasm"
      wide
      @close="zoomUrl = null"
    >
      <img
        v-if="zoomUrl !== null"
        :src="zoomUrl"
        alt="Kattalashtirilgan rasm"
        class="mx-auto max-h-[75dvh] w-auto rounded-lg object-contain"
      >
    </BaseModal>
  </div>
</template>
