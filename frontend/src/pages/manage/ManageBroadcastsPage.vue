<script setup lang="ts">
import { ref } from 'vue'

import { GroupBroadcastComposer, GroupBroadcastHistory } from '@/features/group-broadcast-send'
import { BaseButton, BaseModal, PageHeader } from '@/shared/ui'

/**
 * "XABARLAR" PANELI (2026-08-16) — o'quv bo'limi/admin tanlangan
 * guruhlarga (shablon yoki qo'lda yozilgan) xabar yuboradi.
 *
 * ★ FORMA MODALDA (2026-08-16, loyiha egasi: "xabar yuborish formasi modal
 * holatida ochilishi kerak") — "Yangi xabar" tugmasi bosilganda
 * `GroupBroadcastComposer` (`bare` prop bilan, o'z `BaseCard`isiz)
 * `BaseModal` ichida ochiladi. Yuborilgach (`@sent`) modal avtomatik
 * yopiladi; `GroupBroadcastHistory` o'z so'rovini mustaqil boshqaradi
 * (`['group-broadcasts']` kaliti) va composer ichida shu kalit
 * invalidate qilingani uchun tarix darhol yangilanadi.
 *
 * RUXSAT: server `[Authorize(Roles = "Academic,Admin")]`
 * (`GroupBroadcastsController`/`MessageTemplatesController`) — marshrut
 * `MANAGERS` bilan qulflangan (`router/index.ts`).
 */
const composerOpen = ref(false)
</script>

<template>
  <div>
    <PageHeader
      title="Xabarlar"
      subtitle="Tanlangan guruhlarga Telegram yoki platforma chati orqali xabar yuboring"
    >
      <template #actions>
        <BaseButton @click="composerOpen = true">
          Yangi xabar
        </BaseButton>
      </template>
    </PageHeader>

    <div class="space-y-5">
      <GroupBroadcastHistory />
    </div>

    <BaseModal
      :open="composerOpen"
      title="Yangi xabar"
      wide
      @close="composerOpen = false"
    >
      <GroupBroadcastComposer
        bare
        @sent="composerOpen = false"
      />
    </BaseModal>
  </div>
</template>
