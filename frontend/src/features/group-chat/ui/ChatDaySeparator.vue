<script setup lang="ts">
/**
 * KUN AJRATGICHI — eski ilovadagi `.datesep`.
 *
 * ★ NEGA ALOHIDA KOMPONENT (2026-08-13, R28): ayni bitta chip UCH xil
 * markup bilan chizilardi — guruh suhbatida `self-center` li `div`,
 * ustoz yozishmasida `mx-auto w-fit` li `p` (`rounded-[20px]`), o'quvchi
 * shaxsiy chatida esa UMUMAN yo'q edi. Uchtasi bitta ma'noni bildiradi
 * va foydalanuvchi ular orasida bir kunda bir necha marta o'tadi.
 *
 * ★ MARKAZLASHTIRISH IKKI KONTEKSTDA HAM ISHLAYDI: `mx-auto` + `w-fit`
 * oddiy blok oqimida ham, ustun flex konteynerida ham (u yerda `margin:
 * auto` ko'ndalang o'q bo'yicha markazlaydi) bir xil natija beradi. Shu
 * sababli chaqiruvchi hech qanday qo'shimcha klass bermaydi.
 */
defineProps<{ label: string }>()
</script>

<template>
  <p
    class="mx-auto my-2 w-fit rounded-full border border-line bg-ink-950 px-3 py-0.5 text-[11px] text-slate-400"
    v-text="label"
  />
</template>
