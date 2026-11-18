<script setup lang="ts">
import { computed } from 'vue'
import { useRouter } from 'vue-router'

import { isManagerRole } from '@/entities/user'
import { useAuthStore } from '@/features/auth/model/auth.store'
import { MySelfStudyStudents, UnassignedSelfStudyStudents } from '@/features/self-study-curator'
import { PageHeader } from '@/shared/ui'

/**
 * "MUSTAQIL O'QUVCHILAR" (2026-11-18).
 *
 * Kurator: o'ziga biriktirilgan Mustaqil o'quvchilar va tekshiruv kutayotgan
 * ishlar. O'quv bo'limi/admin: qo'shimcha ravishda kuratorsiz qolgan
 * o'quvchilar — ular shu yerning o'zida biriktiriladi.
 */
const auth = useAuthStore()
const router = useRouter()
const isManager = computed(() => isManagerRole(auth.role ?? ''))
</script>

<template>
  <div class="space-y-6">
    <PageHeader
      title="Mustaqil o‘quvchilar"
      subtitle="Vazifani tekshirasiz, savollariga javob berasiz va darsni «o‘zlashtirdi» deb belgilaysiz"
    />
    <UnassignedSelfStudyStudents v-if="isManager" />
    <MySelfStudyStudents @open="(row) => router.push({ name: 'teacher-self-study-student', params: { studentId: row.studentId } })" />
  </div>
</template>
