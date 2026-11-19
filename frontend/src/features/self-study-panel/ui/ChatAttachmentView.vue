<script setup lang="ts">
import { onBeforeUnmount, ref } from 'vue'

import { fetchSelfStudyChatAttachment } from '@/entities/self-study-panel'
import { toUserMessage } from '@/shared/api'
import type { DirectMessageAttachmentDto } from '@/shared/types'
import { AppIcon } from '@/shared/ui'

/**
 * Nazorat ko'rinishidagi bitta biriktirma. Fayl FAQAT bosilganda olinadi:
 * har ochish server tomonida auditga yoziladi, ya'ni yozishmani varaqlash
 * barcha rasm/ovozlarni "ochilgan" qilib qo'ymasligi kerak.
 */
const props = defineProps<{ attachment: DirectMessageAttachmentDto }>()

const url = ref<string | null>(null)
const loading = ref(false)
const error = ref<string | null>(null)

async function open(): Promise<void> {
  if (url.value !== null || loading.value) return
  loading.value = true
  error.value = null
  try {
    const file = await fetchSelfStudyChatAttachment(props.attachment.id)
    url.value = URL.createObjectURL(file.blob)
    if (props.attachment.kind === 'Document') {
      const link = document.createElement('a')
      link.href = url.value
      link.download = props.attachment.fileName ?? file.fileName
      link.click()
    }
  } catch (err) {
    error.value = toUserMessage(err)
  } finally {
    loading.value = false
  }
}

onBeforeUnmount(() => {
  if (url.value !== null) URL.revokeObjectURL(url.value)
})

const label = {
  Image: 'Rasm',
  Audio: 'Ovozli xabar',
  Document: 'Hujjat',
} as const
</script>

<template>
  <div class="mt-1.5">
    <img
      v-if="url !== null && attachment.kind === 'Image'"
      :src="url"
      :alt="attachment.fileName ?? 'Rasm'"
      class="max-h-64 rounded-lg border border-line"
    >
    <audio
      v-else-if="url !== null && attachment.kind === 'Audio'"
      :src="url"
      controls
      class="w-full max-w-xs"
    />
    <button
      v-else
      type="button"
      class="inline-flex items-center gap-1.5 rounded-lg border border-line bg-ink-800 px-2.5 py-1.5 text-xs font-semibold text-slate-200 transition-colors hover:bg-ink-700"
      :disabled="loading"
      @click="open"
    >
      <AppIcon
        :name="attachment.kind === 'Audio' ? 'mic' : attachment.kind === 'Image' ? 'image' : 'note'"
        :size="14"
      />
      {{ loading ? 'Yuklanmoqda…' : label[attachment.kind] }}
      <span
        v-if="attachment.durationSec !== null"
        class="text-dim"
        v-text="`${attachment.durationSec} s`"
      />
    </button>
    <p
      v-if="error !== null"
      class="mt-1 text-[11px] text-rose-400"
      v-text="error"
    />
  </div>
</template>
