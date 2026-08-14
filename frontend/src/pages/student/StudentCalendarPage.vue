<script setup lang="ts">
import { computed, ref } from 'vue'
import { useRouter } from 'vue-router'

import { sessionTitle } from '@/entities/session'
import {
  sessionState,
  useStudentSchedule,
} from '@/features/student-schedule/model/useStudentSchedule'
import { formatTime, monthNameCapitalized, WEEKDAY_HEADERS_UZ } from '@/shared/lib/datetime'
import { useNow } from '@/shared/lib/use-now'
import type { LiveSessionDto } from '@/shared/types'
import { AppIcon, BaseBadge, BaseButton } from '@/shared/ui'

/**
 * KALENDAR — eski `#calendar` bo'limi.
 *
 * Tuzilishi eski ilovadan aynan: guruh chiplari -> oy setkasi -> tanlangan
 * kun darslari. Hafta YAKSHANBADAN boshlanadi (eski `WD` massivi).
 *
 * ★ SERVER CHEGARASI: `GET /api/v1/live-sessions` faqat `scheduledEnd >=
 *   hozir - 6 soat` bo'lgan darslarni beradi va boshqa endpoint yo'q
 *   (`/groups/{id}/schedule` o'quvchiga 403). Ya'ni O'TGAN OYLAR bo'sh
 *   ko'rinadi. Buni yashirmaymiz — o'tgan oyga o'tilganda sabab yoziladi,
 *   aks holda o'quvchi "darslarim yo'qolibdi" deb o'ylardi.
 */
const now = useNow()
const router = useRouter()
const schedule = useStudentSchedule(now)

/** `null` — "Barchasi". */
const selectedGroupId = ref<number | null>(null)

const today = computed(() => {
  const value = new Date(now.value)
  value.setHours(0, 0, 0, 0)
  return value
})

/** Ko'rsatilayotgan oyning birinchi kuni. */
const viewMonth = ref(new Date(new Date().getFullYear(), new Date().getMonth(), 1))

/** Tanlangan kun; boshida — bugun. */
const selectedDay = ref(new Date(new Date().setHours(0, 0, 0, 0)))

const visibleSessions = computed(() =>
  selectedGroupId.value === null
    ? schedule.sessions.value
    : schedule.sessions.value.filter((item) => item.groupId === selectedGroupId.value),
)

/** `2026-6-30` -> shu kundagi darslar. Kalendar setkasi shu xaritadan o'qiydi. */
const sessionsByDay = computed(() => {
  const map = new Map<string, LiveSessionDto[]>()
  for (const item of visibleSessions.value) {
    const date = new Date(item.scheduledStart)
    if (Number.isNaN(date.getTime())) continue
    const key = `${date.getFullYear()}-${date.getMonth()}-${date.getDate()}`
    const bucket = map.get(key)
    if (bucket === undefined) map.set(key, [item])
    else bucket.push(item)
  }
  return map
})

interface CalendarCell {
  key: string
  day: number
  isToday: boolean
  isSelected: boolean
  /** Kun ostidagi nuqtalar: dars turlari (takrorsiz, ko'pi bilan 3 ta). */
  marks: string[]
}

const monthLabel = computed(
  () => `${monthNameCapitalized(viewMonth.value.getMonth())} ${viewMonth.value.getFullYear()}`,
)

/** Ko'rsatilayotgan oy butunlay o'tib ketganmi (server ma'lumot bermaydi). */
const isPastMonth = computed(() => {
  const current = new Date(now.value.getFullYear(), now.value.getMonth(), 1)
  return viewMonth.value.getTime() < current.getTime()
})

const cells = computed<CalendarCell[]>(() => {
  const year = viewMonth.value.getFullYear()
  const month = viewMonth.value.getMonth()
  const leading = new Date(year, month, 1).getDay()
  const dayCount = new Date(year, month + 1, 0).getDate()

  const result: CalendarCell[] = []
  for (let index = 0; index < leading; index += 1) {
    result.push({ key: `empty-${index}`, day: 0, isToday: false, isSelected: false, marks: [] })
  }
  for (let day = 1; day <= dayCount; day += 1) {
    const key = `${year}-${month}-${day}`
    const daySessions = sessionsByDay.value.get(key) ?? []
    const cellDate = new Date(year, month, day)
    result.push({
      key,
      day,
      isToday: cellDate.getTime() === today.value.getTime(),
      isSelected: cellDate.getTime() === selectedDay.value.getTime(),
      marks: [...new Set(daySessions.map((item) => item.type))].slice(0, 3),
    })
  }
  return result
})

