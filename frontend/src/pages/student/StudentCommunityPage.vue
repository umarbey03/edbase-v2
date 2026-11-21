<script setup lang="ts">
import CommunityRoom from '@/features/community/ui/CommunityRoom.vue'
import { AppIcon } from '@/shared/ui'

/**
 * Mustaqil o'quvchining ikkinchi chati — HAMJAMIYAT (2026-11-21).
 * Birinchisi — kurator bilan shaxsiy yozishma («Yordamchi»); ikkalasi
 * orasida tugma bilan o'tiladi. Balandlik — `StudentChatPage` dagi
 * `CHAT_FILL_STYLE` hisobidan, tepadagi qaytish qatori ayirilgan holda.
 */
</script>

<template>
  <div class="-mb-6 lg:mb-0">
    <div class="mb-2 mt-2 flex items-center gap-2">
      <RouterLink
        :to="{ name: 'student-chat' }"
        class="tap-target flex items-center gap-1.5 rounded-xl border border-line-strong bg-ink-900 px-3 text-sm font-bold text-slate-100 transition-colors hover:bg-ink-800"
      >
        <AppIcon
          name="arrow-left"
          :size="15"
        />
        Kuratorga yozish
      </RouterLink>
    </div>
    <CommunityRoom height-class="h-[calc(100dvh-198px-env(safe-area-inset-bottom,0px))] min-h-[320px] lg:h-[calc(100dvh-190px)]" />
  </div>
</template>
