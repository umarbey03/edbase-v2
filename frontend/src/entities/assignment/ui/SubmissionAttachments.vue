<script setup lang="ts">
import { computed } from 'vue'

import type { SubmissionFileDto } from '@/shared/types'

import { groupAttachments } from '../model/types'

import SubmissionAttachment from './SubmissionAttachment.vue'

/**
 * Ishga biriktirilgan fayllar ro'yxati — ovoz, rasm, hujjat bo'limlariga
 * ajratilgan holda (eski ilovadagi "Ovozli javob" / "Rasm javob" sarlavhalari).
 *
 * ALOHIDA KOMPONENT, chunki fayllarni IKKI joy ko'rsatadi: tekshirish navbati
 * va baholash oynasi. Ular bir-birini import qila olmaydi (ikkalasi ham
 * `features` qatlamida), shuning uchun umumiy ko'rinish shu yerda —
 * `Submission` obyektining o'z qatlamida — yashaydi.
 */
const props = withDefaults(
  defineProps<{
    files: readonly SubmissionFileDto[]
    /** `SubmissionAttachment` dagi izohga qarang — oyna ichida `false`. */
    zoomable?: boolean
  }>(),
  { zoomable: true },
)

/** Rasmni to'liq ko'rish so'rovi yuqoriga uzatiladi (oynani ota komponent chizadi). */
const emit = defineEmits<{ zoom: [url: string] }>()

const groups = computed(() => groupAttachments(props.files))
</script>

<template>
  <div class="space-y-3">
    <section
      v-for="group in groups"
      :key="group.kind"
    >
      <h3
        class="mb-1.5 text-[11px] font-bold uppercase tracking-wide text-slate-400"
        v-text="group.label"
      />
      <div class="space-y-2">
        <SubmissionAttachment
          v-for="file in group.items"
          :key="file.id"
          :file="file"
          :zoomable="props.zoomable"
          @zoom="(url) => emit('zoom', url)"
        />
      </div>
    </section>
  </div>
</template>
