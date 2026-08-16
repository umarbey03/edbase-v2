<script setup lang="ts">
import { computed, ref } from 'vue'

import { sessionStartState, sessionStatusLabel, sessionTypeLabel } from '@/entities/session'
import { isManagerRole } from '@/entities/user'
import { useAuthStore } from '@/features/auth/model/auth.store'
import { toUserMessage } from '@/shared/api'
import { formatWeekdayDateTime, formatTime, monthNameCapitalized } from '@/shared/lib/datetime'
import { useConfirm } from '@/shared/lib/useConfirm'
import { useNow } from '@/shared/lib/use-now'
import type { ScheduledSessionDto } from '@/shared/types'
import { BaseButton, BaseCard, DataStatus } from '@/shared/ui'

import type { CalendarDay, CalendarEventTone } from '../model/calendar'
import { buildMonthGrid, calendarEvent, TEACHER_WEEKDAYS } from '../model/calendar'
import { useGroupSchedule, useSessionCancel, useSessionStart } from '../model/use-group-schedule'

/**
 * "Darslar" tabi — eski `#tab-lessons` dagi oy kalendari.
 *
 * ★ ATAYLAB KO'CHIRILMAGAN IKKI BLOK (sabab: v2 da endpoint yo'q):
 *  1) "Kurs sur'ati" kartochkasi (`/course-progress` + `/taught`) — ustoz
 *     qaysi darsni o'tganini belgilar va o'quvchilarga keyingi video dars
 *     ochilardi. v2 da kurs gating'i bor (`LessonLockReason.TeacherPace`),
 *     lekin uni ustoz qo'lda siljitadigan endpoint yozilmagan.
 *  2) "Yordamchi dars qo'shish" formasi — v2 da darslar guruh JADVALIDAN
 *     avtomatik yaratiladi (`schedule/regenerate`), bitta dars qo'shish
 *     endpointi yo'q.
 */
const props = defineProps<{ groupId: number }>()

const now = useNow()
const auth = useAuthStore()
const confirm = useConfirm()
const scheduleQuery = useGroupSchedule(props.groupId)
const { start, openRoom, pendingId, error: actionError } = useSessionStart(props.groupId)
const {
  cancel: cancelSession,
  pendingId: cancelPendingId,
  error: cancelError,
} = useSessionCancel(props.groupId)

/** ★ FAQAT ACADEMIC/ADMIN (loyiha egasi: "qo'lda o'quv va admin bo'limi orqali"). */
const canCancel = computed(() => auth.role !== null && isManagerRole(auth.role))

/**
 * Bekor qilishdan OLDIN tasdiq — QAYTARIB BO'LMAYDIGAN amal: guruh jadvali
 * darhol qayta tuziladi va o'rnini bosuvchi dars kurs oxiriga qo'shiladi.
 */
async function askCancel(session: ScheduledSessionDto): Promise<void> {
  const ok = await confirm({
    title: 'Darsni bekor qilish',
    message: `${formatWeekdayDateTime(session.scheduledStart)} dagi dars bekor qilinadi.`,
    confirmLabel: 'Darsni bekor qilish',
    // ★ ATAYLAB O'ZGARTIRILDI ("Bekor qilish" standartidan): bu amalning
    // O'ZI "bekor qilish" bo'lgani uchun standart yorliq TASDIQ va RAD
    // tugmalarida BIR XIL matn hosil qilardi ("Bekor qilish" / "Bekor
    // qilish") — foydalanuvchi qaysi tugma NIMANI bekor qilishini
    // (dars — deb tasdiqlashni, yoki so'rovni — deb rad etishni)
    // ANIQLAY OLMASDI.
    cancelLabel: 'Yopish',
    tone: 'danger',
    details: [
      'Guruh jadvali avtomatik qayta tuziladi.',
      'O‘rnini bosuvchi dars kurs oxiriga qo‘shiladi — o‘quvchilar jami dars sonini yo‘qotmaydi.',
    ],
  })
  if (!ok) return

  cancelSession(session.id)
}

const sessions = computed(() => scheduleQuery.data.value ?? [])

const scheduleError = computed(() =>
  scheduleQuery.error.value !== null ? toUserMessage(scheduleQuery.error.value) : null,
)

/* ------------------------------------------------------------------- oy */

const anchor = ref(new Date())

const monthTitle = computed(
  () => `${monthNameCapitalized(anchor.value.getMonth())} ${anchor.value.getFullYear()}`,
)

