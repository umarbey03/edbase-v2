<script setup lang="ts">
import { computed } from 'vue'

import { formatTime } from '@/shared/lib/datetime'

import { useRecordingIndicator } from '../model/useRecordingIndicator'

/**
 * ============================================================================
 *  🔴 "YOZUV KETMOQDA" NISHONI — JONLI XONADA, HAR BIR ISHTIROKCHIGA
 * ============================================================================
 *
 * ★ NIMA UCHUN MAVJUD: 2026-08-13 da dars yozuvi AVTOMATIK bo'ldi. Bungacha
 * host tugmasining O'ZI indikator edi; avtomatik rejim uni yo'q qiladi.
 * Rozilik dalili (backend: `IRecordingService` izohi, 1-dalil) yozuvni
 * XONADAGI HAR KIM ko'rishini TALAB qiladi — o'quvchi ham. Bu komponent
 * o'sha talabning bajarilishi va u avtomatik yozuv qarorining SHARTLI
 * qismi, keyinga qoldirilgan bezak emas.
 *
 * ── QANDAY QILIB "PASSIV" ──────────────────────────────────────────────
 *
 * Bu NISHON, tugma emas: bosilmaydi, yopilmaydi, hech qanday amal
 * bermaydi. Yopiladigan qilib qo'yish uni ogohlantirishdan xabarga
 * aylantirardi — bir marta yopilgan indikator qolgan 80 daqiqada
 * ko'rinmasdi.
 *
 * ── JOYLASHUVI: YUQORI PANEL, "Jonli" NISHONI YONIDA ───────────────────
 *
 * ★ NIMA UCHUN `MediaControlBar` GA EMAS: pastki panel AMALLAR uchun
 * (mikrofon, kamera, chiqish) va yotiq telefonda u video USTIDA suzadi —
 * ya'ni u yerdagi element boshqariladigan tugma kabi o'qilardi. Yuqori
 * panel esa HOLAT qatori: "Jonli", qolgan vaqt, ishtirokchilar soni. Yozuv
 * holati aynan shu turkumga tegishli va panel har ikkala yo'nalishda ham
 * (tik va yotiq) ekranda qoladi.
 *
 * ── HOSTGA HAM KO'RSATILADI ─────────────────────────────────────────────
 *
 * ⚠️ Host yonida `SessionRecordingControl` tugmasi ham turadi va bu
 * KO'RINISHDAN takrorga o'xshaydi. ATAYLAB shunday: nishon — o'quvchi
 * KO'RAYOTGAN narsaning AYNI o'zi, ya'ni host xonadagilar nima ko'rib
 * turganini biladi. Tugma esa amal beradi. Ikkalasini birlashtirish
 * indikatorni yana "faqat host ko'radigan" holatga qaytarardi.
 */
const props = defineProps<{
  sessionId: number
  /** Dars jonli emas — so'rov yubormaymiz. */
  isLive: boolean
}>()

const indicator = useRecordingIndicator({
  sessionId: props.sessionId,
  enabled: () => props.isLive,
})

/**
 * Izoh matni. Yozuv hali `Active` bo'lmaganda vaqt YO'Q (`startedAt`
 * `null`) — bunda umumiy matn qoladi. "… dan beri" ni bo'sh vaqt bilan
 * yozish "yozuv  dan beri ketmoqda" degan buzuq jumla berardi.
 */
const hint = computed(() => {
  const startedAt = indicator.startedAt.value
  return startedAt === null
    ? 'Bu dars yozib olinmoqda'
    : `Bu dars ${formatTime(startedAt)} dan beri yozib olinmoqda`
})
</script>

<template>
  <!--
    `role="status"` — ekran o'qiydigan dasturlar uchun. `aria-live` ATAYLAB
    QO'YILMADI (`role="status"` o'zi `polite` beradi): `assertive` bo'lsa u
    ustozning gapini o'rtasidan kesib e'lon qilinardi.

    ★ `title` — sichqoncha uchun, `aria-label` — ekran o'quvchisi uchun.
    Nishon matni ("Yozuvda") qisqa, izoh esa to'liq jumla.
  -->
  <span
    v-if="indicator.isRecording.value"
    class="inline-flex shrink-0 items-center gap-1.5 rounded-full bg-rose-500/12 px-2 py-0.5 text-[11px] font-medium leading-tight text-rose-200"
    role="status"
    :title="hint"
    :aria-label="hint"
  >
    <!--
      Pulsatsiya — kino va efirdagi "REC" nuqtasining AYNI kodi va u shu
      yerda ZARUR: statik qizil nuqta yuqori paneldagi boshqa nishonlardan
      (qolgan vaqt, ishtirokchilar) ajralib turmasdi.

      ⚠️ `motion-reduce:animate-none` — harakatga sezgir foydalanuvchida
      pulsatsiya o'chadi, LEKIN nuqta va matn QOLADI: indikatorning o'zi
      hech qachon yo'qolmasligi kerak.
    -->
    <span
      class="size-1.5 shrink-0 animate-pulse rounded-full bg-rose-500 motion-reduce:animate-none"
      aria-hidden="true"
    />
    <!--
      🔴 MATN TELEFONDA HAM YASHIRILMAYDI (`hidden sm:inline` YO'Q —
      yuqori paneldagi boshqa elementlardan ATAYLAB farq qiladi).
      Yolg'iz qizil nuqta "aloqa", "efir" yoki "xato" degani ham bo'lishi
      mumkin; rozilik signali esa TUSHUNARSIZ bo'lishga haqli emas.
      "Yozuvda" — 7 belgi, u eng tor ekranda ham sig'adi, sarlavha esa
      allaqachon `truncate` bilan qisqaradi.
    -->
    <span>Yozuvda</span>
  </span>
</template>
