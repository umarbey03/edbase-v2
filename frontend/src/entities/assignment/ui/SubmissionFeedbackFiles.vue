<script setup lang="ts">
import type { SubmissionFeedbackFileDto } from '@/shared/types'

import SubmissionFeedbackFile from './SubmissionFeedbackFile.vue'

/**
 * R37 · ustoz tekshirishda biriktirgan fayllar RO'YXATI.
 *
 * ALOHIDA KOMPONENT, chunki uni IKKI `features` moduli ko'rsatadi va ular
 * bir-birini import qila olmaydi (`SubmissionAttachments` bilan AYNI
 * sabab):
 *
 *   • ustozda — baholash oynasi (`features/grading`): o'chirish tugmasi
 *     BILAN;
 *   • o'quvchida — "Vazifalarim" sahifasi: FAQAT ko'rish.
 *
 * ★ `SubmissionAttachments` DAN FARQI — TURKUMLARGA AJRATILMAYDI. U yerda
 * "Ovozli javob" / "Rasm javob" sarlavhalari eski ilovadan ko'chirilgan va
 * o'quvchi bir vazifaga 5 tagacha fayl qo'yadi. Ustozning tekshiruvi esa
 * odatda BITTA fayl (tuzatilgan varaq yoki PDF sharh) — bir elementli
 * ro'yxat ustidagi turkum sarlavhasi faqat shovqin bo'lardi.
 */
const props = defineProps<{
  files: readonly SubmissionFeedbackFileDto[]
  /** Ustozda `true`. O'quvchida HECH QACHON berilmaydi. */
  canDelete?: boolean
  /** Rasm bosilganda kattalashtirilsinmi. */
  zoomable?: boolean
  /** Hozir o'chirilayotgan fayl (tugmani bloklash uchun). */
  deletingId?: number | null
}>()

const emit = defineEmits<{ zoom: [url: string]; remove: [fileId: number] }>()
</script>

<template>
  <div class="space-y-2">
    <SubmissionFeedbackFile
      v-for="file in props.files"
      :key="file.id"
      :file="file"
      :can-delete="props.canDelete ?? false"
      :zoomable="props.zoomable ?? true"
      :deleting="props.deletingId === file.id"
      @zoom="(url) => emit('zoom', url)"
      @remove="() => emit('remove', file.id)"
    />
  </div>
</template>