const grid = computed(() => buildMonthGrid(anchor.value, sessions.value, now.value))

function moveMonth(delta: number): void {
  anchor.value = new Date(anchor.value.getFullYear(), anchor.value.getMonth() + delta, 1)
}

function goToday(): void {
  anchor.value = new Date()
}

/* ---------------------------------------------------------- tanlangan dars */

/**
 * ★ CHEKINISH: eski ilova darsni bosganda `toast()` yoki `confirm()`
 * ko'rsatardi. v2 xodim karkasida toast yo'q va `confirm()` — brauzer
 * modali (uslubsiz, telefonda qo'pol). Shuning uchun tanlangan dars
 * kalendar OSTIDA panel bo'lib ochiladi: bir xil ma'lumot, lekin
 * yo'qolib ketmaydi va amal tugmasi shu yerda.
 */
const selectedId = ref<number | null>(null)

const selected = computed<ScheduledSessionDto | null>(
  () => sessions.value.find((item) => item.id === selectedId.value) ?? null,
)

const selectedState = computed(() =>
  selected.value === null ? null : sessionStartState(selected.value, now.value),
)

/* ═══════════════════════════════════════════════════════════════════════
   R25 — "kalendar katagi darsda dars borligini ANIQROQ bildirsin"

   Bugungi holat: katak faqat OYGA TEGISHLILIK va BUGUN ekanini
   ko'rsatardi; darsning yagona belgisi — past kontrastli matnli pill.
   58px lik katakda darsli va darssiz kun deyarli bir xil ko'rinardi.

   🔴 ENG QATTIQ CHEKLOV: kenglik. `min-w-[520px]` va 7 ustun — yozma
   qaror (`MOSLASHUVCHANLIK.md`, 145-qator): setka HAQIQIY oy kalendari,
   ustunlar ma'no tashiydi. Shuning uchun "aniqroq" yechimlarning
   HAMMASI kenglik TALAB QILMAYDIGANLARIDAN tanlandi:

    1) KATAK SIRTI — darsli kun `ink-800` (kulrang), darssiz kun
       `ink-900` (oq). Bir qarashda "shu haftada qaysi kunlarda dars
       bor" ko'rinadi, 0px kenglik.
    2) CHAP CHEKKADAGI CHIZIQ (`border-l-[3px]`) — kunning ustun
       ohangi (jonli/o'tilmagan = qizil, o'tilgan = yashil, rejada =
       indigo). Chegara katakning O'ZIDA, ya'ni ichki joyni yemaydi.
    3) KUN RAQAMI darsli kunda to'q (`slate-100`), bo'sh kunda ochroq —
       raqamning o'zi ham signalga aylanadi.
    4) PILL'ga chegara qo'shildi: oq/kulrang sirtda 12–20% lik tint
       chekkasi ko'rinmasdi, endi tugma "tugma" bo'lib turadi.

   ★ Fon utility'lari BITTA funksiyada tanlanadi, shablonda ustma-ust
   qo'yilmaydi: `bg-ink-900` va `bg-brand-500/12` bir vaqtda berilsa
   qaysi biri g'olib bo'lishi CSS faylidagi tartibga bog'liq bo'lardi
   (sinf atributidagi tartibga EMAS).
   ═══════════════════════════════════════════════════════════════════════ */

/*
  ★ TUZATILDI (2026-08-14): `assistant` ILGARI `held` BILAN AYNI YASHIL EDI.

  Ular BOSHQA-BOSHQA holat: `held` — dars O'TILGAN (tugagan ish),
  `assistant` — kuratorning REJADAGI, hali bo'lmagan darsi. Legendaning
  yashil qatori esa "O'tilgan" deb yozilgan edi, ya'ni kalendarda yashil
  ko'ringan kurator darsi "o'tilgan" deb O'QILARDI — legenda ikkinchi
  holat uchun shunchaki YOLG'ON edi.

  Endi kurator darsi KO'K (`sky`): dizayn tizimida bu rang aynan
  "yordamchi rol, kurator" uchun ajratilgan (`style.css` dagi izoh), ya'ni
  yangi ma'no o'ylab topilmadi. Natijada rang ↔ ma'no MUNOSABATI BIRMA-BIR:
    brand  — ustozning rejadagi darsi
    sky    — kuratorning rejadagi darsi
    green  — o'tilgan
    rose   — o'tilmagan yoki hozir jonli
  va legendada TO'RTTALA qator ham bor.
*/
const TONE_CLASS: Record<CalendarEventTone, string> = {
  live: 'border-rose-500/45 bg-rose-500/20 text-rose-400 font-bold',
  held: 'border-green-500/35 bg-green-500/15 text-green-400',
  missed: 'border-rose-500/35 bg-rose-500/12 text-rose-400',
  teacher: 'border-brand-500/35 bg-brand-500/14 text-brand-500',
  assistant: 'border-sky-500/35 bg-sky-500/15 text-sky-400',
}

