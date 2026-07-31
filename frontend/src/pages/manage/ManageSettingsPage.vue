<script setup lang="ts">
import SettingsPanel from '@/features/settings-manage/ui/SettingsPanel.vue'

/**
 * TIZIM SOZLAMALARI sahifasi — FAQAT `Admin`.
 *
 * ★ Sahifada BIZNES MANTIQ YO'Q (loyiha qoidasi): so'rov, saqlash, tasdiq
 * oynasi va sir bilan ishlash `features/settings-manage` ichida. Bu yerda
 * faqat marshrut va panel bog'lanadi.
 *
 * ★ ROL: server barcha `/api/v1/settings/*` yo'llarini
 * `[Authorize(Roles = "Admin")]` bilan yopgan — o'quv bo'limi ham 403 oladi.
 * Marshrutdagi `roles: ['Admin']` shu qoidaning NUSXASI: usiz Academic
 * sahifani ochib, faqat bo'sh xato ekranini ko'rardi. Guard esa uni
 * menyudagi o'z sahifasiga qaytaradi.
 */
</script>

<template>
  <SettingsPanel />
</template>
