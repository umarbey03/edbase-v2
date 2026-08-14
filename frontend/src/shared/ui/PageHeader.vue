<script setup lang="ts">
/*
  Eski dizayndagi `h1` (22px/700) + `.sub` (13px, muted) juftligi.
  Telefonda sarlavha 18px — 390px kengliqda ikki qatorga bo'linmasin.
*/
withDefaults(defineProps<{ title: string; subtitle?: string }>(), { subtitle: '' })
</script>

<template>
  <header class="mb-5 flex flex-wrap items-end justify-between gap-3">
    <div class="min-w-0">
      <h1
        class="text-lg font-bold tracking-tight sm:text-[22px]"
        v-text="title"
      />
      <p
        v-if="subtitle.length > 0"
        class="mt-1 text-[13px] text-slate-400"
        v-text="subtitle"
      />
    </div>
    <!--
      🔴 `max-w-full` — `shrink-0` BILAN BIRGA bo'lishi SHART (2026-08-13).

      `shrink-0` konteynerni `max-content` kengligida QOTIRADI: u parent'dan
      kichrayolmaydi. Natijada yonidagi `flex-wrap` HECH QACHON ishga
      tushmasdi — o'ralish uchun element avval torayishi kerak. Slot ichida
      qidiruv maydoni + ikkita `select` + tugma bo'lgan sahifalarda
      (ManageUsersPage, ManageGroupsPage, ManagePaymentsPage) bu butun
      SAHIFANI gorizontal skrollga majbur qilardi.

      ★ `shrink-0` OLIB TASHLANMADI: usiz amallar bloki uzun sarlavha
      yonida siqilib, tugma yozuvlari ikki qatorga bo'linib ketardi.
      `max-w-full` esa faqat YUQORI chegara qo'yadi — joy yetganda
      `shrink-0` avvalgidek ishlaydi, yetmaganda `flex-wrap` uyg'onadi.
    -->
    <div
      v-if="$slots.actions"
      class="flex max-w-full shrink-0 flex-wrap items-center gap-2"
    >
      <slot name="actions" />
    </div>
  </header>
</template>