/** Katakning chap chekkasidagi chiziq — pill bilan BIR XIL rang oilasi. */
const ACCENT_CLASS: Record<CalendarEventTone, string> = {
  live: 'border-l-[3px] border-l-rose-500',
  held: 'border-l-[3px] border-l-green-500',
  missed: 'border-l-[3px] border-l-rose-500',
  teacher: 'border-l-[3px] border-l-brand-500',
  assistant: 'border-l-[3px] border-l-sky-500',
}

/**
 * Kunda bir nechta dars bo'lsa chekka chizig'i BITTA — qaysi ohang
 * ustun bo'lishi shu tartib bilan hal qilinadi.
 *
 * ★ Tartib "e'tibor talab qiladimi?" savoliga qarab tuzilgan: jonli dars
 * hozir ketyapti (eng shoshilinch), o'tilmagan dars — muammo,
 * o'tilgani — tugagan ish, qolgani — reja.
 */
const TONE_PRIORITY: readonly CalendarEventTone[] = [
  'live',
  'missed',
  'held',
  'teacher',
  'assistant',
]

interface CalendarEventView {
  session: ScheduledSessionDto
  label: string
  tone: CalendarEventTone
}

interface CalendarCell extends CalendarDay {
  events: CalendarEventView[]
  /** Katakning tayyor sinflari — shablonda shart qo'yilmaydi. */
  classes: string[]
  dayNumberClass: string
}

function cellClasses(day: CalendarDay, tone: CalendarEventTone | null): string[] {
  // Oldingi oy quyrug'i: darsi bo'lsa ham xira qoladi — u boshqa oyning ishi.
  if (!day.inMonth) return ['border-line', 'bg-ink-900', 'opacity-25']

  const classes: string[] = day.isToday
    ? ['border-brand-500', 'bg-brand-500/12']
    : tone === null
      ? ['border-line', 'bg-ink-900']
      : ['border-line-strong', 'bg-ink-800']

  if (tone !== null) classes.push(ACCENT_CLASS[tone])
  return classes
}

/*
  Bezash BIR MARTA, `computed` ichida: ilgari shablon har bir dars uchun
  `eventOf()` ni IKKI marta chaqirardi (sinf uchun va `title` uchun) va
  har qayta chizishda hammasi qaytadan hisoblanardi.
*/
const cells = computed<CalendarCell[]>(() =>
  grid.value.map((day) => {
    const events: CalendarEventView[] = day.sessions.map((session) => ({
      session,
      ...calendarEvent(session, now.value),
    }))

    let best = TONE_PRIORITY.length
    for (const event of events) {
      const index = TONE_PRIORITY.indexOf(event.tone)
      if (index >= 0 && index < best) best = index
    }
    const tone = TONE_PRIORITY[best] ?? null

    return {
      ...day,
      events,
      classes: cellClasses(day, tone),
      dayNumberClass: day.isToday
        ? 'text-brand-500'
        : events.length > 0
          ? 'text-slate-100'
          : 'text-slate-400',
    }
  }),
)
</script>

