<script setup lang="ts">
import { GroupBroadcastComposer, GroupBroadcastHistory } from '@/features/group-broadcast-send'
import { PageHeader } from '@/shared/ui'

/**
 * "XABARLAR" PANELI (2026-08-16) — o'quv bo'limi/admin tanlangan
 * guruhlarga (shablon yoki qo'lda yozilgan) xabar yuboradi.
 *
 * ★ IKKI QISM BITTA SAHIFADA, TAB EMAS: "yuborish" va "tarix" bir-biri
 * bilan ketma-ket ishlatiladi (xabar yuboriladi -> tarixda darhol
 * ko'rinadi), ya'ni tab orasida sakrash foydalanuvchini natijadan
 * chalg'itardi. `GroupBroadcastHistory` o'z so'rovini mustaqil boshqaradi
 * (`['group-broadcasts']` kaliti); yuborilgach `GroupBroadcastComposer`
 * shu kalitni invalidate qiladi (`@sent` kutilmaydi — sahifa faqat
 * joylashuvni belgilaydi, ma'lumot oqimi ikki komponent ORASIDA emas,
 * query kesh orqali ishlaydi).
 *
 * RUXSAT: server `[Authorize(Roles = "Academic,Admin")]`
 * (`GroupBroadcastsController`/`MessageTemplatesController`) — marshrut
 * `MANAGERS` bilan qulflangan (`router/index.ts`).
 */
</script>

<template>
  <div>
    <PageHeader
      title="Xabarlar"
      subtitle="Tanlangan guruhlarga Telegram yoki platforma chati orqali xabar yuboring"
    />

    <div class="space-y-5">
      <GroupBroadcastComposer />
      <GroupBroadcastHistory />
    </div>
  </div>
</template>
