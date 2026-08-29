<script setup lang="ts">
import { useQuery } from '@tanstack/vue-query'
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'

import { fetchMyAssignments } from '@/entities/assignment'
import { fetchBlockStatus } from '@/entities/payment'
import { fetchAttendanceSummary } from '@/entities/progress'
import { homeRouteFor } from '@/entities/user'
import { useAuthStore } from '@/features/auth/model/auth.store'
import {
  hasGreetedBefore,
  markGreeted,
  NuriMascot,
  pickGreeting,
  useTypewriter,
} from '@/features/nuri-greeting'
import type { GreetingMessage } from '@/features/nuri-greeting'
import { sessionState, useStudentSchedule } from '@/features/student-schedule/model/useStudentSchedule'
import { useNow } from '@/shared/lib/use-now'
import { useMediaQuery } from '@/shared/lib/useBreakpoint'
import { BaseButton } from '@/shared/ui'

/**
 * ════════════════════════════════════════════════════════════════════════
 * SALOMLASHUV EKRANI — «Nuri» o'quvchini kutib oladi (2026-08-30)
 * ════════════════════════════════════════════════════════════════════════
 *
 * Loyiha egasining talabi: kirishdan keyin o'quvchi darhol bosh sahifaga
 * tushmasin. Maskot (boyqush «Nuri») uni ISM bilan kutib oladi va gapi
 * O'QUVCHINING HOLATIGA qarab o'zgaradi.
 *
 * ══════════════════════════════════════════════════════════════════════
 * 1) KIMGA KO'RSATILADI — FAQAT O'QUVCHIGA (`roles: STUDENT`).
 *
 * Xodim (ustoz, kurator, o'quv bo'limi, admin) panelga ISHLASH uchun
 * kiradi va kuniga bir necha marta kiradi — unga har safar salomlashuv
 * ekrani ish yo'lidagi to'siq bo'lardi. Uning ustiga bu ekranning butun
 * mazmuni o'quvchi ma'lumotidan yasaladi (davomat, vazifa, jadval,
 * qarz) — xodimda bularning birortasi ham yo'q va u BO'SH salomni
 * ko'rardi, ya'ni ekran mazmunsiz qolardi.
 *
 * ⚠️ Marshrutdagi `roles: STUDENT` — himoyaning O'ZI: xodim manzilni
 *    qo'lda yozsa ham qo'riqchi uni o'z bosh sahifasiga qaytaradi.
 *
 * ══════════════════════════════════════════════════════════════════════
 * 2) QACHON KO'RSATILADI — KUNIGA BIR MARTA, VA FAQAT KIRISH ORQALI.
 *
 * Kuniga bir marta bo'lishining sababi `greeting-seen.ts` da yozilgan.
 *
 * ★ EKRANGA FAQAT `LoginPage` YUBORADI, global qo'riqchi EMAS. Bu ataylab
 *   qilingan chekinish: qo'riqchiga "o'quvchi bugun salomlashmagan bo'lsa
 *   uni /salom ga burib yubor" degan qoida qo'shilsa, u HAR navigatsiyada
 *   ishlardi — ya'ni chuqur havola (dars, vazifa, bildirishnomadagi
 *   manzil) bo'yicha kirgan o'quvchi ham salomlashuvga tashlanardi va
 *   mo'ljalidan mahrum bo'lardi. Sahifani yangilash ham xuddi shunday
 *   bo'lardi.
 *
 * ⚠️ BUNING NARXI OCHIQ: brauzerda sessiyasi saqlanib qolgan o'quvchi
 *    (refresh token bilan avtomatik kirgan) salomlashuvni KO'RMAYDI —
 *    u kirish oqimidan o'tmaydi. Asosiy kanal Telegram Mini App bo'lgani
 *    va u HAR ochilishda qaytadan kirgani uchun bu amalda kam odamga
 *    tegadi. Qamrovni kengaytirish kerak bo'lsa to'g'ri joy — `StudentShell`
 *    ichidagi qoplama, marshrut qo'riqchisi EMAS.
 *
 * ══════════════════════════════════════════════════════════════════════
 * 3) MA'LUMOT KUTILADI, LEKIN CHEKSIZ EMAS.
 *
 * To'rt so'rov ketadi va ularsiz gap tanlab bo'lmaydi. Lekin salomlashuv
 * — YUKLANISH EKRANI EMAS: mobil internet sekin bo'lsa o'quvchi
 * boyqushga qarab turishi mumkin emas. Shuning uchun ikki chegara bor:
 *   • `MASCOT_MS` — eng KAM kutish. Maskot paydo bo'lish animatsiyasi
 *     tugasin, keyin pufak chiqsin (aks holda kesh to'liq bo'lganda
 *     hammasi bir zumda "paydo bo'lib qolardi").
 *   • `MAX_WAIT_MS` — eng KO'P kutish. Shundan keyin gap KELGAN
 *     ma'lumot bilan tanlanadi; kelmagani `null` bo'lib qoladi va
 *     `pickGreeting` u holatni o'zi biladi (zaxira salom).
 *
 * 🔴 GAP BIR MARTA HISOBLANADI VA MUZLATILADI (`message` — `ref`,
 *    `computed` EMAS). Aks holda kechikkan so'rov javobi kelganda matn
 *    YOZILAYOTGAN PAYTDA almashib ketardi.
 */

