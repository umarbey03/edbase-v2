<script setup lang="ts">
import { useQuery } from '@tanstack/vue-query'
import { computed } from 'vue'

import { fetchAttendanceSummary } from '@/entities/progress'
import { fetchRecordingSection } from '@/entities/recording'
import { useStudentSchedule } from '@/features/student-schedule/model/useStudentSchedule'
import NextLessonCard from '@/features/student-schedule/ui/NextLessonCard.vue'
import { useNow } from '@/shared/lib/use-now'
import { AppIcon, BaseButton } from '@/shared/ui'

/**
 * BOSH SAHIFA — eski `#home` bo'limi.
 *
 * Ikki qism: "keyingi dars" kartochkalari (ustoz + kurator) va davomat doirasi.
 *
 * TELEFONDA ular ustma-ust, DESKTOPDA (≥1024px) yonma-yon ikki ustun —
 * tafsilot shablondagi setka izohida (`docs/MOSLASHUVCHANLIK.md` 6.3).
 *
 * DAVOMAT: `GET /api/v1/progress/attendance` dan `overall` chelagi olinadi —
 * eski ilovada ham doira BARCHA darslarni (ustoz + kurator) ko'rsatardi.
 * Server `teacher`/`assistant` ni alohida beradi, lekin ular reyting va
 * hisobotlar uchun; bosh sahifada bittasini ko'rsatish o'quvchini chalg'itardi.
 */
const now = useNow()
const schedule = useStudentSchedule(now)

const attendanceQuery = useQuery({
  queryKey: ['progress', 'attendance'],
  queryFn: ({ signal }) => fetchAttendanceSummary({}, { signal }),
})

const attendance = computed(() => attendanceQuery.data.value?.overall ?? null)

/** Doira uzunligi: 2 × π × 40 (eski `CIRC` o'zgaruvchisi). */
const RING_CIRCUMFERENCE = 251.3

/**
 * Doiraning to'ldirilmagan qismi. Ma'lumot kelmaguncha doira BO'SH turadi —
 * nol foizli to'la doira "hammasini qoldirgan" degan yolg'on taassurot berardi.
 */
const ringOffset = computed(() => {
  const percent = attendance.value?.percent ?? 0
  return RING_CIRCUMFERENCE * (1 - Math.min(100, Math.max(0, percent)) / 100)
})

/** Dars hali o'tilmagan bo'lsa foiz emas, chiziqcha ko'rsatiladi. */
const hasLessons = computed(() => (attendance.value?.total ?? 0) > 0)

function statValue(value: number | undefined): string {
  if (attendanceQuery.isPending.value) return '…'
  return value === undefined ? '—' : String(value)
}

/*
  ============================================================================
   "VAZIFA VA TESTLAR" — "O'quv" sahifasidan BU YERGA ko'chirildi
  ============================================================================

  Sabab: "Darslar" bosilganda endi to'liq Dars Dashboard ochiladi (video +
  shu darsning vazifasi/testi/chati bitta joyda) — o'quvchi ko'p hollarda
  vazifa/testga ALOHIDA emas, dars ORQALI keladi. Lekin GURUHGA bog'langan
  (biror darsga bog'liq bo'lmagan) vazifa/testlar ham bo'ladi
  (`Assignment.GroupId`/`Test.Kind=Competition`) — ularning boshqa kirish
  nuqtasi yo'q, shuning uchun umumiy ro'yxatga havola BUTUNLAY o'chirilmaydi,
  faqat shu yerga (Bosh sahifa) ko'chadi. Marshrutlar o'zgarmadi.
*/
const sectionQuery = useQuery({
  queryKey: ['recordings', 'section'],
  queryFn: ({ signal }) => fetchRecordingSection({ signal }),
})

const recordingsVisible = computed(() => sectionQuery.data.value?.visible ?? true)
</script>

