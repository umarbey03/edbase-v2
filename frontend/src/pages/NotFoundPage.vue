<script setup lang="ts">
import { useRouter } from 'vue-router'

import { BaseButton } from '@/shared/ui'

const router = useRouter()
</script>

<template>
  <div class="flex min-h-dvh flex-col items-center justify-center gap-4 bg-ink-950 px-6 text-center">
    <p class="text-5xl font-bold tracking-tight text-ink-700">404</p>
    <h1 class="text-lg font-semibold text-slate-100">Sahifa topilmadi</h1>
    <p class="max-w-xs text-sm text-slate-500">
      Siz izlagan sahifa mavjud emas yoki ko‘chirilgan.
    </p>
    <BaseButton class="mt-2" @click="router.push({ name: 'sessions' })">Bosh sahifa</BaseButton>
  </div>
</template>
