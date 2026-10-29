<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, useTemplateRef } from 'vue'

import { MascotAvatar } from '@/features/mascot'
import { AppIcon } from '@/shared/ui'

import { PLATFORM_TOUR } from '../model/content'
import PlatformTourScreen from './PlatformTourScreen.vue'

/*
  ══════════════════════════════════════════════════════════════════════════
   «PLATFORMA ICHIDAN» KARUSELI — 2026-10-29
  ══════════════════════════════════════════════════════════════════════════

  Matn va slaydlar `content.ts` dagi `PLATFORM_TOUR` da (sabab o'sha
  yerdagi izohda). Bu yerda faqat xatti-harakat.

  ★ KUTUBXONASIZ — CSS SCROLL-SNAP. Trek oddiy gorizontal skroll,
    slayd — `snap-center`. Telefonda tabiiy swipe JS'siz ishlaydi;
    JS faqat tablar, ‹ › tugmalari, nuqtalar va klaviatura uchun.

  ★ FAOL SLAYD SKROLLDAN ANIQLANADI (`scrollLeft / clientWidth`), alohida
    holat sifatida saqlanmaydi: barmoq bilan surilganda ham, tugma bosilganda
    ham manba bitta.

  🔴 DASTURIY SURISHDA KUZATUV TO'XTAYDI (`target`). 1-slayddan 6-ga
     o'tilganda trek 2..5 ustidan o'tadi va ularning har biri bir lahza
     «faol» bo'lib, Nuri kayfiyati miltillab ketardi. Maqsadga yetguncha
     (`scrollend` yoki zaxira taymer) oraliq holatlar e'tiborsiz qoladi.

  ★ AVTO-AYLANISH YO'Q — ATAYLAB: odam o'qib turgan ekran o'zidan-o'zi
    almashmasligi kerak (WCAG 2.2.2).

  ★ `prefers-reduced-motion` — surish silliq emas, darhol.

  🔴 SSR XAVFSIZ: `window` / `matchMedia` faqat `onMounted` dan keyin
     (landing build paytida HTML ga prerender qilinadi — `entry-ssg.ts`).
*/
const SLIDES = PLATFORM_TOUR.slides
const LAST = SLIDES.length - 1

const active = ref(0)
const slide = computed(() => SLIDES[active.value] ?? SLIDES[0]!)

/*
  «Nur» darajasi: 1-slaydda 40%, oxirgisida 100%. Nuri orqasidagi
  yorug'lik har ekranda kuchayadi — «o'rgangan sari nur ko'payadi».
*/
const nur = computed(() => 0.4 + (0.6 * active.value) / LAST)

const track = useTemplateRef<HTMLElement>('track')
const tablist = useTemplateRef<HTMLElement>('tablist')

/** Dasturiy surish nishoni (sabab yuqoridagi izohda). */
let target: number | null = null
let targetTimer: ReturnType<typeof setTimeout> | null = null
let frame = 0
let resizeObserver: ResizeObserver | null = null

function clamp(index: number): number {
  return Math.max(0, Math.min(LAST, index))
}

function indexFromScroll(): number {
  const el = track.value
  if (el === null || el.clientWidth === 0) return active.value
  return clamp(Math.round(el.scrollLeft / el.clientWidth))
}

function prefersReducedMotion(): boolean {
  return window.matchMedia('(prefers-reduced-motion: reduce)').matches
}

/**
 * Faol tabni chip qatorida ko'rinadigan joyga suradi (telefonda).
 *
 * 🔴 `scrollIntoView` EMAS: u butun SAHIFANI ham vertikal surib, odamni
 *    o'qiyotgan joyidan sakratib yuborardi. Bu yerda faqat qatorning
 *    o'zi gorizontal suriladi.
 */
function revealTab(index: number): void {
  const list = tablist.value
  if (list === null || list.scrollWidth <= list.clientWidth) return

  const tab = list.children[index] as HTMLElement | undefined
  if (tab === undefined) return

  list.scrollTo({
    left: tab.offsetLeft - (list.clientWidth - tab.clientWidth) / 2,
    behavior: prefersReducedMotion() ? 'auto' : 'smooth',
  })
}

