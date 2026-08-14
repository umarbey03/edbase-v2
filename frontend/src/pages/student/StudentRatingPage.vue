<script setup lang="ts">
import { useQuery } from '@tanstack/vue-query'
import { computed, ref, watch } from 'vue'

import {
  fetchCenterLeaderboard,
  fetchGroupLeaderboard,
  fetchMyRank,
  rankBadge,
  scoreParts,
} from '@/entities/leaderboard'
import { currentPeriod, isValidPeriod, periodLabel } from '@/entities/payment'
import { fetchAttendanceSummary } from '@/entities/progress'
import { toUserMessage } from '@/shared/api'
import type { LeaderboardRowDto } from '@/shared/types'
import { AppIcon, BaseAvatar, BaseSheet, DataStatus } from '@/shared/ui'

/**
 * REYTING — eski `#progress` bo'limi ("Leaderboard").
 *
 * Tuzilishi eski ilovadagidek: sarlavha (guruh nomi + oy) -> podium (uchlik,
 * birinchida toj) -> to'liq ro'yxat -> qatorni bosganda ball tafsiloti.
 *
 * DESKTOPDA (≥1024px) TUZILISH O'SHA-O'SHA, faqat joylashuv boshqacha:
 * podium to'liq kenglikdagi bannerga, ro'yxat esa ikki ustunga o'tadi
 * (`docs/MOSLASHUVCHANLIK.md` 6.3). Tafsilotlar shablondagi izohlarda.
 *
 * IKKI SO'ROV, ketma-ket: avval `/leaderboard/me` (o'quvchi qaysi guruhda va
 * o'rni qanday), keyin shu guruhning jadvali. Nima uchun bitta so'rov emas:
 * o'quvchi guruhini bilmaydi — guruh Id'sini SERVER aytadi, frontend uni
 * o'zi qidirmaydi (ruxsat qoidasi serverda qolsin).
 *
 * UCHINCHI so'rov — davomat xulosasi, FAQAT "ketma-ket qatnashish"
 * seriyasi uchun (R14). U reyting so'rovlariga BOG'LIQ EMAS va bosh sahifa
 * bilan bir xil kesh kalitidan foydalanadi — tafsiloti pastdagi izohda.
 *
 * ══════════════════════════════════════════════════════════════════════
 *  IKKI QAMROV: GURUHIM / O'QUV MARKAZ (R11, egasining qarori)
 * ══════════════════════════════════════════════════════════════════════
 *
 * 🔴 "O'QUV MARKAZ" — "TIZIMDAGI HAMMA" DEGANI EMAS. Mahsulot bir necha
 *    o'quv markazga sotiladi va chegara SERVERDA (`ILearningCenterScope`)
 *    ushlab turiladi. Frontend chegarani takrorlamaydi va uni "hamma
 *    foydalanuvchi" deb nomlamaydi ham — yorliq shu sababdan "O'quv
 *    markaz".
 */
type BoardScope = 'group' | 'center'

const scope = ref<BoardScope>('group')

const isCenter = computed(() => scope.value === 'center')

/*
  ══════════════════════════════════════════════════════════════════════
   NATIJALAR ARXIVI (R12, talab: *"natijalarni arxivi ham bo'lsin"*)
  ══════════════════════════════════════════════════════════════════════

  ★ SERVER TARAFI ALLAQACHON TAYYOR: ikkala endpoint ham `?period=YYYY-MM`
    ni qabul qiladi va o'tgan davrni UZOQROQ keshlaydi (`PastPeriodTtl`) —
    ya'ni arxivni varaqlash joriy oyni ko'rishdan arzonroq. Frontend
    shu paytgacha `undefined` yuborardi, ya'ni FAQAT joriy oy ko'rinardi.

  ★ OY QAMROVGA BOG'LIQ EMAS — U IKKALASIGA BIR XIL QO'LLANADI. "Guruhim"
    dan "O'quv markaz" ga o'tganda tanlangan oy SAQLANADI: o'quvchi bir
    oyning ikki kesimini solishtirayotgan bo'lishi mumkin va tanlovni
    qaytadan qilish uni ma'nosiz ishga majburlardi. Shu sababli `period`
    bitta, `scope` esa alohida.

  ★ SANA FORMATLASH YOZILMADI: `entities/payment` dagi `currentPeriod()`,
    `periodLabel()` va `isValidPeriod()` ilovada allaqachon 8 joyda
    ishlatiladi (to'lovlar, moliya, o'quvchi hisobi). Ikkinchi nusxa
    "iyul 2026" ni boshqacha yozib qo'yishi mumkin edi.

  🔴 QAYSI OYLARDA NATIJA BOR — HECH KIM AYTMAYDI. Bunday endpoint yo'q,
     o'quvchi esa guruh boshlanish sanasini ham ko'ra olmaydi
     (`GroupDto` xodimlar uchun). Shu sababli ro'yxat TO'QILMAYDI: maydon
     ochiq, yuqori chegara — joriy oy, natija bo'lmasa bo'sh holat sababni
     aytadi. Aks holda ekranda 12 ta oy turib, ularning o'ntasi bo'sh
     chiqardi.
*/
const period = ref(currentPeriod())

