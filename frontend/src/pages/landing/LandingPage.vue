<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, ref, useTemplateRef } from 'vue'

import { EnrollmentRequestForm } from '@/features/enrollment-request'
import { LevelTestModal } from '@/features/level-test'
import { COURSE_FACTS } from '@/shared/config/course-facts'
import { AppIcon, BaseButton } from '@/shared/ui'

import {
  BOOKS,
  BOT_LINK,
  CONTACT,
  COURSE_OPTIONS,
  COURSE_PATH,
  DECOR_LETTERS,
  FAQ,
  FEATURES,
  FREE_LESSON,
  HERO,
  HERO_CARDS,
  HERO_LETTER,
  LEVEL_TEST,
  OUTCOMES,
  PRICE,
  SOCIALS,
  STATS,
  STEPS,
  WEEK,
  WEEK_DAYS,
} from './model/content'

/*
  ══════════════════════════════════════════════════════════════════════════
  LANDING SAHIFA — loyiha egasining qarori
  ══════════════════════════════════════════════════════════════════════════

  ★ NIMA UCHUN QURILDI: ilgari `/` manzili KIRMAGAN odamni darhol
    `/login` ga tashlardi. Ya'ni saytga birinchi marta kelgan odam
    "kimningdir ichki tizimi" ni ko'rardi va u yerdan qaytib ketardi.

  🔴 KIRGAN FOYDALANUVCHI BU SAHIFANI KO'RMAYDI. Marshrut qo'riqchisi
     (`app/router/index.ts` dagi `beforeEnter`) uni rolga mos bosh
     sahifaga yo'naltiradi.

  ★ MATN SHU YERDA EMAS, `model/content.ts` DA.

  ══════════════════════════════════════════════════════════════════════════
   2026-08-29 — TUZILMA QAYTA ISHLANDI
  ══════════════════════════════════════════════════════════════════════════

  Sahifa endi PLATFORMANI emas, KURSNI sotadi (sabab `content.ts` da).
  Bo'limlar tartibi sotuv skriptining mantig'ini takrorlaydi:

     hero (va'da)  ->  bepul dars (ishonch)  ->  natija  ->  hafta qanday
     o'tadi  ->  afzalliklar  ->  kitob bonusi  ->  narx  ->  ariza  ->  savollar

  ★ NIMA UCHUN BEPUL DARS SHUNCHA YUQORIDA: odam pul haqidagi gapdan
    OLDIN "bu ustoz qanday tushuntiradi?" degan savolga javob olishi
    kerak. Narxni yuqoriga chiqarish — arizani yo'qotishning eng tez yo'li.

  ★ NIMA UCHUN NARX YASHIRILMADI: skript narxni ochiq aytadi va uni
    bitta darsning summasiga bo'lib ko'rsatadi (67 500 so'm).
    Yashirilgan narx qo'ng'iroqni ko'paytiradi, lekin ishonchni
    kamaytiradi.

    ⚠️ 2026-08-30 gacha bu yerda "kunlik summa (18 000 so'm)" deb
       yozilgan edi. Narx endi KUNGA emas, DARSGA bo'linadi — sabab
       `content.ts` dagi `PRICE` izohida.
*/

/**
 * Sahifa pastga surilganmi — yuqori panel fonini o'zgartirish uchun.
 *
 * ★ NEGA KERAK: panel shaffof va hero ustida "suzib" turadi. Kontent
 * uning ostiga kirganda matn matnga qo'shilib ketardi.
 */
const isScrolled = ref(false)

/** Mobil menyu ochiqmi. */
const isMenuOpen = ref(false)

/**
 * Video hali bosilmaganmi.
 *
 * 🔴 IFRAME DARHOL QO'YILMAYDI — ATAYLAB. YouTube o'rnatmasi sahifa
 * ochilishi bilan ~1 MB skript va bir nechta tashqi so'rov olib keladi,
 * ya'ni landing'ning ochilish tezligini (va mobil trafikni) buzadi.
 * Shuning uchun avval YENGIL poster ko'rsatiladi va iframe FAQAT
 * foydalanuvchi bosganda yaratiladi.
 *
 * ★ Poster tasviri YouTube'ning o'z CDN'idan (`img.youtube.com`) keladi —
 * bu ~15 KB jpg, skript emas.
 */
const isVideoPlaying = ref(false)

/**
 * Video posteri.
 *
 * ⚠️ 2026-08-30 — SIFAT TUZATILDI. Ilgari bu yerda `hqdefault.jpg`
 * qotib turardi va u 480×360, ya'ni 4:3. Karta esa 16:9 — natijada
 * tasvir CHO'ZILIB kesilardi (tepasi va pasti yo'qolardi) va katta
 * ekranda ustiga-ustak xiralashardi.
 *
 * ★ `maxresdefault.jpg` — 1280×720, aynan 16:9.
 *
 * 🔴 LEKIN U HAR DOIM MAVJUD EMAS: YouTube bu o'lchamni faqat yuqori
 * sifatli yuklamalar uchun yasaydi va bo'lmaganda 404 qaytaradi
 * (brauzerda buzuq rasm belgisi ko'rinadi). Shuning uchun `@error`
 * bo'yicha `hqdefault` ga tushamiz — u HAR DOIM bor.
 */
const posterSrc = ref(
  `https://img.youtube.com/vi/${FREE_LESSON.youtubeId}/maxresdefault.jpg`,
)

function onPosterError(): void {
  const fallback = `https://img.youtube.com/vi/${FREE_LESSON.youtubeId}/hqdefault.jpg`

  // Zaxira ham xato bersa cheksiz tsikl bo'lmasin.
  if (posterSrc.value === fallback) return

  posterSrc.value = fallback
}

/*
  ══════════════════════════════════════════════════════════════════════════
  TELEFONDAGI DOIMIY «KURSGA YOZILISH» PANELI (2026-08-30)
  ══════════════════════════════════════════════════════════════════════════

  🔴 MUAMMO: ariza qoldirish imkoni sahifaning ENG OXIRIDA edi. Telefonda
     bu to'qqizta bo'limni aylantirib o'tish degani — odam yo'lda qiziqib
     qolsa ham, unga yetib borish uchun ish qilishi kerak edi.

  Panel ikki shart BIR VAQTDA bajarilganda ko'rinadi:

    1) odam hero'dan pastga o'tgan — yuqorida panel keraksiz, u yerda
       katta «Kursga yozilish» tugmasi allaqachon turibdi;
    2) ariza bo'limi EKRANDA EMAS — aks holda panel aynan o'sha formani,
       ko'pincha «Yuborish» tugmasini to'sib turardi.

  ★ IKKINCHI SHART UCHUN `IntersectionObserver`, `getBoundingClientRect`
    EMAS: ikkinchisi har skroll hodisasida brauzerni sahifa
    o'lchamlarini QAYTA HISOBLASHGA majbur qiladi (layout thrashing).
    Kuzatuvchi esa hisobni brauzerning o'ziga qoldiradi va faqat holat
    o'zgarganda xabar beradi.
*/
const isPastHero = ref(false)
const isEnrollVisible = ref(false)

/*
  IKKI FORMA HAM KUZATILADI: qisqasi bepul darsdan keyin, to'lig'i
  sahifa oxirida. Faqat bittasi kuzatilsa, panel ikkinchisining ustiga
  tushib, «Yuborish» tugmasini to'sib qolardi.
*/
const quickEnrollSection = useTemplateRef<HTMLElement>('quickEnrollSection')
const enrollSection = useTemplateRef<HTMLElement>('enrollSection')

/** Panel ko'rinadigan yagona holat. */
const showStickyCta = computed(() => isPastHero.value && !isEnrollVisible.value)

let enrollObserver: IntersectionObserver | null = null

/**
 * Ayni paytda ekranda turgan formalar.
 *
 * ★ NEGA TO'PLAM, oddiy `boolean` EMAS: ikkita nishon kuzatiladi va
 * kuzatuvchi ular haqida ALOHIDA xabar beradi. Bitta bayroq bilan
 * ishlansa, bir forma ekrandan chiqishi ikkinchisi hamon ko'rinib
 * turganda ham bayroqni o'chirib yuborardi.
 */
const visibleEnrollForms = new Set<Element>()

function onScroll(): void {
  isScrolled.value = window.scrollY > 8

  // 70vh — hero balandligiga yaqin. Aniq o'lchash shart emas: bu yerda
  // "yuqorida turibdimi" degan qo'pol savolga javob yetarli.
  isPastHero.value = window.scrollY > window.innerHeight * 0.7
}

onMounted(() => {
  onScroll()
  // `passive` — brauzer skroll'ni to'xtatib kutmasin (jankka qarshi).
  window.addEventListener('scroll', onScroll, { passive: true })

  enrollObserver = new IntersectionObserver(
    (entries) => {
      for (const entry of entries) {
        if (entry.isIntersecting) visibleEnrollForms.add(entry.target)
        else visibleEnrollForms.delete(entry.target)
      }

      isEnrollVisible.value = visibleEnrollForms.size > 0
    },
    // Bo'limning kichik bir qismi ko'rinishi ham yetarli: panel forma
    // ekranga KIRA boshlaganda yo'qolsin, to'liq ochilguncha kutmasin.
    { threshold: 0.01 },
  )

  for (const target of [quickEnrollSection.value, enrollSection.value]) {
    if (target !== null) enrollObserver.observe(target)
  }

  /*
    AKTIV BO'LIM KUZATUVCHISI (sabab `activeSection` izohida).

    Tasma: yuqoridan 96px (panel balandligi + kichik zaxira) va
    ekranning 30% chizig'igacha.

    ⚠️ PASTKI CHEGARA FOIZDA, PIKSELDA EMAS: past ekranda (masalan
       yotiq holatdagi telefon) qat'iy piksel tasmani teskari qilib
       yuborardi — ya'ni balandligi manfiy bo'lib, hech qachon hech
       narsa kesishmasdi va menyu umuman javob bermay qolardi.
  */
  sectionObserver = new IntersectionObserver(
    (entries) => {
      for (const entry of entries) {
        const href = `#${entry.target.id}`

        if (entry.isIntersecting) visibleSections.add(href)
        else visibleSections.delete(href)
      }

      /*
        Bir vaqtda ikkitasi kesishsa — PASTKISI tanlanadi: odam pastga
        qarab surilyapti va yangi bo'lim aynan pastdan keladi.
      */
      const current = [...NAV]
        .reverse()
        .find(item => visibleSections.has(item.href))

      if (current !== undefined) activeSection.value = current.href
    },
    { rootMargin: '-96px 0px -70% 0px' },
  )

  for (const item of NAV) {
    const target = document.querySelector(item.href)
    if (target !== null) sectionObserver.observe(target)
  }
})

onBeforeUnmount(() => {
  window.removeEventListener('scroll', onScroll)
  enrollObserver?.disconnect()
  sectionObserver?.disconnect()
})

const NAV: readonly { href: string, label: string }[] = [
  { href: '#dars', label: 'Bepul dars' },
  { href: '#natija', label: 'Natija' },
  { href: '#kurs', label: 'Kurs tuzilmasi' },
  { href: '#daraja', label: 'Daraja testi' },
  { href: '#narx', label: 'Narx' },
  { href: '#savollar', label: 'Savollar' },
]

/*
  ══════════════════════════════════════════════════════════════════════════
  AKTIV BO'LIM — YUQORI PANELDA JOYNI KO'RSATISH (2026-09-03)
  ══════════════════════════════════════════════════════════════════════════

  ★ NIMA UCHUN: sahifa uzun (o'nta bo'lim) va menyu punktlari hech
    qachon o'zgarmasdi — ya'ni odam "men qayerdaman?" degan savolga
    javob topmasdi va menyu shunchaki havolalar ro'yxati edi.

  ★ NEGA `IntersectionObserver`, `scroll` + `getBoundingClientRect` EMAS:
    ikkinchisi har kadrda oltita bo'limning o'lchamini SO'RAYDI va
    brauzerni tartibni qayta hisoblashga majbur qiladi. Kuzatuvchi esa
    hisobni brauzerning o'ziga qoldiradi (ayni sabab pastdagi doimiy
    panel izohida ham).

  ★ TOR TASMA (`rootMargin`): kuzatuv butun ekran bo'ylab emas, panel
    ostidagi ingichka gorizontal tasmada ishlaydi. Bo'limlar bir-birining
    ketidan zich turgani uchun bu tasmani odatda BITTA bo'lim qoplaydi —
    ya'ni "aktiv" tushunchasi bir ma'noli chiqadi.

  🔴 NOMSIZ BO'LIMLARDA OLDINGI NOM SAQLANADI. Sahifada menyuda yo'q
     bo'limlar ham bor (qisqa ariza formasi, kitob bloki). Ular
     kuzatilmaydi va o'sha yerdan o'tayotganda to'plam bo'shab qoladi —
     shunda oxirgi aktiv nom o'z joyida turadi. Aks holda menyudagi
     belgi o'sha bo'limlarda "o'chib-yonib" ketardi.
*/
const activeSection = ref<string | null>(null)

/**
 * Ayni paytda kuzatuv tasmasini kesib turgan bo'limlar.
 *
 * ★ NEGA TO'PLAM: chegarada ikkita bo'lim birga tushishi mumkin va
 * kuzatuvchi ular haqida ALOHIDA xabar beradi.
 */
const visibleSections = new Set<string>()

let sectionObserver: IntersectionObserver | null = null

/**
 * Langar havolasiga o'tish.
 *
 * ★ NEGA QO'LDA, oddiy `href="#..."` YETARLI EMAS: ilova
 * `createWebHistory` bilan ishlaydi va router `#narx` ni MARSHRUT
 * fragmenti deb qabul qilib, sahifani qaytadan hal qilardi.
 *
 * ★ `scrollIntoView` MANZILNI O'ZGARTIRMAYDI: bu ataylab, aks holda
 * brauzerning «orqaga» tugmasi bo'limlar bo'ylab yurib, foydalanuvchini
 * saytdan chiqara olmay qolardi.
 */
function scrollToSection(href: string): void {
  isMenuOpen.value = false

  const target = document.querySelector(href)
  target?.scrollIntoView({ behavior: 'smooth', block: 'start' })
}

/*
  ══════════════════════════════════════════════════════════════════════════
  DARAJA ANIQLASH TESTI (2026-09-03)
  ══════════════════════════════════════════════════════════════════════════

  Test o'zi `features/level-test` da; bu yerda faqat uni ochish va
  natijasini ariza formasiga uzatish.

  ★ NEGA MODAL, ALOHIDA SAHIFA EMAS: alohida sahifa odamni landing'dan
    OLIB CHIQADI va u test tugagach qaytib kelishi kerak bo'lardi.
    Ko'pchilik qaytmaydi. Modal esa sahifani joyida qoldiradi — yopilgach
    odam aynan o'zi to'xtagan bo'limda turadi.
*/
const isLevelTestOpen = ref(false)

/*
  Ariza formasining nusxasi.

  ★ NEGA AYNAN PASTKI (to'liq) FORMA: qisqa formada «Yo'nalish» va «Izoh»
    maydonlari YO'Q (`EnrollmentRequestForm` dagi `compact`), ya'ni test
    natijasini yozadigan joy ham yo'q.
*/
const enrollForm = useTemplateRef<InstanceType<typeof EnrollmentRequestForm>>(
  'enrollForm',
)

/**
 * Test natijasini arizaga ko'chirish va formaga olib tushish.
 *
 * ⚠️ `courseMatch` — TO'LIQ nom emas, BOSHLANISHI ('ATF', 'Amaliyot II').
 *    Sabab: `COURSE_OPTIONS` dagi qatorlar davomiylik bilan yozilgan
 *    ("ATF — 8 oylik asosiy kurs") va testdagi mantiq ularning aniq
 *    matniga bog'lanib qolmasligi kerak. Bu yerda mos qator topiladi.
 */
function onLevelTestApply(payload: { course: string, note: string }): void {
  const matched = COURSE_OPTIONS.find(option => option.startsWith(payload.course))

  enrollForm.value?.applyLevelResult({
    course: matched ?? '',
    note: payload.note,
  })

  /*
    Modal yopilishi bilan darhol emas, keyingi kadrda suriladi: modal
    `body` skrollini qulflab turgan edi va qulf ochilmasdan turib
    `scrollIntoView` hech qayerga bormasdi.
  */
  void nextTick(() => scrollToSection('#ariza'))
}

/*
  ══════════════════════════════════════════════════════════════════════════
  KARTALARDAGI KURSOR YORUG'LIGI (2026-09-03)
  ══════════════════════════════════════════════════════════════════════════

  Sichqoncha karta ustida yurganda uning ortidan juda xira yashil
  yorug'lik ergashadi (`--mx` / `--my` -> `.glow-card::before`).

  ★ NEGA ARZIYDI: kartalar sahifada o'n oltita va ular hover'da faqat
    ko'tarilardi. Yorug'lik "bu element tirik" degan mayda signal
    beradi — bosiladigan bo'lmasa ham, sahifa qotib qolgandek
    ko'rinmaydi.

  ★ O'LCHAM `mouseenter` DA BIR MARTA OLINADI, har harakatda emas:
    `getBoundingClientRect` brauzerni tartibni qayta hisoblashga
    majbur qiladi va uni sichqoncha harakatining har kadrida chaqirish
    aynan o'sha "layout thrashing" ni beradi (ayni sabab skroll
    ishlovchisi izohida).

  ⚠️ KARTA USTIDA TURGANDA SAHIFA SURILSA o'lcham eskiradi va yorug'lik
     biroz siljib qoladi. Bu ataylab qabul qilingan: effekt sof bezak
     va uning uchun har skrollda qayta hisoblash arzimaydi.

  🔴 BITTA O'ZGARUVCHI YETADI: sichqoncha bir vaqtda faqat BITTA karta
     ustida bo'ladi, ya'ni kartalar sonidan qat'i nazar bu yerda bitta
     qiymat saqlanadi.
*/
let hoveredCardRect: DOMRect | null = null

function onCardEnter(event: MouseEvent): void {
  hoveredCardRect = (event.currentTarget as HTMLElement).getBoundingClientRect()
}

function onCardMove(event: MouseEvent): void {
  if (hoveredCardRect === null) return

  const card = event.currentTarget as HTMLElement
  const x = ((event.clientX - hoveredCardRect.left) / hoveredCardRect.width) * 100
  const y = ((event.clientY - hoveredCardRect.top) / hoveredCardRect.height) * 100

  card.style.setProperty('--mx', `${x}%`)
  card.style.setProperty('--my', `${y}%`)
}

