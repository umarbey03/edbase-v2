<script setup lang="ts">
import { computed } from 'vue'
import { useRoute, useRouter } from 'vue-router'

import { StudentLessonBoard } from '@/features/self-study-curator'
import { AppIcon, PageHeader } from '@/shared/ui'

/**
 * Kurator: bitta Mustaqil o'quvchining darslari va qarorlar (2026-11-18).
 * Ruxsatni server tekshiradi — begona o'quvchi uchun 403 keladi.
 */
const route = useRoute()
const router = useRouter()
const studentId = computed(() => Number(route.params.studentId))
</script>

<template>
  <div class="space-y-4">
    <button
      type="button"
      class="inline-flex items-center gap-1 text-sm text-slate-400 hover:text-slate-200"
      @click="router.push({ name: 'teacher-self-study' })"
    >
      <AppIcon
        name="chevron-left"
        :size="16"
      />
      Mustaqil o‘quvchilar
    </button>
    <PageHeader
      title="O‘quvchi darslari"
      subtitle="Ish to‘liq bajarilgach «O‘zlashtirdi» ni bosing — keyingi dars shundagina ochiladi"
    />
    <StudentLessonBoard
      :key="studentId"
      :student-id="studentId"
    />
  </div>
</template>