/**
 * Serverga YUBORILADIGAN qiymat.
 *
 * `<input type="month">` ni qo'lda tozalash mumkin (bo'sh satr qoladi), va
 * buzuq qiymatda server 400 beradi — jadval o'rniga xato ekrani chiqardi.
 * Kelajakdagi oy ham yuborilmaydi: `max` atributi klaviaturadan kiritishni
 * to'smaydi, satrlarni esa `YYYY-MM` formatida oddiy solishtirish mumkin.
 * Ikkala holatda ham `undefined` ketadi va server JORIY oyni beradi.
 */
const effectivePeriod = computed(() =>
  isValidPeriod(period.value) && period.value <= currentPeriod() ? period.value : undefined,
)

/** Joriy oy emasmi — bo'sh holat matni va streak qatori shunga qaraydi. */
const isArchive = computed(
  () => effectivePeriod.value !== undefined && effectivePeriod.value !== currentPeriod(),
)

/**
 * ★ "MENING O'RNIM" SO'ROVI DAVRGA BOG'LANMADI. Bu yerda undan FAQAT
 *   `groupId` olinadi (o'quvchi qaysi guruhda), guruh esa oydan qat'i
 *   nazar o'sha-o'sha. Kalitga `period` qo'shilsa har oy almashganda
 *   ikkinchi, keraksiz so'rov ketardi.
 *
 * 🔴 SHU BILAN BIRGA: bu — o'quvchining BUGUNGI faol guruhi. Guruh
 *    arxivlangan bo'lsa server `groupId: null` qaytaradi (serverda
 *    `PrimaryGroupAsync` faqat `Group.IsActive` ni oladi), ya'ni bitirgan
 *    o'quvchi o'z tarixini KO'RA OLMAYDI. Bu mijozda tuzatilmaydi —
 *    hisobotga yozildi.
 */
const myRankQuery = useQuery({
  queryKey: ['leaderboard', 'me'],
  queryFn: ({ signal }) => fetchMyRank(undefined, { signal }),
})

const groupId = computed(() => myRankQuery.data.value?.groupId ?? null)

const boardQuery = useQuery({
  // Davr KALIT ICHIDA: har oy alohida keshlanadi, ya'ni arxivda oldinga-
  // orqaga varaqlash takroriy so'rov yubormaydi.
  queryKey: ['leaderboard', 'group', groupId, effectivePeriod],
  queryFn: ({ signal }) =>
    fetchGroupLeaderboard(groupId.value as number, effectivePeriod.value, { signal }),
  // Guruh aniqlanmaguncha so'rov yuborilmaydi (aks holda `null` bilan 404 ketardi).
  enabled: computed(() => groupId.value !== null),
})

/**
 * MARKAZ JADVALI — DANGASA (lazy).
 *
 * ★ `enabled` MARKAZ TABI OCHILGUNICHA `false`. Sabab serverdagi qaror
 *   bilan bir xil: markaz jadvali butun markazni hisoblaydi va u guruh
 *   jadvalidan qimmatroq. Tabni hech qachon ochmaydigan o'quvchi bu
 *   narxni to'lamasligi kerak.
 *
 * ★ BIR MARTA OLINGACH KESHDA QOLADI: TanStack kalit bo'yicha keshlaydi,
 *   ya'ni tablar orasida u yoq-bu yoq o'tish qo'shimcha so'rov yubormaydi
 *   (global `staleTime: 30_000`).
 *
 * ★ KALITDA GURUH YO'Q — jadval qamrovi guruhga bog'liq emas.
 */
const centerQuery = useQuery({
  queryKey: ['leaderboard', 'center', effectivePeriod],
  queryFn: ({ signal }) => fetchCenterLeaderboard(effectivePeriod.value, { signal }),
  enabled: isCenter,
})

const groupBoard = computed(() => boardQuery.data.value ?? null)
const centerBoard = computed(() => centerQuery.data.value ?? null)

/**
 * Ikki qamrov uchun UMUMIY ko'rinish. Podium, ro'yxat va tafsilot varaqasi
 * AYNAN bir xil qatordan (`LeaderboardRowDto`) chizilgani uchun shablon
 * ikki marta yozilmaydi.
 */
const board = computed(() => (isCenter.value ? centerBoard.value : groupBoard.value))
const rows = computed<LeaderboardRowDto[]>(() => board.value?.rows ?? [])

/** Sarlavha: guruh nomi yoki markaz yorlig'i. */
const boardTitle = computed(() =>
  isCenter.value ? 'O‘quv markaz' : (groupBoard.value?.groupName ?? 'Guruh'),
)

