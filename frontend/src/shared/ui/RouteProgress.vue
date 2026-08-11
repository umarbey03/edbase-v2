<script setup lang="ts">
import { useIsFetching, useIsMutating } from '@tanstack/vue-query'
import { computed, onScopeDispose, ref, watch } from 'vue'
import { useRouter } from 'vue-router'

/**
 * GLOBAL YUPQA PROGRESS — sahifa yuqorisidagi 2px brend chizig'i.
 *
 * Talab: *"tugma ishlagani va ma'lumot load qilinayotganini bilish uchun"* —
 * uch qatlamning UCHINCHISI. Tugma ichidagi spinner faqat SHU tugmani
 * qamrab oladi; bu chiziq esa sahifa almashishi va fon so'rovlarini ham
 * ko'rsatadi (menyudan boshqa bo'limga o'tish, kesh yangilanishi).
 *
 * MANBA uchta: `useIsFetching()` (o'qish), `useIsMutating()` (yozish) va
 * router navigatsiyasi. Faqat bittasi bo'lsa yetmaydi: `POST` so'rovi
 * `isFetching` ga kirmaydi, sahifa almashishi esa lazy chunk yuklanishida
 * hech qanday so'rov yubormasligi mumkin.
 *
 * 🔴 400 ms KECHIKISH MAJBURIY: lokal so'rov 30 ms da qaytadi va chiziq
 * "chaqnab" ketsa interfeys asabiy ko'rinadi (foydalanuvchi nima bo'lganini
 * tushunmaydi, faqat titrashni sezadi). Ko'ringandan keyin esa KAMIDA 250 ms
 * turadi — aks holda paydo bo'lgani ko'z ilg'amaydi.
 */
const router = useRouter()
const isFetching = useIsFetching()
const isMutating = useIsMutating()

/** Router navigatsiyasi davom etmoqda. */
const navigating = ref(false)

/*
  Guard'lar `onScopeDispose` da olib tashlanadi: komponent (nazariy jihatdan)
  qayta mount qilinsa router'da ikkita bir xil guard qolib ketmasin.
*/
const stopBefore = router.beforeEach(() => {
  navigating.value = true
  return true
})
const stopAfter = router.afterEach(() => {
  navigating.value = false
})
const stopError = router.onError(() => {
  navigating.value = false
})

const busy = computed(() => navigating.value || isFetching.value > 0 || isMutating.value > 0)

const visible = ref(false)
const SHOW_DELAY_MS = 400
const MIN_VISIBLE_MS = 250

let showTimer: number | null = null
let hideTimer: number | null = null
let shownAt = 0

function clearTimer(handle: number | null): null {
  if (handle !== null) window.clearTimeout(handle)
  return null
}

watch(busy, (isBusy) => {
  if (isBusy) {
    hideTimer = clearTimer(hideTimer)
    // Allaqachon ko'rinib turgan yoki kutayotgan bo'lsa — qayta boshlamaymiz,
    // aks holda ketma-ket so'rovlar chiziqni cho'zib ketardi.
    if (visible.value || showTimer !== null) return
    showTimer = window.setTimeout(() => {
      showTimer = null
      visible.value = true
      shownAt = Date.now()
    }, SHOW_DELAY_MS)
    return
  }

  // Tez javob: kechikish tugamagan — chiziq umuman ko'rinmaydi.
  if (showTimer !== null) {
    showTimer = clearTimer(showTimer)
    return
  }
  if (!visible.value) return

  const remaining = Math.max(0, MIN_VISIBLE_MS - (Date.now() - shownAt))
  hideTimer = window.setTimeout(() => {
    hideTimer = null
    visible.value = false
  }, remaining)
})

onScopeDispose(() => {
  showTimer = clearTimer(showTimer)
  hideTimer = clearTimer(hideTimer)
  stopBefore()
  stopAfter()
  stopError()
})
</script>

<template>
  <!--
    `Teleport to="body"` — chiziq karkasdan (`AppShell`, sahifa `transform`
    lari) TASHQARIDA turishi kerak: `transform` li ota element `position:
    fixed` ni o'ziga bog'lab qo'yadi va chiziq sahifa ichida "adashib" qolardi.

    `aria-hidden` — screen reader uchun bu chiziq ma'lumot bermaydi (tugma
    `:loading` holati va `role="status"` li skeletonlar allaqachon aytadi).
    Doimiy `aria-live` esa har so'rovda ovozli e'lon qilib bezor qilardi.
  -->
  <Teleport to="body">
    <div
      v-if="visible"
      class="pointer-events-none fixed inset-x-0 top-0 z-[60] h-0.5 overflow-hidden bg-brand-500/20"
      aria-hidden="true"
    >
      <!--
        Harakatlanuvchi segment. `prefers-reduced-motion` da segment
        surilmaydi, chizig'ning o'zi to'liq kenglikda turadi — holat baribir
        ko'rinadi, lekin harakat yo'q.
      -->
      <div
        class="h-full w-1/3 animate-progress-slide bg-brand-500 motion-reduce:w-full motion-reduce:animate-none"
      />
    </div>
  </Teleport>
</template>
