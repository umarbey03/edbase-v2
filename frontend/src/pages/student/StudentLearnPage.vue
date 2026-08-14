<script setup lang="ts">
import { useQuery } from '@tanstack/vue-query'
import { computed, ref, watch } from 'vue'

import { fetchRecordingSection } from '@/entities/recording'
import {
  countsTowardProgress,
  useStudentCourse,
} from '@/features/student-course/model/useStudentCourse'
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

/* ==========================================================================
   R5 — "DARS YOZUVLARI" KIRISH KARTOCHKASI DINAMIK
   ========================================================================== */

/**
 * Loyiha egasi: *"dars yozuvlari qismi student uchun dynamic bo'lishi
 * kerak … ko'rinish yoki ko'rinmasligi, entire part of records"*.
 *
 * 🔴 KARTOCHKA HAM YASHIRILISHI SHART, FAQAT RO'YXAT EMAS. Bo'lim
 * yopilganda kartochka qolsa, o'quvchi uni bosib ABADIY BO'SH sahifaga
 * tushardi va buni ilovaning nosozligi deb o'ylardi — ya'ni sozlamani
 * yoqqan xodim o'zi bilmagan holda "buzuq" ekran yasagan bo'lardi.
 *
 * ★ NEGA ALOHIDA SO'ROV, RO'YXATNI O'QIB KO'RISH EMAS: bo'sh ro'yxat
 * IKKI xil ma'noga ega — "yopilgan" va "hali yozuv yo'q". Ikkinchisida
 * kartochka QOLISHI kerak (ertaga yozuv paydo bo'ladi), birinchisida esa
 * yo'q. Ro'yxat bu ikkisini ajrata olmaydi, bu endpoint esa aynan shu
 * savolga javob beradi.
 *
 * ⚠️ XATO BO'LSA KARTOCHKA KO'RSATILADI (`?? true`): tarmoq nosozligi
 * o'quvchidan bo'limni olib qo'ymasin. Eng yomon holatda u bo'sh sahifa
 * ko'radi — bu kartochkaning "sababsiz yo'qolishi" dan ancha yaxshi.
 */
const sectionQuery = useQuery({
  queryKey: ['recordings', 'section'],
  queryFn: ({ signal }) => fetchRecordingSection({ signal }),
})

const recordingsVisible = computed(() => sectionQuery.data.value?.visible ?? true)

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
  () => RING_CIRCUMFERENCE * (1 - course.progressPercent.value / 100),
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

/*
  MODUL HISOBLAGICHI ham kurs doirasi bilan AYNI qoidaga bo'ysunadi
  (`countsTowardProgress`): surat — tugatilgan darslar, maxraj — o'quv
  rejasidagi darslar. Aks holda bitta ekranda ikki xil arifmetika bo'lardi:
  doira 12/20, modullar esa jami 12/28 ko'rsatardi.

  ★ To'liq `BeforeGroupStart` moduldan "0/0" chiqadi — bu HALOL: guruh bu
    modulni umuman o'tmaydi.
*/
function doneCount(lessons: CourseLessonDto[]): number {
  return lessons.filter((lesson) => countsTowardProgress(lesson) && lesson.completed).length
}

function plannedCount(lessons: CourseLessonDto[]): number {
  return lessons.filter(countsTowardProgress).length
}
</script>