const route = useRoute()
const router = useRouter()
const auth = useAuthStore()

/*
  ★ ID BIR MARTA OLINADI: bu sahifaga faqat kirgan o'quvchi tushadi
  (`requiresAuth` + `roles`), ya'ni qiymat sahifa umri davomida
  o'zgarmaydi. `null` — bo'lishi mumkin bo'lmagan holat, lekin unda
  so'rov ham, belgi ham yozilmaydi (ekran baribir ishlaydi).
*/
const studentId = auth.userId

const now = useNow()
const reducedMotion = useMediaQuery('(prefers-reduced-motion: reduce)')

/**
 * Shu o'quvchi ILGARI salomlashganmi — «tanishuv» matnini tanlash uchun.
 *
 * 🔴 QIYMAT SHU YERDA, `markGreeted` DAN OLDIN OLINADI. Belgi quyida,
 *    `onMounted` da YOZILADI — ya'ni keyinroq (`reveal` ichida) so'ralsa
 *    javob HAR DOIM "ha" bo'lib chiqardi va birinchi kirish matni hech
 *    qachon ko'rinmasdi. Bu tartib TASODIF EMAS, shartning o'zi:
 *    o'qish yozishdan oldin.
 */
const greetedBefore = studentId !== null && hasGreetedBefore(studentId)

/* ═══════════════════════════════════════════════════════ MA'LUMOT ═════ */

/*
  ★ `queryKey` LAR BOSH SAHIFANIKI BILAN AYNAN BIR XIL
  (`['live-sessions']`, `['progress','attendance']`, `['assignments','mine']`).
  Shu tufayli bu ekran "ortiqcha so'rov" emas, OLDINDAN YUKLASH bo'lib
  qoladi: o'quvchi «Boshladik!» ni bosganda bosh sahifa allaqachon
  keshdan chiziladi.
*/
const schedule = useStudentSchedule(now)

const attendanceQuery = useQuery({
  queryKey: ['progress', 'attendance'],
  queryFn: ({ signal }) => fetchAttendanceSummary({}, { signal }),
})

const assignmentsQuery = useQuery({
  queryKey: ['assignments', 'mine'],
  queryFn: ({ signal }) => fetchMyAssignments({ signal }),
})

/*
  QARZ — `GET /payments/students/{id}/block` orqali.

  ★ NEGA `fetchStudentAccount` EMAS: u AYNI qarz raqamini beradi, lekin
    yoniga oylar tarixini va moliya jurnalini ham qo'shadi — bitta son
    uchun o'nlab qatorli javob. `block` javobi esa kichik (qarz, chegara,
    istisno bayrog'i) va ruxsati AYNI (`EnsureCanViewStudentAsync`:
    o'quvchi O'Z hisobini ko'radi).

  ★ `scope: 'Video'` — endpointning O'Z standart qiymati. Bu yerda
    `blocked` bayrog'i UMUMAN o'qilmaydi (bloklash haqidagi xabarni
    server o'zi, o'z joyida beradi), faqat `debt` va `exempt` kerak —
    ular esa qamrovga bog'liq emas.

  🔴 `retry: false`: 403 kelsa (moliya moduli yopilgan yoki qoida
     o'zgargan) uni uch marta takrorlash foydasiz. Xato bo'lsa qarz
     `null` bo'lib qoladi va salomlashuv shu holatsiz davom etadi —
     foydalanuvchi hech qanday xato ko'rmaydi.
*/
const blockQuery = useQuery({
  queryKey: ['payments', 'block', studentId],
  queryFn: ({ signal }) => fetchBlockStatus(studentId ?? 0, 'Video', { signal }),
  enabled: studentId !== null,
  retry: false,
})

