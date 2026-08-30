<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref } from 'vue'

import { EnrollmentRequestForm } from '@/features/enrollment-request'
import { AppIcon, BaseButton } from '@/shared/ui'

import {
  BOOKS,
  BOT_LINK,
  CONTACT,
  COURSE_OPTIONS,
  COURSE_PATH,
  FAQ,
  FEATURES,
  FREE_LESSON,
  HERO,
  OUTCOMES,
  PRICE,
  SOCIALS,
  STATS,
  STEPS,
  WEEK,
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

function onScroll(): void {
  isScrolled.value = window.scrollY > 8
}

onMounted(() => {
  onScroll()
  // `passive` — brauzer skroll'ni to'xtatib kutmasin (jankka qarshi).
  window.addEventListener('scroll', onScroll, { passive: true })
})

onBeforeUnmount(() => {
  window.removeEventListener('scroll', onScroll)
})

const NAV: readonly { href: string, label: string }[] = [
  { href: '#dars', label: 'Bepul dars' },
  { href: '#natija', label: 'Natija' },
  { href: '#kurs', label: 'Kurs tuzilmasi' },
  { href: '#narx', label: 'Narx' },
  { href: '#savollar', label: 'Savollar' },
]

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

const year = new Date().getFullYear()
</script>

<template>
  <div class="min-h-dvh bg-ink-950">
    <!-- ═══════════════════════════════════════════ YUQORI PANEL ═══ -->
    <header
      class="sticky top-0 z-30 transition-colors duration-200"
      :class="isScrolled
        ? 'bg-ink-950/85 backdrop-blur ring-1 ring-inset ring-line'
        : ''"
    >
      <div class="mx-auto flex h-16 max-w-6xl items-center justify-between gap-4 px-4 sm:px-6">
        <a
          class="flex items-center gap-2.5"
          href="#"
          @click.prevent="scrollToSection('body')"
        >
          <!--
            HAQIQIY BREND LOGOSI (2026-08-29). Ilgari bu yerda gradientli
            kvadrat ichida "Z" harfi turardi — yasama belgi. Endi markazning
            o'z logosi. `public/` dan kelgani uchun manzil ildizdan boshlanadi.
          -->
          <img
            class="size-9 rounded-full"
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

        <nav class="hidden items-center gap-1 lg:flex">
          <a
            v-for="item in NAV"
            :key="item.href"
            :href="item.href"
            class="rounded-lg px-3 py-2 text-sm font-medium text-slate-400 transition-colors hover:bg-ink-800 hover:text-slate-100"
            @click.prevent="scrollToSection(item.href)"
          >{{ item.label }}</a>
        </nav>

        <div class="flex items-center gap-2">
          <RouterLink
            class="hidden sm:block"
            to="/login"
          >
            <BaseButton
              size="md"
              variant="ghost"
            >
              Kirish
            </BaseButton>
          </RouterLink>

          <button
            type="button"
            class="hidden h-10 items-center rounded-xl bg-brand-500 px-4 text-sm font-semibold text-on-brand transition-colors hover:bg-brand-600 sm:inline-flex"
            @click="scrollToSection('#ariza')"
          >
            Kursga yozilish
          </button>

          <button
            type="button"
            class="flex size-10 items-center justify-center rounded-xl text-slate-400 transition-colors hover:bg-ink-800 hover:text-slate-100 lg:hidden"
            :aria-expanded="isMenuOpen"
            aria-label="Menyu"
            @click="isMenuOpen = !isMenuOpen"
          >
            <AppIcon
              :name="isMenuOpen ? 'close' : 'menu'"
              :size="20"
            />
          </button>
        </div>
      </div>

      <!-- Mobil menyu -->
      <nav
        v-if="isMenuOpen"
        class="border-t border-line bg-ink-950/95 px-4 py-2 backdrop-blur lg:hidden"
      >
        <a
          v-for="item in NAV"
          :key="item.href"
          :href="item.href"
          class="block rounded-lg px-3 py-2.5 text-sm font-medium text-slate-300 transition-colors hover:bg-ink-800"
          @click.prevent="scrollToSection(item.href)"
        >{{ item.label }}</a>

        <div class="mt-2 flex gap-2 border-t border-line pt-3">
          <RouterLink
            class="flex-1"
            to="/login"
          >
            <span
              class="flex h-11 items-center justify-center rounded-xl border border-line-strong text-sm font-semibold text-slate-200"
            >Kirish</span>
          </RouterLink>
          <button
            type="button"
            class="flex h-11 flex-1 items-center justify-center rounded-xl bg-brand-500 text-sm font-semibold text-on-brand"
            @click="scrollToSection('#ariza')"
          >
            Kursga yozilish
          </button>
        </div>
      </nav>
    </header>

    <main class="relative">
      <!-- ═══════════════════════════════════════════════ HERO ═══ -->
      <!--
        TO'Q YASHIL BREND SIRTI ("Nur").

        `.surface-brand` ichidagi HAMMA token almashadi — shuning uchun
        pastdagi `text-slate-100`, `bg-ink-900`, `text-brand-500` kabi
        klasslarga TEGILMADI, ular o'z-o'zidan to'q sirt qiymatlarini
        oladi. Qoidalar `style.css` dagi shu nomli blokda.

        ★ `data-surface="brand"` EMAS, klass: `vue-tsc` shablondagi
        `data-*` atributini rad etadi (TS2353).
      -->
      <section class="surface-brand relative overflow-hidden bg-ink-950">
        <!--
          ═══════════════════════════════════════════ JONLI FON ═══
          Fon nuri — yumshoq yorug'lik. To'q yashil sirtda u sirtni
          yassilikdan chiqaradi. `pointer-events-none` — bosishni to'smasin.

          ⚠️ 2026-08-30 — FON JONLANTIRILDI (loyiha egasining talabi).
          Ilgari bu yerda IKKITA QIMIRLAMAYDIGAN gradient turardi va
          birinchi ekran "rasm" bo'lib qolardi. Endi uch qatlam bor:
          statik asos, sekin suriladigan nuqtali setka va uchta suzuvchi
          yorug'lik shari.

          🔴 UCHALASI HAM `aria-hidden` OSTIDA: bu qatlamlar bezak,
             ekran o'quvchiga o'qilmasligi kerak.

          Uslublar va tanlovlarning sababi (nega faqat `transform`,
          nega `will-change` yo'q, `prefers-reduced-motion` da nima
          bo'ladi) — `style.css` dagi "LANDING HERO — JONLI FON" blokida.
        -->
        <div
          class="pointer-events-none absolute inset-0 overflow-hidden"
          aria-hidden="true"
        >
          <!--
            1-QATLAM — statik chuqurlik. Sharlar harakatlanganda ham
            sirtning umumiy yorug'ligi o'zgarmasin uchun asos ATAYLAB
            qimirlamaydi.
          -->
          <div
            class="absolute inset-0"
            style="
              background:
                radial-gradient(
                    60rem 40rem at 12% -20%,
                    color-mix(in oklab, var(--color-brand-vivid) 16%, transparent),
                    transparent 62%
                  ),
                radial-gradient(
                  40rem 30rem at 92% 0%,
                  color-mix(in oklab, var(--color-brand-500) 10%, transparent),
                  transparent 60%
                );
            "
          />

          <!--
            2-QATLAM — SETKALI NAQSH (chiziqlar + kesishma tugunlari).
            Rangi `currentColor` dan olinadi, shuning uchun
            `text-slate-50`: sirt o'zgarsa naqsh ham o'ziga moslashadi.

            🔴 IKKI ELEMENT — BITTA EMAS: tashqisi (`hero-grid-mask`)
               qimirlamaydi va naqshni chetlarga borib so'ndiradi,
               ichkisi (`hero-grid`) esa suriladi. Sabab `style.css`
               dagi `.hero-grid-mask` izohida — niqob harakatlanuvchi
               elementga qo'yilsa, so'nish chegarasi ham u bilan birga
               sudralib yurardi.
          -->
          <div class="hero-grid-mask">
            <div class="hero-grid text-slate-50" />
          </div>

          <!--
            3-QATLAM — suzuvchi yorug'lik sharlari. Har biri BOSHQA
            yo'nalishda va BOSHQA tezlikda (22s / 27s / 32s): bir xil
            bo'lsa uchalasi birga "nafas olib", naqsh takrorlanayotgani
            darhol sezilardi.
          -->
          <div
            class="hero-orb -left-32 -top-40 size-[34rem]"
            style="background: color-mix(in oklab, var(--color-brand-vivid) 30%, transparent);"
          />
          <div
            class="hero-orb hero-orb--b -right-24 -top-24 size-[28rem]"
            style="background: color-mix(in oklab, var(--color-brand-500) 20%, transparent);"
          />
          <div
            class="hero-orb hero-orb--c -bottom-48 left-1/3 size-[30rem]"
            style="background: color-mix(in oklab, var(--color-brand-vivid) 22%, transparent);"
          />
        </div>

        <div class="relative mx-auto max-w-6xl px-4 pb-16 pt-14 sm:px-6 sm:pb-24 sm:pt-20">
          <div class="max-w-3xl">
            <span
              class="hero-rise inline-flex items-center gap-2 rounded-full bg-brand-500/12 px-3 py-1.5 text-xs font-semibold text-brand-300 ring-1 ring-inset ring-brand-500/25"
            >
              <span
                class="size-1.5 rounded-full bg-brand-vivid"
                aria-hidden="true"
              />
              {{ HERO.badge }}
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
              <span class="text-brand-500">{{ HERO.titleAccent }}</span>
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
              <button
                type="button"
                class="inline-flex h-12 items-center justify-center gap-2.5 rounded-xl bg-brand-500 px-6 text-base font-semibold text-on-brand shadow-sm transition-colors hover:bg-brand-600 active:bg-brand-700"
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
                class="inline-flex h-12 items-center justify-center gap-2.5 rounded-xl border border-line-strong bg-ink-900/60 px-6 text-base font-semibold text-slate-100 transition-colors hover:bg-ink-800"
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
              <span class="text-xs font-medium text-slate-500">Bizni kuzating:</span>
              <a
                v-for="social in SOCIALS"
                :key="social.href"
                class="inline-flex items-center gap-1.5 text-sm font-medium text-slate-300 transition-colors hover:text-brand-500"
                :href="social.href"
                target="_blank"
                rel="noopener noreferrer"
              >
                <AppIcon
                  :name="social.icon"
                  :size="15"
                />
                {{ social.label }}
              </a>
            </div>
          </div>

          <dl
            class="hero-rise mt-14 grid grid-cols-2 gap-3 sm:mt-20 sm:grid-cols-4 sm:gap-4"
            style="--rise-delay: 450ms"
          >
            <div
              v-for="stat in STATS"
              :key="stat.label"
              class="rounded-2xl bg-ink-900/60 px-4 py-5 text-center ring-1 ring-inset ring-line"
            >
              <dt class="font-display text-2xl font-semibold tracking-tight text-slate-50 sm:text-3xl">
                {{ stat.value }}
              </dt>
              <dd class="mt-1 text-xs text-slate-400 sm:text-sm">
                {{ stat.label }}
              </dd>
            </div>
          </dl>
        </div>
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
              class="card-lift rounded-2xl border border-line bg-ink-900 p-6 hover:border-brand-500/40"
            >
              <span
                class="flex size-12 items-center justify-center rounded-2xl bg-gradient-to-br from-brand-500/25 to-brand-500/5 text-brand-500 ring-1 ring-inset ring-brand-500/20"
              >
                <AppIcon
                  :name="item.icon"
                  :size="22"
                />
              </span>
              <h3 class="mt-4 text-base font-bold text-slate-100">
                {{ item.title }}
              </h3>
              <p class="mt-2 text-sm leading-relaxed text-slate-400">
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

            <ul
              v-if="stage.modules !== undefined"
              class="mt-5 space-y-2.5 border-t border-line pt-5"
            >
              <li
                v-for="module in stage.modules"
                :key="module.name"
                class="flex items-baseline justify-between gap-3"
              >
                <span class="text-sm text-slate-300">{{ module.name }}</span>
                <span class="shrink-0 text-xs font-semibold text-brand-500">
                  {{ module.duration }}
                </span>
              </li>
            </ul>
          </li>
        </ol>

        <!-- ═══════════════════════════════════ HAFTA JADVALI ═══ -->
        <h3
          class="mt-16 font-display text-2xl font-semibold tracking-tight text-slate-100 sm:text-3xl"
        >
          Haftangiz qanday o‘tadi
        </h3>
        <p class="mt-3 max-w-2xl text-base leading-relaxed text-slate-400">
          Haftasiga 5 kun dars: 2 kuni asosiy ustoz bilan, 3 kuni support
          teacher bilan. Jadval oldindan ma'lum — ishingiz yoki
          o‘qishingizga moslashtira olasiz.
        </p>

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

        <!-- Afzalliklar — tuzilmadan keyin, "nega aynan biz". -->
        <div class="mt-16 grid gap-6 sm:grid-cols-2 lg:grid-cols-3">
          <div
            v-for="feature in FEATURES"
            :key="feature.title"
            class="flex gap-4"
          >
            <span
              class="mt-0.5 flex size-10 shrink-0 items-center justify-center rounded-xl bg-brand-500/10 text-brand-500"
            >
              <AppIcon
                :name="feature.icon"
                :size="19"
              />
            </span>
            <div>
              <h3 class="text-sm font-bold text-slate-100">
                {{ feature.title }}
              </h3>
              <p class="mt-1.5 text-sm leading-relaxed text-slate-400">
                {{ feature.text }}
              </p>
            </div>
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
            </div>

            <ul class="space-y-4">
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
        </div>
      </section>

      <!-- ═════════════════════════════════════════════ NARX ═══ -->
      <section
        id="narx"
        class="surface-brand relative overflow-hidden border-y border-line bg-ink-950"
      >
        <!--
          🔴 HERO BILAN AYNI SIRT — ATAYLAB. Sahifada ikkita qaror
             nuqtasi bor: "qiziqdim" (hero) va "to‘layman" (shu yer).
             Ikkalasi ham vizual cho‘qqi bo‘lishi kerak, oraliqdagi
             bo‘limlar esa ularni ko‘tarib turadi. To‘liq ritm
             `style.css` dagi "LANDING BO‘LIMLARI" izohida.

          ★ ICHKI KLASSLARGA TEGILMADI: `surface-brand` tokenlarni
            almashtiradi, ya’ni `text-slate-100`, `bg-ink-900` va
            `text-brand-500` o‘z-o‘zidan to‘q sirt qiymatlarini oladi
            (aksent bu sirtda shampan bo‘ladi).
        -->
        <div
          class="dot-layer text-slate-50"
          aria-hidden="true"
        />

        <div class="relative mx-auto max-w-6xl scroll-mt-20 px-4 py-16 sm:px-6 sm:py-24">
          <div class="grid gap-10 lg:grid-cols-2 lg:gap-14">
            <div>
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

              <div class="mt-7 flex flex-wrap items-baseline gap-x-3">
                <span
                  class="font-display text-5xl font-semibold tracking-tight text-brand-500 sm:text-6xl"
                >{{ PRICE.amount }}</span>
                <span class="text-xl font-semibold text-slate-300">{{ PRICE.currency }}</span>
                <span class="text-base text-slate-500">/ {{ PRICE.period }}</span>
              </div>

              <p class="mt-3 text-base font-semibold text-slate-200">
                {{ PRICE.perLesson }}
              </p>
              <p class="mt-2 max-w-md text-sm leading-relaxed text-slate-400">
                {{ PRICE.note }}
              </p>

              <button
                type="button"
                class="mt-8 inline-flex h-12 items-center justify-center gap-2.5 rounded-xl bg-brand-500 px-6 text-base font-semibold text-on-brand shadow-xs transition-colors hover:bg-brand-600 active:bg-brand-700"
                @click="scrollToSection('#ariza')"
              >
                Joyni band qilish
                <AppIcon
                  name="chevron-right"
                  :size="17"
                />
              </button>
            </div>

            <div class="rounded-2xl bg-ink-900 p-7 ring-1 ring-inset ring-line">
              <h3 class="text-sm font-bold uppercase tracking-[1.2px] text-slate-400">
                To‘lov ichiga nima kiradi
              </h3>
              <ul class="mt-5 space-y-3.5">
                <li
                  v-for="item in PRICE.includes"
                  :key="item"
                  class="flex items-start gap-3"
                >
                  <AppIcon
                    class="mt-0.5 shrink-0 text-brand-500"
                    name="check"
                    :size="17"
                  />
                  <span class="text-sm leading-relaxed text-slate-300">{{ item }}</span>
                </li>
              </ul>
            </div>
          </div>
        </div>
      </section>

      <!-- ═══════════════════════════════════════════ ARIZA ═══ -->
      <section
        id="ariza"
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

            <!-- Boshlash bosqichlari — arizadan keyin nima bo'lishini ko'rsatadi. -->
            <ol class="mt-9 space-y-5">
              <li
                v-for="(step, index) in STEPS"
                :key="step.title"
                class="flex gap-4"
              >
                <span
                  class="flex size-8 shrink-0 items-center justify-center rounded-full bg-brand-500/10 text-sm font-bold text-brand-500"
                >{{ index + 1 }}</span>
                <div>
                  <p class="text-sm font-bold text-slate-100">
                    {{ step.title }}
                  </p>
                  <p class="mt-1 text-sm leading-relaxed text-slate-400">
                    {{ step.text }}
                  </p>
                </div>
              </li>
            </ol>

            <div class="mt-9 space-y-4 border-t border-line pt-7">
              <div class="flex items-start gap-3">
                <AppIcon
                  class="mt-0.5 shrink-0 text-brand-500"
                  name="phone"
                  :size="17"
                />
                <div>
                  <p class="text-sm font-medium text-slate-200">
                    Telefon
                  </p>
                  <a
                    class="text-sm text-slate-400 transition-colors hover:text-slate-200"
                    :href="`tel:${CONTACT.phoneHref}`"
                  >{{ CONTACT.phone }}</a>
                </div>
              </div>
              <div class="flex items-start gap-3">
                <AppIcon
                  class="mt-0.5 shrink-0 text-brand-500"
                  name="clock"
                  :size="17"
                />
                <div>
                  <p class="text-sm font-medium text-slate-200">
                    Ish vaqti
                  </p>
                  <p class="text-sm text-slate-400">
                    {{ CONTACT.workingHours }}
                  </p>
                </div>
              </div>
            </div>
          </div>

          <EnrollmentRequestForm :courses="COURSE_OPTIONS" />
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
          <div class="mt-10 divide-y divide-line border-y border-line">
            <details
              v-for="item in FAQ"
              :key="item.question"
              class="group py-5"
            >
              <summary
                class="flex cursor-pointer list-none items-center justify-between gap-4 text-base font-semibold text-slate-100 transition-colors hover:text-brand-500"
              >
                {{ item.question }}
                <AppIcon
                  class="shrink-0 text-slate-500 transition-transform group-open:rotate-180"
                  name="chevron-down"
                  :size="18"
                />
              </summary>
              <p class="mt-3 text-sm leading-relaxed text-slate-400">
                {{ item.answer }}
              </p>
            </details>
          </div>
        </div>
      </section>
    </main>

    <!-- ═══════════════════════════════════════════ FOOTER ═══ -->
    <footer class="border-t border-line">
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
  </div>
</template>
