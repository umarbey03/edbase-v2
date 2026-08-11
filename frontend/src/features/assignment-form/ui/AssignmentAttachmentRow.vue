<script setup lang="ts">
import { computed, ref } from 'vue'

import { attachmentKindLabel, fetchAssignmentAttachmentFile } from '@/entities/assignment'
import { assetDurationLabel } from '@/entities/course'
import { useProtectedBlobUrl } from '@/features/lesson-media'
import { saveBlob } from '@/shared/lib/download'
import { formatFileSize } from '@/shared/lib/text'
import type { AssignmentAttachmentDto } from '@/shared/types'
import { AppIcon, BaseButton, BaseSpinner, IconButton } from '@/shared/ui'
import type { IconName } from '@/shared/ui'

/**
 * VAZIFA SHARTIGA biriktirilgan BITTA fayl qatori.
 *
 * ★ FAYL FAQAT OCHILGANDA so'raladi (`opened`): vazifada 10 ta biriktirma
 * bo'lishi mumkin va ularning hammasini ro'yxat ochilishi bilan yuklash
 * bir necha o'n megabaytni behuda tortardi. `useProtectedBlobUrl` ning
 * `id()` funksiyasi yopiq holatda `null` qaytaradi — ya'ni so'rov UMUMAN
 * yuborilmaydi.
 *
 * ★ HIMOYALANGAN FAYL: endpoint `Authorization` talab qiladi, brauzer esa
 * `<img src>`/`<audio src>` da uni yubormaydi — shuning uchun Blob yo'li
 * (mexanizm `features/lesson-media/lib/useProtectedBlobUrl.ts` da, ikki
 * joyda takrorlanmaydi).
 *
 * ⚠️ `assetDurationLabel` KURS entity'sidan olinadi: davomiylikni
 * formatlash qoidasi butun platformada BITTA bo'lishi kerak (ikkinchi
 * nusxa "1:05" va "65 s" degan ikki xil ko'rinishga olib kelardi).
 */
const props = defineProps<{
  attachment: AssignmentAttachmentDto
  /** O'chirish so'rovi ketmoqdami (tugmada spinner). */
  deleting: boolean
}>()

const emit = defineEmits<{ delete: [] }>()

const opened = ref(false)

const preview = useProtectedBlobUrl(
  'assignment-attachment',
  () => (opened.value ? props.attachment.id : null),
  fetchAssignmentAttachmentFile,
)

const KIND_ICONS: Record<string, IconName> = {
  Image: 'image',
  Audio: 'mic',
  Document: 'paperclip',
}

const icon = computed<IconName>(() => KIND_ICONS[props.attachment.kind] ?? 'paperclip')
const isImage = computed(() => props.attachment.kind === 'Image')
const isAudio = computed(() => props.attachment.kind === 'Audio')

function download(): void {
  const blob = preview.blob.value
  if (blob === null) return
  saveBlob(blob, preview.fileName.value)
}
</script>

<template>
  <li class="rounded-lg border border-line bg-ink-850 p-2.5">
    <div class="flex items-center gap-3">
      <span
        class="shrink-0 text-slate-500"
        aria-hidden="true"
      >
        <AppIcon
          :name="icon"
          :size="16"
        />
      </span>

      <div class="min-w-0 flex-1">
        <p
          class="truncate text-[13px] font-medium text-slate-200"
          v-text="attachmentKindLabel(props.attachment.kind)"
        />
        <p class="mt-0.5 text-[11px] tabular-nums text-dim">
          {{ formatFileSize(props.attachment.sizeBytes) }}
          <template v-if="isAudio">
            · {{ assetDurationLabel(props.attachment.durationSec) }}
          </template>
          · {{ props.attachment.contentType }}
        </p>
      </div>

      <!-- 🔴 `gap-3` — 24-tuzoq (`tap-expand` maydonlari ustma-ust tushmasin). -->
      <div class="flex shrink-0 items-center gap-3">
        <IconButton
          icon="eye"
          :label="opened ? 'Yopish' : 'Ochish'"
          size="sm"
          :active="opened"
          @click="opened = !opened"
        />
        <IconButton
          icon="trash"
          label="Biriktirmani o‘chirish"
          size="sm"
          tone="danger"
          :loading="props.deleting"
          @click="emit('delete')"
        />
      </div>
    </div>

    <div
      v-if="opened"
      class="mt-2.5"
    >
      <div
        v-if="preview.isPending.value"
        class="flex min-h-16 items-center justify-center gap-2 text-xs text-slate-400"
      >
        <BaseSpinner size="sm" />
        Fayl yuklanmoqda…
      </div>

      <div
        v-else-if="preview.errorMessage.value !== null"
        class="rounded-lg border border-rose-500/25 bg-rose-500/10 px-3 py-2.5 text-center"
        role="alert"
      >
        <p
          class="text-[11px] text-rose-200"
          v-text="preview.errorMessage.value"
        />
        <BaseButton
          class="mt-2"
          size="sm"
          variant="secondary"
          :loading="preview.isFetching.value"
          @click="preview.refetch()"
        >
          Qayta urinish
        </BaseButton>
      </div>

      <template v-else-if="preview.url.value !== null">
        <img
          v-if="isImage"
          :src="preview.url.value"
          alt="Shart rasmi"
          class="max-h-72 w-full rounded-lg border border-line bg-ink-950 object-contain"
        >
        <audio
          v-else-if="isAudio"
          class="w-full"
          controls
          preload="metadata"
          :src="preview.url.value"
        />
        <!--
          Hujjat (PDF) ICHKI KO'RINISHDA ochilmaydi: `<iframe>` ichida Blob
          PDF ba'zi brauzerlarda bloklanadi va bo'sh oq maydon bo'lib
          qolardi. Yuklab olish — bir ma'noli yo'l.
        -->
        <BaseButton
          v-else
          size="sm"
          variant="secondary"
          @click="download"
        >
          <template #icon>
            <AppIcon
              name="download"
              :size="13"
            />
          </template>
          Yuklab olish
        </BaseButton>
      </template>
    </div>
  </li>
</template>
