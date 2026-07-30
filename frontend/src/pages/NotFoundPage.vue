<script setup lang="ts">
import { computed } from 'vue'
import { useRouter } from 'vue-router'

import { homeRouteFor } from '@/entities/user'
import { useAuthStore } from '@/features/auth/model/auth.store'
import { BaseButton } from '@/shared/ui'

const router = useRouter()
const auth = useAuthStore()

// "Bosh sahifa" har rol uchun BOSHQACHA — o'quvchini boshqaruv paneliga
// yuborib bo'lmaydi va aksincha.
const homeRoute = computed(() => homeRouteFor(auth.role))
</script>

<template>
  <div class="flex min-h-dvh flex-col items-center justify-center gap-4 bg-ink-950 px-6 text-center">
    <p class="text-5xl font-bold tracking-tight text-ink-700">
      404
    </p>
    <h1 class="text-lg font-semibold text-slate-100">
      Sahifa topilmadi
    </h1>
    <p class="max-w-xs text-sm text-slate-400">
      Siz izlagan sahifa mavjud emas yoki ko‘chirilgan.
    </p>
    <BaseButton
      class="mt-2"
      @click="router.push({ name: homeRoute })"
    >
      Bosh sahifa
    </BaseButton>
  </div>
</template>
