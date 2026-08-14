<script setup lang="ts">
import { TeacherSessionsTable } from '@/features/teacher-sessions'
import { PageHeader } from '@/shared/ui'
import { SessionBoard } from '@/widgets/session-board'

/**
 * ═══════════════════════════════════════════════════════════════════════
 * R31 (2026-08-13) — SAHIFA IKKI QATLAMLI BO'LDI
 * ═══════════════════════════════════════════════════════════════════════
 *
 * Loyiha egasi: *"darslarim bo'limida jadval ma'lumoti sifatida nechta
 * student borligi, nechta qatnashganligi, davomiyligi"*.
 *
 * ★ `SessionBoard` O'ZGARMADI va YUQORIDA QOLDI. U o'quv bo'limining
 * `ManageSessionsPage` sahifasi bilan BO'LISHILADI — uni parametrlashtirish
 * akademik ko'rinishni ham o'zgartirardi. Bundan tashqari u BOSHQA savolga
 * javob beradi: "hozir nima qilay?" (jonli / yaqinda / o'tgan kartochkalari,
 * darsga kirish tugmasi). Bu oqim ustozning asosiy amali va unga tegilmadi.
 *
 * Yangi jadval esa "darslarim QANDAY o'tdi?" degan savolga javob beradi va
 * mustaqil endpointdan (`GET /live-sessions/stats`) oziqlanadi. Ikki savol —
 * ikki komponent; backendda ham AYNI ajratish qilingan (`SessionStatsDto`
 * `LiveSessionDto` dan alohida tur).
 */
</script>

<template>
  <div>
    <PageHeader
      title="Darslarim"
      subtitle="Siz olib boradigan jonli darslar. Boshlash/yakunlash dars sahifasida."
    />
    <SessionBoard />
    <TeacherSessionsTable />
  </div>
</template>