<template>
  <!--
    ══════════════════════════════════════════════════════════════════════
     DESKTOP SETKASI (≥1024px) — `docs/MOSLASHUVCHANLIK.md` 6.3
    ══════════════════════════════════════════════════════════════════════

    `lg:grid-cols-[minmax(0,1fr)_360px]`: chapda keyingi dars kartochkalari,
    o'ngda davomat. Ilgari HAMMASI ustma-ust turardi va 1600px lik ustunda
    92px lik doira ikkita keng kartochka OSTIDA yolg'iz qolardi — bo'sh joyni
    kontent emas, havo to'ldirardi (loyiha egasi rad etgan holat).

    ★ `minmax(0,1fr)` — sof `1fr` EMAS: setka trekining eng kichik o'lchami
    sukut bo'yicha `auto`, ya'ni ichidagi `truncate` qilingan uzun dars nomi
    trekni CHO'ZIB yuborardi va o'ng ustun siqilardi.

    ★ `lg:items-start` IKKI VAZIFA bajaradi: (1) qisqaroq ustun cho'zilmaydi,
    (2) `lg:sticky` ISHLASHI uchun SHART — cho'zilgan setka elementi o'z
    setka maydoni bilan bir xil balandlikda bo'ladi va yopishishga joy
    qolmaydi. `start` da element kontent balandligida qoladi, yopishish esa
    setka MAYDONI (qator balandligi) ichida hisoblanadi.

    Telefonda bu klasslarning BIRORTASI qo'llanmaydi — ildiz `<div>` avvalgidek
    oddiy blok bo'lib qoladi (Telegram Mini App yo'li).
  -->
  <div class="lg:grid lg:grid-cols-[minmax(0,1fr)_360px] lg:items-start lg:gap-6">
    <!-- ======================= Chap ustun: keyingi dars ======================= -->
    <!--
      ★ Bu o'ram DESKTOP UCHUN qo'shildi, lekin telefonda ZARARSIZ: yagona
      klassi `lg:` ostida, ya'ni 1024px dan pastda bu shunchaki chegarasiz va
      to'ldirmasiz blok `<div>`. Shu sababli ichidagi `mb-4` va pastdagi
      sarlavhaning `mt-6` si AVVALGIDEK yig'ilib 24px beradi (margin collapse
      bo'sh o'ramdan o'tib ketadi). Bir piksel ham siljimaydi.

      `lg:min-w-0` — `minmax(0,1fr)` trekiga JUFT: element o'zi ham
      qisqarishga rozi bo'lmasa (`min-width:auto`) uzun sarlavha uni
      kengaytirib yuborardi.
    -->
    <div class="lg:min-w-0">
      <div
        v-if="schedule.isPending.value"
        class="mb-4 h-[190px] animate-pulse rounded-[18px] border border-line bg-ink-900"
      />

      <div
        v-else-if="schedule.error.value !== null"
        class="mb-4 rounded-xl border border-rose-500/25 bg-rose-500/10 px-5 py-6 text-center"
        role="alert"
      >
        <p
          class="text-sm text-rose-200"
          v-text="schedule.error.value"
        />
        <BaseButton
          class="mt-4"
          size="sm"
          variant="secondary"
          :loading="schedule.isFetching.value"
          @click="schedule.refetch()"
        >
          Qayta urinish
        </BaseButton>
      </div>

      <!-- Ikkala tur ham bo'sh bo'lsa — eski ilovadagi bitta umumiy matn. -->
      <div
        v-else-if="schedule.nextTeacher.value === null && schedule.nextAssistant.value === null"
        class="mb-4 rounded-xl border border-line bg-ink-900 px-2.5 py-8 text-center text-sm text-slate-400"
      >
        Rejalashtirilgan darslar yo‘q
      </div>

      <!--
        Eski `.herostack`: telefonda ustun, 560px dan keng ekranda yonma-yon.

        ★ `xs:` (560px) QATLAMIGA TEGILMADI — desktopda ham AYNAN shu qoida
        ishlaydi: chap ustun ~1150px, ya'ni ikkala kartochka yonma-yon
        qoladi. Yangi `lg:` qoidasi qo'shish shart emas edi.
      -->
      <div
        v-else
        class="mb-4 flex flex-col gap-3 xs:flex-row"
      >
        <NextLessonCard
          class="min-w-0 xs:flex-1"
          type="Teacher"
          :session="schedule.nextTeacher.value"
          :now="now"
        />
        <NextLessonCard
          class="min-w-0 xs:flex-1"
          type="Assistant"
          :session="schedule.nextAssistant.value"
          :now="now"
        />
      </div>
    </div>

    <!-- ========================= O'ng ustun: davomat ========================= -->
    <!--
      `lg:sticky lg:top-24` — chap ustun uzunroq bo'lsa (masalan jonli dars
      kartochkasi ochilganda) skrollda davomat KO'ZDAN YO'QOLMAYDI.
      `top-24` (96px) — yopishqoq appbar balandligidan (≈76px) yuqori, ya'ni
      ustun panel ostiga kirib ketmaydi.
    -->
    <div class="lg:sticky lg:top-24">
      <!-- ★ `lg:mt-0`: desktopda o'ng ustun chap ustun bilan BIR SATRDAN
           boshlansin (telefonda `mt-6` bo'limlar orasidagi bo'shliq bo'lib
           qoladi). -->
      <h2
        class="mb-3 ml-1 mt-6 flex items-center gap-[7px] text-xs font-bold uppercase tracking-[1.4px] text-slate-400 lg:mt-0"
      >
        <AppIcon
          name="chart"
          :size="15"
        />
        Davomat
      </h2>

      <section class="rounded-xl border border-line bg-ink-900 p-[18px] lg:p-5">
        <div class="flex items-center gap-5">
          <!--
            ★ DOIRA DESKTOPDA KATTALASHADI (92 -> 112px), lekin SVG ning
            ichki koordinatalari o'zgarmaydi: `width`/`height` atributlari
            o'rniga `viewBox` + `size-full` ishlatiladi, ya'ni bitta
            geometriya ikki o'lchamga xizmat qiladi. Telefonda o'ram hamon
            `size-[92px]` — chizma piksel-bapiksel avvalgidek.
          -->
          <div class="relative size-[92px] shrink-0 lg:size-[112px]">
            <svg
              viewBox="0 0 92 92"
              class="size-full -rotate-90"
              aria-hidden="true"
            >
              <!--
                🔴 Halqa YO'LI (qatnashilmagan qismi). `stroke-ink-800`
                (#f2f4f9) oq kartochkada 1.06:1 berardi — yo'l ko'rinmasdi va
                davomat DOIM 100% dek tuyulardi (ma'no buzilishi: 40% ham
                to'liq halqa bo'lib ko'rinardi).

                `ink-750` — `StudentLearnPage` dagi "Kurs davomi" halqasi
                allaqachon shunga o'tkazilgan, ikki ekran bir xil bo'lsin.
              -->
              <circle
                cx="46"
                cy="46"
                r="40"
                fill="none"
                stroke-width="9"
                class="stroke-ink-750"
              />
              <circle
                cx="46"
                cy="46"
                r="40"
                fill="none"
                stroke-width="9"
                stroke-linecap="round"
                class="stroke-green-500"
                :stroke-dasharray="RING_CIRCUMFERENCE"
                :stroke-dashoffset="ringOffset"
                style="transition: stroke-dashoffset .5s cubic-bezier(.4,0,.2,1)"
              />
            </svg>
            <div class="absolute inset-0 flex flex-col items-center justify-center">
              <!--
                Dars o'tilmagan bo'lsa ham FOIZ ko'rsatiladi (`0%`) — eski
                ilovada aynan shunday edi va o'quvchi shunga o'rgangan.
                Sababi esa pastdagi izohda aytiladi, ya'ni "0%" ayblov bo'lib
                tuyulmaydi.
              -->
              <b
                class="text-[21px] font-extrabold lg:text-[26px]"
                v-text="`${Math.round(attendance?.percent ?? 0)}%`"
              />
              <span class="text-[9px] text-slate-400 lg:text-[10px]">qatnashish</span>
            </div>
          </div>

          <!--
            ★ Statistika qatorlari desktopda bir pog'ona kattaroq: doira
            112px ga chiqqach 13px lik yozuv uning yonida "yo'qolib"
            ketardi. Kenglik yetadi — 360px ustunning ichki bo'shlig'i
            320px, eng uzun yorliq ("Qatnashmagan") 15px da ~110px.
          -->
          <dl class="flex-1">
            <div
              class="flex items-center justify-between border-b border-line py-1.5 text-[13px] lg:py-2 lg:text-sm"
            >
              <dt class="flex items-center gap-2 text-slate-400">
                <i
                  class="size-[9px] rounded-full bg-green-500"
                  aria-hidden="true"
                />
                Qatnashgan
              </dt>
              <dd
                class="text-[15px] font-bold tabular-nums lg:text-base"
                v-text="statValue(attendance?.attended)"
              />
            </div>
            <div
              class="flex items-center justify-between border-b border-line py-1.5 text-[13px] lg:py-2 lg:text-sm"
            >
              <dt class="flex items-center gap-2 text-slate-400">
                <i
                  class="size-[9px] rounded-full bg-red-500"
                  aria-hidden="true"
                />
                Qatnashmagan
              </dt>
              <dd
                class="text-[15px] font-bold tabular-nums lg:text-base"
                v-text="statValue(attendance?.missed)"
              />
            </div>
            <div class="flex items-center justify-between py-1.5 text-[13px] lg:py-2 lg:text-sm">
              <dt class="flex items-center gap-2 text-slate-400">
                <i
                  class="size-[9px] rounded-full bg-dim"
                  aria-hidden="true"
                />
                Jami o‘tgan
              </dt>
              <dd
                class="text-[15px] font-bold tabular-nums lg:text-base"
                v-text="statValue(attendance?.total)"
              />
            </div>
          </dl>
        </div>

        <!--
          Hali dars o'tilmagan bo'lsa sabab AYTILADI: nol foiz "hammasini
          qoldirgan" degan taassurot berardi.
        -->
        <p
          v-if="!hasLessons && !attendanceQuery.isPending.value"
          class="mt-3.5 border-t border-line pt-3 text-xs leading-relaxed text-slate-400"
        >
          Hali o‘tilgan dars yo‘q — birinchi darsdan keyin bu yerda foiz va
          sonlar paydo bo‘ladi.
        </p>
        <p
          v-else-if="(attendanceQuery.data.value?.streak ?? 0) > 1"
          class="mt-3.5 border-t border-line pt-3 text-xs text-brand-300"
        >
          Ketma-ket {{ attendanceQuery.data.value?.streak }} darsda qatnashdingiz.
        </p>
      </section>
    </div>
  </div>

  <!--
    ==================== Vazifa va testlar ("O'quv"dan ko'chirilgan) ====================
    Sabab va tafsilot — skriptdagi `recordingsVisible` izohi.
  -->
  <div class="mt-6">
    <h2
      class="mb-3 ml-1 flex items-center gap-[7px] text-xs font-bold uppercase tracking-[1.4px] text-slate-400"
    >
      <AppIcon
        name="clipboard"
        :size="15"
      />
      Vazifa va testlar
    </h2>

    <div class="grid grid-cols-1 gap-2.5 sm:grid-cols-3">
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

      <RouterLink
        v-if="recordingsVisible"
        :to="{ name: 'student-recordings' }"
        class="group flex min-h-11 items-center gap-3 rounded-[15px] border border-line bg-ink-900 p-[15px] transition-colors hover:border-line-strong hover:bg-ink-800"
      >
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
</template>
