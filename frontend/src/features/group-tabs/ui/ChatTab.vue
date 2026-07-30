<script setup lang="ts">
import { computed } from 'vue'
import { useRouter } from 'vue-router'

import { useAuthStore } from '@/features/auth/model/auth.store'
import { BaseBadge, BaseButton, BaseCard } from '@/shared/ui'

/**
 * "Chat" tabi — eski `#tab-chat` (guruh chati, Telegram uslubida).
 *
 * ★ ATAYLAB BO'SH, sababi: v2 da GURUH chati YO'Q. Backendda ikki xil
 * yozishma bor va ikkalasi ham boshqa narsa:
 *   • JONLI DARS chati — SignalR `/hubs/live-class` + `GET
 *     /live-sessions/{id}/messages`, ya'ni bitta DARSGA bog'langan va dars
 *     tugagach yopiladi;
 *   • SHAXSIY yozishma (DM) — kurator ↔ o'quvchi juftligi
 *     (`/api/v1/messages/...`), "Savollar" bo'limida.
 * Guruhning doimiy umumiy chati uchun jadval ham, endpoint ham yozilmagan.
 *
 * Bo'sh joyni "hali xabar yo'q" deb ko'rsatish YOLG'ON bo'lardi: ustoz
 * xabar yozib ko'rar va u hech kimga bormasdi. Shuning uchun sabab
 * oshkora, yo'nalish esa bor imkoniyatlarga beriladi.
 */
const router = useRouter()
const auth = useAuthStore()

const isCurator = computed(() => auth.role === 'Assistant')
</script>

<template>
  <BaseCard>
    <div class="flex items-center gap-2.5">
      <BaseBadge
        :tone="isCurator ? 'assistant' : 'teacher'"
        size="sm"
        dot
      >
        {{ isCurator ? 'Kurator chati' : 'Ustoz chati' }}
      </BaseBadge>
      <span class="text-xs text-slate-400">o‘quvchilar bilan</span>
    </div>

    <p class="mt-3.5 text-sm leading-relaxed text-slate-300">
      Guruhning doimiy umumiy chati v2 da hali yo‘q — server tomonida bunday
      yozishma yaratilmagan.
    </p>

    <ul class="mt-2.5 space-y-1.5 text-xs leading-relaxed text-slate-400">
      <li>
        • <b class="text-slate-200">Jonli dars chati</b> dars sahifasida ishlaydi
        (dars boshlanganda ochiladi).
      </li>
      <li>
        • <b class="text-slate-200">Shaxsiy savollar</b> — “Savollar” bo‘limida,
        har o‘quvchi bilan alohida.
      </li>
    </ul>

    <BaseButton
      class="mt-4"
      size="sm"
      variant="secondary"
      @click="router.push({ name: 'teacher-chat' })"
    >
      Savollar bo‘limiga o‘tish
    </BaseButton>
  </BaseCard>
</template>
