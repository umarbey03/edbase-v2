<script setup lang="ts">
import { GroupBroadcastComposer } from '@/features/group-broadcast-send'
import type { GroupDto } from '@/shared/types'

/**
 * "Xabar" tabi — guruh ICHIDAN, shu guruhga Telegram/platforma chati orqali
 * xabar yuborish (loyiha egasi, 2026-08-16: *"xabar yuborish har bir guruh
 * ichida ham bo'lishi kerak"*).
 *
 * ★ FAQAT YUBORISH, TARIX YO'Q: markaziy "Xabarlar" panelidan (`manage-
 * broadcasts`) farqli, bu yerda o'tgan xabarlar RO'YXATI ko'rsatilmaydi —
 * server tarixni guruh bo'yicha filtrlashni BILMAYDI (`GroupBroadcastService.
 * ListAsync` — faqat sahifalash, `GroupId` filtri yo'q). Yolg'on/qisman
 * ro'yxat ko'rsatish o'rniga bu tab shunchaki markaziy panelga yo'naltiradi.
 *
 * ★ FAQAT O'QUV BO'LIMI/ADMIN KO'RADI — `visibleGroupTabs` allaqachon
 * filtrlagan (`MANAGER_ONLY_TABS`), bu yerda qo'shimcha tekshiruv shart
 * emas (`GroupTabs` bir marta ko'rinadigan tablarni chizadi).
 */
defineProps<{ group: GroupDto }>()
</script>

<template>
  <div>
    <p class="mb-4 text-xs text-slate-400">
      Xabar shu guruhga yuboriladi. Barcha guruhlar bo‘yicha tarixni
      <RouterLink
        :to="{ name: 'manage-broadcasts' }"
        class="font-medium text-brand-400 hover:underline"
      >
        Xabarlar
      </RouterLink>
      bo‘limida ko‘rishingiz mumkin.
    </p>
    <GroupBroadcastComposer :fixed-group="group" />
  </div>
</template>
