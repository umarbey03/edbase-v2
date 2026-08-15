<script setup lang="ts">
import { RouterView } from 'vue-router'

import ConfirmHost from '@/shared/ui/ConfirmHost.vue'
import RouteProgress from '@/shared/ui/RouteProgress.vue'
import ToastHost from '@/shared/ui/ToastHost.vue'
</script>

<template>
  <div class="min-h-dvh bg-ink-950 text-slate-100">
    <RouterView v-slot="{ Component }">
      <component :is="Component" />
    </RouterView>

    <!--
      Ikkalasi ILDIZDA, BIR MARTA:

      • `RouteProgress` — sahifa almashishi va 400 ms dan uzoq so'rovlarda
        yuqoridagi yupqa chiziq. Router va vue-query'ga global obuna bo'ladi,
        shuning uchun sahifada emas, ildizda yashaydi.
      • `ConfirmHost` — `useConfirm()` navbatini chizadi. Ikkinchi nusxa
        qo'yilsa har tasdiq ikki marta ko'rinardi.
      • `ToastHost` — `showToast()` navbati (amal tasdig'i). Ilgari toast
        FAQAT o'quvchi karkasida bor edi va xodim panelida umuman
        ishlamasdi; endi u ham ildizda, ya'ni ikkala karkasda ham
        bir xil ishlaydi.

      Ikkisi ham `Teleport to="body"` bilan chiziladi, ya'ni bu `<div>` ning
      fon rangi yoki `transform` i ularga ta'sir qilmaydi. Tema esa `<html>`
      da (`DAVOM_ETTIRISH.md` 9-tuzog'i) — teleport qilingan kontent ham
      to'g'ri tokenlarni oladi.
    -->
    <RouteProgress />
    <ConfirmHost />
    <ToastHost />
  </div>
</template>