function setActive(index: number): void {
  if (index === active.value) return
  active.value = index
  revealTab(index)
}

function clearTarget(): void {
  target = null
  if (targetTimer !== null) clearTimeout(targetTimer)
  targetTimer = null
}

function go(index: number): void {
  const el = track.value
  const next = clamp(index)
  if (el === null) return

  target = next
  if (targetTimer !== null) clearTimeout(targetTimer)
  /*
    ⚠️ ZAXIRA TAYMER: `scrollend` hamma brauzerda yo'q (eski Safari).
       Usiz `target` hech qachon tozalanmas va barmoq bilan surish
       kuzatilmay qolardi.
  */
  targetTimer = setTimeout(() => {
    clearTarget()
    setActive(indexFromScroll())
  }, 900)

  el.scrollTo({
    left: next * el.clientWidth,
    behavior: prefersReducedMotion() ? 'auto' : 'smooth',
  })
  setActive(next)
}

function onScroll(): void {
  if (target !== null) return
  cancelAnimationFrame(frame)
  frame = requestAnimationFrame(() => setActive(indexFromScroll()))
}

function onScrollEnd(): void {
  if (target !== null) {
    const wanted = target
    clearTarget()
    if (indexFromScroll() !== wanted) go(wanted)
    return
  }
  setActive(indexFromScroll())
}

/** Odam o'zi surishni boshladi — dasturiy nishon bekor. */
function onUserScrollStart(): void {
  clearTarget()
}

/**
 * Klaviatura: ←/→ qo'shni ekran, Home/End — birinchi/oxirgi.
 * Tablarda fokus ham yangi tabga o'tadi (WAI-ARIA tabs naqshi).
 */
function onKeydown(event: KeyboardEvent, moveFocus: boolean): void {
  const map: Record<string, number> = {
    ArrowRight: active.value + 1,
    ArrowLeft: active.value - 1,
    Home: 0,
    End: LAST,
  }
  const next = map[event.key]
  if (next === undefined) return

  event.preventDefault()
  go(next)

  if (moveFocus) {
    const tab = tablist.value?.children[clamp(next)] as HTMLElement | undefined
    tab?.focus()
  }
}

onMounted(() => {
  const el = track.value
  if (el === null) return

  /*
    O'lcham o'zgarsa (telefon burilsa, oyna kengaysa) trek faol slaydga
    qayta tekislanadi — aks holda u ikki slayd o'rtasida qolardi.
  */
  resizeObserver = new ResizeObserver(() => {
    el.scrollTo({ left: active.value * el.clientWidth, behavior: 'auto' })
  })
  resizeObserver.observe(el)
})

onBeforeUnmount(() => {
  resizeObserver?.disconnect()
  cancelAnimationFrame(frame)
  if (targetTimer !== null) clearTimeout(targetTimer)
})
</script>

