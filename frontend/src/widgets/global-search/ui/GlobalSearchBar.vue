<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'

import { AppIcon } from '@/shared/ui'

import GlobalSearchPalette from './GlobalSearchPalette.vue'

/**
 * NAVBARDAGI QIDIRUV (2026-08-18 · 2026-08-19 da yorliqlar kengaytirildi).
 *
 * ★ MAYDON EMAS, TUGMA: bosilganda to'liq oyna ochiladi. Navbardagi
 * tor maydonga natijalar ro'yxatini sig'dirib bo'lmasdi, ikkita
 * qidiruv maydoni (biri navbarda, biri oynada) esa qaysi biriga
 * yozishni noaniq qilardi.
 *
 * ★ `Ctrl/⌘+K` BUTUN ILOVADA ISHLAYDI: tinglovchi `window` da, ya'ni
 * fokus qayerda bo'lishidan qat'i nazar oyna ochiladi. Faqat matn
 * kiritilayotgan joyda emas — u yerda ham kerak bo'ladi.
 *
 * ★ ALMASHTIRADI, FAQAT OCHMAYDI (2026-08-19, namunadagi kabi): ilgari
 * oyna ochiq turganda `Ctrl+K` hech narsa qilmasdi va yopish uchun
 * qo'lni klaviaturadan uzib sichqonchaga borish yoki `Esc` ni topish
 * kerak edi. Bitta kombinatsiya ikki tomonga ishlagani barmoq
 * xotirasiga tez tushadi.
 *
 * ★ `/` — IKKINCHI, QISQAROQ YO'L (namunadan): matn kiritilmayotgan
 * paytda bitta belgi yetadi. FAQAT maydondan tashqarida ishlaydi —
 * aks holda xodim izohga "/" yozolmay qolardi.
 */
const open = ref(false)

/** Fokus matn kiritiladigan joydami — `/` yorlig'i shunda O'CHADI. */
function isTyping(target: EventTarget | null): boolean {
  if (!(target instanceof HTMLElement)) return false

  return target.tagName === 'INPUT'
    || target.tagName === 'TEXTAREA'
    || target.tagName === 'SELECT'
    || target.isContentEditable
}

function onKeydown(event: KeyboardEvent): void {
  // ★ `metaKey` (macOS) va `ctrlKey` (Windows/Linux) IKKALASI ham:
  //   xodimlar ikkala tizimda ishlaydi va har biri o'z odatini
  //   ishlatadi.
  if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'k') {
    event.preventDefault()
    open.value = !open.value
    return
  }

  // Oyna ochiq bo'lsa `/` maydonga yoziladi — bu yerda ushlanmaydi.
  if (event.key === '/' && !open.value && !isTyping(event.target)) {
    event.preventDefault()
    open.value = true
  }
}

onMounted(() => window.addEventListener('keydown', onKeydown))
onBeforeUnmount(() => window.removeEventListener('keydown', onKeydown))

/** macOS'da `⌘K`, qolgan joyda `Ctrl K`. */
const shortcutLabel = computed(() =>
  /mac|iphone|ipad/i.test(navigator.userAgent) ? '⌘ K' : 'Ctrl K',
)
</script>

<template>
  <button
    type="button"
    class="flex min-w-0 items-center gap-2 rounded-xl border border-line bg-ink-800 px-3 py-2 text-sm text-slate-500 transition-colors hover:border-line-strong hover:text-slate-300"
    :aria-label="`Global qidiruv (${shortcutLabel})`"
    @click="open = true"
  >
    <AppIcon
      name="search"
      :size="16"
      class="shrink-0"
    />
    <span class="hidden truncate sm:inline">Qidirish...</span>
    <!--
      Kombinatsiya KO'RSATILADI: yashirin klaviatura yorlig'ini hech kim
      o'zi topmaydi, ko'rsatilgani esa bir necha kundan keyin barmoq
      xotirasiga tushadi.
    -->
    <span
      class="ml-auto hidden shrink-0 rounded border border-line px-1.5 text-[10px] text-slate-500 lg:inline"
      v-text="shortcutLabel"
    />
  </button>

  <GlobalSearchPalette
    :open="open"
    @close="open = false"
  />
</template>
