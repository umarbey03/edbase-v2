<script setup lang="ts">
import { useConfirmHostState } from '@/shared/lib/useConfirm'

import ConfirmDialog from './ConfirmDialog.vue'

/**
 * `useConfirm` navbatini chizuvchi host.
 *
 * 🔴 ILOVADA BITTA BO'LADI — `App.vue` ga qo'yilgan. Ikkinchisi qo'yilsa har
 * tasdiq IKKI marta chizilardi (ikkisi ham bir xil modul holatiga qaraydi) va
 * skroll qulfi sanog'i ikki barobar oshib ketardi.
 *
 * `:key` — navbatdagi keyingi oyna TOZA komponentda ochilishi uchun: fokus
 * qaytarish, animatsiya va boshlang'ich fokus qaytadan ishlaydi.
 */
const { current, settle } = useConfirmHostState()
</script>

<template>
  <ConfirmDialog
    v-if="current !== null"
    :key="current.id"
    :open="true"
    :title="current.options.title"
    :message="current.options.message"
    :confirm-label="current.options.confirmLabel"
    :cancel-label="current.options.cancelLabel"
    :tone="current.options.tone"
    :details="current.options.details"
    @confirm="settle(true)"
    @cancel="settle(false)"
  />
</template>