const year = new Date().getFullYear()
</script>

<template>
  <!--
    `pb-24 sm:pb-0` — pastdagi doimiy panel footer matnini to‘smasin.
    Panel `fixed`, ya‘ni u hujjat oqimidan chiqib ketadi va o‘z joyini
    egallamaydi; bo‘shliqni o‘ram berishi kerak.
  -->
  <div class="landing-root min-h-dvh bg-ink-950 pb-24 sm:pb-0">
    <!--
      ══════════════════════════════════════════════════════════════════
       SAHIFA FONI — 2026-09-03
      ══════════════════════════════════════════════════════════════════

      ★ NIMA UCHUN: bo'limlar orasidagi oq maydonlar butunlay tekis edi
        va uzun sahifada ular "tugamaydigan oq" bo'lib ko'rinardi. Endi
        fonda sekin suzuvchi rangli nurlar va juda xira arab harflari
        bor — ular skroll paytida sahifaga chuqurlik beradi.

      🔴 `fixed` — SKROLL BILAN QIMIRLAMAYDI. Aynan shu narsa effektni
         beradi: kontent fon ustidan suriladi. Oqimda tursa, u
         shunchaki yana bir bo'lim bo'lib qolardi.

      🔴 `z-0` + kontentda `z-10`: `fixed` element joylashtirilgan
         (positioned) hisoblanadi va u oddiy oqimdagi bloklarning
         FONIDAN YUQORIDA chiziladi. Kontentga aniq qatlam berilmasa,
         fon oq kartalarning ustiga chiqib qolardi.

      ★ SHARLAR TELEFONDA CHIZILMAYDI (`hidden sm:block`): ularning
        har biri 70px blur bilan chiziladi va harakatlanganda qayta
        rasterlanadi. Telefonda bu bekorga batareya. Harflar esa
        arzon (oddiy matn), shuning uchun ular hamma joyda qoladi.
    -->
    <div
      class="pointer-events-none fixed inset-0 z-0 overflow-hidden"
      aria-hidden="true"
    >
      <div class="decor-blob decor-blob--a hidden sm:block" />
      <div class="decor-blob decor-blob--b hidden sm:block" />
      <div class="decor-blob decor-blob--c hidden sm:block" />

      <span
        v-for="(letter, letterIndex) in DECOR_LETTERS"
        :key="letter"
        class="decor-letter lt-arabic"
        :class="`decor-letter--${letterIndex}`"
        v-text="letter"
      />
    </div>

    <!-- ═══════════════════════════════════════════ E'LON PANELI ═══ -->
    <!--
      ⚠️ 2026-09-03 DA QO'SHILDI.

      ★ NIMA UCHUN: sahifadagi yagona TANQISLIK signali «18–20 kishilik
        guruh» edi va u statistikada, hero'ning eng pastida turardi —
        ya'ni odam uni skroll qilmasa umuman ko'rmasdi. Qabul ochiqligi
        va joy sonini birinchi qatorda aytish qaror qabul qilish
        tezligini oshiradi.

      🔴 PANEL YOPISHQOQ EMAS — ATAYLAB. Yuqori panel (`sticky`) allaqachon
         ekranda qoladi; ikkalasi birga qolsa telefonda kontent uchun
         100px dan kam joy qolardi. Bu panel bir marta o'qiladi va
         yuqoriga surilib ketadi.

      ★ `surface-brand` ISHLATILMADI: u tokenlarni BUTUNLAY almashtiradi
        va bu yerda faqat bitta tor chiziq bor — ichida `text-slate-*`
        ishlatadigan hech narsa yo'q. Ranglar to'g'ridan-to'g'ri
        `green-*` shkalasidan olindi.
    -->
    <div class="announce-bar relative z-10 text-[13.5px] text-green-900">
      <div
        class="mx-auto flex max-w-6xl flex-wrap items-center justify-center gap-x-5 gap-y-1 px-4 py-2 sm:px-6"
      >
        <span class="inline-flex items-center gap-2 font-semibold text-white">
          <!--
            Jonli nuqta — «hozir ochiq» degan signal. `aria-hidden`:
            u bezak, ma'no yonidagi matnda.
          -->
          <span
            class="announce-dot"
            aria-hidden="true"
          />
          {{ HERO.badge }}
        </span>
        <span>Guruhda atigi {{ COURSE_FACTS.groupSize }} joy</span>
        <!-- Telefonda raqam pastdagi doimiy panelda va menyuda bor. -->
        <a
          class="ml-auto hidden font-semibold text-white/90 transition-colors hover:text-white sm:block"
          :href="`tel:${CONTACT.phoneHref}`"
        >
          {{ CONTACT.phone }}
        </a>
      </div>
    </div>

    <!-- ═══════════════════════════════════════════ YUQORI PANEL ═══ -->
    <!--
      ══════════════════════════════════════════════════════════════════
       ⚠️ 2026-09-03 — PANEL "SUZUVCHI PILYUSKA" GA AYLANTIRILDI
      ══════════════════════════════════════════════════════════════════

      Ilgari panel butun kenglikni egallagan chiziq edi va surilganda
      ostiga oq fon + pastki chegara qo'yardi. Muammo shundaki, u
      HERO'NING TO'Q YASHIL SIRTIGA yopishib turardi: yuqorida oq
      chiziq, ostida darhol to'q yashil — ikkalasi orasida hech qanday
      bo'shliq yo'q edi va panel sahifaning bir qismi emas, uning
      "qopqog'i" bo'lib ko'rinardi.

      ★ ENDI U ALOHIDA QATLAM: yumaloq (999px), oynasimon oq, ostidan
        kontent SURILIB O'TADI. Tepasidagi 14px bo'shliq ataylab
        shaffof — aynan o'sha bo'shliq panelni sahifadan "ko'targan"
        qilib ko'rsatadi.

      🔴 PANEL `.surface-brand` DAN TASHQARIDA. Ya'ni uning ichidagi
         `text-slate-*` tokenlari YORUG' sirt qiymatlarini oladi —
         hero to'q yashil bo'lsa ham panel oq bo'lib qolaveradi. Bu
         to'g'ri: panel butun sahifa bo'ylab suriladi va u faqat hero
         ustida emas, oq bo'limlar ustida ham turadi.
    -->
    <!--
      🔴 `nav-overlay` — PANEL KONTENT USTIDA SUZADI, uning TEPASIDA
         emas.

         Muammo aynan shu loyihada tug'iladi: hero to'q yashil, panel
         esa oq. Panel oqim ichida tursa, e'lon paneli (yashil) bilan
         hero (to'q yashil) orasida 74px lik OQ TASMA qolardi va u
         "unutilgan bo'shliq" bo'lib ko'rinardi.

         Yechim — panelning pastki chegarasini o'z balandligicha manfiy
         qilish: u oqimda joy egallamaydi, hero esa uning ostidan
         boshlanadi. Hero'ga qo'shilgan yuqori to'ldirish (`pt-*`)
         matnni panel ostidan chiqarib turadi.

      ⚠️ `--nav-h` IKKI JOYDA ISHLATILADI — shu yerda va hero'ning
         `pt-[calc(...)]` ida. Panel balandligi o'zgarsa (masalan tugma
         `h-10` dan `h-11` ga chiqsa), FAQAT `--nav-h` ni yangilash
         yetadi.
    -->
    <header class="nav-overlay sticky top-0 z-30">
      <div class="relative mx-auto max-w-6xl px-4 pt-3.5 sm:px-6">
        <!--
          ⚠️ `gap` VA ICHKI TO'LDIRISHLAR `xl` DA KATTARADI.

          🔴 SABAB: 1024px (lg) da menyu KO'RINA boshlaydi va o'sha
             kenglikda oltita punkt + logo + ikkita tugma pilyuskaga
             zo'rg'a sig'ardi — natijada matnlar ikki qatorga
             tushib ketardi. Bo'shliqlar `lg` da qisiladi, keng
             ekranda esa tiklanadi.
        -->
        <div
          class="nav-pill flex items-center gap-3 rounded-full py-2.5 pl-4 pr-3 xl:gap-5"
          :class="isScrolled ? 'nav-pill--stuck' : ''"
        >
          <a
            class="nav-brand flex shrink-0 items-center gap-2.5"
            href="#"
            @click.prevent="scrollToSection('body')"
          >
            <!--
            HAQIQIY BREND LOGOSI (2026-08-29). Ilgari bu yerda gradientli
            kvadrat ichida "Z" harfi turardi — yasama belgi. Endi markazning
            o'z logosi. `public/` dan kelgani uchun manzil ildizdan boshlanadi.
          -->
            <img
              class="nav-mark size-9 rounded-full"
              src="/logo-64.png"
              alt="ZIN-NUR logosi"
              width="36"
              height="36"
            >
            <!--
            SO'Z BELGISI — IKKI QATOR, LOGODAGIDEK (2026-08-29).

            🔴 IKKALA QATOR HAM BIR XIL: rang (`text-brand-500`), shrift
            (`font-display`), o'lcham va vazn. Loyiha egasining aniq
            talabi. Ilgari "ONLINE" kichikroq va kul rangda edi — RAD
            ETILDI, chunki logoda ikkala so'z ham teng va bir xil oq.

            🔴 BALANDLIK LOGO BILAN AYNAN TENG (loyiha egasining talabi):
            logo `size-9` = 36px, shuning uchun har qator `leading-[18px]`
            va 18 + 18 = 36. Blokka ham `h-9` qo'yilgan — brauzer qator
            oralig'ini yaxlitlab yuborsa ham balandlik SILJIMASIN.

            ★ NIMA UCHUN NISBIY (`leading-[1.02]`) EMAS: nisbiy qiymat
              shrift o'lchamiga bog'liq va u o'zgarsa balandlik ham
              o'zgarardi, ya'ni logo bilan tenglik jimgina buzilardi.
              Bu yerda tenglik — TALAB, tasodif emas.

            ★ Qat'iy `leading` bu matnda XAVFSIZ: nom butunlay katta
              harflardan iborat, ya'ni pastga tushuvchi element
              (`g`, `y`, `p`) yo'q va harflar kesilmaydi.
          -->
            <span class="flex h-9 flex-col justify-center">
              <span class="font-display text-[17px] font-semibold leading-[18px] tracking-tight text-brand-500">
                ZIN-NUR
              </span>
              <span class="font-display text-[17px] font-semibold leading-[18px] tracking-tight text-brand-500">
                ONLINE
              </span>
            </span>
          </a>

          <!--
            ★ `mx-auto` — menyu pilyuska ICHIDA markazda turadi. `lg` dan
              past ekranda u yashiriladi va markazlash ishlamay qoladi;
              o'ng blokni chetga o'sha yerdagi `lg:ml-0` bilan birga
              `ml-auto` itaradi.
          -->
          <!--
            🔴 MENYU 1080px DAN KO'RINADI, `lg` (1024px) DAN EMAS.

               Sabab hisobda: 1024px da o'ram 976px, undan olti punkt
               (~520px), logo (~120px) va ikkita tugma (~205px) —
               ya'ni deyarli hammasi. Zaxira qolmagani uchun shrift
               yoki matn biroz o'zgarsa panel darhol buzilardi.
               1080px da esa ~130px zaxira qoladi.

               Bu chegara `min-[1080px]` — Tailwind'ning tayyor
               qadamlari orasida bunday qiymat yo'q, aniq son esa
               hisobdan chiqadi.
          -->
          <nav class="mx-auto hidden items-center gap-0.5 min-[1080px]:flex">
            <!--
              ★ `whitespace-nowrap` + `shrink-0` — IKKALASI HAM KERAK.
                Birinchisisiz «Kurs tuzilmasi» ikki so'zga bo'linib
                tagma-tag tushardi, ikkinchisisiz esa flex ularni
                siqib, matnni baribir sindirardi.

              ★ AKTIV BO'LIM: `aria-current` ekran o'qigich uchun,
                `nav-link--active` esa ko'z uchun. Ikkalasi bitta
                holatdan chiqadi (`activeSection`).
            -->
            <a
              v-for="item in NAV"
              :key="item.href"
              :href="item.href"
              class="nav-link shrink-0 whitespace-nowrap rounded-full px-2.5 py-2 text-[13.5px] font-medium text-slate-300 transition-colors xl:px-3.5 xl:text-sm"
              :class="activeSection === item.href ? 'nav-link--active' : ''"
              :aria-current="activeSection === item.href ? 'true' : undefined"
              @click.prevent="scrollToSection(item.href)"
            >{{ item.label }}</a>
          </nav>

          <div class="ml-auto flex shrink-0 items-center gap-2 min-[1080px]:ml-0">
            <!--
            ⚠️ 2026-08-30 — RAQAM YUQORI PANELGA CHIQARILDI.

            Ilgari u FAQAT ariza blokida, sahifaning pastida turardi.
            Qo‘ng‘iroq qilmoqchi bo‘lgan odam uni qidirib topishi kerak
            edi — holbuki telefon eng past to‘siqli aloqa kanali.

            ★ `md` dan boshlab: undan tor ekranda raqam menyu tugmasini
              siqib qo‘yardi. Telefonda u mobil menyuda va pastdagi
              doimiy panelda turadi.
          -->
            <!--
              ⚠️ 2026-09-03 — RAQAM ENDI IKONKA, MATN EMAS.

              🔴 SABAB: to'liq raqam («+998 (78) 777-77-17») pilyuskada
                 ~190px egallaydi. Markazlashgan menyu bilan birga u
                 hech qanday kenglikda sig'masdi — o'ram `max-w-6xl`
                 bilan cheklangani uchun ekran kattalashsa ham panel
                 kengaymaydi.

              ★ QO'NG'IROQ IMKONI YO'QOLMADI (u eng past to'siqli aloqa
                kanali — 2026-08-30 dagi qaror): raqam e'lon panelida,
                mobil menyuda, ariza blokida, footerda va telefondagi
                doimiy panelda matn bilan turibdi. Bu yerda esa uning
                eng ixcham shakli.

              ★ `aria-label` — tugmada faqat ikonka bor, ekran o'qigich
                uchun raqamning O'ZI o'qiladi.
            -->
            <a
              class="nav-quiet hidden size-10 shrink-0 items-center justify-center rounded-full text-slate-300 xl:inline-flex"
              :href="`tel:${CONTACT.phoneHref}`"
              :aria-label="`Qo‘ng‘iroq qilish: ${CONTACT.phone}`"
              :title="CONTACT.phone"
            >
              <AppIcon
                name="phone"
                :size="17"
              />
            </a>

            <!--
              «Kirish» — TINCH HAVOLA, tugma emas. Pilyuskada ikkita
              to'ldirilgan tugma yonma-yon turganda ikkalasi ham
              e'tiborni tortadi va asosiysi («Kursga yozilish») o'z
              ustunligini yo'qotadi.
            -->
            <RouterLink
              class="nav-quiet hidden shrink-0 whitespace-nowrap rounded-full px-2.5 py-2 text-sm font-semibold text-slate-300 sm:block xl:px-3"
              to="/login"
            >
              Kirish
            </RouterLink>

            <button
              type="button"
              class="nav-cta hidden h-10 shrink-0 items-center whitespace-nowrap rounded-full px-4 text-sm font-semibold text-on-brand sm:inline-flex xl:px-5"
              @click="scrollToSection('#ariza')"
            >
              Kursga yozilish
            </button>

            <!--
              BURGER — uch chiziqdan xochga aylanadi.

              ★ NEGA `AppIcon` EMAS: ikonka almashtirilganda belgi
                "sakrab" o'zgarardi. Bu yerda chiziqlarning O'ZI buriladi,
                ya'ni ochilish va yopilish bitta uzluksiz harakat.

              `aria-label` bilan `aria-expanded` qoldi — ekran o'qigich
              uchun tugmaning ma'nosi o'zgarmadi.
            -->
            <button
              type="button"
              class="nav-burger grid size-10 shrink-0 place-items-center rounded-xl min-[1080px]:hidden"
              :class="isMenuOpen ? 'nav-burger--open' : ''"
              :aria-expanded="isMenuOpen"
              aria-label="Menyu"
              @click="isMenuOpen = !isMenuOpen"
            >
              <span aria-hidden="true" />
            </button>
          </div>
        </div>

        <!--
          MOBIL MENYU — pilyuska OSTIDA suzuvchi karta.

          ★ NEGA `absolute`, oqimda emas: oqimda tursa u panelni
            cho'zib, ostidagi kontentni pastga surib yuborardi — ya'ni
            menyu ochilishi sahifani "sakratardi". Endi u kontent
            USTIGA tushadi.

          ★ O'RAM (`relative`) — pilyuskaning O'ZI emas, uning tashqi
            konteyneri: shunda menyu qanchalik baland bo'lsa ham
            pilyuskaning yumaloq chegarasi uni kesmaydi.
        -->
        <nav
          v-if="isMenuOpen"
          class="nav-drawer absolute inset-x-4 top-full mt-2.5 rounded-3xl p-3 sm:inset-x-6 min-[1080px]:hidden"
        >
          <a
            v-for="item in NAV"
            :key="item.href"
            :href="item.href"
            class="nav-drawer-link block rounded-2xl px-4 py-3 text-[15px] font-semibold text-slate-200 transition-colors"
            :class="activeSection === item.href ? 'nav-drawer-link--active' : ''"
            :aria-current="activeSection === item.href ? 'true' : undefined"
            @click.prevent="scrollToSection(item.href)"
          >{{ item.label }}</a>

          <!-- Telefonda raqam menyuda: yuqori panelda unga joy yo‘q. -->
          <a
            class="nav-drawer-link flex items-center gap-2.5 rounded-2xl px-4 py-3 text-[15px] font-semibold text-slate-200 transition-colors"
            :href="`tel:${CONTACT.phoneHref}`"
          >
            <AppIcon
              class="text-brand-500"
              name="phone"
              :size="16"
            />
            {{ CONTACT.phone }}
          </a>

          <div class="mt-2 flex gap-2 border-t border-line pt-3">
            <RouterLink
              class="flex-1"
              to="/login"
            >
              <span
                class="flex h-11 items-center justify-center rounded-full border border-line-strong text-sm font-semibold text-slate-200"
              >Kirish</span>
            </RouterLink>
            <button
              type="button"
              class="nav-cta flex h-11 flex-1 items-center justify-center rounded-full text-sm font-semibold text-on-brand"
              @click="scrollToSection('#ariza')"
            >
              Kursga yozilish
            </button>
          </div>
        </nav>
      </div>
    </header>

    <main class="relative z-10">
      <!-- ═══════════════════════════════════════════════ HERO ═══ -->
      <!--
        ══════════════════════════════════════════════════════════════
         ⚠️ 2026-09-03 — HERO TO'Q YASHILDAN YORUG'GA O'TDI
        ══════════════════════════════════════════════════════════════

        🔴 BU 2026-08-29 DAGI «NUR» QARORINI BEKOR QILADI. O'shanda
           loyiha egasi hero'ni to'q yashil qilishni tanlagan edi
           («panel ishchi va tinch bo'lsin, landing esa birinchi
           ekrandayoq esda qolsin»). 2026-09-03 da u yangi maketni
           ko'rib, yorug' variantni tanladi.

        ★ SAHIFA BREND SIRTISIZ QOLMADI: to'q yashil endi hero'da emas,
          uning OSTIDAGI statistika kartasida va narx kartasida.
          Ya'ni "esda qoladigan yashil" saqlandi, faqat birinchi
          ekranni to'liq egallamaydi.

        ★ `style.css` DAGI `.surface-brand` BLOKI TEGILMADI: uni
          `LoginPage` ning chap paneli, statistika va narx kartalari
          ishlatadi.

        ┌──────────────────────────────────────────────────────────┐
        │ ★ ICHKI KLASSLAR DEYARLI TEGILMADI — TOKENLAR TESKARI     │
        └──────────────────────────────────────────────────────────┘
        `surface-brand` olib tashlanishi bilan `text-slate-50`
        (to'q sirtda OQ edi) o'z-o'zidan #0d1310 ga, `bg-ink-900` esa
        oqqa aylanadi. Shkalalar ataylab teskari yozilgani uchun
        (`style.css` dagi "NEYTRAL SIRTLAR" izohi) sirtni almashtirish
        matn ranglarini QO'LDA to'g'rilashni talab qilmaydi.

        ┌──────────────────────────────────────────────────────────┐
        │ 🔴 HERO O'Z FONINI YO'QOTDI — ATAYLAB                     │
        └──────────────────────────────────────────────────────────┘
        Ilgari bu yerda uch qatlamli fon bor edi: gradient asos,
        suriluvchi nuqtali setka va uchta yorug'lik shari. Ularning
        HAMMASI to'q sirt uchun hisoblangan — yorug' fonda setka
        ko'rinmay qoladi, sharlar esa loyqa dog' bo'lib qolardi.

        Ularning o'rnini sahifaning UMUMIY foni egalladi (yuqorida,
        `landing-root` ichidagi `decor-*` bloklari): u aynan yorug'
        sirt uchun yasalgan va butun sahifa bo'ylab davom etadi.
        Ikkita fon tizimi bir joyda ishlaganda ular bir-birini
        loyqalatardi.

        ⚠️ `bg-*` YO'Q: hero shaffof, ya'ni umumiy fon undan
           KO'RINIB turadi. Fon bersak, dekor hero ostida qolardi.
      -->
      <section class="relative overflow-hidden">
        <!--
          Yuqori to'ldirish `--nav-h` ni HISOBGA OLADI: panel oqimdan
          chiqarilgani uchun (sabab yuqoridagi `nav-overlay` izohida)
          usiz hero sarlavhasi panel ostida qolardi.
        -->
        <div
          class="relative mx-auto max-w-6xl px-4 pb-16 pt-[calc(2.5rem+var(--nav-h))] sm:px-6 sm:pb-24 sm:pt-[calc(4rem+var(--nav-h))]"
        >
          <!--
            ⚠️ 2026-09-03 — HERO IKKI USTUNGA BO'LINDI.

            Ilgari matn `max-w-3xl` bilan chapda turardi va o'ng yarim
            BO'SH edi (ortida faqat fon nurlari). Endi o'ngda vizual
            sahna bor: aylanuvchi orbita, markazda «ع» va to'rtta
            suzuvchi karta.

            ★ USTUNLAR FAQAT `lg` DAN: undan tor ekranda sahna matn
              ostiga tushadi va to'liq kenglikni oladi.
          -->
          <div class="grid gap-12 lg:grid-cols-[1.05fr_0.95fr] lg:items-center lg:gap-16">
            <div>
              <span
                class="hero-rise inline-flex items-center gap-2 rounded-full border border-green-900 bg-green-950 px-3.5 py-1.5 text-[12.5px] font-bold uppercase tracking-[0.13em] text-brand-500"
              >
                <span
                  class="size-1.5 rounded-full bg-brand-500"
                  aria-hidden="true"
                />
                {{ HERO.eyebrow }}
              </span>

              <!--
              🔴 `font-semibold` (600) — `font-bold` EMAS. Newsreader faqat
              600 vaznda yuklanadi; 700 so'ralsa brauzer sun'iy
              qalinlashtiradi va bu o'lchamda darhol ko'rinadi.
              Batafsil: `style.css` dagi `@font-face` izohi.
            -->
              <h1
                class="hero-rise mt-5 font-display text-[2.6rem] font-semibold leading-[1.05] tracking-[-0.01em] text-slate-50 sm:text-[4.1rem]"
                style="--rise-delay: 90ms"
              >
                {{ HERO.title }}
                <!--
                  ══════════════════════ QO'LDA CHIZILGAN TAG CHIZIQ ═══
                  ⚠️ 2026-09-03 DA QO'SHILDI.

                  ★ NEGA SVG, `underline` EMAS: brauzerning tag chizig'i
                    ideal to'g'ri va u sarlavhaning boshqa qismidan
                    ajralmasdi. Bu egri chiziq esa "qo'lda tortilgan"
                    ko'rinadi — ya'ni ATAYLAB belgilangan so'z.

                  ★ CHIZIQ CHIZILADI (`stroke-dasharray` animatsiyasi):
                    sahifa ochilgach so'z chapdan o'ngga ostiga
                    chiziladi va ko'z aynan o'sha yerga tushadi.

                  ★ `inline-block` + `relative` — SVG so'zning ostiga
                    joylashadi. Usiz u qatorning butun kengligini
                    olardi.
                -->
                <span class="relative inline-block text-brand-500">
                  {{ HERO.titleAccent }}
                  <svg
                    class="hero-underline"
                    viewBox="0 0 300 12"
                    preserveAspectRatio="none"
                    aria-hidden="true"
                  >
                    <path d="M4 8 C 70 2, 150 11, 296 4" />
                  </svg>
                </span>
              </h1>

              <p
                class="hero-rise mt-6 max-w-2xl text-base leading-relaxed text-slate-300 sm:text-lg"
                style="--rise-delay: 180ms"
              >
                {{ HERO.lead }}
              </p>

              <div
                class="hero-rise mt-9 flex flex-wrap items-center gap-3"
                style="--rise-delay: 270ms"
              >
                <!--
                  ★ TUGMALAR YUMALOQ (`rounded-full`) — yuqori paneldagi
                    pilyuska va uning ichidagi tugma bilan bir tilda
                    gapirishi uchun. Ilgari ular 12px burchakli edi va
                    panel bilan bir sahifada ikki xil shakl ko'rinardi.
                -->
                <button
                  type="button"
                  class="nav-cta inline-flex h-12 items-center justify-center gap-2.5 rounded-full px-7 text-base font-semibold text-on-brand"
                  @click="scrollToSection('#ariza')"
                >
                  Kursga yozilish
                  <AppIcon
                    name="chevron-right"
                    :size="17"
                  />
                </button>

                <button
                  type="button"
                  class="hero-ghost inline-flex h-12 items-center justify-center gap-2.5 rounded-full border border-line bg-ink-900 px-7 text-base font-semibold text-slate-100"
                  @click="scrollToSection('#dars')"
                >
                  <AppIcon
                    name="play"
                    :size="17"
                  />
                  Bepul darsni ko‘rish
                </button>
              </div>

              <!-- Ijtimoiy tarmoqlar — hero ichida, ishonch signali sifatida. -->
              <div
                class="hero-rise mt-8 flex flex-wrap items-center gap-x-5 gap-y-2"
                style="--rise-delay: 360ms"
              >
                <span class="text-[13px] font-medium text-slate-400">Bizni kuzating:</span>
                <!--
                  ⚠️ 2026-09-03 — NOM OLIB TASHLANDI, IKONKA QOLDI.

                  ★ NEGA: uchta havola matn bilan («Telegram», «YouTube»,
                    «Instagram») bir qatorda 250px egallardi va ular
                    ustidagi ikkita katta tugma bilan raqobatlashardi.
                    Brend belgilarining o'zi tanib olinadi — bu aynan
                    ular uchun `brand-icon-paths.ts` da haqiqiy logolar
                    qo'yilgan sabab (2026-08-30).

                  ★ `aria-label` — tugmada faqat shakl qoldi, ekran
                    o'qigich uchun nom shu yerdan keladi.
                -->
                <a
                  v-for="social in SOCIALS"
                  :key="social.href"
                  class="hero-social grid size-10 place-items-center rounded-xl border border-line bg-ink-900 text-slate-300"
                  :href="social.href"
                  :aria-label="social.label"
                  target="_blank"
                  rel="noopener noreferrer"
                >
                  <AppIcon
                    :name="social.icon"
                    :size="18"
                  />
                </a>
              </div>
            </div>

            <!--
              ════════════════════════════════════ VIZUAL SAHNA ═══
              🔴 `aria-hidden` — SAHNADA YANGI MA'LUMOT YO'Q. To'rtala
                 kartaning mazmuni sahifada matn bilan takrorlanadi
                 (sabab `content.ts` dagi `HERO_CARDS` izohida), harf esa
                 bezak. Ekran o'qigich uchun bu faqat shovqin bo'lardi.

              ★ TELEFONDA KO'RSATILMAYDI (`hidden sm:block`): 400px
                balandlikdagi bezak «Kursga yozilish» tugmasini ekrandan
                pastga surib yuborardi. Telefonda birinchi ekranning
                vazifasi — va'da va tugma.
            -->
            <div
              class="hero-stage hidden sm:block"
              aria-hidden="true"
            >
              <!-- Aylanuvchi orbitalar. -->
              <div class="hero-orbit">
                <span class="hero-pip hero-pip--a" />
                <span class="hero-pip hero-pip--b" />
              </div>
              <div class="hero-orbit hero-orbit--inner" />

              <!-- Markazdan tarqaluvchi halqalar. -->
              <div class="hero-ripple" />
              <div class="hero-ripple hero-ripple--b" />

              <!-- Yadro: harf va uning nomi. -->
              <div class="hero-core">
                <span
                  class="hero-glyph"
                  lang="ar"
                  dir="rtl"
                >{{ HERO_LETTER.glyph }}</span>
                <span class="hero-cap">{{ HERO_LETTER.caption }}</span>
              </div>

              <!-- Suzuvchi kartalar. -->
              <div
                v-for="(card, cardIndex) in HERO_CARDS"
                :key="card.title"
                class="hero-card"
                :class="`hero-card--${cardIndex}`"
              >
                <p class="text-[13px] font-bold leading-tight text-slate-50">
                  {{ card.title }}
                </p>
                <p class="mt-0.5 text-[11.5px] leading-snug text-slate-400">
                  {{ card.text }}
                </p>

                <!-- Avatarlar — guruhdagi odamlar. -->
                <div
                  v-if="card.kind === 'avatars'"
                  class="mt-2 flex"
                >
                  <i
                    v-for="(avatar, avatarIndex) in ['A', 'M', 'S', '+15']"
                    :key="avatar"
                    class="hero-avatar"
                    :class="`hero-avatar--${avatarIndex}`"
                    v-text="avatar"
                  />
                </div>

                <!-- Tovush to'lqini — audio tekshiruvi. -->
                <div
                  v-else-if="card.kind === 'wave'"
                  class="mt-1.5 flex h-6 items-end gap-[3px]"
                >
                  <i
                    v-for="bar in 8"
                    :key="bar"
                    class="hero-wave-bar"
                    :style="{ '--bar': bar }"
                  />
                </div>

                <!-- Progress — yo'ldagi jo'natma. -->
                <div
                  v-else-if="card.kind === 'progress'"
                  class="mt-2 h-1.5 overflow-hidden rounded-full bg-ink-800"
                >
                  <i class="hero-progress block h-full rounded-full" />
                </div>
              </div>
            </div>
          </div>
        </div>
      </section>

      <!-- ═══════════════════════════════════════ STATISTIKA ═══ -->
      <!--
        ══════════════════════════════════════════════════════════════
         ⚠️ 2026-09-03 — RAQAMLAR HERO'DAN CHIQARILDI
        ══════════════════════════════════════════════════════════════

        Ilgari ular hero ICHIDA, to'q yashil sirtda, yarim shaffof
        kartalarda turardi. Hero yorug'ga o'tgach o'sha kartalar oq
        fonda oq bo'lib qolardi.

        ★ ENDI ULAR TO'Q YASHIL BITTA KARTA. Ikki foyda bor:
          1) hero yorug'lashgach sahifa yuqorisi butunlay oqarib
             ketmadi — brend rangi darhol keyingi ekranda qaytadi;
          2) to'rt raqam yonma-yon, ajratgich chiziqlar bilan —
             ular endi to'rtta alohida karta emas, BITTA dalil bloki.

        🔴 RAQAMLARNING O'ZI HALI HAM MAHSULOT TAVSIFI, ISBOT EMAS
           (8 oy, 5 kun, 18–20, 540 000). Bitiruvchilar soni kabi
           haqiqiy son berilsa, ikkitasini almashtirish kerak —
           `content.ts` dagi `STATS`.
      -->
      <section class="relative mx-auto max-w-6xl px-4 pb-4 sm:px-6">
        <dl
          class="stats-card surface-brand grid grid-cols-2 overflow-hidden rounded-3xl p-2 sm:grid-cols-4"
        >
          <div
            v-for="stat in STATS"
            :key="stat.label"
            class="stats-cell px-4 py-6 text-center sm:px-5 sm:py-7"
          >
            <dt class="stats-value font-display text-2xl font-semibold tracking-tight sm:text-[2.1rem]">
              {{ stat.value }}
            </dt>
            <dd class="mt-2 text-[13px] text-slate-400">
              {{ stat.label }}
            </dd>
          </div>
        </dl>
      </section>

      <!-- ═════════════════════════════════════════ BEPUL DARS ═══ -->
      <section
        id="dars"
        class="relative scroll-mt-20 overflow-hidden"
      >
        <!--
          ⚠️ 2026-08-30 — BO‘LIM SAHIFANING ENG YASSISI EDI.

          Fon ham, tekstura ham, ramka ham yo‘q edi: hero (kuchli brend
          sirti) bilan «Natija» tasmasi orasida bu bo‘lim shunchaki
          "matn + rasm" bo‘lib qolardi. Holbuki BEPUL DARS — sahifadagi
          eng muhim ishonch nuqtasi: odam pul haqidagi gapdan oldin
          aynan shu yerda "bu ustoz qanday tushuntiradi?" degan savolga
          javob oladi.

          Endi videoning orqasida yumshoq yashil nur bor — u bo‘limni
          bo‘yamaydi, faqat videoni sahifadan "ko‘taradi".
        -->
        <div
          class="pointer-events-none absolute inset-0"
          aria-hidden="true"
          style="
            background:
              radial-gradient(
                42rem 26rem at 78% 45%,
                color-mix(in oklab, var(--color-brand-vivid) 9%, transparent),
                transparent 70%
              );
          "
        />

        <div class="relative mx-auto grid max-w-6xl items-center gap-10 px-4 py-16 sm:px-6 sm:py-24 lg:grid-cols-2 lg:gap-14">
          <div>
            <span class="eyebrow text-xs font-bold uppercase tracking-[1.4px] text-brand-500">
              {{ FREE_LESSON.eyebrow }}
            </span>
            <h2
              class="mt-3 font-display text-3xl font-semibold tracking-tight text-slate-100 sm:text-[2.5rem] sm:leading-[1.1]"
            >
              {{ FREE_LESSON.title }}
            </h2>
            <p class="mt-4 text-base leading-relaxed text-slate-400">
              {{ FREE_LESSON.text }}
            </p>

            <!--
              QISQA FAKTLAR — yuqoridagi gapni skanerlanadigan shaklga
              soladi. Yangi va’da qo‘shmaydi (sabab `content.ts` dagi
              `FREE_LESSON.facts` izohida).
            -->
            <ul class="mt-7 flex flex-wrap gap-2">
              <li
                v-for="fact in FREE_LESSON.facts"
                :key="fact"
                class="inline-flex items-center gap-1.5 rounded-full bg-ink-900 px-3 py-1.5 text-xs font-medium text-slate-300 ring-1 ring-inset ring-line"
              >
                <AppIcon
                  class="text-brand-500"
                  name="check"
                  :size="13"
                />
                {{ fact }}
              </li>
            </ul>

            <!--
              ★ MATNLI HAVOLA EMAS, RAMKALI TUGMA: ilgari bu oddiy
                havola edi va videoning yonida ko‘zga umuman
                ilinmasdi. Belgi ham almashtirildi — endi YouTube‘ning
                O‘Z logosi, ya’ni tugma qayerga olib borishi
                bosishdan oldin ko‘rinadi.
            -->
            <a
              class="mt-7 inline-flex h-11 items-center gap-2.5 rounded-xl border border-line-strong px-5 text-sm font-semibold text-slate-200 transition-colors hover:border-brand-500 hover:text-brand-500"
              :href="FREE_LESSON.href"
              target="_blank"
              rel="noopener noreferrer"
            >
              <AppIcon
                name="youtube"
                :size="17"
              />
              YouTube'da ochish
            </a>
          </div>

          <!--
            VIDEO — bosilgunga qadar FAQAT poster.
            Sabab `isVideoPlaying` izohida: YouTube iframe'i og'ir va uni
            sahifa ochilishi bilan yuklash landing tezligini buzardi.

            `youtube-nocookie.com` — Google'ning kengaytirilgan maxfiylik
            domeni: video ko'rilmaguncha kuzatuv cookie'si qo'yilmaydi.
          -->
          <div class="relative">
            <!--
              ORQADAGI NUR. `-inset-*` — nur ramkadan sal chiqib turadi,
              `blur` esa uni chetiga qadar yumshatadi. Natijada video
              sahifaga "yopishtirilgan rasm" emas, ko‘tarilgan ob’ekt
              bo‘lib ko‘rinadi.
            -->
            <div
              class="pointer-events-none absolute -inset-3 rounded-[2rem] bg-brand-500/12 blur-2xl sm:-inset-5"
              aria-hidden="true"
            />

            <div
              class="relative aspect-video overflow-hidden rounded-2xl bg-ink-800 shadow-2xl ring-1 ring-inset ring-line-strong sm:rounded-3xl"
            >
              <!--
                🔴 SARLAVHA BOG‘LANDI (2026-08-30). Ilgari bu yerda
                   dars nomi QO‘LDA yozilgan edi va `content.ts` dagi
                   sarlavha o‘zgarsa, o‘rnatmaning nomi eski holicha
                   qolardi — ya’ni ekran o‘quvchi va qidiruv tizimi
                   noto‘g‘ri nom ko‘rardi. Bu faylning o‘z qoidasiga
                   ham zid edi: bu yerda qotib qolgan matn bo‘lmaydi.
              -->
              <iframe
                v-if="isVideoPlaying"
                class="size-full"
                :src="`https://www.youtube-nocookie.com/embed/${FREE_LESSON.youtubeId}?autoplay=1&rel=0`"
                :title="FREE_LESSON.title"
                allow="accelerometer; autoplay; clipboard-write; encrypted-media; gyroscope; picture-in-picture"
                allowfullscreen
                frameborder="0"
              />

              <button
                v-else
                type="button"
                class="group absolute inset-0 size-full cursor-pointer"
                aria-label="Bepul darsni ijro etish"
                @click="isVideoPlaying = true"
              >
                <!--
                  Manba `posterSrc` dan: 1280×720, mavjud bo‘lmasa
                  `@error` bo‘yicha kichigiga tushadi. Sabab script
                  blokidagi `posterSrc` izohida.
                -->
                <img
                  class="size-full object-cover transition-transform duration-500 group-hover:scale-[1.03]"
                  :src="posterSrc"
                  alt=""
                  loading="lazy"
                  @error="onPosterError"
                >

                <span
                  class="absolute inset-0 bg-black/30 transition-colors group-hover:bg-black/20"
                  aria-hidden="true"
                />

                <!--
                  Pastdagi qorayish — tasvirning pastki cheti ramkaga
                  yumshoq ulanadi. Usiz kadr ramkada "kesilgandek"
                  tugardi.
                -->
                <span
                  class="absolute inset-x-0 bottom-0 h-24 bg-gradient-to-t from-black/55 to-transparent"
                  aria-hidden="true"
                />

                <!--
                  ★ `ring` — tugma atrofidagi yorug‘ halqa. Ijro tugmasi
                    RASM ustida turadi va rasm har xil rangda bo‘lishi
                    mumkin; halqa uni har qanday kadrda ajratib turadi.
                -->
                <span
                  class="absolute left-1/2 top-1/2 flex size-[4.5rem] -translate-x-1/2 -translate-y-1/2 items-center justify-center rounded-full bg-brand-500 text-on-brand shadow-xl ring-8 ring-white/15 transition-transform duration-300 group-hover:scale-110"
                  aria-hidden="true"
                >
                  <AppIcon
                    name="play"
                    :size="28"
                  />
                </span>
              </button>
            </div>
          </div>
        </div>
      </section>

      <!-- ═════════════════════════════════════ TEZKOR ARIZA ═══ -->
      <!--
        🔴 IKKINCHI FORMA — SAHIFANING YUQORI YARMIDA (2026-08-30).

        Ilgari ariza qoldirish imkoni FAQAT sahifa oxirida edi. Odam
        bepul darsni ko‘rib qiziqib qolsa ham, unga yettita bo‘limni
        aylantirib o‘tish kerak bo‘lardi — va ko‘pchilik o‘tmaydi.

        ★ NEGA AYNAN SHU YERDA, HERO‘DAN KEYIN EMAS: sahifa tartibi
          ataylab "avval ishonch, keyin so‘rov" mantig‘ida qurilgan
          (fayl boshidagi izoh). Hero‘dan darrov keyin forma qo‘yilsa,
          u bepul darsni — sahifadagi eng kuchli ishonch dalilini —
          pastga surib yuborardi. Bu yerda esa odam ustozni allaqachon
          ko‘rgan bo‘ladi.

        ★ FORMA QISQA (ism + telefon): batafsil maydonlar pastdagi
          to‘liq formada qoladi. Sabab `EnrollmentRequestForm` izohida.
      -->
      <section
        ref="quickEnrollSection"
        class="mx-auto max-w-6xl px-4 pb-16 sm:px-6 sm:pb-24"
      >
        <div class="grid gap-8 lg:grid-cols-2 lg:items-center lg:gap-14">
          <div>
            <span class="eyebrow text-xs font-bold uppercase tracking-[1.4px] text-brand-500">
              Ariza
            </span>
            <h2
              class="mt-3 font-display text-3xl font-semibold tracking-tight text-slate-100 sm:text-[2.2rem] sm:leading-[1.15]"
            >
              Guruhda joy bormi — bir daqiqada bilib oling
            </h2>
            <p class="mt-4 text-base leading-relaxed text-slate-400">
              Ism va telefon raqamingizni qoldiring. Menejerlarimiz
              bog‘lanib, darajangizni aniqlaydi va yaqin guruhlardan
              qaysi biri sizga to‘g‘ri kelishini aytadi.
            </p>

            <div class="mt-7 flex flex-wrap items-center gap-x-6 gap-y-3">
              <a
                class="inline-flex items-center gap-2 text-base font-semibold text-slate-100 transition-colors hover:text-brand-500"
                :href="`tel:${CONTACT.phoneHref}`"
              >
                <AppIcon
                  class="text-brand-500"
                  name="phone"
                  :size="18"
                />
                {{ CONTACT.phone }}
              </a>
              <span class="text-sm text-slate-500">{{ CONTACT.workingHours }}</span>
            </div>
          </div>

          <EnrollmentRequestForm
            :courses="COURSE_OPTIONS"
            compact
          />
        </div>
      </section>

      <!-- ═══════════════════════════════════════════ NATIJA ═══ -->
      <section
        id="natija"
        class="relative overflow-hidden border-y border-line bg-ink-900/40"
      >
        <!--
          Bo'lim tepasidagi yumshoq yorug'lik. Ilgari bo'limlar bir-biridan
          FAQAT chegara chizig‘i bilan ajralardi va uzun sahifada u ko‘zga
          ilinmasdi. Sabab va ritm `style.css` dagi "LANDING BO'LIMLARI".
        -->
        <div
          class="section-glow"
          aria-hidden="true"
        />

        <div class="relative mx-auto max-w-6xl scroll-mt-20 px-4 py-16 sm:px-6 sm:py-24">
          <div class="max-w-2xl">
            <span class="eyebrow text-xs font-bold uppercase tracking-[1.4px] text-brand-500">
              Natija
            </span>
            <h2
              class="mt-3 font-display text-3xl font-semibold tracking-tight text-slate-100 sm:text-[2.5rem] sm:leading-[1.1]"
            >
              8 oydan keyin nima o‘zgaradi
            </h2>
          </div>

          <div class="mt-12 grid gap-6 sm:grid-cols-2 lg:grid-cols-3">
            <div
              v-for="item in OUTCOMES"
              :key="item.title"
              class="glow-card group rounded-3xl border border-line bg-ink-900 p-7"
              @mouseenter="onCardEnter"
              @mousemove="onCardMove"
            >
              <span class="card-ico flex size-12 items-center justify-center rounded-2xl">
                <AppIcon
                  :name="item.icon"
                  :size="22"
                />
              </span>
              <h3 class="relative mt-5 font-display text-lg font-semibold tracking-tight text-slate-100">
                {{ item.title }}
              </h3>
              <p class="relative mt-2 text-sm leading-relaxed text-slate-400">
                {{ item.text }}
              </p>
            </div>
          </div>
        </div>
      </section>

      <!-- ═════════════════════════════════ KURS TUZILMASI ═══ -->
      <section
        id="kurs"
        class="mx-auto max-w-6xl scroll-mt-20 px-4 py-16 sm:px-6 sm:py-24"
      >
        <div class="max-w-2xl">
          <span class="eyebrow text-xs font-bold uppercase tracking-[1.4px] text-brand-500">
            Kurs tuzilmasi
          </span>
          <h2
            class="mt-3 font-display text-3xl font-semibold tracking-tight text-slate-100 sm:text-[2.5rem] sm:leading-[1.1]"
          >
            Kurs qanday bo‘linadi
          </h2>
          <p class="mt-4 text-base leading-relaxed text-slate-400">
            Asosiy kurs — ATF, 8 oy va uchta modul. Undan keyin Amaliyot II
            va alohida Grammatika kursi bor.
          </p>
        </div>

        <!--
          ══════════════════════════════════════════ BOSQICHLAR ═══
          KURS YO'LI (2026-08-30 da qo'shildi).

          ★ NIMA UCHUN HAFTA JADVALIDAN OLDIN: "hafta qanday o'tadi"
            degan savol FAQAT "men nimaga yozilyapman?" degan savolga
            javob bo'lgandan keyin ma'noli bo'ladi. Ilgari sahifa
            kursni bitta "8 oylik" blok deb ko'rsatardi va o'quvchi
            ATF tugagach nima bo'lishini bilmasdi.

          ★ MODULLAR ICHKI RO'YXATDA, alohida karta EMAS: ular ATF ning
            BO'LAKLARI, ya'ni Amaliyot II bilan bir qatorga qo'yilsa
            ierarxiya yo'qolardi va sahifa "oltita kurs bor" deb
            ko'rsatardi.
        -->
        <ol class="mt-12 grid gap-5 lg:grid-cols-3">
          <!--
            🔴 BIRINCHI BOSQICH AJRATIB KO‘RSATILADI. Uchta karta bir xil
               bo‘lganda odam qaysi biridan boshlashini tanlay olmasdi —
               holbuki javob bitta: ATF kirish nuqtasi, qolgan ikkitasi
               undan KEYIN keladi.
          -->
          <li
            v-for="(stage, index) in COURSE_PATH"
            :key="stage.name"
            class="card-lift flex flex-col rounded-2xl border p-6"
            :class="index === 0
              ? 'border-brand-500/45 bg-brand-500/8'
              : 'border-line bg-ink-900 hover:border-brand-500/30'"
          >
            <div class="flex items-center gap-3">
              <span
                class="flex size-8 shrink-0 items-center justify-center rounded-full bg-brand-500/10 text-sm font-bold text-brand-500"
              >{{ index + 1 }}</span>
              <span
                class="rounded-full bg-ink-800 px-2.5 py-1 text-xs font-bold text-slate-300"
              >{{ stage.duration }}</span>
            </div>

            <h3 class="mt-4 font-display text-xl font-semibold tracking-tight text-slate-100">
              {{ stage.name }}
            </h3>
            <p class="mt-2 text-sm leading-relaxed text-slate-400">
              {{ stage.text }}
            </p>

            <!--
              ══════════════════════════════════ MODUL CHIZIG'I ═══
              ⚠️ 2026-09-03 — RO'YXAT O'RNIGA NISBIY CHIZMA.

              🔴 MUAMMO: ilgari modullar "1-modul — 3 oy / 2-modul — 4 oy"
                 deb ro'yxat bo'lib turardi. Uchala qator bir xil
                 balandlikda bo'lgani uchun ular TENG ko'rinardi — holbuki
                 Qoida moduli Amaliyot I dan TO'RT BAROBAR uzun. Odam
                 raqamlarni o'qib, boshida qayta hisoblashi kerak edi.

              ★ ENDI KENGLIK — DAVOMIYLIK. `flex-grow` qiymati `months`
                dan olinadi, ya'ni chizma ma'lumotdan chiqadi va oy soni
                o'zgarsa o'zi to'g'rilanadi.

              ★ TOR EKRANDA USTUNGA AYLANADI (`max-sm`): 320px kenglikda
                to'rt oylik bo'lak ham 90px bo'lib qolardi va modul nomi
                sig'masdi.

              ⚠️ `min-w-[4.5rem]` — O'QILISHLIK POLI. Sof nisbatda
                 1 oylik modul ~55px bo'lardi va ichidagi matn "3-mo…"
                 bo'lib kesilardi. Ya'ni chizma nisbiy, LEKIN eng kichik
                 bo'lak o'qilmaydigan darajaga tushmaydi. Aniq oy sonlari
                 pastdagi izoh ro'yxatida yozilgan.
            -->
            <div
              v-if="stage.modules !== undefined"
              class="mt-5 border-t border-line pt-5"
            >
              <div class="modbar flex h-14 gap-1.5 max-sm:h-auto max-sm:flex-col">
                <div
                  v-for="(module, moduleIndex) in stage.modules"
                  :key="module.name"
                  class="flex min-w-[4.5rem] flex-col justify-center rounded-xl px-3 py-2 text-white max-sm:min-w-0 max-sm:flex-row max-sm:items-center max-sm:justify-between max-sm:gap-3"
                  :class="`modbar-${moduleIndex}`"
                  :style="{ flexGrow: module.months }"
                >
                  <b class="truncate text-xs font-bold">{{ module.short }}</b>
                  <span class="whitespace-nowrap text-[10.5px] opacity-90">
                    {{ module.duration }}
                  </span>
                </div>
              </div>

              <!--
                IZOH RO'YXATI — chizmadagi qisqa nomlarning TO'LIQ shakli.
                Chizma o'zi yetarli emas: rangli bo'lak nima ekanini
                faqat shu yerda to'liq o'qish mumkin. Tor ekranda esa
                chizmaning o'zi ustunga aylanib to'liq nomni ko'rsata
                boshlaydi — shuning uchun ro'yxat `hidden sm:flex`.
              -->
              <ul class="mt-3.5 hidden flex-wrap gap-x-4 gap-y-1.5 text-xs text-slate-400 sm:flex">
                <li
                  v-for="(module, moduleIndex) in stage.modules"
                  :key="module.name"
                  class="inline-flex items-center gap-1.5"
                >
                  <i
                    class="size-2.5 rounded-[3px]"
                    :class="`modbar-${moduleIndex}`"
                    aria-hidden="true"
                  />
                  {{ module.name }} — {{ module.duration }}
                </li>
              </ul>
            </div>

            <!--
              ══════════════════════════ KURSNI AJRATUVCHI FAKTLAR ═══
              ⚠️ 2026-09-03 DA QO'SHILDI.

              🔴 SABAB: sahifa Fonetika va Grammatika haqida UMUMIY
                 gapirardi, holbuki oradagi farq katta — 5 kun / 2 kun,
                 support bor / yo'q, test bor / yo'q. Batafsil sabab
                 `content.ts` dagi `CourseStage.facts` izohida.

              ★ `mt-auto` — faktlar KARTA TAGIGA yopishadi. Kartalar
                turli balandlikda (birida modul chizmasi bor, birida
                yo'q) va faktlar matndan keyin darhol kelsa, ular
                qatorda turli balandlikda qolib, solishtirish
                qiyinlashardi.
            -->
            <dl
              v-if="stage.facts !== undefined"
              class="mt-auto pt-6"
            >
              <div
                v-for="fact in stage.facts"
                :key="fact.label"
                class="flex items-baseline justify-between gap-3 border-t border-dashed border-line py-2.5"
              >
                <dt class="text-[13px] text-slate-400">
                  {{ fact.label }}
                </dt>
                <dd class="text-right text-[13px] font-semibold text-slate-100">
                  {{ fact.value }}
                </dd>
              </div>
            </dl>
          </li>
        </ol>

        <!-- ═══════════════════════════════════ HAFTA JADVALI ═══ -->
        <!--
          ⚠️ 2026-09-03 — SARLAVHA «FONETIKA KURSIDA» DEB ANIQLASHTIRILDI.

          🔴 SABAB: bu jadval FAQAT fonetika kursiniki. Grammatikada
             haftasiga 2 kun dars va support teacher umuman yo'q
             (`content.ts` dagi `CourseStage.facts`). Sarlavha
             umumiy bo'lganda odam bu jadvalni HAR kursga tegishli deb
             o'qirdi.
        -->
        <h3
          class="mt-16 font-display text-2xl font-semibold tracking-tight text-slate-100 sm:text-3xl"
        >
          Fonetika kursida haftangiz qanday o‘tadi
        </h3>
        <p class="mt-3 max-w-2xl text-base leading-relaxed text-slate-400">
          Haftasiga 5 kun dars: 2 kuni asosiy ustoz bilan, 3 kuni support
          teacher bilan. Jadval oldindan ma'lum — ishingiz yoki
          o‘qishingizga moslashtira olasiz. Grammatika kursi boshqacha
          quriladi: haftasiga 2 kun dars, support teacher esa yo‘q.
        </p>

        <!--
          ═══════════════════════════════════ KUNLAR CHIZMASI ═══
          ⚠️ 2026-09-03 DA QO'SHILDI.

          Ostidagi ikki blok "nima bo'ladi" ni aytadi, bu chizma esa
          "QACHON" ni. Ikkalasi birga turadi — sabab `content.ts` dagi
          `WEEK_DAYS` izohida (u yerda kunlar tasdiqlanishi kerakligi
          ham yozilgan).

          ★ TO'LIQ HAFTA KO'RSATILADI, faqat dars kunlari emas: dam
            olish kunlari tushib qolsa, odam "demak shanba ham dars"
            deb o'ylashi mumkin edi. Ular xiraroq (`opacity`) — ya'ni
            ko'rinadi, lekin e'tiborni tortmaydi.
        -->
        <ul class="mt-8 grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-6">
          <li
            v-for="day in WEEK_DAYS"
            :key="day.label"
            class="card-lift rounded-2xl border p-4 text-center"
            :class="{
              'week-day-main border-transparent': day.kind === 'main',
              'border-line bg-ink-900': day.kind === 'support',
              'border-line bg-ink-900 opacity-55': day.kind === 'off',
            }"
          >
            <span
              class="block text-[11px] font-bold uppercase tracking-[0.12em]"
              :class="day.kind === 'main' ? 'text-green-800' : 'text-slate-500'"
              v-text="day.label"
            />
            <span
              class="mx-auto my-2.5 flex size-9 items-center justify-center rounded-xl"
              :class="{
                'bg-white/16 text-white': day.kind === 'main',
                'bg-green-950 text-green-100': day.kind === 'support',
                'bg-ink-800 text-slate-500': day.kind === 'off',
              }"
            >
              <AppIcon
                :name="day.kind === 'main'
                  ? 'video'
                  : day.kind === 'support' ? 'user-check' : 'clock'"
                :size="18"
              />
            </span>
            <!--
              `whitespace-pre-line` — matndagi `\n` haqiqiy qator
              uzilishiga aylanadi. Shablonda `<br>` yozilsa, matn
              `v-html` talab qilardi.
            -->
            <span
              class="block whitespace-pre-line text-xs font-semibold leading-snug"
              :class="day.kind === 'main' ? 'text-white' : 'text-slate-400'"
              v-text="day.text"
            />
          </li>
        </ul>

        <!--
          🔴 `sm:grid-cols-2` — ILGARI `lg:grid-cols-3` EDI. Blok soni
             uchtadan ikkitaga tushdi (sabab `content.ts` dagi `WEEK`
             izohida), va uch ustunli setkada oxirgi katak bo'sh qolib,
             qator "yarim tugagan" ko'rinardi.
        -->
        <div class="mt-8 grid gap-5 sm:grid-cols-2">
          <!--
            ★ SHAKLI BOSQICH KARTALARIDAN ATAYLAB BOSHQA: ikkalasi bir xil
              bo‘lsa, ikkita ro‘yxat bitta uzun ro‘yxatdek o‘qilardi.
              Bu yerda kun soni KATTA raqam bo‘lib chapda turadi — blok
              kartadan ko‘ra JADVALGA o‘xshaydi.

            "kun" so‘zi SHABLONDA, ma’lumotda emas (sabab `content.ts`
            faylidagi `WeekBlock.days` izohida).
          -->
          <div
            v-for="block in WEEK"
            :key="block.title"
            class="card-lift flex gap-5 rounded-2xl border border-line bg-ink-900 p-6 hover:border-brand-500/30"
          >
            <div
              class="flex w-16 shrink-0 flex-col items-center gap-1 border-r border-line pr-5"
            >
              <span class="font-display text-4xl font-semibold leading-none text-brand-500">
                {{ block.days }}
              </span>
              <span class="text-[11px] font-semibold uppercase tracking-[0.8px] text-slate-500">
                kun
              </span>
              <AppIcon
                class="mt-2 text-slate-600"
                :name="block.icon"
                :size="18"
              />
            </div>

            <div>
              <h3 class="text-base font-bold text-slate-100">
                {{ block.title }}
              </h3>
              <p class="mt-2 text-sm leading-relaxed text-slate-400">
                {{ block.text }}
              </p>
            </div>
          </div>
        </div>

        <!--
          Afzalliklar — tuzilmadan keyin, "nega aynan biz".

          ⚠️ 2026-09-03 — QATORLARDAN KARTALARGA. Ilgari bular ikonka +
             matn qatorlari edi va ular yuqoridagi «Natija» kartalari
             bilan bir sahifada TURLI og'irlikda ko'rinardi: biri
             ko'tarilgan karta, ikkinchisi tekis matn. Holbuki ikkalasi
             ham bir xil vazifani bajaradi — sotuv dalili. Endi ikkalasi
             ham bitta karta shaklida.
        -->
        <div class="mt-16 grid gap-5 sm:grid-cols-2 lg:grid-cols-3">
          <div
            v-for="feature in FEATURES"
            :key="feature.title"
            class="glow-card group rounded-3xl border border-line bg-ink-900 p-7"
            @mouseenter="onCardEnter"
            @mousemove="onCardMove"
          >
            <span class="card-ico flex size-12 items-center justify-center rounded-2xl">
              <AppIcon
                :name="feature.icon"
                :size="22"
              />
            </span>
            <h3 class="relative mt-5 font-display text-lg font-semibold tracking-tight text-slate-100">
              {{ feature.title }}
            </h3>
            <p class="relative mt-2 text-sm leading-relaxed text-slate-400">
              {{ feature.text }}
            </p>
          </div>
        </div>
      </section>

      <!-- ═══════════════════════════════════ KITOB BONUSI ═══ -->
      <section class="mx-auto max-w-6xl px-4 pb-16 sm:px-6 sm:pb-24">
        <!--
          ⚠️ 2026-08-30 — BREND SIRTIDAN OLINDI.

          Bu blok NARXDAN darhol oldin turadi. Ikkalasi ham to‘q yashil
          bo‘lganda ular bitta uzun yashil maydonga qo‘shilib ketardi va
          narx — sahifaning eng muhim bloki — o‘z urg‘usini yo‘qotardi.
          Endi bu yerda ko‘tarilgan to‘q karta va nozik nuqtali tekstura.
        -->
        <div
          class="relative overflow-hidden rounded-3xl border border-line bg-ink-900 px-6 py-12 sm:px-12 sm:py-16"
        >
          <div
            class="dot-layer text-brand-500"
            aria-hidden="true"
          />

          <div class="relative grid gap-10 lg:grid-cols-2 lg:items-center lg:gap-14">
            <div>
              <span class="eyebrow text-xs font-bold uppercase tracking-[1.4px] text-brand-500">
                {{ BOOKS.eyebrow }}
              </span>
              <h2
                class="mt-3 font-display text-3xl font-semibold tracking-tight text-slate-50 sm:text-[2.4rem] sm:leading-[1.1]"
              >
                {{ BOOKS.title }}
              </h2>
              <p class="mt-4 text-base leading-relaxed text-slate-300">
                {{ BOOKS.lead }}
              </p>

              <ul class="mt-6 space-y-4">
                <li
                  v-for="point in BOOKS.points"
                  :key="point"
                  class="flex items-start gap-3"
                >
                  <span
                    class="mt-0.5 flex size-6 shrink-0 items-center justify-center rounded-full bg-brand-500 text-on-brand"
                  >
                    <AppIcon
                      name="check"
                      :size="14"
                    />
                  </span>
                  <span class="text-sm leading-relaxed text-slate-200">{{ point }}</span>
                </li>
              </ul>
            </div>

            <!--
              ══════════════════════════════════ YETKAZISH YO'LI ═══
              ⚠️ 2026-09-03 DA QO'SHILDI.

              ★ NIMA UCHUN CHIZMA: kitob yetkazish — markazning eng kuchli
                ajratuvchi tomoni, lekin u sahifada oddiy belgili ro'yxat
                bo'lib turardi va boshqa ro'yxatlardan farq qilmasdi.
                Chizma bitta gapni ko'z bilan aytadi: kitob BIZDAN
                chiqib, SIZGA eng yaqin pochtaga boradi.

              🔴 SMIL (`animateMotion`) ISHLATILMADI — ATAYLAB, garchi
                 u qisqaroq bo'lsa ham: SMIL animatsiyasini CSS bilan
                 to'xtatib bo'lmaydi, ya'ni `prefers-reduced-motion`
                 tanlagan foydalanuvchida quti baribir yugurib yurardi.
                 CSS `offset-path` esa oddiy animatsiya — pastdagi
                 tinchlik rejimi bloki uni o'chiradi.

              ★ `aria-hidden` + `role="img"` EMAS: chizmada YANGI ma'lumot
                yo'q, uning hammasi yonidagi ro'yxatda matn bilan
                yozilgan. Ekran o'qigich uchun u takror bo'lardi.
            -->
            <div
              class="rounded-3xl border border-line bg-ink-950/60 p-4 sm:p-5"
              aria-hidden="true"
            >
              <svg
                class="w-full"
                viewBox="0 0 420 210"
                fill="none"
              >
                <defs>
                  <path
                    id="ship-route"
                    d="M56 152 C 130 96, 190 190, 250 118 S 340 44, 372 62"
                    fill="none"
                  />
                  <clipPath id="ship-logo-clip">
                    <circle
                      cx="56"
                      cy="152"
                      r="19"
                    />
                  </clipPath>
                </defs>

                <!-- Fon nuqtalari — "xarita" hissi uchun. -->
                <g
                  fill="currentColor"
                  class="text-brand-500/15"
                >
                  <circle
                    cx="120"
                    cy="46"
                    r="2.6"
                  />
                  <circle
                    cx="200"
                    cy="34"
                    r="2"
                  />
                  <circle
                    cx="300"
                    cy="150"
                    r="2.4"
                  />
                  <circle
                    cx="90"
                    cy="100"
                    r="1.8"
                  />
                  <circle
                    cx="352"
                    cy="120"
                    r="2"
                  />
                  <circle
                    cx="160"
                    cy="180"
                    r="2.2"
                  />
                </g>

                <use
                  class="ship-line"
                  href="#ship-route"
                />

                <!-- Boshlanish: ZIN-NUR ombori (haqiqiy logotip). -->
                <image
                  href="/logo-64.png"
                  x="37"
                  y="133"
                  width="38"
                  height="38"
                  clip-path="url(#ship-logo-clip)"
                  preserveAspectRatio="xMidYMid slice"
                />
                <circle
                  class="ship-pulse"
                  cx="56"
                  cy="152"
                  r="19"
                />
                <text
                  class="ship-label"
                  x="56"
                  y="190"
                  text-anchor="middle"
                >ZIN-NUR</text>
                <text
                  class="ship-sublabel"
                  x="56"
                  y="202"
                  text-anchor="middle"
                >kitob ombori</text>

                <!-- Tugash: Uzpost filiali. -->
                <circle
                  class="ship-dest"
                  cx="372"
                  cy="62"
                  r="19"
                />
                <g
                  stroke="#fff"
                  stroke-width="1.8"
                  stroke-linejoin="round"
                  transform="translate(372,62)"
                >
                  <path d="M-7 -4h14v9h-14z" />
                  <path d="M-7 -4 0 1.5 7 -4" />
                </g>
                <text
                  class="ship-label"
                  x="372"
                  y="100"
                  text-anchor="middle"
                >Uzpost</text>
                <text
                  class="ship-sublabel"
                  x="372"
                  y="112"
                  text-anchor="middle"
                >eng yaqin filial</text>

                <!-- Yo'lda ketayotgan quti. -->
                <g class="ship-box">
                  <g transform="translate(-11,-11)">
                    <rect
                      x="0"
                      y="0"
                      width="22"
                      height="22"
                      rx="6"
                      fill="var(--color-ink-900)"
                      stroke="var(--color-green-400)"
                      stroke-width="2"
                    />
                    <path
                      d="M0 8h22M11 8v14"
                      stroke="var(--color-green-400)"
                      stroke-width="1.8"
                    />
                    <path
                      d="M4.5 8 8 1.5M17.5 8 14 1.5"
                      stroke="var(--color-amber-500)"
                      stroke-width="1.8"
                      stroke-linecap="round"
                    />
                  </g>
                </g>

                <!-- Muddat yorlig'i. -->
                <g transform="translate(196,44)">
                  <rect
                    x="-38"
                    y="-14"
                    width="76"
                    height="27"
                    rx="13"
                    fill="var(--color-ink-900)"
                    stroke="var(--color-line-strong)"
                  />
                  <text
                    class="ship-label"
                    x="0"
                    y="4"
                    text-anchor="middle"
                  >5–7 kun</text>
                </g>
              </svg>
            </div>
          </div>
        </div>
      </section>

      <!-- ════════════════════════════════════ DARAJA TESTI ═══ -->
      <!--
        ⚠️ 2026-09-03 — BO'LIM NARXDAN OLDINGA KO'CHIRILDI.

        🔴 SABAB: u narx bilan ariza orasida turardi, MENYUDA esa
           narxdan OLDIN sanalardi. Ya'ni menyudagi tartib sahifadagi
           tartibga to'g'ri kelmasdi: «Daraja testi» ni bosgan odam
           narxdan PASTGA tushardi, aktiv bo'lim belgisi esa orqaga
           sakrardi.

        ★ YANGI JOY MANTIQAN HAM TO'G'RI: odam kurs tuzilmasi va
          jadvalni o'qib bo'ldi, va uning boshidagi savol —
          "men qayerdan boshlayman?". Javob narxdan OLDIN berilsa,
          narx allaqachon aniq modulga bog'langan bo'ladi.

        ★ TEST NATIJASI HAMON ARIZAGA TUSHADI: modal «Shu daraja bilan
          ariza qoldirish» tugmasi formani to'ldirib, o'zi pastga
          suradi — ya'ni bo'limlar orasidagi masofa ahamiyatsiz.
      -->
      <section
        id="daraja"
        class="mx-auto max-w-6xl scroll-mt-20 px-4 py-16 sm:px-6 sm:py-24"
      >
        <div class="grid items-center gap-10 lg:grid-cols-2 lg:gap-14">
          <div>
            <span class="eyebrow text-xs font-bold uppercase tracking-[1.4px] text-brand-500">
              {{ LEVEL_TEST.eyebrow }}
            </span>
            <h2
              class="mt-3 font-display text-3xl font-semibold tracking-tight text-slate-100 sm:text-[2.5rem] sm:leading-[1.1]"
            >
              {{ LEVEL_TEST.title }}
            </h2>
            <p class="mt-4 max-w-[60ch] text-base leading-relaxed text-slate-400">
              {{ LEVEL_TEST.text }}
            </p>

            <ul class="mt-7 grid gap-2.5">
              <li
                v-for="(topic, index) in LEVEL_TEST.topics"
                :key="topic.name"
                class="flex items-center gap-3 rounded-2xl border border-line bg-ink-900 px-4 py-3 transition-colors hover:border-brand-500"
              >
                <span
                  class="grid size-7 shrink-0 place-items-center rounded-lg bg-green-950 text-xs font-bold text-green-100"
                  v-text="index + 1"
                />
                <b class="text-sm font-semibold text-slate-100">{{ topic.name }}</b>
                <!-- Tor ekranda izoh sarlavhani siqib qo'yadi — yashiriladi. -->
                <span class="ml-auto hidden text-right text-[13px] text-slate-400 sm:block">
                  {{ topic.hint }}
                </span>
              </li>
            </ul>

            <BaseButton
              class="mt-7"
              size="lg"
              @click="isLevelTestOpen = true"
            >
              {{ LEVEL_TEST.cta }}
            </BaseButton>

            <div class="mt-5 flex flex-wrap gap-2">
              <span
                v-for="chip in LEVEL_TEST.chips"
                :key="chip"
                class="inline-flex items-center gap-1.5 rounded-full border border-line bg-ink-900 px-3.5 py-2 text-[13px] font-semibold text-slate-400"
              >
                <AppIcon
                  class="text-brand-500"
                  name="check"
                  :size="14"
                />
                {{ chip }}
              </span>
            </div>
          </div>

          <!--
            KO'RGAZMA KARTASI — `aria-hidden`: bu testning RASMI, ishlaydigan
            savol emas. Ekran o'qigich uni javob berilishi mumkin bo'lgan
            savol deb o'qisa, foydalanuvchi bosolmaydigan variantlar ustida
            qolib ketardi.
          -->
          <div
            class="lt-preview rounded-3xl border border-green-900 p-6 shadow-lg"
            aria-hidden="true"
          >
            <div class="flex items-center justify-between gap-3">
              <span class="text-[11px] font-bold uppercase tracking-[0.1em] text-brand-500">
                {{ LEVEL_TEST.preview.stage }}
              </span>
              <span class="text-xs tabular-nums text-slate-400">
                {{ LEVEL_TEST.preview.count }}
              </span>
            </div>

            <div class="mt-4 h-1.5 overflow-hidden rounded-full bg-green-900">
              <i class="lt-preview-bar block h-full w-2/5 rounded-full" />
            </div>

            <p class="mt-5 font-display text-lg text-slate-100">
              {{ LEVEL_TEST.preview.question }}
            </p>

            <p
              class="lt-arabic mt-3.5 rounded-2xl border border-line bg-ink-900 px-4 py-2.5 text-center text-[52px] leading-[1.35] text-green-100"
              lang="ar"
              dir="rtl"
            >
              {{ LEVEL_TEST.preview.arabic }}
            </p>

            <div class="mt-4 grid gap-2">
              <div
                v-for="option in LEVEL_TEST.preview.options"
                :key="option.key"
                class="flex items-center gap-2.5 rounded-xl border px-3.5 py-2.5 text-sm"
                :class="option.correct
                  ? 'border-green-400 bg-green-950 font-bold text-green-100'
                  : 'border-line bg-ink-900 text-slate-400'"
              >
                <span
                  class="grid size-6 shrink-0 place-items-center rounded-md text-[11px] font-bold"
                  :class="option.correct
                    ? 'bg-green-400 text-white'
                    : 'bg-ink-800 text-slate-500'"
                  v-text="option.key"
                />
                {{ option.label }}
              </div>
            </div>
          </div>
        </div>
      </section>

      <!-- ═════════════════════════════════════════════ NARX ═══ -->
      <!--
        ══════════════════════════════════════════════════════════════
         ⚠️ 2026-09-03 — BO'LIM TO'Q YASHILDAN OQQA, NARX KARTAGA
        ══════════════════════════════════════════════════════════════

        Ilgari BUTUN bo'lim to'q yashil sirt edi (hero bilan ayni).
        Qaror o'zgardi, LEKIN SABABI O'ZGARMADI: narx sahifadagi
        ikkinchi qaror nuqtasi va u vizual cho'qqi bo'lib qolishi
        kerak — endi cho'qqi bo'limning O'ZI emas, uning ichidagi
        to'q yashil KARTA.

        ★ NEGA SHUNDAY YAXSHIROQ: to'liq to'q bo'lim ichida "nima
          kiradi" ro'yxati ham to'q fonda edi va u narx bilan BIR XIL
          og'irlikda ko'rinardi. Endi ular qarama-qarshi: to'q karta
          — summa, oq karta — nima olasiz. Ko'z avval summani ko'radi.

        🔴 `surface-brand` ENDI FAQAT KARTADA. Uning ichidagi
           `text-slate-*` va `text-brand-500` o'z-o'zidan to'q sirt
           qiymatlarini oladi (aksent u yerda shampan).
      -->
      <section
        id="narx"
        class="relative mx-auto max-w-6xl scroll-mt-20 px-4 py-16 sm:px-6 sm:py-24"
      >
        <div class="mx-auto max-w-2xl text-center">
          <span class="eyebrow text-xs font-bold uppercase tracking-[1.4px] text-brand-500">
            {{ PRICE.eyebrow }}
          </span>
          <!--
            🔴 "OYLIK TO'LOV" EMAS (2026-08-30). Sarlavhaning o'zi
               ham narxni oyga bog'lab qo'yardi — sabab
               `content.ts` dagi `PRICE` izohida.
          -->
          <h2
            class="mt-3 font-display text-3xl font-semibold tracking-tight text-slate-100 sm:text-[2.5rem] sm:leading-[1.1]"
          >
            Kurs to‘lovi
          </h2>
          <p class="mx-auto mt-4 max-w-[60ch] text-base leading-relaxed text-slate-400">
            {{ PRICE.lead }}
          </p>
        </div>

        <div class="mt-12 grid items-stretch gap-5 lg:grid-cols-2">
          <!-- ══════════════════════════════════ NARX KARTASI ═══ -->
          <div class="price-card surface-brand relative overflow-hidden rounded-3xl p-8 sm:p-10">
            <!--
              Fondagi arabcha «٨» (sakkiz) — kurs 8 oy va to'lov 8 ta
              dars uchun. Bezak juda xira (5%), ya'ni matnga xalaqit
              bermaydi; `aria-hidden` — u o'qilmaydi.
            -->
            <span
              class="price-deco lt-arabic"
              aria-hidden="true"
            >٨</span>

            <div class="relative">
              <p class="text-[13px] font-bold uppercase tracking-[0.13em] text-slate-400">
                ATF · Arab tili Fonetika
              </p>

              <div class="mt-2 flex flex-wrap items-baseline gap-x-2.5 gap-y-1">
                <span class="price-amount font-display text-5xl font-semibold tracking-tight sm:text-6xl">
                  {{ PRICE.amount }}
                </span>
                <span class="text-xl font-semibold text-brand-500">{{ PRICE.currency }}</span>
                <span class="text-sm text-slate-400">/ {{ PRICE.period }}</span>
              </div>

              <p
                class="mt-4 inline-flex items-center gap-2 rounded-full border border-line-strong bg-ink-900 px-4 py-2 text-[13px] font-semibold text-slate-200"
              >
                <AppIcon
                  class="text-brand-500"
                  name="check"
                  :size="15"
                />
                {{ PRICE.perLesson }}
              </p>

              <p class="mt-5 text-sm leading-relaxed text-slate-400">
                {{ PRICE.note }}
              </p>

              <div class="mt-6 flex flex-wrap items-center gap-2.5">
                <span class="text-[13px] text-slate-400">To‘lov usullari:</span>
                <span
                  v-for="method in PRICE.methods"
                  :key="method"
                  class="price-method rounded-lg px-3.5 py-1.5 text-[13px] font-bold"
                  v-text="method"
                />
              </div>

              <!--
                ★ TUGMA OQ, SHAMPAN EMAS: to'q yashil kartada oq
                  to'ldirish eng kuchli kontrastni beradi va bu
                  kartadagi YAGONA harakatga chaqiruv.
              -->
              <button
                type="button"
                class="price-cta mt-7 inline-flex h-12 items-center justify-center gap-2.5 rounded-full px-7 text-base font-semibold"
                @click="scrollToSection('#ariza')"
              >
                Joyni band qilish
                <AppIcon
                  name="chevron-right"
                  :size="17"
                />
              </button>
            </div>
          </div>

          <!-- ═══════════════════════════ NIMA KIRADI (OQ KARTA) ═══ -->
          <div class="rounded-3xl border border-line bg-ink-900 p-7 shadow-xs sm:p-9">
            <h3 class="font-display text-xl font-semibold tracking-tight text-slate-100">
              To‘lov ichiga nima kiradi
            </h3>

            <!--
              PUNKTIR AJRATGICHLAR — to'liq chiziq emas. Ro'yxat beshta
              va qattiq chiziqlar uni jadvalga o'xshatib qo'yardi;
              punktir esa punktlarni ajratadi, lekin "katak" yasamaydi.
            -->
            <ul class="mt-5">
              <li
                v-for="item in PRICE.includes"
                :key="item"
                class="group flex items-start gap-3.5 border-b border-dashed border-line py-3.5 last:border-b-0"
              >
                <span
                  class="price-tick mt-0.5 grid size-6 shrink-0 place-items-center rounded-lg text-on-brand"
                >
                  <AppIcon
                    name="check"
                    :size="13"
                  />
                </span>
                <span class="text-sm leading-relaxed text-slate-300">{{ item }}</span>
              </li>
            </ul>

            <p
              class="mt-6 flex items-start gap-3 rounded-2xl border border-amber-800 bg-amber-950 p-4 text-sm leading-relaxed text-amber-200"
            >
              <AppIcon
                class="mt-0.5 shrink-0 text-amber-400"
                name="alert"
                :size="18"
              />
              {{ PRICE.booksNote }}
            </p>
          </div>
        </div>
      </section>


      <!-- ═══════════════════════════════════════════ ARIZA ═══ -->
      <section
        id="ariza"
        ref="enrollSection"
        class="mx-auto max-w-6xl scroll-mt-20 px-4 py-16 sm:px-6 sm:py-24"
      >
        <div class="grid gap-10 lg:grid-cols-2 lg:gap-14">
          <div>
            <span class="eyebrow text-xs font-bold uppercase tracking-[1.4px] text-brand-500">
              Ariza
            </span>
            <h2
              class="mt-3 font-display text-3xl font-semibold tracking-tight text-slate-100 sm:text-[2.5rem] sm:leading-[1.1]"
            >
              Joyingizni band qiling
            </h2>
            <p class="mt-4 text-base leading-relaxed text-slate-400">
              Guruhda atigi 18–20 joy bor. Ism va telefon raqamingizni
              qoldiring — menejerlarimiz bog‘lanib, darajangizga mos guruhni
              tanlaydi.
            </p>

            <!--
              BOSHLASH BOSQICHLARI — arizadan keyin nima bo'lishini
              ko'rsatadi.

              ⚠️ 2026-09-03 — BOSQICHLAR ORASIGA BOG'LOVCHI CHIZIQ.

              ★ NEGA: to'rtta raqamli doira bir-birining ostida
                turganda ular RO'YXAT bo'lib o'qilardi — ya'ni "to'rtta
                alohida narsa". Holbuki bu KETMA-KETLIK: biri
                tugagandan keyin ikkinchisi boshlanadi. Chiziq aynan
                shu bog'liqlikni ko'rsatadi va odam "keyin nima
                bo'ladi?" degan savolga javobni ko'z bilan oladi.

              ★ CHIZIQ OXIRGI BOSQICHDA CHIZILMAYDI (`last:before:hidden`):
                aks holda u hech qayerga bormasdan osilib qolardi.
            -->
            <ol class="mt-9">
              <li
                v-for="(step, index) in STEPS"
                :key="step.title"
                class="apply-step group relative flex gap-4 pb-7 last:pb-0"
              >
                <span
                  class="apply-step-no relative z-10 grid size-10 shrink-0 place-items-center rounded-2xl text-sm font-bold"
                >{{ index + 1 }}</span>
                <div>
                  <p class="font-display text-base font-semibold tracking-tight text-slate-100">
                    {{ step.title }}
                  </p>
                  <p class="mt-1 text-sm leading-relaxed text-slate-400">
                    {{ step.text }}
                  </p>
                </div>
              </li>
            </ol>

            <!--
              ALOQA — ikki quti. Ilgari bular ikonkali qatorlar edi va
              telefon raqami oddiy matndek ko'rinardi. Quti shaklida u
              bosiladigan narsaga o'xshaydi — telefonda aynan shunday
              ham (raqam `tel:` havolasi).
            -->
            <div class="mt-9 grid gap-3 sm:grid-cols-2">
              <a
                class="glow-card rounded-2xl border border-line bg-ink-900 p-4"
                :href="`tel:${CONTACT.phoneHref}`"
              >
                <span class="block text-[11px] font-bold uppercase tracking-[0.12em] text-slate-400">
                  Telefon
                </span>
                <span class="mt-1.5 block font-display text-base font-semibold tracking-tight text-slate-100">
                  {{ CONTACT.phone }}
                </span>
              </a>
              <div class="rounded-2xl border border-line bg-ink-900 p-4">
                <span class="block text-[11px] font-bold uppercase tracking-[0.12em] text-slate-400">
                  Ish vaqti
                </span>
                <span class="mt-1.5 block text-sm font-semibold text-slate-100">
                  {{ CONTACT.workingHours }}
                </span>
              </div>
            </div>
          </div>

          <EnrollmentRequestForm
            ref="enrollForm"
            :courses="COURSE_OPTIONS"
          />
        </div>
      </section>

      <!-- ════════════════════════════════════════ SAVOLLAR ═══ -->
      <section
        id="savollar"
        class="relative overflow-hidden border-t border-line bg-ink-900/40"
      >
        <div
          class="section-glow"
          aria-hidden="true"
        />

        <div class="relative mx-auto max-w-3xl scroll-mt-20 px-4 py-16 sm:px-6 sm:py-24">
          <h2
            class="font-display text-3xl font-semibold tracking-tight text-slate-100 sm:text-[2.5rem] sm:leading-[1.1]"
          >
            Ko‘p so‘raladigan savollar
          </h2>

          <!--
            `<details>` — ATAYLAB, JS'siz akkordeon. Brauzerning o'zi
            ochib-yopadi, klaviatura bilan ham ishlaydi va qidiruv
            tizimlari matnni ko'radi.
          -->
          <!--
            ⚠️ 2026-09-03 — RO'YXATDAN KARTALARGA.

            Ilgari savollar bitta ramka ichida, faqat ingichka chiziq
            bilan ajratilgan qator edi. To'qqizta savol bunday
            ko'rinishda bitta uzun matn bloki bo'lib qolardi va odam
            o'ziga keraklisini KO'Z bilan topa olmasdi. Endi har savol
            alohida karta — orasida bo'shliq bor va ochilgani ramkasi
            bilan ajralib turadi.
          -->
          <div class="mt-10 grid gap-3">
            <details
              v-for="item in FAQ"
              :key="item.question"
              class="group overflow-hidden rounded-2xl border border-line bg-ink-900 transition-colors open:border-brand-500/45 hover:border-brand-500/45"
            >
              <summary
                class="flex cursor-pointer list-none items-center gap-4 p-5 text-base font-semibold text-slate-100 transition-colors hover:text-brand-500"
              >
                <span
                  class="grid size-7 shrink-0 place-items-center rounded-lg bg-green-950 text-[13px] font-extrabold text-green-100 transition-colors group-open:bg-brand-500 group-open:text-on-brand"
                  aria-hidden="true"
                >?</span>
                {{ item.question }}
                <AppIcon
                  class="ml-auto shrink-0 text-brand-500 transition-transform group-open:rotate-180"
                  name="chevron-down"
                  :size="18"
                />
              </summary>
              <!--
                Javob chap tomondan «?» belgisi kengligicha suriladi —
                shunda u savolga tegishli ekani ko'rinib turadi.
                Telefonda joy yo'q, shuning uchun tekislanadi.
              -->
              <p class="px-5 pb-5 text-sm leading-relaxed text-slate-400 sm:pl-16">
                {{ item.answer }}
              </p>
            </details>
          </div>
        </div>
      </section>
    </main>

    <!-- ═══════════════════════════════════════════ FOOTER ═══ -->
    <!--
      ⚠️ 2026-09-03 — FOOTER TO'Q YASHIL SIRTGA O'TDI.

      Ilgari u sahifaning qolgan qismi bilan bir xil oq fonda edi va
      faqat ingichka chiziq bilan ajralardi — ya'ni sahifa "tugadi"
      degan signal bermasdi, matn shunchaki tugab qolardi.

      ★ `surface-brand` — hero va narx bloki bilan AYNI sirt (qoidalar
        `style.css` da). Ichidagi `text-slate-*`, `border-line` kabi
        klasslarga TEGILMADI: tokenlar o'z-o'zidan to'q qiymatlarni
        oladi.

      ★ YUQORI BURCHAKLAR YUMALOQ: footer sahifaga yopishgan yassi blok
        emas, ostiga surilgan alohida qatlam bo'lib ko'rinadi.
    -->
    <footer class="surface-brand relative z-10 rounded-t-[2rem] bg-ink-950">
      <div class="mx-auto max-w-6xl px-4 py-12 sm:px-6">
        <div class="flex flex-col gap-8 sm:flex-row sm:items-start sm:justify-between">
          <div class="max-w-sm">
            <div class="flex items-center gap-2.5">
              <img
                class="size-8 rounded-full"
                src="/logo-64.png"
                alt=""
                width="32"
                height="32"
              >
              <!--
                So'z belgisi — yuqori paneldagi bilan AYNI qoida.
                Bu yerda logo `size-8` = 32px, ya'ni qator 16px.
              -->
              <span class="flex h-8 flex-col justify-center">
                <span class="font-display text-[15px] font-semibold leading-[16px] tracking-tight text-brand-500">
                  ZIN-NUR
                </span>
                <span class="font-display text-[15px] font-semibold leading-[16px] tracking-tight text-brand-500">
                  ONLINE
                </span>
              </span>
            </div>
            <p class="mt-3 text-sm leading-relaxed text-slate-400">
              Arab tili akademiyasi. ATF — 8 oylik asosiy kurs: jonli
              darslar, support teacher va kitoblar Uzpost orqali.
            </p>

            <div class="mt-5 flex items-center gap-2">
              <a
                v-for="social in SOCIALS"
                :key="social.href"
                class="flex size-10 items-center justify-center rounded-xl border border-line text-slate-400 transition-colors hover:border-brand-500 hover:text-brand-500"
                :href="social.href"
                :aria-label="social.label"
                target="_blank"
                rel="noopener noreferrer"
              >
                <AppIcon
                  :name="social.icon"
                  :size="18"
                />
              </a>
            </div>
          </div>

          <div class="flex flex-col gap-2.5 text-sm">
            <a
              v-for="item in NAV"
              :key="item.href"
              :href="item.href"
              class="text-slate-400 transition-colors hover:text-slate-100"
              @click.prevent="scrollToSection(item.href)"
            >{{ item.label }}</a>

            <RouterLink
              class="text-slate-400 transition-colors hover:text-slate-100"
              to="/login"
            >
              Tizimga kirish
            </RouterLink>

            <!--
              Bot havolasi FAQAT nom sozlangan bo'lsa ko'rinadi. Buzuq
              havola ("t.me/") ko'rsatgandan ko'ra uni umuman
              chizmagan yaxshi.
            -->
            <a
              v-if="BOT_LINK !== null"
              class="text-slate-400 transition-colors hover:text-slate-100"
              :href="BOT_LINK"
              target="_blank"
              rel="noopener noreferrer"
            >Telegram bot</a>
          </div>
        </div>

        <p class="mt-10 border-t border-line pt-6 text-xs text-slate-500">
          © {{ year }} ZIN-NUR ONLINE. Barcha huquqlar himoyalangan.
        </p>
      </div>
    </footer>

    <!-- ═════════════════════════ TELEFONDAGI DOIMIY PANEL ═══ -->
    <!--
      Ko‘rinish sharti va nega aynan shunday ekani `showStickyCta`
      izohida. Qisqasi: hero‘dan pastda VA ariza formasi ekranda
      bo‘lmaganda.

      ★ `sm:hidden` — faqat telefonda. Kompyuterda sahifa kengroq va
        yuqori paneldagi «Kursga yozilish» tugmasi doim yetib turadi.

      ★ Pastki bo‘shliq `env(safe-area-inset-bottom)` bilan: iPhone‘da
        panel "home" chizig‘i ostiga tushib, tugma bosilmay qolardi.
    -->
    <Transition
      enter-active-class="transition-transform duration-300"
      enter-from-class="translate-y-full"
      leave-active-class="transition-transform duration-200"
      leave-to-class="translate-y-full"
    >
      <div
        v-if="showStickyCta"
        class="fixed inset-x-0 bottom-0 z-40 border-t border-line bg-ink-950/95 px-4 py-3 backdrop-blur sm:hidden"
        style="padding-bottom: max(0.75rem, env(safe-area-inset-bottom));"
      >
        <div class="flex items-center gap-2.5">
          <a
            class="flex size-12 shrink-0 items-center justify-center rounded-xl border border-line-strong text-slate-200"
            :href="`tel:${CONTACT.phoneHref}`"
            aria-label="Qo‘ng‘iroq qilish"
          >
            <AppIcon
              name="phone"
              :size="19"
            />
          </a>

          <button
            type="button"
            class="h-12 flex-1 rounded-xl bg-brand-500 text-base font-semibold text-on-brand"
            @click="scrollToSection('#ariza')"
          >
            Kursga yozilish
          </button>
        </div>
      </div>
    </Transition>

    <!-- ═══════════════════════════════ DARAJA TESTI OYNASI ═══ -->
    <LevelTestModal
      :open="isLevelTestOpen"
      @close="isLevelTestOpen = false"
      @apply="onLevelTestApply"
    />
  </div>