<template>
  <BaseCard flush>
    <div class="p-3.5 sm:p-5">
      <header class="mb-3.5 flex flex-wrap items-center justify-between gap-2.5">
        <h2 class="text-[15px] font-semibold">
          Darslar — <span v-text="monthTitle" />
        </h2>
        <!--
          Oy navigatsiyasi NATIV tugmalarda: `BaseButton` `aria-label` ni
          turlangan prop sifatida qabul qilmaydi, "‹"/"›" belgilarining
          o'zi esa ekran o'quvchisiga hech nima demaydi.
        -->
        <div class="flex items-center gap-1.5">
          <button
            type="button"
            class="tap-target rounded-lg text-slate-300 transition-colors hover:bg-ink-800 hover:text-slate-100"
            aria-label="Oldingi oy"
            @click="moveMonth(-1)"
          >
            ‹
          </button>
          <button
            type="button"
            class="tap-target rounded-lg px-3 text-xs font-semibold text-slate-300 transition-colors hover:bg-ink-800 hover:text-slate-100"
            @click="goToday"
          >
            Bugun
          </button>
          <button
            type="button"
            class="tap-target rounded-lg text-slate-300 transition-colors hover:bg-ink-800 hover:text-slate-100"
            aria-label="Keyingi oy"
            @click="moveMonth(1)"
          >
            ›
          </button>
        </div>
      </header>

      <p
        v-if="actionError !== null"
        class="mb-3 rounded-lg border border-rose-500/25 bg-rose-500/10 p-3 text-xs text-rose-200"
        role="alert"
        v-text="actionError"
      />
      <p
        v-if="cancelError !== null"
        class="mb-3 rounded-lg border border-rose-500/25 bg-rose-500/10 p-3 text-xs text-rose-200"
        role="alert"
        v-text="cancelError"
      />

      <DataStatus
        :pending="scheduleQuery.isPending.value"
        :error="scheduleError"
        :empty="sessions.length === 0"
        :retrying="scheduleQuery.isFetching.value"
        :skeleton-rows="3"
        empty-icon="calendar"
        empty-title="Hali darslar yo‘q"
        @retry="scheduleQuery.refetch()"
      >
        <!--
          ★ QAROR: 7 USTUN QOLADI, TELEFONDA GORIZONTAL SKROLL QO'SHILADI.

          Bu setka — HAQIQIY oy kalendari (`buildMonthGrid`): Du..Ya
          sarlavhalari va oyning har kuni. Ya'ni 7 ustun bezak emas,
          MA'NO: hafta kunlari ustma-ust turgani uchun ustoz "har seshanba
          dars bor" degan naqshni bir qarashda ko'radi. Ustunlar sonini
          kamaytirsak (masalan 4 ustun) qatorlar hafta bo'lishdan to'xtaydi
          va kalendar kalendar bo'lmay qoladi.

          Lekin o'quvchi kalendaridan (`StudentCalendarPage`) FARQI bor:
          u yerda katakda faqat kun raqami va nuqta bor — 40px da ham
          o'qiladi. Bu yerda katakda kun raqami + «Bugun» yorlig'i +
          dars tugmalari («09:00 Dars») bor. 375px ekranda katak atigi
          ~40px, padding'dan keyin ~28px — vaqt ham, «Bugun» ham sig'maydi.
          Shuning uchun setkaga eng kichik kenglik berilib, skroll SHU
          konteyner ichida qoldirildi (`scroll-x-safe` — `FinanceTrendCard`
          dagi bilan bir xil naqsh): matnlar ham, tartib ham, 7 ustun ham
          o'zgarmaydi, sahifaning o'zi esa yon skrollga tushmaydi.

          ★ 520px — tasodifiy son emas: shunda katak ~69px bo'ladi, ya'ni
          640px ekrandagi (sm) katak o'lchamiga teng. Kengroq ekranda
          setka joyga sig'adi va skroll umuman paydo bo'lmaydi.
        -->
        <div class="scroll-x-safe scrollbar-slim">
          <div class="grid min-w-[520px] grid-cols-7 gap-1.5">
            <p
              v-for="weekday in TEACHER_WEEKDAYS"
              :key="weekday"
              class="py-1 text-center text-[11px] font-semibold uppercase tracking-[0.4px] text-slate-400"
              v-text="weekday"
            />

            <!--
              Katak sinflari `cells` da tayyorlanadi (skriptdagi R25
              izohiga qarang): darsli kun sirti, chap chekkadagi ohang
              chizig'i va kun raqamining to'qligi — uchalasi ham
              kenglikni oshirmasdan "bu kunda dars bor" deydi.
            -->
            <div
              v-for="cell in cells"
              :key="cell.key"
              class="min-h-[58px] rounded-lg border p-1.5 sm:min-h-[85px]"
              :class="cell.classes"
            >
              <p class="flex items-center justify-between text-[11px] font-bold">
                <span
                  :class="cell.dayNumberClass"
                  v-text="cell.dayNumber"
                />
                <span
                  v-if="cell.isToday"
                  class="rounded-[10px] bg-brand-500 px-1.5 text-[9px] text-on-brand"
                >Bugun</span>
              </p>

              <button
                v-for="event in cell.events"
                :key="event.session.id"
                type="button"
                class="mt-1 block w-full truncate rounded-md border px-1.5 py-0.5 text-left text-[11px] font-medium"
                :class="[
                  TONE_CLASS[event.tone],
                  event.session.id === selectedId ? 'ring-1 ring-brand-500' : '',
                ]"
                :title="`${event.session.title ?? sessionTypeLabel(event.session.type)} — ${event.label}`"
                @click="selectedId = event.session.id"
              >
                {{ formatTime(event.session.scheduledStart) }} {{ event.label }}
              </button>
            </div>
          </div>
        </div>

        <!--
          Eski `.legend` — kalendar ranglarining ma'nosi.

          ★ TUZATILDI: legenda NUQTA chizardi, kataklar esa to'ldirilgan
          PILL — bir xil ma'noning ikki xil alifbosi (reja hujjatining
          "yo'l-yo'lakay topilgan xatolar" ro'yxatida ham qayd etilgan).
          Endi namuna katakdagi pill'ning AYNAN o'zi: bir xil fon, bir
          xil chegara, bir xil radius.

          ★ 2026-08-14: "Rejadagi dars" IKKIGA bo'lindi (ustoz va kurator)
          — sabab skriptdagi `TONE_CLASS` izohida. Ilgari kurator darsi
          legendada UMUMAN yo'q edi va yashil rangi tufayli "o'tilgan"
          deb o'qilardi.
        -->
        <div
          class="mt-3.5 flex flex-wrap gap-4 rounded-lg border border-line bg-ink-950 px-4 py-3 text-xs text-slate-400"
        >
          <span class="inline-flex items-center gap-1.5">
            <span
              class="h-3.5 w-6 shrink-0 rounded-md border"
              :class="TONE_CLASS.teacher"
              aria-hidden="true"
            />Ustoz darsi (rejada)
          </span>
          <span class="inline-flex items-center gap-1.5">
            <span
              class="h-3.5 w-6 shrink-0 rounded-md border"
              :class="TONE_CLASS.assistant"
              aria-hidden="true"
            />Kurator darsi (rejada)
          </span>
          <span class="inline-flex items-center gap-1.5">
            <span
              class="h-3.5 w-6 shrink-0 rounded-md border"
              :class="TONE_CLASS.held"
              aria-hidden="true"
            />O‘tilgan
          </span>
          <span class="inline-flex items-center gap-1.5">
            <span
              class="h-3.5 w-6 shrink-0 rounded-md border"
              :class="TONE_CLASS.missed"
              aria-hidden="true"
            />O‘tilmagan / jonli
          </span>
        </div>

        <!-- ===================== Tanlangan dars paneli ===================== -->
        <div
          v-if="selected !== null"
          class="mt-3.5 flex flex-wrap items-center justify-between gap-3 rounded-lg border border-line bg-ink-950 p-3.5"
        >
          <div class="min-w-0">
            <p
              class="truncate text-sm font-semibold text-slate-100"
              v-text="selected.title ?? sessionTypeLabel(selected.type)"
            />
            <p class="mt-0.5 text-xs text-slate-400">
              {{ sessionTypeLabel(selected.type) }} ·
              {{ formatWeekdayDateTime(selected.scheduledStart) }} ·
              {{ sessionStatusLabel(selected.status) }}
            </p>
          </div>
          <div class="flex items-center gap-2">
            <BaseButton
              v-if="selectedState?.kind === 'live'"
              size="sm"
              variant="success"
              @click="openRoom(selected.id)"
            >
              Darsga qaytish
            </BaseButton>
            <BaseButton
              v-else-if="selectedState?.kind === 'ready'"
              size="sm"
              :loading="pendingId === selected.id"
              @click="start(selected.id)"
            >
              Darsni boshlash
            </BaseButton>
            <span
              v-else-if="selectedState?.kind === 'wait'"
              class="text-xs text-slate-400"
            >
              Dars boshlanishiga vaqt bor — {{ selectedState.text }} qoldi
            </span>

            <!--
              "Bekor qilish" — dars HALI BOSHLANMAGAN (`Scheduled`) bo'lsa
              va rol Academic/Admin bo'lsa, `selectedState.kind` qanday
              bo'lishidan qat'i nazar ko'rinadi (masalan uzoq kelajakdagi
              dars uchun `selectedState` `null` bo'ladi).
            -->
            <BaseButton
              v-if="canCancel && selected.status === 'Scheduled'"
              size="sm"
              variant="danger"
              :loading="cancelPendingId === selected.id"
              @click="askCancel(selected)"
            >
              Bekor qilish
            </BaseButton>
          </div>
        </div>
      </DataStatus>
    </div>
  </BaseCard>
</template>
