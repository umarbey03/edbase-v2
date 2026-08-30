<script setup lang="ts">
/**
 * Yagona ikonka komponenti — tashqi ikonka kutubxonasi qo'shilmaydi (bundle kichik qoladi).
 * Barcha yo'llar 24×24 setkada, `stroke` uslubida.
 */
import { computed } from 'vue'

import { BRAND_ICON_PATHS } from './brand-icon-paths'
import type { BrandIconName, IconName } from './icon-names'

const props = withDefaults(
  defineProps<{
    name: IconName
    size?: number
  }>(),
  { size: 20 },
)

/*
  ⚠️ `Exclude<…, BrandIconName>` — brend belgilari BU RO'YXATDA YO'Q.
  Ular `BRAND_ICON_PATHS` da va boshqacha (to'ldirib) chiziladi; shu tur
  yozuvi ularni bu yerga tasodifan qo'shib qo'yishdan saqlaydi.
*/
const PATHS: Record<Exclude<IconName, BrandIconName>, string> = {
  mic: 'M12 1a3 3 0 0 0-3 3v8a3 3 0 0 0 6 0V4a3 3 0 0 0-3-3z M19 10v2a7 7 0 0 1-14 0v-2 M12 19v4 M8 23h8',
  'mic-off':
    'M1 1l22 22 M9 9v3a3 3 0 0 0 5.12 2.12 M15 9.34V4a3 3 0 0 0-5.94-.6 M17 16.95A7 7 0 0 1 5 12v-2 M19 10v2a7 7 0 0 1-.11 1.23 M12 19v4 M8 23h8',
  camera: 'M23 7l-7 5 7 5V7z M14 5H3a2 2 0 0 0-2 2v10a2 2 0 0 0 2 2h11a2 2 0 0 0 2-2V7a2 2 0 0 0-2-2z',
  'camera-off':
    'M16 16v1a2 2 0 0 1-2 2H3a2 2 0 0 1-2-2V7a2 2 0 0 1 2-2h2 M10.66 5H14a2 2 0 0 1 2 2v3.34l1 1L23 7v10 M1 1l22 22',
  'screen-share': 'M2 3h20v13H2z M8 21h8 M12 16v5',
  hand: 'M18 11V6a2 2 0 0 0-4 0v5 M14 10V4a2 2 0 0 0-4 0v6 M10 10.5V6a2 2 0 0 0-4 0v8 M18 8a2 2 0 1 1 4 0v6a8 8 0 0 1-8 8h-2c-2.8 0-4.5-.9-6-2.4l-3.6-3.6a2 2 0 0 1 2.9-2.8L7 15',
  chat: 'M21 15a2 2 0 0 1-2 2H7l-4 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2z',
  leave:
    'M10.7 13.3a16 16 0 0 0 3.4 2.6l1.3-1.3a2 2 0 0 1 2.1-.4 12.8 12.8 0 0 0 2.8.7 2 2 0 0 1 1.7 2v3a2 2 0 0 1-2.2 2 19.8 19.8 0 0 1-8.6-3.1 19.4 19.4 0 0 1-3.3-2.7 M5.4 10a19.8 19.8 0 0 1-3.1-8.6A2 2 0 0 1 4.1 2h3a2 2 0 0 1 2 1.7 12.8 12.8 0 0 0 .7 2.8 2 2 0 0 1-.5 2.1L8.1 9.9 M23 1L1 23',
  users:
    'M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2 M9 3a4 4 0 1 0 0 8 4 4 0 0 0 0-8z M23 21v-2a4 4 0 0 0-3-3.9 M16 3.1a4 4 0 0 1 0 7.8',
  send: 'M22 2L11 13 M22 2l-7 20-4-9-9-4 20-7z',
  'arrow-down': 'M12 5v14 M19 12l-7 7-7-7',
  'arrow-up': 'M12 19V5 M5 12l7-7 7 7',
  trash:
    'M3 6h18 M8 6V4a1 1 0 0 1 1-1h6a1 1 0 0 1 1 1v2 M19 6l-1 14a2 2 0 0 1-2 2H8a2 2 0 0 1-2-2L5 6 M10 11v6 M14 11v6',
  'arrow-left': 'M19 12H5 M12 19l-7-7 7-7',
  close: 'M18 6L6 18 M6 6l12 12',
  logout: 'M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4 M16 17l5-5-5-5 M21 12H9',
  calendar: 'M8 2v4 M16 2v4 M3 10h18 M5 4h14a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2z',
  refresh: 'M23 4v6h-6 M1 20v-6h6 M3.5 9a9 9 0 0 1 14.9-3.4L23 10 M1 14l4.6 4.4A9 9 0 0 0 20.5 15',
  'wifi-off':
    'M1 1l22 22 M16.7 11.1A11 11 0 0 1 19 12.6 M5 12.6a11 11 0 0 1 5.2-2.4 M10.7 5.1A16 16 0 0 1 22.6 9 M1.4 9a16 16 0 0 1 4.7-2.9 M8.5 16.1a6 6 0 0 1 7 0 M12 20h.01',
  'chevron-down': 'M6 9l6 6 6-6',
  lock: 'M5 11h14a2 2 0 0 1 2 2v7a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-7a2 2 0 0 1 2-2z M7 11V7a5 5 0 0 1 10 0v4',
  mail: 'M4 4h16a2 2 0 0 1 2 2v12a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2z M22 6l-10 7L2 6',
  play: 'M5 3l14 9-14 9V3z',
  check: 'M20 6L9 17l-5-5',
  menu: 'M3 6h18 M3 12h18 M3 18h18',
  search: 'M11 4a7 7 0 1 0 0 14 7 7 0 0 0 0-14z M21 21l-5.2-5.2',
  plus: 'M12 5v14 M5 12h14',
  edit: 'M11 4H5a2 2 0 0 0-2 2v13a2 2 0 0 0 2 2h13a2 2 0 0 0 2-2v-6 M18.5 2.5a2.1 2.1 0 0 1 3 3L12 15l-4 1 1-4 9.5-9.5z',
  clipboard:
    'M9 2h6a1 1 0 0 1 1 1v2H8V3a1 1 0 0 1 1-1z M8 4H6a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V6a2 2 0 0 0-2-2h-2',
  'file-text':
    'M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8l-6-6z M14 2v6h6 M9 13h6 M9 17h6',
  grid: 'M4 4h7v7H4z M13 4h7v7h-7z M4 13h7v7H4z M13 13h7v7h-7z',
  'chevron-right': 'M9 18l6-6-6-6',
  alert: 'M12 3l9.5 16.5H2.5L12 3z M12 10v4 M12 17.5h.01',
  clock: 'M12 3a9 9 0 1 0 0 18 9 9 0 0 0 0-18z M12 7v5l3.2 2',
  star: 'M12 2.8l2.9 5.9 6.5.9-4.7 4.6 1.1 6.5-5.8-3-5.8 3 1.1-6.5L2.6 9.6l6.5-.9L12 2.8z',
  award: 'M12 2a6 6 0 1 0 0 12A6 6 0 0 0 12 2z M8.2 13.3L7 22l5-3 5 3-1.2-8.7',
  paperclip:
    'M21.4 11.1l-9.2 9.2a6 6 0 0 1-8.5-8.5l9.2-9.2a4 4 0 0 1 5.7 5.7l-9.2 9.2a2 2 0 0 1-2.9-2.9l8.5-8.5',
  download: 'M12 3v12 M7 10l5 5 5-5 M4 20h16',
  list: 'M8 6h13 M8 12h13 M8 18h13 M3.5 6h.01 M3.5 12h.01 M3.5 18h.01',
  eye: 'M1.5 12S5 5.5 12 5.5 22.5 12 22.5 12 19 18.5 12 18.5 1.5 12 1.5 12z M12 9a3 3 0 1 0 0 6 3 3 0 0 0 0-6z',
  'eye-off':
    'M9.9 5.7A9.9 9.9 0 0 1 12 5.5c7 0 10.5 6.5 10.5 6.5a17.6 17.6 0 0 1-3.4 4.3 M6.2 7.7A17.4 17.4 0 0 0 1.5 12S5 18.5 12 18.5c1.6 0 3-.3 4.2-.8 M9.9 9.9a3 3 0 0 0 4.2 4.2 M1 1l22 22',

  /*
    Eski o'quvchi ilovasining ikonka sprite'idan (`student.html`,
    `<symbol id="i-…">`) AYNAN ko'chirilgan yo'llar. `<circle>` elementlari
    yoy (`a`) buyrug'iga aylantirilgan, chunki `AppIcon` bitta `<path>` chizadi.
  */
  home: 'm3 9 9-7 9 7v11a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z M9 22V12h6v10',
  book: 'M2 3h6a4 4 0 0 1 4 4v14a3 3 0 0 0-3-3H2z M22 3h-6a4 4 0 0 0-4 4v14a3 3 0 0 1 3-3h7z',
  chart: 'M3 3v18h18 M7 16v-4 M12 16V8 M17 16v-6',
  graduation: 'M22 10 12 5 2 10l10 5 10-5z M6 12v5c0 1 2.7 3 6 3s6-2 6-3v-5',
  'user-check':
    'M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2 M9 3a4 4 0 1 0 0 8 4 4 0 0 0 0-8z M16 11l2 2 4-4',
  'message-circle': 'M7.9 20A9 9 0 1 0 4 16.1L2 22Z',

  /*
    Eski USTOZ panelining sprite'idan (`teacher.html`) AYNAN ko'chirilgan:
    `i-att`, `i-board`, `i-student`, `i-phone`. `<circle>`/`<line>`/`<polyline>`
    elementlari bitta `<path>` ga sig'ishi uchun yoy va chiziq buyruqlariga
    aylantirilgan — shakl o'zgarmagan.
  */
  'check-square': 'M9 11l3 3L22 4 M21 12v7a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h11',
  trophy:
    'M6 9H4.5a2.5 2.5 0 0 1 0-5H6 M18 9h1.5a2.5 2.5 0 0 0 0-5H18 M4 22h16 M10 14.66V17c0 .55-.45 1-1 1H4v2h16v-2h-5c-.55 0-1-.45-1-1v-2.34 M12 2a4 4 0 0 0-4 4v5c0 2.2 1.8 4 4 4s4-1.8 4-4V6a4 4 0 0 0-4-4z',
  user: 'M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2 M9 3a4 4 0 1 0 0 8 4 4 0 0 0 0-8z',
  phone:
    'M22 16.9v3a2 2 0 0 1-2.2 2 19.8 19.8 0 0 1-8.6-3.1 19.5 19.5 0 0 1-6-6A19.8 19.8 0 0 1 2.1 4.2 2 2 0 0 1 4.1 2h3a2 2 0 0 1 2 1.7c.1 1 .4 1.9.7 2.8a2 2 0 0 1-.5 2.1L8.1 9.9a16 16 0 0 0 6 6l1.3-1.3a2 2 0 0 1 2.1-.4c.9.3 1.8.6 2.8.7a2 2 0 0 1 1.7 2z',

  // Uchta vertikal "regulyator" — tizim sozlamalari bo'limi (sababi `icon-names.ts` da).
  sliders: 'M4 21v-7 M4 10V3 M12 21v-9 M12 8V3 M20 21v-5 M20 12V3 M1 14h6 M9 8h6 M17 16h6',

  /*
    ============ IKONKALI AMAL TUGMALARI (`IconButton`) ============

    Hammasi mavjud uslubga moslashtirilgan: 24×24 setka, faqat `stroke`,
    `stroke-width` 1.75 (SVG atributi umumiy), doiralar yoy (`a`) buyrug'iga
    aylantirilgan — `AppIcon` bitta `<path>` chizadi.

    Juftliklari MIRROR qilib olingan, shunda qarama-qarshi amal bir qarashda
    tanilsin: `pause` ↔ `play`, `upload` ↔ `download`,
    `chevron-left` ↔ `chevron-right`, `user-x` ↔ `user-check`.
  */

  // To'xtatish (jonli dars yozuvi, video). `play` bilan bir o'lchamda ko'rinsin
  // uchun to'rtburchak — ikki yupqa chiziq 1.75px da "sinib" ketardi.
  pause: 'M6 4h4v16H6z M14 4h4v16h-4z',

  // "Ko'chirish" (o'quvchini boshqa guruhga): ikki yo'nalishli almashinuv.
  'arrow-right-left': 'M8 3L4 7l4 4 M4 7h16 M16 21l4-4-4-4 M20 17H4',

  // Guruhdan CHIQARISH — `user-check` ning teskarisi (o'sha odam shakli + ✕).
  'user-x': 'M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2 M9 3a4 4 0 1 0 0 8 4 4 0 0 0 0-8z M17 8l5 5 M22 8l-5 5',

  // Fayl yuklash — `download` ning aynan mirror'i (o'q yuqoriga, chiziq tepada).
  upload: 'M12 21V9 M7 14l5-5 5 5 M4 4h16',

  // Rasm biriktirmasi: ramka + quyosh + tog' (tanish "gallereya" shakli).
  image:
    'M5 3h14a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2z M8.5 8a1.5 1.5 0 1 0 0 3 1.5 1.5 0 0 0 0-3z M21 15l-5-5L5 21',

  // DARS VIDEOSI: ekran + ichida play. `camera` (videoqo'ng'iroq) dan farq
  // qilishi shart — ikkisi bir sahifada yonma-yon turadi.
  video: 'M4 4h16a1 1 0 0 1 1 1v14a1 1 0 0 1-1 1H4a1 1 0 0 1-1-1V5a1 1 0 0 1 1-1z M10 9l5 3-5 3V9z',

  // Havolani UZISH (Telegram uzish, biriktirmani olib tashlash). Chizilgan
  // chiziq (`M1 1l22 22`) — `mic-off`/`eye-off`/`wifi-off` bilan bir naqsh.
  'link-off':
    'M10 13a5 5 0 0 0 7.5.5l3-3a5 5 0 0 0-7-7l-1.5 1.5 M14 11a5 5 0 0 0-7.5-.5l-3 3a5 5 0 0 0 7 7l1.5-1.5 M1 1l22 22',

  // Hamyon — to'lov/balans amallari (qoplama + qopqoq + qulf nuqtasi).
  wallet:
    'M3 7.5A2.5 2.5 0 0 1 5.5 5H18a1 1 0 0 1 1 1v1.5 M3 7.5v10A2.5 2.5 0 0 0 5.5 20H19a2 2 0 0 0 2-2v-8a2 2 0 0 0-2-2H5.5A2.5 2.5 0 0 1 3 7.5z M16.5 14h.01',

  // Izoh/eslatma — burchagi qayrilgan varaq. `file-text` dan FARQ QILADI
  // (u yerda qayrilma yuqori-o'ngda, hujjat ma'nosida).
  note: 'M4 4h16v11l-5 5H4z M15 20v-5h5 M8 9h8 M8 12.5h4',

  // Orqaga o'tish (drawer ichidagi ko'p qadamli oqim) — `chevron-right` mirror'i.
  'chevron-left': 'M15 18l-6-6 6-6',

  /*
    R35/R36 — bildirishnoma qo'ng'iroqchasi: qo'ng'iroq tanasi + tili.
    Pastdagi kichik yoy ("tili") ATAYLAB alohida bo'lak: usiz shakl 20px
    da oddiy gumbazga o'xshab, `home` ikonkasi bilan chalkashardi.
  */
  bell: 'M18 8a6 6 0 0 0-12 0c0 7-3 9-3 9h18s-3-2-3-9 M13.73 21a2 2 0 0 1-3.46 0',
}

/**
 * Brend logosimi (Telegram, Instagram, YouTube).
 *
 * 🔴 SHAKL BOSHQACHA CHIZILADI: brend belgisi TO'LDIRILADI (`fill`),
 * qolgan ikonkalar esa CHIZIQ (`stroke`) bilan chiziladi. Brend shaklini
 * chiziqqa aylantirish uni tanib bo'lmas holga keltiradi — masalan
 * YouTube'ning to'ldirilgan to'rtburchagi ichki uchburchagini yo'qotadi.
 */
const isBrand = computed(() => props.name in BRAND_ICON_PATHS)

const path = computed(
  () =>
    isBrand.value
      ? BRAND_ICON_PATHS[props.name as BrandIconName]
      : PATHS[props.name as Exclude<IconName, BrandIconName>],
)
</script>

<template>
  <svg
    :width="props.size"
    :height="props.size"
    viewBox="0 0 24 24"
    :fill="isBrand ? 'currentColor' : 'none'"
    :stroke="isBrand ? 'none' : 'currentColor'"
    stroke-width="1.75"
    stroke-linecap="round"
    stroke-linejoin="round"
    aria-hidden="true"
    focusable="false"
    class="shrink-0"
  >
    <path :d="path" />
  </svg>
</template>