/**
 * MARKAZ JADVALI QISQARTIRILGANMI.
 *
 * ★ `rows.length < studentCount` — XATO EMAS, SHUNDAY MO'LJALLANGAN:
 *   server eng yaxshi `topCount` ta qatorni yuboradi. O'quvchiga buni
 *   AYTISH SHART, aks holda 900 kishilik markazda 100 ta qator ko'rgan
 *   o'quvchi "qolganlari qani?" deb o'ylardi.
 */
const isTrimmed = computed(
  () => centerBoard.value !== null && rows.value.length < centerBoard.value.studentCount,
)

/**
 * O'Z QATORI JADVALDAN TASHQARIDA BO'LSA — ALOHIDA KO'RSATILADI.
 *
 * Markaz jadvalida o'quvchi yuqori yuzlikka kirmasligi mumkin; serverda
 * uning HAQIQIY o'rni baribir hisoblanadi va `me` da keladi. Guruh
 * jadvalida bunday holat YO'Q (jadval to'liq), shuning uchun bu qiymat
 * u yerda doim `null`.
 */
const meOutsideTop = computed(() => {
  const me = centerBoard.value?.me ?? null
  if (!isCenter.value || me === null) return null
  return rows.value.some((row) => row.studentId === me.studentId) ? null : me
})

/**
 * Podium — ATAYLAB `rows` tartibidan olinadi, `rank` bo'yicha emas: bir xil
 * ballda server takroriy o'rin beradi (1, 2, 2, 4) va `rank` bo'yicha
 * qidirilsa ikkita "2-o'rin" ustma-ust tushardi.
 */
const podium = computed(() => rows.value.slice(0, 3))

/** Podium ustunlari eski ilovadagi tartibda: 2 — 1 — 3. */
const podiumOrder = computed(() => {
  const [first, second, third] = podium.value
  return [second, first, third].filter((row): row is LeaderboardRowDto => row !== undefined)
})

/*
  ══════════════════════════════════════════════════════════════════════
   HOLAT (kutish / xato / bo'sh) — HAR QAMROV UCHUN ALOHIDA
  ══════════════════════════════════════════════════════════════════════

  ★ QAMROVLAR HOLATI ARALASHTIRILMAYDI. Guruh so'rovi yiqilgan bo'lsa
    ham markaz jadvali ochilishi kerak (va aksincha) — ular boshqa-boshqa
    endpoint va boshqa-boshqa ruxsat qoidasiga tayanadi. Ilgari bu yerda
    ikki so'rovning xatosi `??` bilan qo'shilardi; endi faqat FAOL
    qamrovniki olinadi.

  ★ GURUHSIZ O'QUVCHI MARKAZ JADVALINI KO'RA OLADI — `groupId === null`
    sharti FAQAT guruh qamrovida bo'sh holatga olib boradi. Serverda ham
    shunday: markaz qamroviga guruhsiz o'quvchi ham kiradi.
*/
const isEmpty = computed(() =>
  isCenter.value
    ? centerQuery.isSuccess.value && rows.value.length === 0
    : groupId.value === null || (boardQuery.isSuccess.value && rows.value.length === 0),
)

/** Guruh umuman topilmadi (arxivda ham sabab shu bo'lishi mumkin). */
const noGroup = computed(() => !isCenter.value && groupId.value === null)

/**
 * ★ JORIY OYDAGI MATNLAR BIR HARF HAM O'ZGARMADI — arxiv FAQAT YANGI
 *   holatga o'z matnini qo'shadi. O'tgan oyda "Guruhga qo'shilganingizdan
 *   keyin reyting shu yerda ko'rinadi" degan matn NOTO'G'RI bo'lardi:
 *   o'quvchi allaqachon guruhda, shunchaki o'sha oyda natija yig'ilmagan.
 */
const isArchiveEmpty = computed(() => isArchive.value && !noGroup.value)

const emptyTitle = computed(() => {
  if (isArchiveEmpty.value) return 'Natija yo‘q'
  return isCenter.value ? 'Natija yo‘q' : 'Guruh yo‘q'
})

const emptyText = computed(() => {
  if (isArchiveEmpty.value) return `${periodLabel(period.value)} uchun natija topilmadi.`
  return isCenter.value
    ? 'Bu oyda o‘quv markazda hali natija yig‘ilmagan.'
    : 'Guruhga qo‘shilganingizdan keyin reyting shu yerda ko‘rinadi.'
})

const errorMessage = computed(() => {
  const error = isCenter.value
    ? centerQuery.error.value
    : (myRankQuery.error.value ?? boardQuery.error.value)

  return error !== null ? toUserMessage(error) : null
})

const isPending = computed(() =>
  isCenter.value
    ? centerQuery.isPending.value
    : myRankQuery.isPending.value || (groupId.value !== null && boardQuery.isPending.value),
)

const isRefetching = computed(() =>
  isCenter.value
    ? centerQuery.isFetching.value
    : myRankQuery.isFetching.value || boardQuery.isFetching.value,
)

function refresh(): void {
  if (isCenter.value) {
    void centerQuery.refetch()
    return
  }

  void myRankQuery.refetch()
  void boardQuery.refetch()
}

/* ------------------------------------------------------------------ streak */

