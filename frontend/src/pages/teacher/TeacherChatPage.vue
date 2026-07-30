<script setup lang="ts">
import { computed } from 'vue'

import { useAuthStore } from '@/features/auth/model/auth.store'
import { TeacherInbox } from '@/features/teacher-inbox'
import { PageHeader } from '@/shared/ui'

/**
 * "Savollar" — eski `teacher.html` dagi `#dm-hub` ("O'quvchilar savollari").
 *
 * ★ SERVER QOIDASI (bu yerda TAKRORLANMAYDI, faqat TUSHUNTIRILADI): shaxsiy
 * yozishma KURATOR ↔ O'QUVCHI juftligi uchun. `CuratorDirectory` xodimning
 * suhbatlarini `AssistantId` bo'yicha (yoki uning kurator guruhiga
 * bog'langan guruhlar bo'yicha) tanlaydi — ustoz `TeacherId` sifatida bu
 * ro'yxatga KIRMAYDI va bo'sh massiv oladi (403 emas).
 *
 * Eski ilovada ham shunday edi: "Savollar" bandi faqat
 * `{% if user.role == 'assistant' %}` shartida ko'rinardi. v2 menyusi uni
 * ustozga ham ko'rsatadi (`entities/user/model/navigation.ts` — bu vazifada
 * tegilmaydigan fayl), shuning uchun bo'sh holat SABABINI aytadi.
 */
const auth = useAuthStore()

const emptyHint = computed(() =>
  auth.role === 'Teacher'
    ? 'Shaxsiy yozishmani kurator olib boradi. Guruhingizga kurator biriktirilgach, o‘quvchilar savoli unga tushadi.'
    : 'Guruhingizga o‘quvchi qo‘shilgach, u shu yerda ko‘rinadi.',
)
</script>

<template>
  <div>
    <PageHeader
      title="O‘quvchilar savollari"
      subtitle="Har o‘quvchi bilan alohida suhbat. O‘quvchi kurs darsidan savol yozsa, shu yerda ko‘rinadi."
    />
    <TeacherInbox :empty-hint="emptyHint" />
  </div>
</template>
