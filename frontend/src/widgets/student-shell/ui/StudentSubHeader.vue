<script setup lang="ts">
import { AppIcon } from '@/shared/ui'

/**
 * "O'quv" tabining ichki sahifalari uchun sarlavha (vazifalar, testlar).
 *
 * Orqaga tugmasi ANIQ marshrutga olib boradi, `router.back()` ga EMAS:
 * o'quvchi bu sahifaga tashqi havoladan ham kirishi mumkin, bunda tarix bo'sh
 * bo'lib "orqaga" ilovadan chiqarib yuborardi.
 *
 * ★ DESKTOPDA (≥1024px) SARLAVHA SHU YERDA QOLADI, yuqori panelga
 * ko'chirilmaydi: appbar 5 tabning ustidagi umumiy panel, bu esa "O'quv"
 * ning ICHKI sahifasi — orqaga tugmasi bilan sarlavha bir-biridan ajralib
 * ketsa, "qayerdan qaytaman" aloqasi yo'qolardi. Faqat o'lchamlar ustun
 * kengayganiga moslanadi (960px da 17px sarlavha juda kichkina ko'rinadi).
 *
 * ★ 2026-08-13, USTUN 1600px BO'LGACH: 17/21px sarlavha va 44px lik tugma
 * shuncha keng maydonda "yo'qolib" ketardi. Uchta desktop qo'shimchasi,
 * hammasi `lg:` ostida:
 *   • sarlavha 26px, izoh 14px — sahifa boshi ustunga MUTANOSIB bo'ladi;
 *   • pastdan chiziq (`lg:border-b`) — u BUTUN ustun bo'ylab cho'ziladi va
 *     kenglikni "ishlatadi": sarlavha endi ekranning chap burchagida
 *     yolg'iz turgan yorliq emas, sahifaning kesimi;
 *   • orqaga tugmasi 48px — desktopda u KAM kerak (yon menyu doim
 *     ko'rinib turadi, ya'ni bu sahifadan chiqish yo'li bitta emas),
 *     lekin sahifa baribir ICHKI sahifa: tugma olib tashlanmaydi, faqat
 *     sarlavha bilan bir og'irlikda bo'ladi.
 */
defineProps<{ title: string; subtitle?: string }>()
</script>

<template>
  <header
    class="mb-4 mt-2 flex items-center gap-3 lg:mb-6 lg:mt-3 lg:gap-4 lg:border-b lg:border-line lg:pb-5"
  >
    <!--
      `hover:` — desktopda tugma sichqonchaga javob berishi kerak (6.5:
      "interaktivroq"). Teginishli ekranda hover umuman qo'llanmaydi —
      Tailwind v4 uni `@media (hover: hover)` ga o'raydi.

      ★ O'TISH RO'YXATI QO'LDA: v4 da `scale-*` alohida `scale` XOSSASIGA
      yoziladi, ya'ni `transition-transform` aslida
      `transform, translate, scale, rotate` degani. Ro'yxat qisqartirilsa
      telefondagi `active:scale-90` bosish effekti o'tishini yo'qotardi —
      eski to'rtlik AYNAN saqlanib, ustiga hover ranglari qo'shildi.
    -->
    <RouterLink
      :to="{ name: 'student-learn' }"
      class="tap-target flex shrink-0 items-center justify-center rounded-[11px] border border-line bg-ink-800 text-slate-100 transition-[transform,translate,scale,rotate,background-color,border-color] hover:border-line-strong hover:bg-ink-750 active:scale-90 lg:size-12 lg:rounded-[14px]"
      aria-label="O‘quv bo‘limiga qaytish"
    >
      <AppIcon
        name="arrow-left"
        :size="18"
      />
    </RouterLink>
    <div class="min-w-0">
      <!--
        2026-08-29: serif sarlavha (`font-display`).
        🔴 `font-extrabold` (800) DAN `font-semibold` (600) GA TUSHIRILDI —
        Newsreader faqat 600 da yuklanadi, boshqa vazn so'ralsa brauzer uni
        sun'iy qalinlashtiradi. Batafsil: `style.css` dagi `@font-face`.
      -->
      <h1
        class="truncate font-display text-[19px] font-semibold leading-tight lg:text-[28px] lg:tracking-[-0.4px]"
        v-text="title"
      />
      <p
        v-if="subtitle !== undefined && subtitle.length > 0"
        class="mt-0.5 truncate text-[12.5px] text-slate-400 lg:mt-1 lg:text-sm"
        v-text="subtitle"
      />
    </div>
  </header>
</template>