/**
 * Qarz summasi.
 *
 * ★ ISTISNO QILINGAN O'QUVCHIDA `null`: server uni to'lovdan ozod qilgan,
 * ya'ni raqamning unga hech qanday ma'nosi yo'q va eslatma faqat
 * chalkashtirardi.
 */
const debt = computed(() => {
  const status = blockQuery.data.value
  if (status === undefined || status.exempt) return null
  return status.debt
})

/** To'rt so'rovning hammasi javob berdimi (xato ham javob hisoblanadi). */
const dataSettled = computed(
  () =>
    !schedule.isPending.value
    && !attendanceQuery.isPending.value
    && !assignmentsQuery.isPending.value
    // O'chirilgan so'rov abadiy `pending` bo'lib qoladi — uni kutmaymiz.
    && (studentId === null || !blockQuery.isPending.value),
)

/* ══════════════════════════════════════════════════════ SAHNALASH ═════ */

/** Maskot paydo bo'lish animatsiyasining davomiyligi (`animate-pop`). */
const MASCOT_MS = 520

/** Ma'lumotni eng ko'pi bilan shuncha kutamiz. */
const MAX_WAIT_MS = 1400

const minElapsed = ref(false)
const maxElapsed = ref(false)

const message = ref<GreetingMessage | null>(null)
const typer = useTypewriter()

/**
 * Hozir jonli ketayotgan dars.
 *
 * `nextAny` jonli darsni boshqalardan ustun qo'yadi (`useStudentSchedule`),
 * shuning uchun uning O'ZINI tekshirish yetarli — alohida qidiruv kerak
 * emas.
 */
const liveSession = computed(() => {
  const next = schedule.nextAny.value
  if (next === null) return null
  return sessionState(next, now.value) === 'live' ? next : null
})

function reveal(): void {
  // Bir marta — yuqoridagi "muzlatish" qoidasi.
  if (message.value !== null) return

  const picked = pickGreeting({
    fullName: auth.displayName,
    greetedBefore,
    now: now.value,
    liveSession: liveSession.value,
    nextSession: schedule.nextAny.value,
    attendance: attendanceQuery.data.value ?? null,
    assignments: assignmentsQuery.data.value ?? null,
    debt: debt.value,
  })

  message.value = picked
  typer.start(picked.text)
}

watch(
  [minElapsed, maxElapsed, dataSettled],
  () => {
    if (!minElapsed.value) return
    if (dataSettled.value || maxElapsed.value) reveal()
  },
  { immediate: true },
)

let minTimer: ReturnType<typeof setTimeout> | null = null
let maxTimer: ReturnType<typeof setTimeout> | null = null

onMounted(() => {
  /*
    ★ BELGI EKRAN OCHILGANDA yoziladi (tugma bosilganda emas) — sabab
    `markGreeted` izohida.
  */
  if (studentId !== null) markGreeted(studentId, now.value)

  /*
    🔴 HARAKAT KAMAYTIRILGANDA SAHNALASH YO'Q: `prefers-reduced-motion`
       da maskotning paydo bo'lish animatsiyasi baribir bajarilmaydi
       (`style.css` dagi global qoida), ya'ni yarim soniya kutish
       hech qanday effekt bermay, shunchaki kechikish bo'lib qolardi.
  */
  minTimer = setTimeout(() => {
    minElapsed.value = true
  }, reducedMotion.value ? 0 : MASCOT_MS)

  maxTimer = setTimeout(() => {
    maxElapsed.value = true
  }, MAX_WAIT_MS)
})

onBeforeUnmount(() => {
  if (minTimer !== null) clearTimeout(minTimer)
  if (maxTimer !== null) clearTimeout(maxTimer)
})

/* ════════════════════════════════════════════════════════ TO'Q SIRT ═══ */