</template>

<style scoped>
/*
  Bo'lim uchun uchta gradient — Tailwind sinflari bilan yozilmaydi.

  ⚠️ `.lt-arabic` `LevelTestModal.vue` da HAM bor. Nusxa ataylab:
     `scoped` uslub komponentdan tashqariga chiqmaydi, umumiy fayl esa
     ikki dona `@import` ga arzimaydi (jami olti qator).
*/

/*
  ══════════════════════════════════════════════════════════════════════
   YUQORI PANEL — SUZUVCHI PILYUSKA
  ══════════════════════════════════════════════════════════════════════

  ★ NEGA SHAFFOF OQ, TO'LIQ OQ EMAS: panel to'q yashil hero ustidan
    ham, oq bo'limlar ustidan ham suriladi. To'liq oq bo'lsa u hero
    ustida "yopishtirilgan yorliq" bo'lib ko'rinardi; yarim shaffof
    sirt esa ostidagi rangni sal o'tkazadi va ikkalasi ham bir sahifa
    bo'lib qoladi.

  ⚠️ `backdrop-filter` QO'LLAB-QUVVATLANMASA: `background` baribir
     72% oq — ya'ni matn o'qiladi. Xiralik yo'qoladi, panel yo'qolmaydi.
*/
/*
  PANEL BALANDLIGI — BITTA MANBA.

  Hisob: 14px (ustki bo'shliq) + 10px + 40px (eng baland element —
  tugma yoki burger) + 10px = 74px. Logo `size-9` = 36px, ya'ni u
  balandlikni belgilamaydi.

  ★ O'ZGARUVCHI SAHIFA ILDIZIDA e'lon qilinadi, panelning o'zida emas:
    uni hero ham o'qiydi (`pt-[calc(...)]`), hero esa panelning ichida
    turmaydi — ya'ni panelga yozilsa meros o'tmasdi.
*/
.landing-root {
  --nav-h: 4.625rem;
}

