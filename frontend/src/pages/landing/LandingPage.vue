<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref } from 'vue'

import { EnrollmentRequestForm } from '@/features/enrollment-request'
import { AppIcon, BaseButton } from '@/shared/ui'

import {
  BOOKS,
  BOT_LINK,
  CONTACT,
  COURSE_OPTIONS,
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
    kunlik summaga bo'lib ko'rsatadi (18 000 so'm). Yashirilgan narx
    qo'ng'iroqni ko'paytiradi, lekin ishonchni kamaytiradi.
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
          Fon nuri — yumshoq yorug'lik. To'q yashil sirtda u sirtni
          yassilikdan chiqaradi. `pointer-events-none` — bosishni to'smasin.
        -->
        <div
          class="pointer-events-none absolute inset-0"
          aria-hidden="true"
          style="
            background:
              radial-gradient(
                  60rem 40rem at 12% -20%,
                  color-mix(in oklab, var(--color-brand-vivid) 22%, transparent),
                  transparent 62%
                ),
              radial-gradient(
                40rem 30rem at 92% 0%,
                color-mix(in oklab, var(--color-brand-500) 14%, transparent),
                transparent 60%
              );
          "
        />

        <div class="relative mx-auto max-w-6xl px-4 pb-16 pt-14 sm:px-6 sm:pb-24 sm:pt-20">
          <div class="max-w-3xl">
            <span
              class="inline-flex items-center gap-2 rounded-full bg-brand-500/12 px-3 py-1.5 text-xs font-semibold text-brand-300 ring-1 ring-inset ring-brand-500/25"
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
              class="mt-5 font-display text-[2.6rem] font-semibold leading-[1.05] tracking-[-0.01em] text-slate-50 sm:text-[4.1rem]"
            >
              {{ HERO.title }}
              <span class="text-brand-500">{{ HERO.titleAccent }}</span>
            </h1>

            <p class="mt-6 max-w-2xl text-base leading-relaxed text-slate-300 sm:text-lg">
              {{ HERO.lead }}
            </p>

            <div class="mt-9 flex flex-wrap items-center gap-3">
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
            <div class="mt-8 flex flex-wrap items-center gap-x-5 gap-y-2">
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

          <dl class="mt-14 grid grid-cols-2 gap-3 sm:mt-20 sm:grid-cols-4 sm:gap-4">
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
        class="mx-auto max-w-6xl scroll-mt-20 px-4 py-16 sm:px-6 sm:py-24"
      >
        <div class="grid items-center gap-10 lg:grid-cols-2 lg:gap-14">
          <div>
            <span class="text-xs font-bold uppercase tracking-[1.4px] text-brand-500">
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

            <a
              class="mt-6 inline-flex items-center gap-2 text-sm font-semibold text-brand-500 transition-colors hover:text-brand-600"
              :href="FREE_LESSON.href"
              target="_blank"
              rel="noopener noreferrer"
            >
              YouTube'da ochish
              <AppIcon
                name="chevron-right"
                :size="15"
              />
            </a>
          </div>

          <!--
            VIDEO — bosilgunga qadar FAQAT poster.
            Sabab `isVideoPlaying` izohida: YouTube iframe'i og'ir va uni
            sahifa ochilishi bilan yuklash landing tezligini buzardi.

            `youtube-nocookie.com` — Google'ning kengaytirilgan maxfiylik
            domeni: video ko'rilmaguncha kuzatuv cookie'si qo'yilmaydi.
          -->
          <div
            class="relative aspect-video overflow-hidden rounded-2xl bg-ink-800 ring-1 ring-inset ring-line-strong"
          >
            <iframe
              v-if="isVideoPlaying"
              class="size-full"
              :src="`https://www.youtube-nocookie.com/embed/${FREE_LESSON.youtubeId}?autoplay=1&rel=0`"
              title="«Ayn» harfini 15 daqiqada o‘rganing"
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
              <img
                class="size-full object-cover"
                :src="`https://img.youtube.com/vi/${FREE_LESSON.youtubeId}/hqdefault.jpg`"
                alt=""
                loading="lazy"
              >
              <span
                class="absolute inset-0 bg-black/25 transition-colors group-hover:bg-black/15"
                aria-hidden="true"
              />
              <span
                class="absolute left-1/2 top-1/2 flex size-16 -translate-x-1/2 -translate-y-1/2 items-center justify-center rounded-full bg-brand-500 text-on-brand shadow-lg transition-transform group-hover:scale-105"
                aria-hidden="true"
              >
                <AppIcon
                  name="play"
                  :size="26"
                />
              </span>
            </button>
          </div>
        </div>
      </section>

      <!-- ═══════════════════════════════════════════ NATIJA ═══ -->
      <section
        id="natija"
        class="border-y border-line bg-ink-900/40"
      >
        <div class="mx-auto max-w-6xl scroll-mt-20 px-4 py-16 sm:px-6 sm:py-24">
          <div class="max-w-2xl">
            <span class="text-xs font-bold uppercase tracking-[1.4px] text-brand-500">
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
              class="rounded-2xl bg-ink-900 p-6 ring-1 ring-inset ring-line"
            >
              <span
                class="flex size-11 items-center justify-center rounded-xl bg-brand-500/10 text-brand-500"
              >
                <AppIcon
                  :name="item.icon"
                  :size="21"
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
          <span class="text-xs font-bold uppercase tracking-[1.4px] text-brand-500">
            Kurs tuzilmasi
          </span>
          <h2
            class="mt-3 font-display text-3xl font-semibold tracking-tight text-slate-100 sm:text-[2.5rem] sm:leading-[1.1]"
          >
            Haftangiz qanday o‘tadi
          </h2>
          <p class="mt-4 text-base leading-relaxed text-slate-400">
            Haftasiga 4 kun dars va 3 kun kurator yordami. Jadval oldindan
            ma'lum — ishingiz yoki o‘qishingizga moslashtira olasiz.
          </p>
        </div>

        <div class="mt-12 grid gap-5 lg:grid-cols-3">
          <div
            v-for="block in WEEK"
            :key="block.title"
            class="rounded-2xl bg-ink-900 p-6 ring-1 ring-inset ring-line"
          >
            <div class="flex items-center gap-3">
              <span
                class="flex size-11 shrink-0 items-center justify-center rounded-xl bg-brand-500/10 text-brand-500"
              >
                <AppIcon
                  :name="block.icon"
                  :size="21"
                />
              </span>
              <span
                class="rounded-full bg-ink-800 px-2.5 py-1 text-xs font-bold text-slate-300"
              >{{ block.days }}</span>
            </div>
            <h3 class="mt-4 text-base font-bold text-slate-100">
              {{ block.title }}
            </h3>
            <p class="mt-2 text-sm leading-relaxed text-slate-400">
              {{ block.text }}
            </p>
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
        <div
          class="surface-brand overflow-hidden rounded-3xl bg-ink-950 px-6 py-12 ring-1 ring-inset ring-line sm:px-12 sm:py-16"
        >
          <div class="grid gap-10 lg:grid-cols-2 lg:items-center lg:gap-14">
            <div>
              <span class="text-xs font-bold uppercase tracking-[1.4px] text-brand-500">
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
        class="border-y border-line bg-ink-900/40"
      >
        <div class="mx-auto max-w-6xl scroll-mt-20 px-4 py-16 sm:px-6 sm:py-24">
          <div class="grid gap-10 lg:grid-cols-2 lg:gap-14">
            <div>
              <span class="text-xs font-bold uppercase tracking-[1.4px] text-brand-500">
                {{ PRICE.eyebrow }}
              </span>
              <h2
                class="mt-3 font-display text-3xl font-semibold tracking-tight text-slate-100 sm:text-[2.5rem] sm:leading-[1.1]"
              >
                Oylik to‘lov
              </h2>

              <div class="mt-7 flex flex-wrap items-baseline gap-x-3">
                <span
                  class="font-display text-5xl font-semibold tracking-tight text-brand-500 sm:text-6xl"
                >{{ PRICE.amount }}</span>
                <span class="text-xl font-semibold text-slate-300">{{ PRICE.currency }}</span>
                <span class="text-base text-slate-500">/ {{ PRICE.period }}</span>
              </div>

              <p class="mt-3 text-base font-semibold text-slate-200">
                {{ PRICE.daily }}
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
            <span class="text-xs font-bold uppercase tracking-[1.4px] text-brand-500">
              Ariza
            </span>
            <h2
              class="mt-3 font-display text-3xl font-semibold tracking-tight text-slate-100 sm:text-[2.5rem] sm:leading-[1.1]"
            >
              Joyingizni band qiling
            </h2>
            <p class="mt-4 text-base leading-relaxed text-slate-400">
              Guruhda atigi 18–20 joy bor. Ism va telefon raqamingizni
              qoldiring — o‘quv bo‘limi bog‘lanib, darajangizga mos guruhni
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
        class="border-t border-line bg-ink-900/40"
      >
        <div class="mx-auto max-w-3xl scroll-mt-20 px-4 py-16 sm:px-6 sm:py-24">
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
              Arab tili akademiyasi. 8 oylik to‘liq kurs — jonli darslar,
              kuratorlik va kitoblar uyingizgacha.
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
