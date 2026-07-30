<script setup lang="ts">
import { UpcomingLessonsCard } from '@/features/teacher-home'
import { PageHeader, TodayPill } from '@/shared/ui'

/**
 * USTOZ / KURATOR — "Bosh sahifa" (eski `teacher.html` dagi `#dash` bo'limi).
 *
 * Sarlavha, izoh va "Bugun" tabletkasi eski ilovadan AYNAN olingan; jadval
 * mantiqi `features/teacher-home` da — sahifa faqat yig'adi.
 */
</script>

<template>
  <div>
    <PageHeader
      title="Bosh sahifa"
      subtitle="Kelgusi darslar jadvali — eng yaqini birinchi"
    />
    <TodayPill />
    <UpcomingLessonsCard />
  </div>
</template>