/*
  ★ TO'Q YASHIL SAHNA — `[data-surface='brand']` (`style.css` dagi mavjud
    blok, landing hero uchun yasalgan). Yangi rang e'lon qilinmaydi:
    o'sha blok `--color-ink-*`, `--color-slate-*` va `--color-brand-*`
    ni to'q sirt uchun qayta belgilaydi, ya'ni bu sahifadagi oddiy
    `bg-ink-900`, `text-slate-400`, `bg-brand-500` klasslari o'z-o'zidan
    to'g'ri chiqadi. Aksent esa SHAMPAN bo'lib qoladi — namunadagi sariq
    tugma va sariq «NURI» yorlig'i aynan shu.

  ★ ATRIBUT `<html>` GA QO'YILADI, sahifa `<div>` iga emas — `StudentShell`
    dagi tema bilan AYNI sabab: `<body>` foni ham (`html` dagi
    `background-color`) shu sirtdan olinadi, aks holda telefonda ortiqcha
    surilganda (overscroll) yorug' chiziq ko'rinardi.

  ⚠️ `<meta name="theme-color">` ATAYLAB TEGILMAYDI. Uni `StudentShell`
     ham o'zgartiradi va eski qiymatni O'ZIDA saqlaydi; bu sahifa ham
     aralashsa, ikki komponentning mount/unmount tartibiga bog'liq
     "kim kimning qiymatini saqlab qoldi" degan nozik xato paydo
     bo'lardi. Ekran bir necha soniya turadi — brauzer manzil panelining
     rangi bunga arzimaydi.
*/
onMounted(() => {
  document.documentElement.dataset['surface'] = 'brand'
})

onBeforeUnmount(() => {
  delete document.documentElement.dataset['surface']
})

/* ═══════════════════════════════════════════════════════ DAVOM ETISH ══ */

/**
 * Salomlashuvdan keyingi manzil.
 *
 * 🔴 FAQAT ICHKI YO'L: `?keyin=` ni `LoginPage` uzatadi va u yerda ham
 *    tekshiriladi, lekin manzilni foydalanuvchi ham yozishi mumkin —
 *    ochiq yo'naltirish zaifligining oldi shu yerda ham olinadi
 *    (`//host` shakli tashqi manzil).
 */
function nextTarget(): string | { name: string } {
  const raw = route.query['keyin']
  const value = Array.isArray(raw) ? raw[0] : raw
  if (typeof value === 'string' && value.startsWith('/') && !value.startsWith('//')) return value
  return { name: homeRouteFor(auth.role) }
}

/**
 * ★ `replace`, `push` EMAS: salomlashuv — o'tish nuqtasi. `push` bo'lsa
 * "orqaga" tugmasi o'quvchini bugun ko'rib bo'lgan ekraniga qaytarardi.
 */
async function goOn(): Promise<void> {
  await router.replace(nextTarget())
}
</script>

