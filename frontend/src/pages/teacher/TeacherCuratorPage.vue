<script setup lang="ts">
import { CuratorOverview } from '@/features/teacher-curator'
import { PageHeader, TodayPill } from '@/shared/ui'

/**
 * "Kuratorlik" — eski `teacher.html` dagi `#curator-hub` bo'limi.
 *
 * Sarlavha eski ilovadan aynan. Izoh esa QISQARTIRILDI: eskisi
 * ("...va yordamchi darslarga jalb etish") hozir BAJARIB BO'LMAYDIGAN
 * amalni va'da qilardi — Telegram taklifnomasi endpointi v2 da yo'q.
 * Sabab ekrandagi ogohlantirishda to'liq yozilgan.
 */
</script>

<template>
  <div>
    <PageHeader
      title="Kuratorlik boshqaruvi"
      subtitle="Ustoz darslarini qoldirgan o‘quvchilarni nazorat qilish"
    />
    <TodayPill />
    <CuratorOverview />
  </div>
</template>