<template>
  <!--
    ================= DESKTOP SETKASI (≥1024px) =================
    `docs/MOSLASHUVCHANLIK.md` 6.3-jadval: `lg:grid-cols-[minmax(0,1fr)_320px]`.
    Chapda modullar akkordeoni va "ilon izi" dars yo'lakchasi, o'ngda 320px
    lik yon reyka — kurs jarayoni doirasi va vazifa/test/yozuv havolalari.

    NEGA: ilgari doira sahifaning ENG TEPASIDA, havolalar esa ENG PASTIDA
    turardi. 1600px lik ustunda ular orasida modullar ro'yxati cho'zilib,
    ikkalasi bir-biriga aloqasiz "yetim" blok bo'lib ko'rinardi.

    ★ NEGA HAR BIR BLOK ALOHIDA SETKA KATAGI, bitta `<aside>` ichida EMAS:
    telefondagi tartib AYNAN shu qolishi shart (doira -> modullar ->
    havolalar), ya'ni DOM'da modullar doira bilan havolalar ORASIDA turadi.
    Ularni bitta o'ramga yig'sak telefon tartibi buzilardi. Shuning uchun
    katak ANIQ ko'rsatiladi (`lg:col-start-*` / `lg:row-start-*`), DOM
    tartibi esa tegilmaydi — telefonda bu klasslarning hech biri
    qo'llanmaydi va sahifa oddiy blok oqimi bo'lib qolaveradi.

    ★ `lg:items-start` SHART: sukut bo'yicha setka bandi satr balandligiga
    cho'ziladi, cho'zilgan bandda esa `sticky` ga siljish joyi qolmaydi.
  -->
  <div class="lg:grid lg:grid-cols-[minmax(0,1fr)_320px] lg:items-start lg:gap-x-8">
    <!--
      ★ `lg:self-center`: o'ng katakda 62px lik doira kartochkasi turadi,
      chapda esa bitta kichkina yorliq — tepaga yopishtirilsa u ~70px bo'sh
      joy ustida osilib qolardi. Markazga tenglashtirilsa yorliq shu satrning
      sarlavhasi bo'lib o'qiladi.
    -->
    <h2
      class="mb-3 ml-1 mt-2 flex items-center gap-[7px] text-xs font-bold uppercase tracking-[1.4px] text-slate-400 lg:col-start-1 lg:row-start-1 lg:self-center"
    >
      <AppIcon
        name="book"
        :size="15"
      />
      O‘quv
    </h2>

    <!--
      Holat bloklari (yuklanish / xato / kurs yo'q) CHAP ustunga tushadi:
      o'ng reykadagi havolalar ular bilan birga YO'QOLMAYDI — vazifa va
      testlar kurs daraxtidan mustaqil ro'yxatlar, kurs kelmasa ham ochiladi.
    -->
    <div
      v-if="course.isPending.value"
      class="space-y-3 lg:col-start-1 lg:row-start-2"
    >
      <div
        v-for="index in 3"
        :key="index"
        class="h-24 animate-pulse rounded-xl border border-line bg-ink-900"
      />
    </div>

    <div
      v-else-if="course.error.value !== null"
      class="rounded-xl border border-rose-500/25 bg-rose-500/10 px-5 py-6 text-center lg:col-start-1 lg:row-start-2"
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
      class="rounded-xl border border-line bg-ink-900 px-[18px] py-[26px] text-center lg:col-start-1 lg:row-start-2"
    >
      <p class="text-[15px] font-bold">
        Kurs hali biriktirilmagan
      </p>
      <p class="mt-[7px] text-[13px] leading-relaxed text-slate-400">
        Guruhingizga video kurs ulanmagan. O‘quv bo‘limi ulagach shu yerda paydo bo‘ladi.
      </p>
    </div>

    <template v-else>
      <!--
        Kurs jarayoni (eski `.c-hero`) — desktopda o'ng reykaning boshi
        (`lg:col-start-2 lg:row-start-1`).
      -->
      <section
        class="mb-3.5 mt-3 flex items-center gap-4 rounded-[18px] border border-line bg-ink-900 p-4 lg:col-start-2 lg:row-start-1 lg:mt-0"
      >
        <div class="relative size-[62px] shrink-0">
          <svg
            width="62"
            height="62"
            viewBox="0 0 62 62"
            class="-rotate-90"
            aria-hidden="true"
          >
            <!--
              Halqa YO'LI (to'lmagan qismi). Ilgari `rgb(255 255 255 / .1)`
              QOTIB QOLGAN edi — oq kartochkada u UMUMAN ko'rinmasdi
              (oq ustiga 10% oq), ya'ni foiz halqasining "qolgan qismi"
              yo'qolib, jarayon 100% dek tuyulardi.
            -->
            <circle
              cx="31"
              cy="31"
              r="26"
              fill="none"
              class="stroke-ink-750"
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
            {{ course.progressPercent.value }}%
          </i>
        </div>

        <div class="min-w-0 flex-1">
          <p class="text-[17px] font-extrabold leading-tight">
            Kurs davomi
          </p>
          <!--
            ★ ESKI ILOVA MATNI QAYTARILDI (2026-08-13, R9).

            Bu yerda ilgari "N / M dars ochilgan" turardi va izohda "server
            darsning TUGATILGANINI bermaydi" deb yozilgan edi. IZOH NOTO'G'RI
            EDI: `CourseLessonDto.completed` WAVE 2 dan beri keladi, faqat
            frontend tipida maydon yo'q edi. Endi hisob AYNAN eski
            ilovadagidek — "N / M dars tugatilgan"
            (`DIZAYN_KOCHIRISH_REJASI.md` 8-bo'lim, 2-band: matn eski ilova
            bilan bir xil bo'lishi shart, "ochilgan" esa CHEKINISH edi).

            ★ MAXRAJ `lessonCount` EMAS, `plannedCount`: guruh boshlamagan
              qismdagi darslar (`BeforeGroupStart`) hech qachon o'tilmaydi va
              maxrajda qolsa progress abadiy qotib qolardi.
          -->
          <p class="mt-1 text-[12.5px] text-slate-400">
            {{ course.completedCount.value }} / {{ course.plannedCount.value }} dars tugatilgan
          </p>
          <!-- Jarayon chizig'i yo'li: `bg-white/10` oq sirtda ko'rinmaydi. -->
          <div class="mt-2 h-[5px] overflow-hidden rounded-sm bg-ink-750">
            <span
              class="block h-full rounded-sm bg-brand-500 transition-[width] duration-500"
              :style="{ width: `${course.progressPercent.value}%` }"
            />
          </div>
        </div>
      </section>

      <!--
        Modullar (eski `.c-mod`) — desktopda CHAP ustun.

        ★ O'RAM `<div>` QO'SHILDI (telefonga ta'sirsiz): setkada modullar
        ro'yxati BITTA band bo'lishi kerak, aks holda har bir modul o'z
        satrini olib, o'ng reyka ular orasiga tarqab ketardi. Telefonda bu
        o'ram chegarasiz/to'ldirishsiz oddiy blok — oxirgi modulning
        `mb-2.5` si avvalgidek quyidagi sarlavhaning `mt-6` si bilan
        yig'iladi (24px), ya'ni bo'shliqlar bir piksel ham o'zgarmaydi.
      -->
      <div class="lg:col-start-1 lg:row-start-2">
        <section
          v-for="(module, moduleIndex) in course.modules.value"
          :key="module.id"
          class="mb-2.5 overflow-hidden rounded-xl border border-line bg-ink-900"
        >
          <!--
            `hover:bg-ink-800` — akkordeon sarlavhasi bosiladigan element,
            desktopda sichqoncha ostida buni bildirishi kerak. Tailwind v4
            `hover:` ni `@media (hover: hover)` ga o'raydi, shuning uchun
            teginishli ekranda holat "yopishib" qolmaydi.
          -->
          <button
            type="button"
            class="flex min-h-11 w-full select-none items-center gap-3 px-[15px] py-3.5 text-left transition-colors hover:bg-ink-800"
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
              <!-- Matn kurs doirasi bilan bir xil o'lchovda: "tugatilgan". -->
              <span class="mt-0.5 block text-[11.5px] font-medium text-slate-400">
                {{ doneCount(module.lessons ?? []) }}/{{ plannedCount(module.lessons ?? []) }} dars tugatilgan
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
      </div>
    </template>

    <!--
      ==================== Vazifalar va testlar ====================
      Desktopda o'ng reykaning pastki qismi (`lg:col-start-2 lg:row-start-2`).

      ★ `lg:top-24` — appbar `sticky top-0` va desktopda 76px baland
      (pt-6 24 + avatar 40 + pb-3 12), ustiga 20px nafas. Modullar ro'yxati
      uzun bo'lsa ham havolalar ekranda qolib turadi.

      ★ DOIRA REYKAGA QO'SHILMAYDI (u satr 1 da): telefonda doira modullar
      ustida turishi shart, ya'ni uni shu o'ramga kiritsak DOM tartibi
      buzilardi. Skrollda doira tepaga chiqib ketishi — ONGLI chekinish:
      foizni o'quvchi bir marta o'qiydi, havolalarga esa qayta-qayta
      qaytadi.

      ★ `lg:mt-0` sarlavhada: setka bandi mustaqil formatlash konteksti
      yaratadi, ya'ni ichkaridagi `mt-6` tashqariga "chiqib" yig'ilmaydi va
      reyka chapdagi birinchi modul kartochkasidan 24px pastga siljib
      qolardi. Telefonda `mt-6` avvalgidek ishlaydi.
    -->
    <div class="lg:col-start-2 lg:row-start-2 lg:sticky lg:top-24">
      <h2
        class="mb-3 ml-1 mt-6 flex items-center gap-[7px] text-xs font-bold uppercase tracking-[1.4px] text-slate-400 lg:mt-0"
      >
        <AppIcon
          name="clipboard"
          :size="15"
        />
        Vazifa va testlar
      </h2>

      <!--
        ★ INTERAKTIVLIK (`MOSLASHUVCHANLIK.md` 6.5): uchala qator ham
        BOSILADIGAN havola, shuning uchun ular to'liq holat oladi — fon,
        chegara va o'ngdagi burchak ("qadam tashlaydi"). Kartochkalardan
        farqi shu: bu yerda butun sirt bosiladi.
      -->
      <div class="flex flex-col gap-2.5">
        <RouterLink
          :to="{ name: 'student-assignments' }"
          class="group flex min-h-11 items-center gap-3 rounded-[15px] border border-line bg-ink-900 p-[15px] transition-colors hover:border-line-strong hover:bg-ink-800"
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
            class="shrink-0 text-dim transition-transform group-hover:translate-x-0.5"
          />
        </RouterLink>

        <RouterLink
          :to="{ name: 'student-tests' }"
          class="group flex min-h-11 items-center gap-3 rounded-[15px] border border-line bg-ink-900 p-[15px] transition-colors hover:border-line-strong hover:bg-ink-800"
        >
          <!--
            Pastel nishon: tint + to'q ikonka. Ilgari `rgb(34 211 238 / .17)`
            fon va `#67e8f9` ikonka QOTIB QOLGAN edi (yorug' firuza qorong'i
            fon uchun) — oq sirtda u 1.6:1 berardi.
          -->
          <span
            class="flex size-[42px] shrink-0 items-center justify-center rounded-xl bg-cyan-500/12 text-cyan-300"
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
            class="shrink-0 text-dim transition-transform group-hover:translate-x-0.5"
          />
        </RouterLink>

        <!--
          ★ "Dars yozuvlari" — eski `student.html` da yozuvlar hisoblagichi
          (`learn-rec-meta`) AYNAN shu "O'quv" ekranida turgan, shuning uchun
          kirish nuqtasi ham shu yerda. Pastki 5 tab TEGILMAGAN.
        -->
        <RouterLink
          v-if="recordingsVisible"
          :to="{ name: 'student-recordings' }"
          class="group flex min-h-11 items-center gap-3 rounded-[15px] border border-line bg-ink-900 p-[15px] transition-colors hover:border-line-strong hover:bg-ink-800"
        >
          <!-- Pastel nishon (ilgari `rgb(139 92 246 / .18)` + `#c4b5fd`). -->
          <span
            class="flex size-[42px] shrink-0 items-center justify-center rounded-xl bg-violet-500/12 text-violet-200"
            aria-hidden="true"
          >
            <AppIcon
              name="camera"
              :size="20"
            />
          </span>
          <span class="min-w-0 flex-1">
            <b class="block text-base">Dars yozuvlari</b>
            <span class="block text-xs text-dim">O‘tilgan darslarni qayta ko‘rish</span>
          </span>
          <AppIcon
            name="chevron-right"
            :size="20"
            class="shrink-0 text-dim transition-transform group-hover:translate-x-0.5"
          />
        </RouterLink>
      </div>
    </div>

    <LessonSheet
      :lesson="selectedLesson"
      :module-name="selectedModuleName"
      @close="selectedLesson = null"
    />
  </div>
</template>