/**
 * KETMA-KET QATNASHISH SERIYASI (talab: *"streak days calculation should be
 * added"*).
 *
 * ★ HISOB-KITOB YOZILMADI — U ALLAQACHON BOR. `AttendanceMath.Streak()`
 *   serverda hisoblanadi va `GET /api/v1/progress/attendance` javobining
 *   `streak` maydonida keladi. Mijozda qayta hisoblash ikkinchi HAQIQAT
 *   MANBAI bo'lardi: davomat holatlari (kelgan / kechikkan / qisman /
 *   qoldirgan) va "seriya uziladimi?" qoidasi FAQAT domenda yashaydi.
 *
 * ★ BU KUN EMAS, DARS SERIYASI — VA SHUNDAY QOLADI. Guruhlar haftasiga
 *   ANIQ 2 kun yig'iladi, ya'ni kalendar kunlari bo'yicha seriya HAR HAFTA
 *   uzilib turardi va "3 kun ketma-ket" degan raqam hech qachon 2 dan
 *   oshmasdi — o'lchov ma'nosini yo'qotardi. Shuning uchun matn ham
 *   "kun" demaydi.
 *
 * ★ SO'ROV KALITI BOSH SAHIFA BILAN AYNI: `['progress','attendance']`
 *   (`StudentHomePage.vue` da o'sha kalit, o'sha `fetchAttendanceSummary({})`
 *   argumenti). TanStack Query kalit bo'yicha keshni ULASHADI — o'quvchi
 *   bosh sahifadan reytingga o'tganda qiymat DARHOL chiqadi va ikkinchi
 *   HTTP so'rovi yuborilmaydi (global `staleTime: 30_000`,
 *   `app/providers/query-client.ts`).
 *
 * ★ REYTING XATOSIGA QO'SHILMAYDI: davomat so'rovi yiqilsa `DataStatus`
 *   butun sahifani xato ekraniga o'tkazmasligi kerak — streak bu yerda
 *   QO'SHIMCHA ma'lumot, jadval esa asosiy. Xato bo'lsa qator shunchaki
 *   chizilmaydi (`?? 0` -> shart bajarilmaydi).
 */
const attendanceQuery = useQuery({
  queryKey: ['progress', 'attendance'],
  queryFn: ({ signal }) => fetchAttendanceSummary({}, { signal }),
})

const streak = computed(() => attendanceQuery.data.value?.streak ?? 0)

/* --------------------------------------------------------- ball tafsiloti */

/**
 * Qatorni bosganda tafsilot ochiladi. Bu ATAYLAB: yakuniy ball uch mezon
 * o'rtachasi va tafsilotsiz reyting "qora quti" bo'lib qolardi — eski
 * ilovada ham qator bosilganda ballar yoyilardi.
 */
const detailRow = ref<LeaderboardRowDto | null>(null)

/**
 * Qamrov almashtirish. Ochiq tafsilot varaqasi YOPILADI: u boshqa
 * qamrovdagi qatorni ko'rsatib turardi va o'quvchi uni yangi ro'yxatdan
 * izlardi.
 */
function selectScope(next: BoardScope): void {
  if (scope.value === next) return
  scope.value = next
  detailRow.value = null
}

/**
 * OY ALMASHGANDA ham ochiq tafsilot varaqasi yopiladi — qamrov
 * almashtirishdagi bilan AYNAN bir sabab: varaqada BOSHQA davrning ballari
 * turardi va o'quvchi ularni yangi jadvaldan izlardi.
 */
watch(effectivePeriod, () => {
  detailRow.value = null
})

/** `null` — "hisobga olinmagan", NOL EMAS. Shuning uchun chiziqcha. */
function formatPercent(value: number | null): string {
  return value === null ? '—' : `${Math.round(value)}%`
}
</script>

