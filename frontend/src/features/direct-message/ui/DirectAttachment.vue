<script setup lang="ts">
import { computed } from 'vue'

import { fetchDirectMessageAttachment } from '@/entities/direct-message'
import { useProtectedBlobUrl } from '@/features/lesson-media'
import { saveBlob } from '@/shared/lib/download'
import { formatDuration } from '@/shared/lib/useVoiceRecorder'
import { formatFileSize } from '@/shared/lib/text'
import type { DirectMessageAttachmentDto } from '@/shared/types'
import { AppIcon, BaseSpinner } from '@/shared/ui'

/**
 * Shaxsiy yozishma xabariga biriktirilgan BITTA fayl (2026-08-17) —
 * `features/group-chat/ui/ChatAttachment.vue` bilan AYNI naqsh (sabab
 * o'sha komponent izohida). Ikkinchi nusxa yaratildi, chunki fayl ikkita
 * MUSTAQIL manbadan olinadi (`/group-chat/attachments/{id}` va
 * `/messages/attachments/{id}`) — bitta umumiy komponent ikkalasiga ham
 * fetch funksiyasini PROP qilib berishni talab qilardi, bu esa foydasi
 * yo'q qo'shimcha qatlam bo'lardi (ikkalasi ham kichik va barqaror).
 */
const props = withDefaults(
  defineProps<{
    attachment: DirectMessageAttachmentDto
    zoomable?: boolean
    flush?: boolean
    /** Rasm ustida «Tahrirlash» tugmasi (24/7 chat, 2026-11-18). */
    editable?: boolean
    /** Ovoz ostida «Ovozli izoh» tugmasi — kurator uchun (2026-11-18). */
    reviewable?: boolean
  }>(),
  { zoomable: true, flush: false, editable: false, reviewable: false },
)

const emit = defineEmits<{
  zoom: [url: string]
  edit: [blob: Blob, fileName: string]
  review: [blob: Blob]
}>()

function review(): void {
  const blob = file.blob.value
  if (blob !== null) emit('review', blob)
}

function edit(): void {
  const blob = file.blob.value
  if (blob !== null) emit('edit', blob, displayName.value)
}

const file = useProtectedBlobUrl(
  'direct-message-attachment',
  () => props.attachment.id,
  (id, options) => fetchDirectMessageAttachment(id, options),
)

const isImage = computed(() => props.attachment.kind === 'Image')
const isAudio = computed(() => props.attachment.kind === 'Audio')

const displayName = computed(
  () => props.attachment.fileName ?? file.fileName.value,
)

function download(): void {
  const blob = file.blob.value
  if (blob === null) return
  saveBlob(blob, displayName.value)
}

function openZoom(): void {
  if (file.url.value !== null) emit('zoom', file.url.value)
}
</script>

<template>
  <div :class="props.flush ? '' : 'mt-1 first:mt-0'">
    <div
      v-if="file.isPending.value"
      class="flex items-center gap-2 rounded-lg bg-black/10 px-2.5 py-3 text-[11px]"
      :class="'text-current opacity-70'"
    >
      <BaseSpinner size="sm" />
      Yuklanmoqda…
    </div>

    <button
      v-else-if="file.errorMessage.value !== null"
      type="button"
      class="flex w-full items-center gap-1.5 rounded-lg bg-black/10 px-2.5 py-2 text-left text-[11px] opacity-80"
      @click="file.refetch()"
    >
      <AppIcon
        name="refresh"
        :size="12"
      />
      Fayl ochilmadi — qayta urinish
    </button>

    <template v-else-if="file.url.value !== null">
      <button
        v-if="isImage && props.editable"
        type="button"
        class="mb-1 inline-flex items-center gap-1 rounded-md bg-black/30 px-2 py-1 text-[11px] font-semibold hover:bg-black/40"
        title="Rasm ustida belgilab, qayta yuborish"
        @click="edit"
      >
        <AppIcon
          name="edit"
          :size="12"
        />
        Tahrirlash
      </button>
      <button
        v-if="isImage && props.zoomable"
        type="button"
        class="block w-full cursor-zoom-in overflow-hidden"
        :class="props.flush ? '' : 'rounded-lg'"
        title="Kattalashtirish"
        @click="openZoom"
      >
        <img
          :src="file.url.value"
          :alt="displayName"
          class="max-h-72 w-full bg-ink-900 object-contain"
        >
      </button>

      <img
        v-else-if="isImage"
        :src="file.url.value"
        :alt="displayName"
        class="max-h-72 w-full bg-ink-900 object-contain"
        :class="props.flush ? '' : 'rounded-lg'"
      >

      <div
        v-else-if="isAudio"
        class="flex items-center gap-2"
      >
        <audio
          class="w-full min-w-52"
          controls
          preload="metadata"
          :src="file.url.value"
        />
        <!-- Brauzer yozgan WebM'da davomiylik yo'q — server saqlagan qiymat (2026-11-18). -->
        <span
          v-if="props.attachment.durationSec != null"
          class="shrink-0 text-[11px] tabular-nums opacity-75"
        >{{ formatDuration(props.attachment.durationSec) }}</span>
      </div>
      <button
        v-if="isAudio && props.reviewable"
        type="button"
        class="mt-1 inline-flex items-center gap-1 rounded-md bg-black/30 px-2 py-1 text-[11px] font-semibold hover:bg-black/40"
        title="Ovozni eshitib, kerakli joylarda ovozli izoh qo‘shish"
        @click="review"
      >
        <AppIcon
          name="mic"
          :size="12"
        />
        Ovozli izoh
      </button>

      <button
        v-else
        type="button"
        class="flex w-full items-center gap-2 rounded-lg bg-black/10 px-2.5 py-2 text-left transition-opacity hover:opacity-80"
        @click="download"
      >
        <AppIcon
          name="download"
          :size="16"
        />
        <span class="min-w-0 flex-1">
          <span
            class="block truncate text-[12.5px] font-semibold"
            v-text="displayName"
          />
          <span
            class="block text-[10.5px] tabular-nums opacity-70"
            v-text="formatFileSize(props.attachment.sizeBytes)"
          />
        </span>
      </button>
    </template>
  </div>
</template>