<template>
  <!--
    Butun ekranga bosish — matnni darhol tugatadi.

    ★ BU QO'SHIMCHA QULAYLIK, YAGONA YO'L EMAS: tugma matn yozilishini
      KUTMAYDI (pastdagi izohga qarang), ya'ni klaviatura bilan
      ishlaydigan foydalanuvchi hech narsa yo'qotmaydi va bu `div` ga
      alohida rol/klaviatura ishlovchisi kerak emas.
  -->
  <div
    class="flex min-h-dvh flex-col items-center justify-center bg-ink-950 px-6 py-10 text-slate-100"
    @click="typer.finish()"
  >
    <div class="w-full max-w-sm">
      <!--
        Maskot. `size-40` (160px) — telefonda ekranning uchdan biri, ya'ni
        u "ikonka" emas, PERSONAJ bo'lib ko'rinadi.
      -->
      <NuriMascot class="mx-auto size-40" />

      <!--
        ══════════════════ GAP PUFAGI ══════════════════
        Pufak matn TANLANGANDAN KEYIN paydo bo'ladi: shu tartibda sahna
        "maskot -> pufak -> yozuv" bo'lib chiqadi (loyiha egasining
        talabi). Ma'lumot kutilayotgan yarim soniyada ekranda yolg'iz
        boyqush turadi — bu bo'shliq emas, kirish qismi.
      -->
      <div
        v-if="message !== null"
        class="relative mt-7 animate-pop rounded-[1.5rem] border border-line bg-ink-900 px-5 pb-5 pt-6 shadow-lg"
      >
        <!--
          «NURI» yorlig'i — pufakning YUQORI QIRRASIDA turadi va shu
          bilan uni tepadagi maskot bilan bog'laydi. Klassik "quyruq"
          (uchburchak) o'rniga shu tanlandi: yorliq ham bog'lovchi, ham
          "kim gapiryapti" degan savolga javob — ikki vazifa bitta
          elementda.
        -->
        <span
          class="absolute -top-2.5 left-1/2 -translate-x-1/2 rounded-full bg-brand-500 px-2.5 py-[3px] text-[10px] font-bold uppercase tracking-[1.4px] text-on-brand"
        >
          Nuri
        </span>

        <!--
          ══════════════════ HARFMA-HARF YOZILISH ══════════════════

          🔴 IKKI QATLAM — SAKRASHNING OLDINI OLADI. Matn o'sib borsa
             pufak ham qator-qator o'sardi va pastdagi tugma har safar
             pastga sakrardi. Shuning uchun:
               • ko'rinmas qatlam (`invisible`) TO'LIQ matnni tutadi va
                 pufak balandligini BOSHIDANOQ yakuniy o'lchamga
                 qo'yadi;
               • ustidagi mutlaq qatlam esa yozilayotgan qismni
                 ko'rsatadi.

          ★ `invisible` (`visibility: hidden`) — `opacity-0` EMAS: ikkinchisi
            matnni sichqoncha bilan tanlashga ochiq qoldirardi va bir xil
            gap ekranda ikki marta nusxalanardi.

          🔴 SKRINRIDER UCHUN UCHINCHI, `sr-only` QATLAM. Sabab: yuqoridagi
             ikkala ko'rinadigan qatlam ham unga YARAMAYDI — `invisible`
             butunlay a11y daraxtidan chiqadi, yozilayotgani esa HAR
             HARFDA o'zgaradi va yordamchi texnologiya chala gapni o'qib
             qolardi. `sr-only` qatlam esa boshidanoq TO'LIQ gapni tutadi
             va joylashuvga ta'sir qilmaydi (`position: absolute`).

          ★ MATN CHAPGA TEKISLANGAN: markazlashtirilgan matnda har yangi
            harf butun qatorni siljitadi va yozuv "chayqalib" ko'rinardi.
        -->
        <p class="relative text-left text-[15px] leading-relaxed text-slate-100">
          <span
            class="sr-only"
            v-text="message.text"
          />
          <span
            class="invisible"
            aria-hidden="true"
            v-text="message.text"
          />
          <span
            class="absolute inset-0"
            aria-hidden="true"
          >
            {{ typer.visible.value }}<span
              v-if="!typer.done.value"
              class="ml-0.5 inline-block h-[1.05em] w-[2px] translate-y-[0.15em] animate-pulse rounded-full bg-brand-500 align-middle"
              aria-hidden="true"
            />
          </span>
        </p>
      </div>

      <!--
        ══════════════════ «BOSHLADIK!» ══════════════════

        ★ TUGMA MATN TUGASHINI KUTMAYDI. Kutgan bo'lsa, taymer to'xtagan
          har holat (yorliq fonga o'tganda brauzer JS taymerini
          sekinlashtiradi) ekranni BOSHI BERK ko'chaga aylantirardi.
          Endi eng yomon holatda o'quvchi gapni o'qib bo'lmasdan davom
          etadi — bu yo'qotish, lekin qulflanib qolish emas.

        ★ YOZUV BARCHA HOLATLARDA BIR XIL («Boshladik!») va manzil ham
          bir xil. Har holat uchun boshqa tugma qo'yish mumkin edi
          (masalan «Vazifaga o'tish»), lekin unda bitta ko'rinishdagi
          tugma har kuni boshqa joyga olib borardi — kirishdan keyingi
          birinchi bosish esa oldindan aytib bo'ladigan bo'lishi kerak.
          Nima kutayotganini GAPNING O'ZI aytadi.
      -->
      <BaseButton
        v-if="message !== null"
        class="mt-6 animate-fade-up"
        size="lg"
        block
        @click="goOn"
      >
        Boshladik!
      </BaseButton>
    </div>
  </div>
</template>