const selectedLabel = computed(
  () => `${selectedDay.value.getDate()}-${monthNameCapitalized(selectedDay.value.getMonth())}`,
)

const selectedSessions = computed(() => {
  const key = `${selectedDay.value.getFullYear()}-${selectedDay.value.getMonth()}-${selectedDay.value.getDate()}`
  return [...(sessionsByDay.value.get(key) ?? [])].sort(
    (a, b) => new Date(a.scheduledStart).getTime() - new Date(b.scheduledStart).getTime(),
  )
})

function moveMonth(delta: number): void {
  const next = new Date(viewMonth.value)
  next.setMonth(next.getMonth() + delta)
  viewMonth.value = next
}

function selectDay(cell: CalendarCell): void {
  if (cell.day === 0) return
  selectedDay.value = new Date(
    viewMonth.value.getFullYear(),
    viewMonth.value.getMonth(),
    cell.day,
  )
}

function open(sessionId: number): void {
  void router.push({ name: 'live-room', params: { sessionId: String(sessionId) } })
}
</script>

<template>
  <div>
    <!-- ====================== Guruh tanlash (eski `.gsel`) ================== -->
    <!--
      ★ CHIP LENTASI "YOZUVLAR" SAHIFASIDAGI NAQSHGA KELTIRILDI (2026-08-13).

      Ikki farq bor edi: (1) `overflow-x-auto` sahifaning o'zi gorizontal
      skroll qilib ketishidan HIMOYA qilmaydi — `scroll-x-safe` esa
      `overscroll-behavior-x: contain` bilan skrollni shu lenta ichida
      ushlaydi; (2) lenta `main` ning 16px chetida tugardi, ya'ni oxirgi chip
      "kesilgan"dek emas, "tugagan"dek ko'rinardi va yana chip borligi
      bilinmasdi. `-mx-4 … px-4` lentani ekran chetigacha cho'zadi, chiplar
      esa o'sha 16px dan boshlanadi — ko'rinish o'zgarmaydi, faqat oxiri
      chetdan "chiqib" turadi.

      ★ DESKTOPDA LENTA — LENTA EMAS, ODDIY QATOR (2026-08-13, 2-iteratsiya).

      Yuqoridagi ikkala sabab TELEFONNIKI: 5 ta chip 358px ga sig'maydi va
      chetdan "chiqib" turishi yana chip borligini bildiradi. 1600px lik
      ustunda esa lenta HECH QACHON to'lmaydi — foydalanuvchi skrollamaydigan
      "skroller"ni ko'radi; ustiga `-mx-4 … px-4` telefonning 16px to'ldirmasi
      uchun o'lchangan, desktopda esa `main` da `lg:px-8` (32px) — ya'ni lenta
      chetgacha yetmay, 16px ichkarida "yarim chiqib" tugardi. Shuning uchun
      `lg:` da: skroll o'chadi, chekka chiqish bekor qilinadi, chiplar sig'masa
      ikkinchi qatorga o'raladi. Telefon yo'li bir piksel ham o'zgarmaydi.
    -->
    <div
      v-if="schedule.groups.value.length > 1"
      class="scroll-x-safe scrollbar-none -mx-4 mb-2.5 flex gap-2 px-4 pb-2 lg:mx-0 lg:flex-wrap lg:overflow-x-visible lg:px-0 lg:pb-0"
    >
      <!--
        ★ `lg:hover:` — sichqoncha bor ekranda chip "bosiladigan"ligini
        bildiradi (6.5-bo'lim). `hover:` ni Tailwind o'zi
        `@media(hover:hover)` ga o'raydi, lekin ustiga `lg:` ham qo'yildi:
        Telegram Mini App telefon KENGLIGIDA, ammo sichqonchali muhitda ham
        ochilishi mumkin — telefon yo'liga bitta ham qoida qo'shilmasin.
      -->
      <button
        type="button"
        class="min-h-11 shrink-0 whitespace-nowrap rounded-[20px] border px-4 text-[13px] font-semibold transition-colors"
        :class="
          selectedGroupId === null
            ? 'border-brand-500 bg-brand-500 text-on-brand lg:hover:border-brand-600 lg:hover:bg-brand-600'
            : 'border-line bg-ink-900 text-slate-400 lg:hover:border-line-strong lg:hover:bg-ink-800 lg:hover:text-slate-100'
        "
        @click="selectedGroupId = null"
      >
        Barchasi
      </button>
      <button
        v-for="group in schedule.groups.value"
        :key="group.id"
        type="button"
        class="min-h-11 shrink-0 whitespace-nowrap rounded-[20px] border px-4 text-[13px] font-semibold transition-colors"
        :class="
          selectedGroupId === group.id
            ? 'border-brand-500 bg-brand-500 text-on-brand lg:hover:border-brand-600 lg:hover:bg-brand-600'
            : 'border-line bg-ink-900 text-slate-400 lg:hover:border-line-strong lg:hover:bg-ink-800 lg:hover:text-slate-100'
        "
        @click="selectedGroupId = group.id"
        v-text="group.name"
      />
    </div>

    <!-- =================== Desktop: ikki ustunli joylashuv ================== -->
    <!--
      `docs/MOSLASHUVCHANLIK.md` 6.3: kalendar sahifasi desktopda
      `minmax(0,600px)_minmax(0,1fr)`.

      NEGA: qo'shimcha kenglik bitta ustunni CHO'ZISH uchun emas, IKKINCHI
      ustun uchun. Chapda oy setkasi (600px dan oshmaydi — 6.4-chegara aynan
      shu ustunda o'lchangan), o'ngda tanlangan kun darslari. Ilgari ro'yxat
      setka OSTIDA edi: 1600px lik ekranda o'quvchi kun tanlagach ro'yxatni
      ko'rish uchun pastga skroll qilardi, yonida esa 1000px bo'sh joy turardi.

      ★ TELEFON: bu `div` da faqat `lg:` qoidalari bor, ya'ni telefonda u
      oddiy blok konteyner — bolalar bugungi TARTIBDA, bugungi bo'shliqlar
      bilan tepadan pastga tiziladi (pastdagi `mt-[18px]` o'z joyida qoldi).

      ★ `lg:items-start` — BEZAK EMAS, `sticky` ning SHARTI: grid elementi
      sukut bo'yicha butun qator balandligiga cho'ziladi va cho'zilgan quti
      ichida `sticky` siljiy olmaydi (quti allaqachon maydonni to'liq
      egallagan). `start` bilan quti o'z kontenti bo'yida qoladi va grid
      maydoni ichida yopishib boradi.
    -->
    <div class="lg:grid lg:grid-cols-[minmax(0,600px)_minmax(0,1fr)] lg:items-start lg:gap-6">
      <!-- ---------------------- CHAP USTUN: oy setkasi --------------------- -->
      <div>
        <!-- ============================ Oy setkasi ============================= -->
        <!--
          ★ `@container`: setka o'lchamlari EKRANGA emas, shu kartochkaning ICHKI
          kengligiga qarab kichrayadi (pastdagi `@2xs:` izohiga qarang).
        -->
        <section class="@container rounded-xl border border-line bg-ink-900 p-[18px]">
          <div class="mb-3.5 flex items-center justify-between">
            <button
              type="button"
              class="tap-target flex items-center justify-center rounded-[11px] border border-line bg-ink-800 text-slate-100 transition-transform active:scale-90"
              aria-label="Oldingi oy"
              @click="moveMonth(-1)"
            >
              <AppIcon
                name="chevron-right"
                :size="17"
                class="rotate-180"
              />
            </button>
            <b
              class="text-base font-bold"
              v-text="monthLabel"
            />
            <button
              type="button"
              class="tap-target flex items-center justify-center rounded-[11px] border border-line bg-ink-800 text-slate-100 transition-transform active:scale-90"
              aria-label="Keyingi oy"
              @click="moveMonth(1)"
            >
              <AppIcon
                name="chevron-right"
                :size="17"
              />
            </button>
          </div>

          <!--
            ★ 7 USTUN — DIZAYN, u hech qachon buzilmaydi. O'zgaradigan narsa —
            katakning ICHI (2026-08-13).

            320px lik ekranda kartochka ichida 252px qoladi: 5px oraliq bilan
            katak ~32px bo'lib, 13px raqam va ostidagi 5px nuqtalar bir-birining
            USTIGA tushardi. Shuning uchun konteyner 18rem dan tor bo'lganda
            (≈360px dan tor ekranlar) oraliq, shrift va nuqtalar bir pog'ona
            kichrayadi; undan kengida — bugungi o'lchovlar AYNAN saqlanadi
            (375px lik telefon ham shu tomonda qoladi).
          -->
          <!--
            🔴 KATAK ~76px DAN KATTA BO'LMAYDI (`MOSLASHUVCHANLIK.md` 6.4).

            Loyiha egasining shikoyati: *"desktop holatida calendar juda
            kattalashib ketibdi, shunday katta ekranda ham ekranga
            sig'mayapti"*. Sababi: `aspect-square` balandlikni KENGLIKDAN
            oladi, kenglikning esa yuqori chegarasi yo'q edi — 1fr ustunlar
            konteynerni bo'lib olardi, ya'ni ustun kengaygan sari katak
            ham, oy setkasining BALANDLIGI ham cheksiz o'sardi (1600px lik
            ustunda ~200px lik katak, ~1400px lik setka).

            Yechim MAKSIMAL KENGLIKDA emas, USTUN TA'RIFIDA:
            `minmax(0,76px)` — track 76px da o'sishdan to'xtaydi, ortiqcha
            joy esa `justify-center` bilan setkaning ikki yoniga chiqadi.
            Bu KONSTRUKSIYA bo'yicha chegaralangan: 76 raqami konteyner
            kengligidan mutlaqo mustaqil, ya'ni 600, 1000, 2560 yoki
            istalgan kenglikda katak baribir 76px. "1600px da tuzatildi,
            2560px da yana buzildi" holati bo'lishi MUMKIN emas. Konteyner
            torayganda esa tracklar birdek qisqaradi, ya'ni `aspect-square`
            avvalgidek ishlaydi — 7 USTUN hech qachon buzilmaydi.

            ★ NEGA `lg:` YETARLI: `lg` dan pastda karkas ustuni 520px ga
            qulflangan (`StudentShell`), ya'ni `main` ning 16px to'ldirmasi
            va kartochkaning 18px to'ldirmasidan keyin setkaga ko'pi bilan
            452px tegadi — katak 60px, chegaradan pastda. Telefon yo'liga
            bitta ham qoida qo'shilmadi.
          -->
          <div
            class="grid w-full grid-cols-7 gap-[3px] @2xs:gap-[5px] lg:grid-cols-[repeat(7,minmax(0,76px))] lg:justify-center"
          >
            <div
              v-for="weekday in WEEKDAY_HEADERS_UZ"
              :key="weekday"
              class="overflow-hidden py-[3px] text-center text-[9px] font-bold uppercase text-dim"
              v-text="weekday"
            />

            <template
              v-for="cell in cells"
              :key="cell.key"
            >
              <div
                v-if="cell.day === 0"
                class="aspect-square"
                aria-hidden="true"
              />
              <!--
                ★ DESKTOPDA UCHTA HOLAT KO'Z BILAN AJRALADI (6.5-bo'lim,
                talab: *"interaktivroq bo'lishi kerak"*):
                  • HOVER — `lg:hover:bg-ink-750` faqat TANLANMAGAN katakda
                    (tanlanganida u brend to'ldirishni yuvib yuborardi:
                    variant qoidasi oddiy `bg-brand-500` dan keyin turadi).
                  • BUGUN — brend KONTURI + brend raqami, to'ldirishsiz.
                  • TANLANGAN — brend TO'LDIRISH + tashqi halqa (`ring`).
                Ya'ni farq rang emas, SHAKL bo'yicha: kontur ≠ to'ldirish.
                Halqa 3px — 5px oraliqning ichida qoladi, qo'shni katakka
                tegmaydi.

                ★ `@lg:text-sm` — 76px lik katakda 13px raqam yo'qolib
                ketardi. Matnning O'ZI o'zgarmaydi (parite shartnomasi),
                faqat o'lchovi.

                ★ NEGA `lg:` EMAS, `@lg:` (KONTEYNER, 32rem): (1) katakning
                ICHI shu faylda DOIM kartochka kengligiga bog'langan
                (yuqoridagi `@2xs:` izohi) — ekran kengligi emas; (2)
                MUHIMROG'I, `lg:text-sm` AMALDA ISHLAMASDI: Tailwind
                konteyner so'rovlarini media so'rovlaridan KEYIN chiqaradi,
                ya'ni bir xil aniqlikdagi `@2xs:text-[13px]` uni yutardi.
                (3) Telefon yo'li xavfsiz: `lg` dan pastda kartochkaning
                ichki kengligi ko'pi bilan 452px (520px karkas − 32 − 36),
                ya'ni 512px lik chegara u yerda HECH QACHON ochilmaydi.

                ★ `lg:transition` — bazadagi `transition-transform` faqat
                `transform` ni animatsiyalaydi, ya'ni hover rangi
                sakrab o'zgarardi. `lg:` da ro'yxat kengayadi; davomiylik
                va egri chiziq o'sha-o'sha (150ms), yangi animatsiya
                qo'shilmadi.

                `focus-visible` halqasiga TEGILMADI — u global
                (`style.css`), bu yerda `outline-none` yo'q, ya'ni
                klaviatura bilan yurgan o'quvchi katakni ko'radi.
              -->
              <button
                v-else
                type="button"
                class="relative flex aspect-square min-w-0 items-center justify-center rounded-[9px] border-[1.5px] text-[11px] transition-transform active:scale-90 @2xs:rounded-[11px] @2xs:text-[13px] @lg:text-sm lg:transition"
                :class="[
                  cell.isSelected
                    ? 'scale-105 border-transparent bg-brand-500 font-bold text-on-brand lg:ring-[3px] lg:ring-brand-500/30'
                    : 'bg-ink-800 lg:hover:bg-ink-750',
                  cell.isToday && !cell.isSelected
                    ? 'border-brand-500 lg:font-bold lg:text-brand-300'
                    : 'border-transparent',
                ]"
                :aria-label="`${cell.day}-${monthNameCapitalized(viewMonth.getMonth())}`"
                :aria-pressed="cell.isSelected"
                @click="selectDay(cell)"
              >
                {{ cell.day }}
                <!--
                  Kun ostidagi nuqtalar — dars TURI. Ranglar tokendan (ilgari
                  `#f5b731` oltin va `#22d3ee` firuza QOTIB QOLGAN edi).
                  Nuqta grafik element, shuning uchun `-500` (to'yingan) daraja:
                  matn darajasi (`-300`) 5px doirada kir dog' bo'lib ko'rinadi.
                -->
                <span
                  v-if="cell.marks.length > 0"
                  class="absolute bottom-[3px] flex gap-[2px] @2xs:bottom-1 @2xs:gap-[2.5px]"
                  aria-hidden="true"
                >
                  <i
                    v-for="mark in cell.marks"
                    :key="mark"
                    class="size-1 rounded-full @2xs:size-[5px]"
                    :class="mark === 'Teacher' ? 'bg-brand-500' : 'bg-cyan-500'"
                  />
                </span>
              </button>
            </template>
          </div>
        </section>

        <!--
          O'tgan oyga o'tilganda: setka bo'sh bo'lishining SABABI aytiladi.
          Bu vaqtinchalik — server tarixni bera boshlagach bu blok o'chiriladi.
        -->
        <p
          v-if="isPastMonth"
          class="mt-3 rounded-xl border border-brand-500/30 bg-brand-500/[0.06] px-4 py-3 text-xs leading-relaxed text-slate-400"
        >
          O‘tgan oylar bo‘sh ko‘rinadi: server hozircha faqat joriy va kelgusi
          darslarni beradi (yakunlangan dars 6 soatdan keyin ro‘yxatdan chiqadi).
        </p>
      </div>

      <!-- ======================== Tanlangan kun darslari ====================== -->
      <!--
        O'NG USTUN. Telefonda — setka ostida, bugungi `mt-[18px]` bo'shlig'i
        bilan (tartib ham, bo'shliq ham o'zgarmadi). Desktopda — yonida va
        `lg:sticky`: oy setkasi baland, ro'yxat esa qisqa; yopishmasa
        o'quvchi kun tanlab, pastga skroll qilib, yana yuqoriga qaytishi
        kerak bo'lardi.

        ★ `lg:top-24` (96px) — appbar `sticky top-0` va desktopda ~76px
        baland (`pt-6` + 40px avatar + `pb-3`); 96px unga tegib ketmaydi.

        ★ `lg:min-w-0` — track `minmax(0,1fr)`, ya'ni u o'z min-content idan
        TOR bo'lishi mumkin (aynan 1024px da ~110px qoladi: chap ustun 600px
        ni oladi). Grid elementining sukutdagi `min-width: auto` si bunday
        holatda elementni track dan CHIQARIB yuborardi va sahifa gorizontal
        skroll qilardi.
      -->
      <div class="mt-[18px] lg:sticky lg:top-24 lg:mt-0 lg:min-w-0">
        <p
          v-if="selectedSessions.length === 0"
          class="px-2.5 py-8 text-center text-sm text-slate-400"
        >
          {{ selectedLabel }} uchun dars yo‘q
        </p>

        <template v-else>
          <h2
            class="mb-3 ml-1 text-xs font-bold uppercase tracking-[1.4px] text-slate-400"
            v-text="selectedLabel"
          />
          <!--
            ★ `lg:flex-wrap` — HIMOYA, bezak emas: aynan 1024px kenglikda
            (iPad yotiq) o'ng ustunga ~110px qoladi, 42px nishon + nishon
            yorlig'i esa unga sig'maydi va `shrink-0` bo'lgani uchun
            qatordan chiqib ketardi. O'ralganda yorliq pastki qatorga
            tushadi. ~1100px dan keng ekranda hech narsa o'ralmaydi, ya'ni
            odatdagi desktop ko'rinishi o'zgarmaydi; telefonda qoida umuman
            yo'q.

            ★ `lg:hover:` — qator "bosiladigan"dek ko'rinsin (6.5).
          -->
          <article
            v-for="item in selectedSessions"
            :key="item.id"
            class="mb-2.5 flex items-center gap-3 rounded-[13px] border border-line bg-ink-900 p-[13px] lg:flex-wrap lg:transition-colors lg:hover:border-line-strong lg:hover:bg-ink-800"
          >
            <!--
              Dars turi nishoni: PASTEL tint + to'q ikonka (ilgari
              `rgb(245 183 49 / .18)` + `#fcd34d` va `rgb(34 211 238 / .17)` +
              `#67e8f9` qotib qolgan edi — qorong'i fonda yorug' ikonka
              kerak edi, oq fonda esa aksincha).
            -->
            <span
              class="flex size-[42px] shrink-0 items-center justify-center rounded-xl"
              :class="
                item.type === 'Teacher'
                  ? 'bg-brand-500/12 text-brand-300'
                  : 'bg-cyan-500/12 text-cyan-300'
              "
              aria-hidden="true"
            >
              <AppIcon
                :name="item.type === 'Teacher' ? 'graduation' : 'user-check'"
                :size="18"
              />
            </span>

            <div class="min-w-0 flex-1">
              <b
                class="block truncate text-sm"
                v-text="sessionTitle(item)"
              />
              <span class="block truncate text-xs text-slate-400">
                {{ item.groupName }} · {{ formatTime(item.scheduledStart) }}
              </span>
            </div>

            <!--
              ★ `tap-expand`: `size="sm"` 36px baland, WCAG 2.5.5 esa 44px
              so'raydi. `BaseButton` o'lchov xaritasi umumiy — uni surish
              xodim panellarida ham joylashuvni siljitardi. Shu sababli
              faqat bosiladigan maydon kengaytiriladi (36 + 2×6 = 48px);
              jonli darsga kirish — bu qatordagi yagona harakat.
            -->
            <BaseButton
              v-if="sessionState(item, now) === 'live'"
              class="tap-expand animate-pulse-btn shrink-0"
              variant="danger"
              size="sm"
              @click="open(item.id)"
            >
              Kirish
            </BaseButton>
            <BaseBadge
              v-else-if="sessionState(item, now) === 'past'"
              class="shrink-0"
              tone="neutral"
            >
              Yakunlangan
            </BaseBadge>
            <BaseBadge
              v-else
              class="shrink-0"
              tone="accent"
            >
              Rejada
            </BaseBadge>
          </article>
        </template>
      </div>
    </div>
  </div>
</template>
