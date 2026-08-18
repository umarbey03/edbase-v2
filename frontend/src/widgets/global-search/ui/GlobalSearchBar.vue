<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref } from 'vue'

import { AppIcon } from '@/shared/ui'

import GlobalSearchPalette from './GlobalSearchPalette.vue'

/**
 * NAVBARDAGI QIDIRUV (2026-08-18).
 *
 * ★ MAYDON EMAS, TUGMA: bosilganda to'liq oyna ochiladi. Navbardagi
 * tor maydonga natijalar ro'yxatini sig'dirib bo'lmasdi, ikkita
 * qidiruv maydoni (biri navbarda, biri oynada) esa qaysi biriga
 * yozishni noaniq qilardi.
 *
 * ★ `Ctrl/⌘+K` BUTUN ILOVADA ISHLAYDI: tinglovchi `window` da, ya'ni
 * fokus qayerda bo'lishidan qat'i nazar oyna ochiladi. Faqat matn
 * kiritilayotgan joyda emas — u yerda ham kerak bo'ladi.
 */
const open = ref(false)

function onKeydown(event: KeyboardEvent): void {
  // ★ `metaKey` (macOS) va `ctrlKey` (Windows/Linux) IKKALASI ham:
  //   xodimlar ikkala tizimda ishlaydi va har biri o'z odatini
  //   ishlatadi.
  if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'k') {
    event.preventDefault()
    open.value = true
  }
}

onMounted(() => window.addEventListener('keydown', onKeydown))
onBeforeUnmount(() => window.removeEventListener('keydown', onKeydown))
</script>

<template>
  <button
    type="button"
    class="flex min-w-0 items-center gap-2 rounded-xl border border-line bg-ink-800 px-3 py-2 text-sm text-slate-500 transition-colors hover:border-line-strong hover:text-slate-300"
    aria-label="Global qidiruv (Ctrl+K)"
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
    <span class="ml-auto hidden shrink-0 rounded border border-line px-1.5 text-[10px] text-slate-500 lg:inline">
      Ctrl K
    </span>
  </button>

  <GlobalSearchPalette
    :open="open"
    @close="open = false"
  />
</template>
