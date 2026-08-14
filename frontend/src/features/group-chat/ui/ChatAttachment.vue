<script setup lang="ts">
import { computed } from 'vue'

import { fetchGroupChatAttachment } from '@/entities/group-chat'
import { useProtectedBlobUrl } from '@/features/lesson-media'
import { saveBlob } from '@/shared/lib/download'
import { formatFileSize } from '@/shared/lib/text'
import type { GroupChatAttachmentDto } from '@/shared/types'
import { AppIcon, BaseSpinner } from '@/shared/ui'

/**
 * Chat xabariga biriktirilgan BITTA fayl (R16b).
 *
 * ★ FAYL HIMOYALANGAN: `GET /group-chat/attachments/{id}` `Authorization`
 * talab qiladi, brauzer esa `<img src>` va `<audio src>` so'rovlarida uni
 * yubormaydi. Shuning uchun mazmun `Blob` sifatida olinadi va
 * `URL.createObjectURL` bilan ko'rsatiladi.
 *
 * ★ NAQSH QAYTA ISHLATILADI (`useProtectedBlobUrl`), NUSXALANMAYDI: u
 * `revokeObjectURL` ni skoup yo'q qilinganda va fayl almashganda O'ZI
 * chaqiradi. Chatda bu MUHIMROQ, chunki xabarlar ro'yxati uzun bo'ladi:
 * har rasm 5 MB bo'lsa, tozalashsiz uzoq suhbat brauzerda yuzlab megabayt
 * ushlab turardi.
 *
 * ★ PUFAKCHA ICHIDA TURADI, shuning uchun o'z ramkasi/foni YO'Q —
 * `SubmissionAttachment` dan farqi shu (u kartochka ichida yashaydi).
 */
const props = withDefaults(
  defineProps<{
    attachment: GroupChatAttachmentDto
    /**
     * Rasm bosilganda kattalashtirilsinmi.
     *
     * ★ SUKUT — `true`. Ichma-ich `BaseModal` endi XAVFSIZ: `useModalHost`
     * ESC uchun QATLAM STEKINI yuritadi va faqat eng tepadagi oynani
     * yopadi (2026-08-11 refaktori). Ilgari bu mumkin emas edi.
     */
    zoomable?: boolean
  }>(),
  { zoomable: true },
)

const emit = defineEmits<{ zoom: [url: string] }>()

const file = useProtectedBlobUrl(
  'group-chat-attachment',
  () => props.attachment.id,
  (id, options) => fetchGroupChatAttachment(id, options),
)

const isImage = computed(() => props.attachment.kind === 'Image')
const isAudio = computed(() => props.attachment.kind === 'Audio')

/**
 * Ko'rsatiladigan nom.
 *
 * Server bergan nom USTUN (u tozalangan va foydalanuvchi bergan), keyin
 * `Content-Disposition` dan olingani. Ikkalasi ham bo'lmasa — turdan
 * kelib chiqqan zaxira.
 */
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
  <div class="mt-1 first:mt-0">
    <div
      v-if="file.isPending.value"
      class="flex items-center gap-2 rounded-lg bg-black/10 px-2.5 py-3 text-[11px]"
      :class="'text-current opacity-70'"
    >
      <BaseSpinner size="sm" />
      Yuklanmoqda…
    </div>

    <!--
      Xato — pufakcha ichida QISQA qatorda. To'liq `DataStatus` bu yerda
      o'rinsiz: chat qatorida u xabarning o'zidan katta bo'lib ketardi.
    -->
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
      <!-- RASM: bosilsa kattalashadi (lightbox ota komponentda chiziladi). -->
      <button
        v-if="isImage && props.zoomable"
        type="button"
        class="block w-full cursor-zoom-in overflow-hidden rounded-lg"
        title="Kattalashtirish"
        @click="openZoom"
      >
        <img
          :src="file.url.value"
          :alt="displayName"
          class="max-h-64 w-full bg-black/10 object-contain"
        >
      </button>

      <img
        v-else-if="isImage"
        :src="file.url.value"
        :alt="displayName"
        class="max-h-64 w-full rounded-lg bg-black/10 object-contain"
      >

      <audio
        v-else-if="isAudio"
        class="w-full"
        controls
        preload="metadata"
        :src="file.url.value"
      />

      <!--
        HUJJAT: nomi + hajmi + yuklab olish. `<a download>` ISHLATILMAYDI —
        `saveBlob` loyihadagi yagona saqlash yo'li va u Safari'dagi
        `objectURL` nozikliklarini allaqachon hal qilgan.
      -->
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