.nav-overlay {
  margin-bottom: calc(-1 * var(--nav-h));
}

.nav-pill {
  background: color-mix(in oklab, var(--color-ink-900) 74%, transparent);
  border: 1px solid color-mix(in oklab, #fff 70%, transparent);
  box-shadow:
    0 1px 2px rgb(15 32 25 / 0.04),
    0 10px 34px -14px color-mix(in oklab, var(--color-green-400) 28%, transparent);
  backdrop-filter: blur(18px) saturate(1.5);
  transition:
    background 0.4s cubic-bezier(0.16, 1, 0.3, 1),
    box-shadow 0.4s cubic-bezier(0.16, 1, 0.3, 1);
}

/*
  Surilgandan keyin panel QUYUQLASHADI. Sabab amaliy, bezak emas: hero
  ostidagi bo'limlarda matn va kartalar zich joylashgan va 74% oq sirt
  orqali ular panel yozuvlariga qo'shilib ketardi.
*/
.nav-pill--stuck {
  background: color-mix(in oklab, var(--color-ink-900) 92%, transparent);
  box-shadow:
    0 2px 6px rgb(15 32 25 / 0.05),
    0 18px 44px -18px color-mix(in oklab, var(--color-green-400) 36%, transparent);
}

/* Logo — bosilishi mumkin ekanini ko'rsatadigan yengil harakat. */
.nav-mark {
  box-shadow:
    0 6px 16px -6px color-mix(in oklab, var(--color-green-400) 55%, transparent),
    0 0 0 1px color-mix(in oklab, var(--color-green-400) 12%, transparent);
  transition:
    transform 0.5s cubic-bezier(0.16, 1, 0.3, 1),
    box-shadow 0.4s cubic-bezier(0.16, 1, 0.3, 1);
}

.nav-brand:hover .nav-mark {
  transform: rotate(-6deg) scale(1.07);
}

/*
  Menyu havolasi: fon pilyuskasi + ostidan o'sib chiquvchi chiziqcha.

  ★ NEGA IKKALASI: faqat fon bo'lsa havola "tugma" ga o'xshab ketardi,
    faqat chiziq bo'lsa harakat sezilmasdi. Ikkalasi birga — havola
    havolaligicha qoladi, lekin javob beradi.
*/
.nav-link {
  position: relative;
}

.nav-link::after {
  content: '';
  position: absolute;
  left: 50%;
  bottom: 5px;
  width: 0;
  height: 2px;
  border-radius: 2px;
  background: var(--color-brand-500);
  transform: translateX(-50%);
  transition: width 0.35s cubic-bezier(0.16, 1, 0.3, 1);
}

.nav-link:hover {
  background: var(--color-green-950);
  color: var(--color-green-100);
}

.nav-link:hover::after {
  width: 18px;
}

/*
  AKTIV BO'LIM.

  ★ HOVERDAN KUCHLIROQ: fon bir xil, LEKIN matn to'q va QALIN, chiziqcha
    esa doim ochiq va uzunroq. Hover — "bosish mumkin", aktiv — "siz shu
    yerdasiz"; ikkalasi bir xil ko'rinsa, sichqoncha ustidan o'tganda
    odam joyini yo'qotardi.
*/
.nav-link--active {
  background: var(--color-green-950);
  color: var(--color-green-100);
  font-weight: 600;
}

.nav-link--active::after {
  width: 22px;
}

.nav-drawer-link--active {
  background: var(--color-green-950);
  color: var(--color-green-100);
}

.nav-quiet {
  transition: background 0.25s, color 0.25s;
}

.nav-quiet:hover {
  background: var(--color-green-950);
  color: var(--color-green-100);
}

/*
  Asosiy tugma — pilyuska ichidagi yagona to'ldirilgan element.

  ★ `::after` — ustidan bir marta o'tuvchi yaltirash. U FAQAT hoverda
    ishlaydi va takrorlanmaydi: doimiy animatsiya panelda ko'z oldida
    turgani uchun tez charchatardi.
*/
.nav-cta {
  position: relative;
  overflow: hidden;
  background: var(--color-brand-500);
  box-shadow: 0 10px 24px -10px color-mix(in oklab, var(--color-green-400) 60%, transparent);
  transition: background 0.25s, transform 0.35s cubic-bezier(0.16, 1, 0.3, 1);
}

.nav-cta::after {
  content: '';
  position: absolute;
  inset: 0;
  background: linear-gradient(
    105deg,
    transparent 30%,
    rgb(255 255 255 / 0.34) 46%,
    transparent 62%
  );
  transform: translateX(-120%);
}

.nav-cta:hover {
  background: var(--color-brand-600);
  transform: translateY(-1px);
}

.nav-cta:hover::after {
  transform: translateX(120%);
  transition: transform 0.85s cubic-bezier(0.16, 1, 0.3, 1);
}

/*
  BURGER — uchta chiziq xochga aylanadi.

  O'rtadagi chiziq `<span>` ning O'ZI, qolgan ikkitasi uning
  `::before`/`::after` i. Ochilganda o'rtadagisi ko'rinmas bo'ladi,
  chetdagilari esa markazga kelib kesishadi.
*/
.nav-burger {
  background: var(--color-green-950);
  border: 1px solid var(--color-green-900);
}

.nav-burger > span,
.nav-burger > span::before,
.nav-burger > span::after {
  display: block;
  width: 17px;
  height: 2px;
  border-radius: 2px;
  background: var(--color-green-100);
  transition: 0.3s cubic-bezier(0.16, 1, 0.3, 1);
}

.nav-burger > span {
  position: relative;
}

.nav-burger > span::before,
.nav-burger > span::after {
  content: '';
  position: absolute;
  left: 0;
}

.nav-burger > span::before {
  top: -5.5px;
}

.nav-burger > span::after {
  top: 5.5px;
}

.nav-burger--open > span {
  background: transparent;
}

.nav-burger--open > span::before {
  top: 0;
  transform: rotate(45deg);
}

.nav-burger--open > span::after {
  top: 0;
  transform: rotate(-45deg);
}

/* Mobil menyu kartasi — panel bilan bir xil oynasimon sirt. */
.nav-drawer {
  z-index: 1;
  background: color-mix(in oklab, var(--color-ink-900) 97%, transparent);
  border: 1px solid var(--color-line);
  box-shadow: 0 34px 80px -28px color-mix(in oklab, var(--color-green-400) 32%, transparent);
  backdrop-filter: blur(20px);
}

.nav-drawer-link:hover {
  background: var(--color-green-950);
  color: var(--color-green-100);
}

/*
  ══════════════════════════════════════════════════════════════════════
   HERO
  ══════════════════════════════════════════════════════════════════════

  ⚠️ 2026-09-03 — SAHNA YORUG' SIRTGA MOSLANDI. Ilgari u `.surface-brand`
     ichida edi va bu yerdagi tokenlar to'q qiymatlarni berardi (aksent
     shampan). Endi hero yorug', ya'ni `--color-brand-500` yashil,
     `--color-ink-900` oq. Qoidalar tokenlar orqali yozilgani uchun
     ko'pchiligi o'z-o'zidan to'g'rilandi; qo'lda o'zgargani — sirt
     ranglari (yadro, kartalar soyasi, orbita chizig'i).

  ★ FAQAT `transform` VA `opacity` ANIMATSIYA QILINADI: qolgan
    xossalar (kenglik, rang) har kadrda qayta bo'yashga majbur qilardi.
*/

/*
  Sarlavha ostidagi qo'lda chizilgan chiziq.

  ★ `stroke-dasharray` = chiziqning taxminiy uzunligi. Aniq o'lchash
    (`getTotalLength`) uchun JS kerak bo'lardi; bu yerda chiziq qat'iy
    va uzunligi o'zgarmaydi, shuning uchun qiymat qo'lda berilgan.
    Kattaroq qiymat zarar qilmaydi — chiziq shunchaki bir oz kechroq
    to'liq ochiladi.
*/
.hero-underline {
  position: absolute;
  left: 0;
  bottom: -0.16em;
  width: 100%;
  height: 0.34em;
  overflow: visible;
}

.hero-underline path {
  fill: none;
  stroke: var(--color-green-800);
  stroke-width: 7;
  stroke-linecap: round;
  stroke-dasharray: 420;
  stroke-dashoffset: 420;
  animation: hero-draw 1.5s 0.5s cubic-bezier(0.16, 1, 0.3, 1) forwards;
}

@keyframes hero-draw {
  to {
    stroke-dashoffset: 0;
  }
}

/* Ikkilamchi tugma va ijtimoiy tarmoq belgilari — bir xil javob. */
.hero-ghost,
.hero-social {
  box-shadow: 0 1px 2px rgb(15 32 25 / 0.04), 0 4px 14px rgb(15 32 25 / 0.05);
  transition:
    transform 0.35s cubic-bezier(0.16, 1, 0.3, 1),
    border-color 0.25s,
    color 0.25s,
    box-shadow 0.35s;
}

.hero-ghost:hover,
.hero-social:hover {
  transform: translateY(-3px);
  border-color: var(--color-green-800);
  color: var(--color-brand-500);
  box-shadow: 0 2px 6px rgb(15 32 25 / 0.05), 0 14px 30px -12px color-mix(in oklab, var(--color-green-400) 30%, transparent);
}

/*
  ══════════════════════════════════════════════════════════════════════
   STATISTIKA KARTASI
  ══════════════════════════════════════════════════════════════════════
*/

.stats-card {
  background: linear-gradient(
    140deg,
    var(--color-green-200),
    var(--color-green-100) 45%,
    var(--color-green-50)
  );
  box-shadow: 0 34px 80px -28px rgb(2 42 26 / 0.45);
}

/*
  Kataklar orasidagi ajratgich.

  ★ NEGA `border`, alohida element emas: to'rt katak setkada turadi va
    chegara birinchi ustundan boshqa hammasiga qo'yiladi — ya'ni
    chekkada ortiqcha chiziq qolmaydi. Telefonda esa ikki ustun bor va
    ikkinchi qatorga ustki chiziq qo'shiladi.
*/
.stats-cell + .stats-cell {
  border-left: 1px solid rgb(255 255 255 / 0.13);
}

@media (width < 40rem) {
  .stats-cell:nth-child(odd) {
    border-left: 0;
  }

  .stats-cell:nth-child(n + 3) {
    border-top: 1px solid rgb(255 255 255 / 0.13);
  }
}

/* Raqam — oqdan och yashilga gradient (narx summasi bilan bir uslub). */
.stats-value {
  background: linear-gradient(180deg, #fff, var(--color-green-800));
  background-clip: text;
  -webkit-background-clip: text;
  color: transparent;
}

.hero-stage {
  position: relative;
  width: 100%;
  max-width: 26rem;
  margin-inline: auto;
  aspect-ratio: 1;
}

/* Punktir orbita — sekin aylanadi. */
.hero-orbit {
  position: absolute;
  inset: 6%;
  border-radius: 50%;
  /* Yashil tinti — neytral kul rang yorug' fonda "iflos" ko'rinardi. */
  border: 1.5px dashed color-mix(in oklab, var(--color-green-400) 20%, transparent);
  animation: hero-spin 44s linear infinite;
}

.hero-orbit--inner {
  inset: 17%;
  border-style: solid;
  border-color: color-mix(in oklab, var(--color-green-400) 10%, transparent);
  animation-duration: 34s;
  animation-direction: reverse;
}

@keyframes hero-spin {
  to {
    transform: rotate(360deg);
  }
}

/* Orbitadagi nuqtalar — aylanish ko'rinishi uchun. */
.hero-pip {
  position: absolute;
  width: 11px;
  height: 11px;
  border-radius: 50%;
}

.hero-pip--a {
  top: -6px;
  left: 50%;
  margin-left: -5px;
  background: var(--color-brand-500);
  box-shadow: 0 0 0 5px color-mix(in oklab, var(--color-brand-500) 18%, transparent);
}

.hero-pip--b {
  right: 2%;
  bottom: 8%;
  background: var(--color-brand-vivid);
  box-shadow: 0 0 0 5px color-mix(in oklab, var(--color-brand-vivid) 22%, transparent);
}

/*
  Yadro — markazdagi harf.

  ★ To'q sirtda yadro YORUG' emas, biroz KO'TARILGAN: oq doira yashil
    fonda "teshik" bo'lib ko'rinardi. Shuning uchun ichki yorug'lik
    nozik va shampan tomon egilgan.
*/
/*
  ⚠️ 2026-09-03 — YOZUV `absolute` DAN OQIMGA QAYTARILDI.

  🔴 MUAMMO: «ع» ning dumi tagidagi «AYN HARFI» yozuviga TUSHIB
     qolgan edi. Ikki sabab birga ishlagan: yozuv `absolute` bilan
     pastga mixlangan, harf esa `line-height: 1` bilan — ya'ni uning
     quyi qismi o'z qatori qutisidan CHIQIB ketardi va tartib bu
     chiqishni umuman hisobga olmasdi.

  ★ ENDI USTUN: harf va yozuv oddiy oqimda, ustma-ust emas, ketma-ket.
    Harfga esa haqiqiy ink balandligini o'z ichiga oladigan
    `line-height` berilgan — shunda tartib uning dumini ham hisoblaydi
    va ular hech qachon to'qnashmaydi.
*/
.hero-core {
  position: absolute;
  inset: 26%;
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 0.15rem;
  border-radius: 50%;
  /*
    ⚠️ 2026-09-03 — YORUG' SIRT UCHUN QAYTA BO'YALDI. Ilgari yadro
       to'q yashil doira edi; oq fonda u "qora dog'" bo'lib qolardi.
       Endi oqdan och yashilga o'tuvchi yumshoq shar.

    ★ SOYA YASHIL TINTLI, neytral emas: kul rang soya iliq oq fonda
      kirlangan halqa qoldirardi.
  */
  background:
    radial-gradient(
      circle at 32% 28%,
      #fff,
      var(--color-green-950) 62%,
      var(--color-green-900)
    );
  box-shadow:
    inset 0 2px 0 #fff,
    0 30px 70px -26px color-mix(in oklab, var(--color-green-400) 55%, transparent);
  animation: hero-bob 6.5s ease-in-out infinite alternate;
}

@keyframes hero-bob {
  from {
    transform: translateY(-10px);
  }

  to {
    transform: translateY(10px);
  }
}

/*
  ══════════════════════════════════════════════════════════════════════
   ⚠️ 2026-09-03 (ikkinchi urinish) — «ع» YOZUV USTIGA TUSHISHI
  ══════════════════════════════════════════════════════════════════════

  🔴 BIRINCHI TUZATISH YETARLI BO'LMADI. O'shanda `line-height: 1.3`
     qo'yilgandi, lekin harf hamon yozuvni kesib o'tardi.

  SABAB — AMIRI'NING VERTIKAL O'LCHAMLARI. Amiri arab yozuvi uchun
  yasalgan va uning quyi chiqishi (descender) lotin shriftlaridagidan
  ANCHA katta: shriftning to'liq balandligi ~1.55em atrofida. Ya'ni
  `line-height: 1.3` da harfning dumi qator qutisidan HAMON tashqarida
  qolardi — tartib esa faqat qutini biladi, siyohni emas.

  ★ YECHIM: `line-height` shriftning to'liq balandligidan KATTA
    qilinadi (1.85). Shunda siyoh butunlay quti ichida qoladi va
    ustundagi keyingi element (yozuv) unga hech qanday holatda
    tegmaydi. Harf o'lchami ham biroz kichraytirildi — yadro ichida
    ikkalasiga ham bemalol joy qolsin uchun.

  ⚠️ BU QIYMATNI KAMAYTIRMANG. Boshqa harf qo'yilsa
     (`HERO_LETTER.glyph`) ham xavfsiz qoladi: 1.85 Amiri'dagi eng
     chuqur tushadigan harflarni ham qoplaydi.
*/
.hero-glyph {
  font-family: var(--font-arabic);
  font-size: clamp(2.75rem, 6.5vw, 4.5rem);
  line-height: 1.85;
  /* Och yadroda to'q yashil — aksent rangidan bir necha qadam quyuqroq. */
  color: var(--color-green-100);
  text-shadow: 0 6px 18px color-mix(in oklab, var(--color-green-400) 22%, transparent);
}

.hero-cap {
  font-size: 11.5px;
  font-weight: 700;
  letter-spacing: 0.14em;
  text-transform: uppercase;
  white-space: nowrap;
  color: var(--color-brand-500);
}

/* Yadrodan tarqaluvchi halqalar — "jonli" hissi. */
.hero-ripple {
  position: absolute;
  inset: 26%;
  border-radius: 50%;
  border: 2px solid color-mix(in oklab, var(--color-brand-500) 45%, transparent);
  opacity: 0;
  animation: hero-ripple 3.4s cubic-bezier(0.16, 1, 0.3, 1) infinite;
}

.hero-ripple--b {
  animation-delay: 1.7s;
}

@keyframes hero-ripple {
  0% {
    transform: scale(1);
    opacity: 0.6;
  }

  100% {
    transform: scale(1.62);
    opacity: 0;
  }
}

/*
  Suzuvchi kartalar.

  ★ HAR BIRI BOSHQA TEZLIK VA KECHIKISH bilan suzadi: bir xil bo'lsa
    to'rttasi birga ko'tarilib-tushib, takrorlanish darhol sezilardi
    (ayni qoida hero fonidagi sharlarda).
*/
.hero-card {
  position: absolute;
  padding: 0.75rem 0.9rem;
  border-radius: 1rem;
  /* Yorug' sirtda kartalar oq va soya bilan "ko'tarilgan". */
  background: color-mix(in oklab, #fff 92%, transparent);
  border: 1px solid color-mix(in oklab, #fff 85%, transparent);
  box-shadow:
    0 2px 8px rgb(15 32 25 / 0.05),
    0 22px 46px -20px color-mix(in oklab, var(--color-green-400) 45%, transparent);
  backdrop-filter: blur(12px);
  animation: hero-float 7s ease-in-out infinite alternate;
  white-space: nowrap;
}

@keyframes hero-float {
  from {
    transform: translateY(0);
  }

  to {
    transform: translateY(-16px);
  }
}

.hero-card--0 {
  top: 1%;
  left: -4%;
  animation-duration: 6.2s;
}

.hero-card--1 {
  top: 24%;
  right: -6%;
  animation-duration: 7.4s;
  animation-delay: -3.4s;
}

.hero-card--2 {
  bottom: 9%;
  left: -6%;
  animation-duration: 8.4s;
  animation-delay: -2s;
}

.hero-card--3 {
  right: 0;
  bottom: 0;
  animation-duration: 6.8s;
  animation-delay: -1.2s;
}

/*
  Eng tor ekranlarda kartalar sahna chegarasi ichiga tortiladi, aks
  holda ular o'ram chetidan chiqib, gorizontal skroll yasardi.
*/
@media (width < 48rem) {
  .hero-card--0,
  .hero-card--2 {
    left: 0;
  }

  .hero-card--1 {
    right: 0;
  }
}

.hero-avatar {
  display: grid;
  place-items: center;
  width: 26px;
  height: 26px;
  margin-left: -8px;
  border-radius: 50%;
  /* Karta oq — halqa ham oq, ya'ni avatarlar bir-birini "kesib" turadi. */
  border: 2px solid #fff;
  font-style: normal;
  font-size: 10px;
  font-weight: 700;
  color: #fff;
}

.hero-avatar--0 {
  margin-left: 0;
  background: linear-gradient(140deg, var(--color-green-500), var(--color-green-100));
}

.hero-avatar--1 {
  background: linear-gradient(140deg, var(--color-amber-500), var(--color-amber-400));
}

.hero-avatar--2 {
  background: linear-gradient(140deg, var(--color-sky-500), var(--color-sky-300));
}

.hero-avatar--3 {
  background: var(--color-green-950);
  color: var(--color-green-100);
  font-size: 9px;
}

/*
  Tovush to'lqini. Balandlik va kechikish `--bar` indeksidan
  hisoblanadi — sakkizta ustunga sakkizta qoida yozish o'rniga.
*/
.hero-wave-bar {
  width: 3.5px;
  border-radius: 99px;
  background: var(--color-brand-500);
  height: calc(35% + (var(--bar) * 7%));
  animation: hero-eq 1.1s ease-in-out infinite alternate;
  animation-delay: calc(var(--bar) * 0.1s);
}

@keyframes hero-eq {
  to {
    transform: scaleY(0.35);
  }
}

.hero-progress {
  background: linear-gradient(90deg, var(--color-brand-600), var(--color-brand-500));
  animation: hero-fill 3.4s cubic-bezier(0.16, 1, 0.3, 1) infinite;
}

@keyframes hero-fill {
  0% {
    width: 6%;
  }

  55%,
  100% {
    width: 78%;
  }
}

/*
  ══════════════════════════════════════════════════════════════════════
   ARIZA — BOSQICHLAR ZANJIRI
  ══════════════════════════════════════════════════════════════════════

  Chiziq raqamli kvadratning ORTIDAN o'tadi va keyingisiga tushadi.
  `top: 2.5rem` — kvadrat balandligi (`size-10`), ya'ni chiziq aynan
  uning ostidan boshlanadi.
*/
.apply-step::before {
  content: '';
  position: absolute;
  top: 2.5rem;
  bottom: 0;
  left: 1.25rem;
  width: 2px;
  background: linear-gradient(
    180deg,
    var(--color-green-800),
    color-mix(in oklab, var(--color-green-400) 12%, transparent)
  );
}

.apply-step:last-child::before {
  display: none;
}

/*
  Raqam. Hoverda to'ldiriladi — bosqich "faollashgandek" ko'rinadi.

  ⚠️ BU FAQAT BEZAK: bosqichlar bosiladigan emas va ular haqiqiy holatni
     ko'rsatmaydi. Shuning uchun `cursor` ham o'zgarmaydi — odam bosishga
     urinmaydi.
*/
.apply-step-no {
  background: var(--color-ink-900);
  border: 1.5px solid var(--color-green-900);
  color: var(--color-green-100);
  box-shadow: 0 1px 2px rgb(15 32 25 / 0.04), 0 4px 14px rgb(15 32 25 / 0.05);
  transition:
    transform 0.45s cubic-bezier(0.16, 1, 0.3, 1),
    background 0.3s,
    color 0.3s,
    border-color 0.3s;
}

.apply-step:hover .apply-step-no {
  transform: scale(1.1) rotate(-6deg);
  background: var(--color-brand-500);
  border-color: transparent;
  color: var(--color-on-brand);
}

/*
  ══════════════════════════════════════════════════════════════════════
   UMUMIY KARTA
  ══════════════════════════════════════════════════════════════════════
*/

.glow-card {
  position: relative;
  overflow: hidden;
  box-shadow: 0 1px 2px rgb(15 32 25 / 0.04), 0 4px 14px rgb(15 32 25 / 0.05);
  transition:
    transform 0.45s cubic-bezier(0.16, 1, 0.3, 1),
    box-shadow 0.45s cubic-bezier(0.16, 1, 0.3, 1),
    border-color 0.3s;
}

/*
  Kursor yorug'ligi. Markazi `--mx`/`--my` dan keladi (JS izohi
  `onCardMove` da), standart qiymat esa kartaning tepa-o'rtasi —
  ya'ni sichqonchasiz qurilmada ham blok mantiqiy ko'rinadi.
*/
.glow-card::before {
  content: '';
  position: absolute;
  inset: 0;
  opacity: 0;
  pointer-events: none;
  background: radial-gradient(
    420px 220px at var(--mx, 50%) var(--my, 0%),
    color-mix(in oklab, var(--color-green-400) 8%, transparent),
    transparent 70%
  );
  transition: opacity 0.4s;
}

.glow-card:hover {
  transform: translateY(-6px);
  border-color: var(--color-green-800);
  box-shadow: 0 2px 6px rgb(15 32 25 / 0.05), 0 18px 44px -14px color-mix(in oklab, var(--color-green-400) 20%, transparent);
}

.glow-card:hover::before {
  opacity: 1;
}

/*
  Ikonka bloki — och yashil gradient.

  ★ HOVERDA BURILADI: karta ko'tarilganda ikonka ham javob beradi,
    ya'ni harakat butun kartaga tarqaladi, faqat soyaga emas.
*/
.card-ico {
  position: relative;
  background: linear-gradient(150deg, var(--color-green-950), var(--color-green-900));
  border: 1px solid var(--color-green-900);
  color: var(--color-green-100);
  transition: transform 0.5s cubic-bezier(0.16, 1, 0.3, 1);
}

.glow-card:hover .card-ico {
  transform: translateY(-3px) rotate(-7deg) scale(1.07);
}

/*
  ══════════════════════════════════════════════════════════════════════
   SAHIFA FONI
  ══════════════════════════════════════════════════════════════════════

  ★ UCHTA SHAR — UCH XIL RANG VA UCH XIL TEZLIK. Bir xil bo'lsa
    uchalasi birga "nafas olib", takrorlanish darhol sezilardi (ayni
    qoida hero fonidagi sharlarda va suzuvchi kartalarda).

  ★ RANGLAR BREND PALITRASIDAN TASHQARIGA CHIQADI (ko'k va sariq):
    faqat yashil bo'lsa fon bitta tekis yashil tumanga aylanardi.
    Ular juda xira, ya'ni "boshqa brend" hissi tug'dirmaydi.
*/
.decor-blob {
  position: absolute;
  border-radius: 50%;
  filter: blur(70px);
  opacity: 0.5;
  animation: decor-drift 26s ease-in-out infinite alternate;
}

.decor-blob--a {
  top: -190px;
  right: -140px;
  width: 520px;
  height: 520px;
  background: radial-gradient(circle, var(--color-green-800), transparent 68%);
}

.decor-blob--b {
  top: 46%;
  left: -220px;
  width: 460px;
  height: 460px;
  background: radial-gradient(circle, var(--color-sky-800), transparent 68%);
  animation-duration: 32s;
  animation-delay: -8s;
}

.decor-blob--c {
  right: 8%;
  bottom: -120px;
  width: 400px;
  height: 400px;
  background: radial-gradient(circle, var(--color-amber-800), transparent 68%);
  animation-duration: 38s;
  animation-delay: -16s;
  opacity: 0.42;
}

@keyframes decor-drift {
  0% {
    transform: translate3d(0, 0, 0) scale(1);
  }

  50% {
    transform: translate3d(30px, -40px, 0) scale(1.09);
  }

  100% {
    transform: translate3d(-24px, 34px, 0) scale(0.96);
  }
}

/*
  Fondagi arab harflari.

  ⚠️ SHAFFOFLIK 4.5% — `content.ts` dagi `DECOR_LETTERS` izohida
     nima uchun oshirilmasligi yozilgan.
*/
.decor-letter {
  position: absolute;
  line-height: 1;
  color: var(--color-brand-500);
  opacity: 0.045;
  user-select: none;
  animation: decor-sway 16s ease-in-out infinite alternate;
}

.decor-letter--0 {
  top: 12%;
  left: 4%;
  font-size: 12.5rem;
}

.decor-letter--1 {
  top: 56%;
  right: 5%;
  font-size: 9.5rem;
  animation-duration: 21s;
  animation-delay: -4s;
}

.decor-letter--2 {
  bottom: 8%;
  left: 16%;
  font-size: 7.5rem;
  animation-duration: 19s;
  animation-delay: -9s;
}

@keyframes decor-sway {
  0% {
    transform: translateY(0) rotate(-4deg);
  }

  100% {
    transform: translateY(-38px) rotate(5deg);
  }
}

/*
  ══════════════════════════════════════════════════════════════════════
   NARX KARTASI
  ══════════════════════════════════════════════════════════════════════
*/

/*
  Karta foni — uch to'xtashli to'q yashil gradient, ustidan ikkita
  yumshoq nur.

  ★ NEGA `::before` DAGI NURLAR: tekis to'q sirt kattaligi tufayli
    "o'lik" ko'rinardi. Nurlar unga chuqurlik beradi, lekin naqsh
    yasamaydi — ya'ni matn ustida hech qanday kontrast yo'qolmaydi.
*/
.price-card {
  background: linear-gradient(
    155deg,
    var(--color-green-200),
    var(--color-green-100) 48%,
    var(--color-green-50)
  );
  box-shadow: 0 34px 80px -28px rgb(2 42 26 / 0.55);
}

.price-card::before {
  content: '';
  position: absolute;
  inset: 0;
  background:
    radial-gradient(
      600px 260px at 12% -10%,
      color-mix(in oklab, var(--color-green-800) 26%, transparent),
      transparent 60%
    ),
    radial-gradient(
      420px 200px at 100% 110%,
      color-mix(in oklab, var(--color-amber-500) 20%, transparent),
      transparent 60%
    );
  pointer-events: none;
}

/* Fondagi arabcha «٨». */
.price-deco {
  position: absolute;
  top: -2.5rem;
  right: -0.75rem;
  font-size: 12rem;
  line-height: 1;
  color: #fff;
  opacity: 0.05;
  user-select: none;
  pointer-events: none;
}

/*
  Summa — oqdan ochiq yashilga gradient matn.

  ⚠️ `color: transparent` bilan birga `background-clip: text` KERAK.
     Ikkisidan biri tushib qolsa summa YO'QOLADI (shaffof matn) —
     shuning uchun ular hech qachon ajratilmaydi.
*/
.price-amount {
  background: linear-gradient(180deg, #fff, var(--color-green-800));
  background-clip: text;
  -webkit-background-clip: text;
  color: transparent;
}

/* Click / Payme — oq yorliqlar. */
.price-method {
  background: color-mix(in oklab, #fff 94%, transparent);
  color: var(--color-green-100);
}

.price-cta {
  background: #fff;
  color: var(--color-green-100);
  box-shadow: 0 12px 30px -10px rgb(0 0 0 / 0.5);
  transition: transform 0.35s cubic-bezier(0.16, 1, 0.3, 1), box-shadow 0.35s;
}

.price-cta:hover {
  transform: translateY(-3px);
  box-shadow: 0 18px 38px -12px rgb(0 0 0 / 0.55);
}

/* «Nima kiradi» ro'yxatidagi belgi. */
.price-tick {
  background: var(--color-brand-500);
  box-shadow: 0 5px 12px -5px color-mix(in oklab, var(--color-green-400) 70%, transparent);
  transition: transform 0.4s cubic-bezier(0.16, 1, 0.3, 1);
}

.group:hover .price-tick {
  transform: scale(1.12) rotate(-8deg);
}

/*
  E'LON PANELI. Gradient chapdan o'ngga to'qdan yorug'gacha — panel tor
  bo'lgani uchun bitta tekis yashil "chiziq" bo'lib qolardi.
*/
.announce-bar {
  background: linear-gradient(
    100deg,
    var(--color-green-100),
    var(--color-green-400) 55%,
    var(--color-green-500)
  );
}

/*
  «Jonli» nuqta: ichki yadro + tarqalib so'nadigan halqa.

  ★ NEGA `::after` HALQA, `animate-ping` EMAS: Tailwind'ning `ping`
    utilitasi ELEMENTNING O'ZINI kattalashtiradi, ya'ni yadro ham
    yo'qolib-paydo bo'lardi. Bu yerda yadro qimirlamaydi.
*/
.announce-dot {
  position: relative;
  flex: none;
  width: 7px;
  height: 7px;
  border-radius: 50%;
  background: var(--color-green-800);
}

.announce-dot::after {
  content: '';
  position: absolute;
  inset: -4px;
  border-radius: 50%;
  border: 1.5px solid var(--color-green-800);
  animation: announce-ping 1.9s cubic-bezier(0.16, 1, 0.3, 1) infinite;
}

@keyframes announce-ping {
  0% {
    transform: scale(0.5);
    opacity: 0.9;
  }

  80%,
  100% {
    transform: scale(1.7);
    opacity: 0;
  }
}

/*
  TINCHLIK REJIMI — loyihadagi qolgan animatsiyalar bilan bir xil qoida.

  ★ QUTI YO'QOLMAYDI, TO'XTAYDI: `animation: none` bilan u `offset-distance`
    ning boshlang'ich qiymatida (0%) — ya'ni omborda turadi va chizma
    "kitob bizdan chiqadi" degan ma'nosini saqlaydi.
*/
@media (prefers-reduced-motion: reduce) {
  .announce-dot::after,
  .decor-blob,
  .decor-letter,
  .ship-line,
  .ship-pulse,
  .ship-box,
  .hero-orbit,
  .hero-core,
  .hero-ripple,
  .hero-card,
  .hero-wave-bar,
  .hero-progress {
    animation: none;
  }

  /* Ko'tarilish ham harakat — tinchlik rejimida karta joyida qoladi. */
  .glow-card:hover,
  .glow-card:hover .card-ico,
  .apply-step:hover .apply-step-no {
    transform: none;
  }

  /*
    Halqa va progress animatsiyasiz "yo'q" bo'lib qolardi: birinchisi
    `opacity: 0` da, ikkinchisi esa kengliksiz boshlanadi. Ikkalasiga
    ham tinch, oxirgi holat beriladi.
  */
  .hero-ripple {
    opacity: 0.35;
  }

  .hero-progress {
    width: 78%;
  }
}

/*
  MODUL CHIZIG'I — uch bo'lak, uch rang.

  ★ RANG TARTIBI MA'NOLI: birinchi ikkitasi yashilning ochiqdan to'qqa
    qarab ketishi (ular bitta narsaning davomi — o'qishni o'rganish),
    uchinchisi esa SARIQ, chunki Amaliyot I boshqa turdagi ish:
    o'rganish emas, o'rganilganini mashq qilish.

  ⚠️ Ro'yxat `COURSE_PATH` dagi modullar soniga bog'liq. To'rtinchi
     modul qo'shilsa, shu yerga ham rang qo'shing — aks holda u
     rangsiz (shaffof) chiqadi.
*/
/*
  ══════════════════════════════════════════════════════════════════════
   KITOB YETKAZISH CHIZMASI
  ══════════════════════════════════════════════════════════════════════
*/

/* Marshrut — uzuq chiziq, uzuqlari yo'nalish bo'ylab suriladi. */
.ship-line {
  stroke: var(--color-green-400);
  stroke-width: 2.5;
  stroke-dasharray: 7 7;
  stroke-linecap: round;
  fill: none;
  animation: ship-dash 1.6s linear infinite;
}

@keyframes ship-dash {
  to {
    stroke-dashoffset: -28;
  }
}

/* Omborni belgilovchi tarqaluvchi halqa. */
.ship-pulse {
  fill: none;
  stroke: var(--color-green-800);
  stroke-width: 2;
  transform-origin: 56px 152px;
  animation: ship-pulse 3.2s ease-out infinite;
}

@keyframes ship-pulse {
  0% {
    transform: scale(1);
    opacity: 0.7;
  }

  100% {
    transform: scale(1.55);
    opacity: 0;
  }
}

.ship-dest {
  fill: var(--color-amber-500);
}

.ship-label {
  font-family: var(--font-sans);
  font-size: 11px;
  font-weight: 700;
  fill: var(--color-green-100);
}

.ship-sublabel {
  font-family: var(--font-sans);
  font-size: 9.5px;
  fill: var(--color-slate-400);
}

/*
  Yo'lda ketayotgan quti.

  ★ `offset-path` — element berilgan egri chiziq bo'ylab yuradi.
    `offset-rotate: auto` uni yo'nalishga qarab buradi, ya'ni quti
    burilishlarda "yotib" ketmaydi.

  🔴 `path()` ICHIDAGI EGRI `<defs>` DAGI `#ship-route` BILAN AYNI
     BO'LISHI SHART. Afsuski CSS `offset-path` SVG'dagi `id` ga
     murojaat qila olmaydi — shuning uchun egri IKKI JOYDA yozilgan.
     Marshrut o'zgarsa, ikkalasini ham yangilang.
*/
.ship-box {
  offset-path: path('M56 152 C 130 96, 190 190, 250 118 S 340 44, 372 62');
  offset-rotate: auto;
  animation: ship-move 6.5s ease-in-out infinite;
}

@keyframes ship-move {
  from {
    offset-distance: 0%;
  }

  to {
    offset-distance: 100%;
  }
}

/*
  ASOSIY USTOZ KUNI — to'q yashil to'ldirish.

  ★ NEGA GRADIENT, tekis rang emas: kartalar qatorda oltita turadi va
    tekis to'ldirishda ular bitta uzun yashil poloska bo'lib ko'rinardi.
*/
.week-day-main {
  background: linear-gradient(160deg, var(--color-green-400), var(--color-green-100));
  box-shadow: 0 14px 30px -14px rgb(6 118 71 / 0.7);
}

.modbar-0 {
  background: linear-gradient(140deg, var(--color-green-500), var(--color-green-400));
}

.modbar-1 {
  background: linear-gradient(140deg, var(--color-green-400), var(--color-green-200));
}

.modbar-2 {
  background: linear-gradient(140deg, var(--color-amber-500), var(--color-amber-400));
}

.lt-preview {
  background: linear-gradient(158deg, var(--color-ink-900), var(--color-green-950) 145%);
}

.lt-preview-bar {
  background: linear-gradient(90deg, var(--color-green-500), var(--color-green-400));
}

.lt-arabic {
  font-family: var(--font-arabic);
  direction: rtl;
  unicode-bidi: isolate;
}
</style>
