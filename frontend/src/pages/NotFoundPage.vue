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
    <!--
      🔴 Rang `text-ink-700` (#dfe3ee) edi — sahifa fonida (`ink-950`) 1.19:1,
      ya'ni "404" deyarli ko'rinmasdi.

      ⚠️ Topshiriqda `ink-750` yoki `slate-700` taklif qilingan. `ink-750`
      YARAMAYDI: teskari neytral shkalada 750 (#e9ecf5) — 700 (#dfe3ee) dan
      YORUG'ROQ, ya'ni kontrast 1.09:1 ga TUSHARDI. `slate-700` (#c3c8d6)
      esa 1.55:1 — yaxshilanish, lekin hali ham "xira".

      Tanlangan `slate-600`: dizayn tizimining O'ZIDA "dekorativ" deb
      belgilangan token (`style.css`) va kontrast auditida allaqachon
      "dekorativ (slate-600) / sahifa" juftligi sifatida 3:1 talabi bilan
      tekshiriladi (3.12:1). Ya'ni yangi qiymat ham, yangi juftlik ham
      kerak emas — mavjud shartnoma ishlatildi.
    -->
    <p class="text-5xl font-bold tracking-tight text-slate-600">
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
