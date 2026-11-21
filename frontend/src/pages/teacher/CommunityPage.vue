<script setup lang="ts">
import CommunityRoom from '@/features/community/ui/CommunityRoom.vue'
import { PageHeader } from '@/shared/ui'

/**
 * Xodim: Mustaqil o'quvchilar hamjamiyati (2026-11-21). Kurator va o'quv
 * bo'limi — moderator: kuzatadi, savollarga javob beradi, "kun savoli"
 * tashlaydi, qoidabuzarni vaqtincha to'xtatadi. Xona sozlamada o'chiq
 * bo'lsa ham xodim uchun ochiq (o'quvchilarga ochishdan oldin tayyorlash).
 */
</script>

<template>
  <div>
    <PageHeader
      title="Hamjamiyat"
      subtitle="Barcha Mustaqil o‘quvchilarning umumiy chati. Siz moderatorsiz: xabarni o‘chirasiz, qoidabuzarni vaqtincha to‘xtatasiz."
    />
    <CommunityRoom height-class="h-[calc(100dvh-13rem)] min-h-[420px]" />
  </div>
</template>