<template>
  <section
    id="platforma"
    class="tour-bg relative scroll-mt-20"
    aria-labelledby="platforma-title"
  >
    <div class="relative mx-auto max-w-6xl px-4 py-16 sm:px-6 sm:py-24">
      <div class="max-w-2xl">
        <span class="eyebrow text-xs font-bold uppercase tracking-[1.4px] text-brand-500">
          {{ PLATFORM_TOUR.eyebrow }}
        </span>
        <h2
          id="platforma-title"
          class="mt-3 font-display text-3xl font-semibold tracking-tight text-slate-100 sm:text-[2.5rem] sm:leading-[1.1]"
        >
          {{ PLATFORM_TOUR.title }}
        </h2>
        <p class="mt-4 text-base leading-relaxed text-slate-400">
          {{ PLATFORM_TOUR.lead }}
        </p>
      </div>

      <!--
        TARTIB: DOM'da tablar → sahna → Nuri. Telefonda shu ketma-ketlikda
        ustma-ust; `lg` da tablar va Nuri chapda, sahna o'ngda (ikki qator
        bo'ylab). Ekran o'qigich uchun tartib har doim mantiqiy.
      -->
      <div
        class="mt-8 grid gap-5 lg:mt-10 lg:grid-cols-[minmax(0,0.9fr)_minmax(0,1.1fr)] lg:grid-rows-[auto_1fr] lg:gap-x-12 lg:gap-y-6"
      >
        <!--
          ═══════════════════════════════════════════════ TABLAR ═══
          BITTA tablist, ikki ko'rinish: telefonda gorizontal chip qatori,
          `lg` da raqamli ro'yxat (faol tab izohi bilan ochiladi). Ikkita
          alohida ro'yxat bo'lsa, ID'lar takrorlanardi.
        -->
        <div
          ref="tablist"
          class="tour-tabs -mx-4 flex gap-2 overflow-x-auto px-4 pb-1 sm:-mx-6 sm:px-6 lg:col-start-1 lg:row-start-1 lg:mx-0 lg:grid lg:gap-1.5 lg:overflow-visible lg:p-0"
          role="tablist"
          :aria-label="PLATFORM_TOUR.tablistLabel"
        >
          <button
            v-for="(item, index) in SLIDES"
            :id="`tour-tab-${item.id}`"
            :key="item.id"
            type="button"
            role="tab"
            class="tour-tab shrink-0"
            :aria-selected="index === active"
            :aria-controls="`tour-slide-${item.id}`"
            :tabindex="index === active ? 0 : -1"
            @click="go(index)"
            @keydown="onKeydown($event, true)"
          >
            <span
              class="tour-tab-no hidden lg:grid"
              aria-hidden="true"
            >{{ index + 1 }}</span>
            <span class="min-w-0">
              <span class="flex flex-wrap items-center gap-2">
                <span class="tour-tab-title">{{ item.title }}</span>
                <span
                  class="hidden rounded-full px-2 py-0.5 text-[10.5px] font-bold lg:inline"
                  :class="item.plan === 'milliy'
                    ? 'bg-amber-950 text-amber-300 ring-1 ring-inset ring-amber-800'
                    : 'bg-green-950 text-brand-500'"
                >{{ item.plan === 'milliy' ? PLATFORM_TOUR.tagMilliy : PLATFORM_TOUR.tagAll }}</span>
              </span>
              <span
                v-if="index === active"
                class="mt-1 hidden text-[13.5px] leading-snug text-slate-400 lg:block"
              >{{ item.caption }}</span>
            </span>
          </button>
        </div>

        <!-- ═══════════════════════════════════════════════ SAHNA ═══ -->
        <div class="min-w-0 lg:col-start-2 lg:row-span-2 lg:row-start-1 lg:self-center">
          <div
            ref="track"
            class="tour-track flex snap-x snap-mandatory overflow-x-auto overscroll-x-contain rounded-[1.75rem]"
            tabindex="0"
            role="region"
            aria-roledescription="karusel"
            :aria-label="PLATFORM_TOUR.trackLabel"
            @scroll.passive="onScroll"
            @scrollend="onScrollEnd"
            @pointerdown.passive="onUserScrollStart"
            @touchstart.passive="onUserScrollStart"
            @wheel.passive="onUserScrollStart"
            @keydown="onKeydown($event, false)"
          >
            <div
              v-for="(item, index) in SLIDES"
              :id="`tour-slide-${item.id}`"
              :key="item.id"
              class="flex flex-[0_0_100%] snap-center justify-center px-1 pb-6 pt-4"
              role="tabpanel"
              aria-roledescription="slayd"
              :aria-label="`${index + 1} / ${SLIDES.length}: ${item.title}`"
              :aria-hidden="index === active ? undefined : 'true'"
            >
              <!--
                TELEFON RAMKASI. Ichida haqiqiy skrinshot (`image`) yoki
                HTML-sxema. Ikkala holatda ham `role="img"` + `alt`.
              -->
              <div class="tour-phone">
                <div class="tour-phone-screen">
                  <img
                    v-if="item.image !== null"
                    class="size-full object-cover object-top"
                    :src="item.image.src"
                    :width="item.image.width"
                    :height="item.image.height"
                    :alt="item.alt"
                    :loading="index === 0 ? 'eager' : 'lazy'"
                  >
                  <div
                    v-else
                    class="size-full"
                    role="img"
                    :aria-label="item.alt"
                  >
                    <PlatformTourScreen :screen="item.id" />
                  </div>
                </div>
              </div>
            </div>
          </div>

          <div class="flex items-center justify-center gap-3.5">
            <button
              type="button"
              class="tour-arrow"
              :aria-label="PLATFORM_TOUR.prev"
              :disabled="active === 0"
              @click="go(active - 1)"
            >
              <AppIcon
                name="chevron-left"
                :size="18"
              />
            </button>
            <!--
              Nuqtalar — sichqoncha va barmoq uchun. Klaviatura va ekran
              o'qigich tablardan foydalanadi, shuning uchun nuqtalar
              tab tartibidan chiqarilgan (takror bo'lmasin).
            -->
            <div
              class="flex gap-1.5"
              aria-hidden="true"
            >
              <button
                v-for="(item, index) in SLIDES"
                :key="item.id"
                type="button"
                tabindex="-1"
                class="tour-dot"
                :aria-current="index === active ? 'true' : undefined"
                @click="go(index)"
              />
            </div>
            <button
              type="button"
              class="tour-arrow"
              :aria-label="PLATFORM_TOUR.next"
              :disabled="active === LAST"
              @click="go(active + 1)"
            >
              <AppIcon
                name="chevron-right"
                :size="18"
              />
            </button>
          </div>

          <p class="mt-3.5 text-center text-xs text-slate-500">
            {{ PLATFORM_TOUR.note }}
          </p>
        </div>

        <!--
          ═══════════════════════════════════════ NURI — YO'LBOSHCHI ═══
          Kayfiyat slaydga qarab (`mood`), orqadagi nur esa har ekranda
          kuchayadi. Nuri bezak (`aria-hidden`), gap esa matn sifatida —
          `aria-live` uni slayd almashganda o'qiydi.
        -->
        <div class="flex min-w-0 items-end gap-3.5 lg:col-start-1 lg:row-start-2 lg:self-start">
          <div
            class="relative w-20 shrink-0 sm:w-24"
            :style="{ '--nur': nur }"
          >
            <div
              class="tour-nur"
              aria-hidden="true"
            />
            <MascotAvatar
              class="relative"
              :mood="slide.mood"
            />
          </div>
          <div
            class="tour-bubble relative min-w-0 flex-1 rounded-2xl border border-line bg-ink-900 px-4 py-3 shadow-sm"
            aria-live="polite"
          >
            <p class="text-[15px] font-bold text-slate-50">
              {{ slide.title }}
              <span
                v-if="slide.plan === 'milliy'"
                class="ml-1.5 rounded-full bg-amber-950 px-2 py-0.5 align-middle text-[10.5px] font-bold text-amber-300 ring-1 ring-inset ring-amber-800"
              >{{ PLATFORM_TOUR.tagMilliy }}</span>
            </p>
            <p class="mt-0.5 text-sm leading-relaxed text-slate-300">
              {{ slide.caption }}
            </p>
          </div>
        </div>
      </div>
    </div>
  </section>
</template>

<style scoped>
/* Bo'lim foni — juda och yashil tasma: oq bo'limlar ketma-ketligini bo'ladi. */
.tour-bg {
  background: linear-gradient(
    180deg,
    transparent,
    color-mix(in oklab, var(--color-green-950) 70%, transparent) 28%,
    color-mix(in oklab, var(--color-green-950) 70%, transparent) 72%,
    transparent
  );
}

.tour-tabs,
.tour-track {
  scrollbar-width: none;
}

.tour-tabs::-webkit-scrollbar,
.tour-track::-webkit-scrollbar {
  display: none;
}

/* Telefonda — chip. */
.tour-tab {
  display: flex;
  align-items: flex-start;
  gap: 0.75rem;
  padding: 0.5rem 0.9rem;
  border: 1px solid var(--color-line);
  border-radius: 999px;
  background: var(--color-ink-900);
  color: var(--color-slate-300);
  font-size: 13px;
  font-weight: 600;
  text-align: left;
  white-space: nowrap;
  transition: background-color 0.25s, border-color 0.25s, box-shadow 0.25s;
}

.tour-tab[aria-selected='true'] {
  border-color: var(--color-brand-500);
  background: var(--color-brand-500);
  color: var(--color-on-brand);
}

.tour-tab-no {
  flex: none;
  place-items: center;
  width: 1.75rem;
  height: 1.75rem;
  border-radius: 0.55rem;
  background: var(--color-green-950);
  color: var(--color-green-400);
  font-size: 12px;
  font-weight: 800;
}

/* `lg` — raqamli ro'yxat. */
@media (width >= 64rem) {
  .tour-tab {
    padding: 0.75rem 0.9rem;
    border-color: transparent;
    border-radius: 1rem;
    background: transparent;
    white-space: normal;
  }

  .tour-tab:hover {
    background: color-mix(in oklab, var(--color-ink-900) 70%, transparent);
  }

  .tour-tab-title {
    color: var(--color-slate-200);
    font-size: 15px;
    font-weight: 700;
  }

  .tour-tab[aria-selected='true'] {
    border-color: var(--color-line);
    background: var(--color-ink-900);
    color: inherit;
    box-shadow: 0 10px 30px -18px rgb(15 32 25 / 0.35);
  }

  .tour-tab[aria-selected='true'] .tour-tab-no {
    background: var(--color-brand-500);
    color: var(--color-on-brand);
  }
}

.tour-tab:focus-visible,
.tour-track:focus-visible,
.tour-arrow:focus-visible {
  outline: 3px solid var(--color-brand-vivid);
  outline-offset: 2px;
}

.tour-phone {
  position: relative;
  flex: none;
  width: 17.5rem;
  height: 34rem;
  padding: 0.6rem;
  border-radius: 2.6rem;
  background: #151c17;
  box-shadow:
    0 40px 80px -30px rgb(2 42 26 / 0.45),
    inset 0 0 0 2px #2b352f;
}

/* Kamera "orolchasi" — telefon ekanini bir qarashda aytadi. */
.tour-phone::before {
  content: '';
  position: absolute;
  top: 1.1rem;
  left: 50%;
  z-index: 3;
  width: 5rem;
  height: 1.35rem;
  margin-left: -2.5rem;
  border-radius: 0.75rem;
  background: #151c17;
}

.tour-phone-screen {
  position: relative;
  height: 100%;
  overflow: hidden;
  border-radius: 2rem;
  background: var(--color-ink-950);
}

.tour-arrow {
  display: grid;
  place-items: center;
  width: 2.75rem;
  height: 2.75rem;
  border: 1px solid var(--color-line);
  border-radius: 50%;
  background: var(--color-ink-900);
  color: var(--color-slate-200);
  box-shadow: 0 6px 16px -10px rgb(15 32 25 / 0.4);
  transition: opacity 0.2s, border-color 0.2s;
}

.tour-arrow:hover:not(:disabled) {
  border-color: var(--color-brand-500);
}

.tour-arrow:disabled {
  opacity: 0.4;
  cursor: default;
}

.tour-dot {
  width: 0.5rem;
  height: 0.5rem;
  border-radius: 999px;
  background: var(--color-line-strong);
  transition: width 0.3s, background-color 0.3s;
}

.tour-dot[aria-current='true'] {
  width: 1.4rem;
  background: var(--color-brand-500);
}

/* Nuri orqasidagi nur — shaffofligi `--nur` (0.4 → 1). */
.tour-nur {
  position: absolute;
  inset: -12% -12% 0;
  border-radius: 50%;
  background: radial-gradient(circle, rgb(255 223 138 / 0.95), transparent 65%);
  opacity: var(--nur, 0.5);
  transition: opacity 0.6s;
}

/* Gap pufakchasining "dumi" — Nuriga qaraydi. */
.tour-bubble::after {
  content: '';
  position: absolute;
  bottom: 1.4rem;
  left: -0.45rem;
  width: 0.75rem;
  height: 0.75rem;
  border-bottom: 1px solid var(--color-line);
  border-left: 1px solid var(--color-line);
  background: var(--color-ink-900);
  transform: rotate(45deg);
}

@media (prefers-reduced-motion: reduce) {
  .tour-tab,
  .tour-dot,
  .tour-nur {
    transition: none;
  }
}
</style>