<template>
  <div>
    <h2
      class="mb-3 ml-1 mt-2 flex items-center gap-[7px] text-xs font-bold uppercase tracking-[1.4px] text-brand-300"
    >
      <AppIcon
        name="chart"
        :size="15"
      />
      Leaderboard
    </h2>

    <!--
      ══════════════════════════════════════════════════════════════════
       QAMROV TANLAGICHI: GURUHIM / O'QUV MARKAZ (R11)
      ══════════════════════════════════════════════════════════════════

      Shakl `GroupTabs` dan ko'chirilgan (yumaloq tugma, faolida accent fon
      va chegara) — o'quvchi ilovasida ham, ustoz panelida ham "tanlov
      qatori" bir xil ko'rinsin.

      ★ `role="tablist"` + `aria-selected`: bu ikki KO'RINISH orasidagi
        almashtirgich, ya'ni skrinrider uchun tab paneli — filtr emas.

      🔴 YORLIQ "O'QUV MARKAZ", "UMUMIY" EMAS. Mahsulot bir necha o'quv
         markazga sotiladi va jadval FAQAT bitta markaz ichida ishlaydi.
         "Umumiy" degan so'z o'quvchiga "butun tizim" degan noto'g'ri
         va'dani berardi.
    -->
    <!--
      ★ QAMROV VA OY BITTA QATORDA, LEKIN IKKI MUSTAQIL BOSHQARUV.
        Telefonda ular ikki qatorga tushadi (`w-full` + `flex-wrap`),
        desktopda esa qamrov chapda, oy o'ngda (`lg:ml-auto`) turadi.
        Tanlagichlar bir-birini almashtirmaydi: oy IKKALA qamrovga ham
        qo'llanadi (skriptdagi arxiv izohi).

      ★ `role="tablist"` O'RAMI TEGILMADI: u FAQAT ikki `role="tab"` ni
        o'rashi kerak. Oy maydoni tab emas — filtr, shuning uchun u
        ro'yxatdan TASHQARIDA.
    -->
    <div class="mb-3 flex flex-wrap items-center gap-2">
      <div
        class="flex w-full gap-2 lg:w-auto"
        role="tablist"
      >
        <button
          v-for="tab in [
            { key: 'group' as const, label: 'Guruhim', icon: 'users' as const },
            { key: 'center' as const, label: 'O‘quv markaz', icon: 'award' as const },
          ]"
          :key="tab.key"
          type="button"
          role="tab"
          class="inline-flex min-h-11 flex-1 shrink-0 items-center justify-center gap-1.5 whitespace-nowrap rounded-[20px] border px-[15px] text-[13px] transition-colors lg:flex-none"
          :class="
            scope === tab.key
              ? 'border-brand-500 bg-brand-500/14 font-semibold text-brand-500'
              : 'border-line bg-ink-900 font-medium text-slate-400 hover:border-line-strong hover:bg-ink-800 hover:text-slate-100'
          "
          :aria-selected="scope === tab.key"
          @click="selectScope(tab.key)"
        >
          <AppIcon
            :name="tab.icon"
            :size="15"
          />
          {{ tab.label }}
        </button>
      </div>

      <!--
        NATIJALAR ARXIVI (R12). `<input type="month">` — ilovadagi mavjud
        naqsh (`ManagePaymentsPage`, `ManageFinancePage`): qurilmaning O'Z
        oy tanlagichini ochadi, ya'ni Mini App ichida ham tanish ishlaydi.

        `max` — joriy oy: kelajakdagi reyting hali mavjud emas. Klaviatura
        orqali kiritilgan qiymat esa skriptda ham tekshiriladi.
      -->
      <label
        class="sr-only"
        for="board-period"
      >
        Natijalar oyi
      </label>
      <input
        id="board-period"
        v-model="period"
        class="zn-input w-full lg:ml-auto lg:w-44"
        type="month"
        :max="currentPeriod()"
      >
    </div>

    <!--
      ★ BO'SH HOLAT MATNI QAMROVGA QARAB O'ZGARADI. "Guruh yo'q" markaz
        jadvalida noto'g'ri bo'lardi: guruhsiz o'quvchi ham markaz
        reytingini ko'radi, va u yerda bo'sh ekran boshqa narsani
        anglatadi — shu oyda markazda hali natija yig'ilmaganini.
    -->
    <DataStatus
      :pending="isPending"
      :error="errorMessage"
      :empty="isEmpty"
      :retrying="isRefetching"
      :skeleton-rows="4"
      empty-icon="chart"
      :empty-title="emptyTitle"
      :empty-text="emptyText"
      @retry="refresh"
    >
      <template v-if="board !== null">
        <!--
          ══════════════════════════════════════════════════════════════════
           DESKTOPDA (≥1024px) SARLAVHA + PODIUM = TO'LIQ KENGLIKDAGI BANNER
          ══════════════════════════════════════════════════════════════════

          `docs/MOSLASHUVCHANLIK.md` 6.3: "Podium to'liq kenglik, ro'yxat
          `lg:grid-cols-2`". 1500px lik maydonda markazlashgan uchta kichik
          ustun havoda osilib qolardi; ramka ularni ATAYLAB markazga
          qo'yilgan "sahna" qilib ko'rsatadi, ya'ni bo'sh joy xatoga emas,
          kompozitsiyaga aylanadi.

          ★ TELEFONDA BU O'RAM KO'RINMAYDI: klasslarning HAMMASI `lg:`
          ostida, o'ramning o'zida esa chegara ham, to'ldirma ham yo'q —
          shuning uchun podiumning `mb-3` si avvalgidek o'ramdan "o'tib"
          ro'yxatgacha 12px bo'shliq beradi (margin collapse). Bir piksel
          ham siljimaydi.
        -->
        <div class="lg:mb-6 lg:rounded-[20px] lg:border lg:border-line lg:bg-ink-900 lg:px-6 lg:py-7">
          <!--
            Sarlavha: guruh nomi yoki markaz yorlig'i — ikkalasi ham AYNI
            joyda, ayni o'lchamda. Qamrov almashganda sahifa "sakramaydi".
          -->
          <p
            class="text-center text-[17px] font-extrabold text-slate-100 lg:text-2xl"
            v-text="boardTitle"
          />
          <!--
            ★ DAVR ENDI YORLIQ BILAN: `2026-07` o'rniga "iyul 2026"
              (`periodLabel` — ilovadagi YAGONA davr formatlagichi, to'lov
              va moliya ekranlari ham shuni ishlatadi). Arxivda bu SHART:
              tanlangan oy raqamlarning qaysi davrga tegishli ekanini aytib
              turishi kerak, aks holda o'quvchi eski jadvalni joriy oy deb
              o'qishi mumkin edi.
          -->
          <p class="mb-3.5 text-center text-xs text-slate-400 lg:mb-7 lg:text-sm">
            {{ periodLabel(board.period) }} · {{ board.studentCount }} o‘quvchi
          </p>

          <!--
            KETMA-KET QATNASHISH SERIYASI (R14).

            ★ MATN BOSH SAHIFADAGI BILAN AYNAN BIR XIL
              (`StudentHomePage.vue`: *"Ketma-ket N darsda qatnashdingiz"*).
              Ikki ekranda bitta o'lchov ikki xil nomlansa o'quvchi ularni
              boshqa-boshqa raqam deb o'ylardi.

            ★ CHEGARA `> 1`, `> 0` EMAS — bosh sahifadagi shart bilan bir xil:
              "ketma-ket 1 darsda qatnashdingiz" — seriya emas, shunchaki
              bitta dars.

            ★ MARJIN FAQAT PASTDA: yuqoridagi davr qatorining `mb-3.5`/`lg:mb-7`
              si bilan yig'iladi (blok oqimida qo'shni chekkalar QO'SHILMAYDI,
              kattasi olinadi), ya'ni seriya bo'lmaganda joylashuv bir piksel
              ham o'zgarmaydi.
          -->
          <!--
            ★ ARXIVDA KO'RSATILMAYDI (R12). Seriya — BUGUNGI holat
              (`/progress/attendance` davr parametrini olmaydi), ya'ni
              o'tgan oyning jadvali ustida turgan "Ketma-ket 5 darsda
              qatnashdingiz" o'sha oyga tegishlidek o'qilardi. Yo'q
              ma'lumotni ko'rsatmaslik — noto'g'risini ko'rsatishdan yaxshi.
          -->
          <p
            v-if="streak > 1 && !isArchive"
            class="mb-3.5 text-center text-xs font-semibold text-brand-300 lg:mb-7 lg:text-sm"
          >
            Ketma-ket {{ streak }} darsda qatnashdingiz.
          </p>

          <!--
            Podium: 2 — 1 — 3, birinchida toj (eski ilovadagidek).

            🔴 DESKTOPDA PODIUM QAYTA O'LCHANDI. Telefon o'lchamlari
            (`max-w-[120px]`, 11px ism, 22px toj) 1500px lik ekranda
            "kichraytirilgan ekran surati" bo'lib ko'rinardi — holbuki bu
            sahifaning VIZUAL MARKAZI. Desktopda: katak 260px gacha,
            avatar 80/96px, toj 38px, ball 28px.

            ★ Avatar o'lchami `size` PROP'i orqali emas, KLASS orqali
            kattalashtiriladi: `BaseAvatar` o'lchamlari qat'iy jadval
            (`sm`/`md`/`lg`), moslashuvchan qiymati yo'q. Klass ildiz
            elementga qo'shiladi va `lg:size-*` media so'rovda bo'lgani
            uchun jadvaldagi `size-14`/`size-10` ni desktopda ustidan
            yopadi — komponentga TEGMASDAN (u 20+ joyda ishlatilgan).
          -->
          <div
            v-if="podium.length > 0"
            class="mb-3 flex items-end justify-center gap-2.5 lg:mb-0 lg:gap-8"
          >
            <button
              v-for="row in podiumOrder"
              :key="row.studentId"
              type="button"
              class="flex max-w-[120px] flex-1 flex-col items-center rounded-2xl p-2 transition-transform active:scale-95 lg:max-w-[260px] lg:rounded-3xl lg:p-5 lg:transition lg:hover:-translate-y-1"
              :class="
                row.isMe
                  ? 'bg-brand-500/12 lg:hover:bg-brand-500/20'
                  : 'lg:hover:bg-ink-800'
              "
              @click="detailRow = row"
            >
              <span
                v-if="row.rank === 1"
                class="-mb-1 text-[22px] lg:-mb-2 lg:text-[38px]"
                aria-hidden="true"
              >👑</span>

              <BaseAvatar
                :class="row.rank === 1 ? 'lg:size-24 lg:text-3xl' : 'lg:size-20 lg:text-2xl'"
                :name="row.studentName ?? '?'"
                :size="row.rank === 1 ? 'lg' : 'md'"
                :ring="row.isMe"
              />

              <span
                class="mt-1.5 w-full truncate text-center text-[11px] font-bold text-slate-200 lg:mt-3 lg:text-[15px]"
                v-text="row.studentName ?? '—'"
              />
              <span
                class="text-[15px] font-extrabold tabular-nums text-brand-400 lg:text-[28px] lg:leading-tight"
              >
                {{ Math.round(row.total) }}
              </span>
              <span
                class="text-[10px] font-bold text-dim lg:text-xs"
                v-text="rankBadge(row.rank)"
              />
            </button>
          </div>
        </div>

        <!--
          To'liq ro'yxat.

          DESKTOPDA IKKI USTUN (`docs/MOSLASHUVCHANLIK.md` 6.3): 20 kishilik
          guruhda bitta ustun ekran bo'yi cho'zilib, o'quvchi o'z o'rnini
          topish uchun uzoq skroll qilardi — ikki ustun skrollni yarimlatadi.
          Tartib O'ZGARMAYDI: setka `row-major`, ya'ni o'qish yo'nalishi
          hamon 1, 2, 3, … (chapdan o'ngga, keyin pastga).

          ★ `lg:space-y-0` SHART: `space-y-2` qo'shni elementlarga yuqori
          chekka qo'yadi va setkada bu chekkalar KATAK ICHIDA qolib,
          ustunlar bir-biriga nisbatan siljib ketardi. Setkada bo'shliqni
          faqat `gap` beradi.
        -->
        <ul class="space-y-2 lg:grid lg:grid-cols-2 lg:gap-3 lg:space-y-0">
          <li
            v-for="row in rows"
            :key="row.studentId"
          >
            <!--
              ★ O'Z QATORI DESKTOPDA KUCHAYTIRILDI (`lg:ring-2`): ikki
              ustunli ro'yxatda 20 ta qator ko'z oldida bir vaqtda turadi va
              faqat fon tusi bilan ajratilgan qatorni izlash kerak bo'lardi.
              Halqa qatorni "ko'tarib" beradi. Ma'lumot buni QO'LLAB-
              QUVVATLAYDI: `isMe` bayrog'ini serverning o'zi qo'yadi
              (`LeaderboardRowDto`), frontend taxmin qilmaydi.

              Telefonda halqa YO'Q — `lg:` ostida, ya'ni Mini App ko'rinishi
              avvalgidek: chegara + fon + "Siz" yozuvi.
            -->
            <button
              type="button"
              class="flex w-full items-center gap-[11px] rounded-[14px] border px-3 py-2.5 text-left transition-colors lg:px-4 lg:py-3.5"
              :class="
                row.isMe
                  ? 'border-brand-500 bg-brand-500/13 lg:ring-2 lg:ring-brand-500/25 lg:hover:bg-brand-500/20'
                  : 'border-line bg-ink-900 hover:bg-ink-800 lg:hover:border-brand-500/40'
              "
              @click="detailRow = row"
            >
              <span
                class="w-7 shrink-0 text-center text-[13px] font-extrabold tabular-nums text-dim lg:w-9 lg:text-[15px]"
                v-text="rankBadge(row.rank)"
              />
              <BaseAvatar
                class="lg:size-10 lg:text-sm"
                :name="row.studentName ?? '?'"
                size="sm"
                :ring="row.isMe"
              />
              <span class="min-w-0 flex-1">
                <span
                  class="block truncate text-sm font-semibold text-slate-100 lg:text-[15px]"
                  v-text="row.studentName ?? '—'"
                />
                <span
                  v-if="row.isMe"
                  class="text-[11px] font-bold text-brand-400"
                >Siz</span>
              </span>
              <span
                class="shrink-0 text-base font-extrabold tabular-nums text-brand-400 lg:text-lg"
              >
                {{ Math.round(row.total) }}
              </span>
            </button>
          </li>
        </ul>

        <!--
          ══════════════════════════════════════════════════════════════════
           MARKAZ JADVALI QISQARTIRILGAN: IZOH + O'Z QATORING
          ══════════════════════════════════════════════════════════════════

          Server butun markazni emas, eng yaxshi `topCount` ta qatorni
          yuboradi (javob hajmi markaz kattaligidan qat'i nazar barqaror
          bo'lsin uchun). Ikki natija:

          ★ IZOH SHART. Yuqorida "{{ studentCount }} o'quvchi" deb yozilgan,
            ro'yxatda esa 100 ta qator — izohsiz o'quvchi buni YO'QOTILGAN
            MA'LUMOT deb o'ylardi.

          ★ O'Z QATORI YO'QOLMAYDI. Reytingning o'quvchi uchun asosiy
            savoli — "men qayerdaman?". Server javobni aynan shuning uchun
            `me` maydonida ALOHIDA yuboradi va o'rin TO'LIQ ro'yxatdan
            olinadi (kesilgan ro'yxatdagi pozitsiya emas).
        -->
        <p
          v-if="isTrimmed && centerBoard !== null"
          class="mt-3 text-center text-[11px] text-slate-400"
        >
          Eng yaxshi {{ centerBoard.topCount }} ta natija ko‘rsatilyapti.
        </p>

        <template v-if="meOutsideTop !== null">
          <p class="mb-1.5 mt-3 text-center text-[11px] font-bold text-brand-300">
            Sizning o‘rningiz
          </p>

          <!--
            ★ QATOR SHAKLI YUQORIDAGI RO'YXATNIKI BILAN AYNI, LEKIN
              SHARTLARSIZ: bu qator TA'RIFI BO'YICHA so'rovchiniki, ya'ni
              `row.isMe` har doim rost. Shu sababli klass tanlovi ham,
              "Siz" yorlig'ining `v-if` i ham qolmaydi — nusxa emas,
              SODDALASHGAN ko'rinish.
          -->
          <button
            type="button"
            class="flex w-full items-center gap-[11px] rounded-[14px] border border-brand-500 bg-brand-500/13 px-3 py-2.5 text-left transition-colors lg:px-4 lg:py-3.5 lg:ring-2 lg:ring-brand-500/25 lg:hover:bg-brand-500/20"
            @click="detailRow = meOutsideTop"
          >
            <span
              class="w-7 shrink-0 text-center text-[13px] font-extrabold tabular-nums text-dim lg:w-9 lg:text-[15px]"
              v-text="rankBadge(meOutsideTop.rank)"
            />
            <BaseAvatar
              class="lg:size-10 lg:text-sm"
              :name="meOutsideTop.studentName ?? '?'"
              size="sm"
              ring
            />
            <span class="min-w-0 flex-1">
              <span
                class="block truncate text-sm font-semibold text-slate-100 lg:text-[15px]"
                v-text="meOutsideTop.studentName ?? '—'"
              />
              <span class="text-[11px] font-bold text-brand-400">Siz</span>
            </span>
            <span
              class="shrink-0 text-base font-extrabold tabular-nums text-brand-400 lg:text-lg"
            >
              {{ Math.round(meOutsideTop.total) }}
            </span>
          </button>
        </template>
      </template>
    </DataStatus>

    <!--
      Ball tafsiloti: pastdan chiquvchi YARIM varaq (`BaseSheet`).

      🔴 XATO TUZATILDI (2026-08-13, R10). Bu yerda `BaseModal` `sheet`
         PROP'ISIZ chaqirilgan edi va yuqoridagi izohda "desktop uchun hech
         narsa kerak emas" deb yozilgan — DESKTOP haqiqatan to'g'ri edi,
         TELEFON esa yo'q: `BaseModal.vue:99-103` `sm:` DAN PASTDA birorta
         `items-*` klassi bermaydi, ya'ni flex konteynerda `align-items`
         sukut bo'yicha `stretch` bo'lib qoladi va panel 390px lik ekranda
         TO'LIQ balandlikni egallaydi. Ichida esa bor-yo'g'i to'rt qator —
         o'quvchi to'rt qator uchun butun reyting ro'yxatini yo'qotardi.

      ★ NEGA `BaseModal` GA `sheet` BERILMADI: `sheet` rejimi ham
        `max-h-dvh` bilan qoladi (balandlik kontentga qarab) va foni
        `bg-slate-900/35 backdrop-blur-sm` — talab esa AYNAN "background
        transparent" va "yarmicha oyna" deydi. `BaseSheet` shu ikki shartni
        o'z ichiga oladi; 30+ `BaseModal` chaqiruv joyiga esa TEGILMAYDI.
    -->
    <BaseSheet
      :open="detailRow !== null"
      :title="detailRow?.studentName ?? 'Ball tafsiloti'"
      @close="detailRow = null"
    >
      <p class="text-xs text-slate-400">
        <!-- R24 dan keyin mezon TO'RTTA, lekin son ATAYLAB yozilmadi:
             "—" bo'lgan mezon o'rtachaga umuman kirmaydi, ya'ni aniq
             son har oy o'zgarib turadi va yolg'on gapirardi. -->
        Yakuniy ball mezonlarning o‘rtachasi. “—” — shu oyda bu mezon
        bo‘yicha ma’lumot yo‘q (nol emas) va u o‘rtachaga kirmaydi.
      </p>

      <dl
        v-if="detailRow !== null"
        class="mt-3 space-y-2"
      >
        <div
          v-for="part in scoreParts(detailRow)"
          :key="part.label"
          class="flex items-center justify-between rounded-xl border border-line bg-ink-950 px-3.5 py-2.5"
        >
          <dt
            class="text-[13px] text-slate-300"
            v-text="part.label"
          />
          <dd
            class="text-sm font-bold tabular-nums text-slate-100"
            v-text="formatPercent(part.percent)"
          />
        </div>

        <div
          class="flex items-center justify-between rounded-xl border border-brand-500/40 bg-brand-500/10 px-3.5 py-2.5"
        >
          <dt class="text-[13px] font-bold text-brand-200">
            Yakuniy ball
          </dt>
          <dd class="text-base font-extrabold tabular-nums text-brand-300">
            {{ Math.round(detailRow.total) }}
          </dd>
        </div>
      </dl>
    </BaseSheet>
  </div>
</template>
