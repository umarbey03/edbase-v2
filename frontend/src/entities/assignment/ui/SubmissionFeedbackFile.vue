<script setup lang="ts">
import { useQuery } from '@tanstack/vue-query'
import { computed, onBeforeUnmount, ref, watch } from 'vue'

import { saveBlob } from '@/shared/lib/download'
import { formatFileSize } from '@/shared/lib/text'
import type { SubmissionFeedbackFileDto } from '@/shared/types'
import { AppIcon, BaseButton, BaseSpinner } from '@/shared/ui'

import { fetchSubmissionFeedbackFile } from '../api/assignment-api'
import { submissionFileError } from '../model/types'

/**
 * ========================================================================
 * R37 · USTOZ TEKSHIRISHDA BIRIKTIRGAN BITTA FAYL
 * ========================================================================
 *
 * 🔴 `SubmissionAttachment.vue` QAYTA ISHLATILMADI, garchi ko'rinishi
 * deyarli bir xil bo'lsa ham. Sabab: u `SubmissionFileDto` (O'QUVCHINING
 * javobi) bilan ishlaydi va uning yo'li boshqa
 * (`/submissions/files/{id}`). Ikkalasini bittaga birlashtirish uchun
 * komponentga "qaysi endpoint" degan bayroq berish kerak bo'lardi — ya'ni
 * RUXSAT QOIDASI IKKI XIL bo'lgan ikki resurs bitta kodga bog'lanib
 * qolardi va bir kuni bayroqni noto'g'ri berish begona faylni ochardi.
 * Backend tomonida ham aynan shu sabab bilan alohida jadval tanlangan
 * (`SubmissionFeedbackFile` izohi).
 *
 * ★ FAYL HIMOYALANGAN: endpoint `Authorization` talab qiladi, brauzer esa
 * `<img src>` / `<audio src>` da uni yubormaydi — mazmun `Blob` sifatida
 * olinadi.
 *
 * ★ XOTIRA: `createObjectURL` Blob'ni ushlab turadi. Manzil fayl
 * almashganda va komponent yo'q qilinganda DARHOL bekor qilinadi.
 */
const props = withDefaults(
  defineProps<{
    file: SubmissionFeedbackFileDto
    /** Ustozda `true` — o'chirish tugmasi chiziladi. O'quvchida HECH QACHON. */
    canDelete?: boolean
    /** Rasm bosilganda kattalashtirilsinmi. */
    zoomable?: boolean
    /** O'chirish davom etyapti (tugma bloklanadi). */
    deleting?: boolean
  }>(),
  { canDelete: false, zoomable: true, deleting: false },
)

const emit = defineEmits<{ zoom: [url: string]; remove: [] }>()

const query = useQuery({
  queryKey: computed(() => ['submission-feedback-file', props.file.id]),
  queryFn: ({ signal }) => fetchSubmissionFeedbackFile(props.file.id, { signal }),
  staleTime: Number.POSITIVE_INFINITY,
  gcTime: 2 * 60_000,
})

const objectUrl = ref<string | null>(null)
/** Blob keldi, lekin brauzer chiza olmadi (buzilgan yoki qo'llanmaydigan format). */
const renderFailed = ref(false)

function release(): void {
  if (objectUrl.value === null) return
  URL.revokeObjectURL(objectUrl.value)
  objectUrl.value = null
}

watch(
  () => query.data.value,
  (downloaded) => {
    // Eski manzil AVVAL bekor qilinadi — aks holda oldingi faylning Blob'i
    // xotirada qolib ketardi.
    release()
    renderFailed.value = false
    if (downloaded !== undefined) objectUrl.value = URL.createObjectURL(downloaded.blob)
  },
  { immediate: true },
)

onBeforeUnmount(release)

const errorMessage = computed(() =>
  query.error.value !== null ? submissionFileError(query.error.value) : null,
)

const isImage = computed(() => props.file.kind === 'Image' && !renderFailed.value)
const isAudio = computed(() => props.file.kind === 'Audio')

/** Ustoz bergan nom USTUN — u tozalangan va foydalanuvchiga tanish. */
const displayName = computed(
  () => props.file.fileName ?? query.data.value?.fileName ?? `fayl-${props.file.id}`,
)

function download(): void {
  const downloaded = query.data.value
  if (downloaded === undefined) return
  saveBlob(downloaded.blob, downloaded.fileName)
}

function openZoom(): void {
  if (objectUrl.value !== null) emit('zoom', objectUrl.value)
}
</script>

<template>
  <div class="rounded-lg border border-line bg-ink-950 p-3">
    <div class="mb-2 flex flex-wrap items-center justify-between gap-2">
      <p class="min-w-0 flex-1 truncate text-xs text-slate-300">
        <span v-text="displayName" />
        <span
          class="ml-1.5 tabular-nums text-dim"
          v-text="formatFileSize(props.file.sizeBytes)"
        />
      </p>
      <div class="flex shrink-0 items-center gap-1.5">
        <BaseButton
          size="sm"
          variant="ghost"
          :disabled="query.data.value === undefined"
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
        <BaseButton
          v-if="props.canDelete"
          size="sm"
          variant="ghost"
          :loading="props.deleting"
          @click="emit('remove')"
        >
          <template #icon>
            <AppIcon
              name="trash"
              :size="13"
            />
          </template>
          O‘chirish
        </BaseButton>
      </div>
    </div>

    <div
      v-if="query.isPending.value"
      class="flex min-h-16 items-center justify-center gap-2 text-xs text-slate-400"
    >
      <BaseSpinner size="sm" />
      Fayl yuklanmoqda…
    </div>

    <div
      v-else-if="errorMessage !== null"
      class="rounded-lg border border-rose-500/25 bg-rose-500/10 px-3 py-3 text-center"
      role="alert"
    >
      <p
        class="text-xs text-rose-200"
        v-text="errorMessage"
      />
      <BaseButton
        class="mt-2.5"
        size="sm"
        variant="secondary"
        :loading="query.isFetching.value"
        @click="query.refetch()"
      >
        <template #icon>
          <AppIcon
            name="refresh"
            :size="13"
          />
        </template>
        Qayta urinish
      </BaseButton>
    </div>

    <template v-else-if="objectUrl !== null">
      <button
        v-if="isImage && props.zoomable"
        type="button"
        class="block w-full cursor-zoom-in overflow-hidden rounded-lg border border-line"
        title="Kattalashtirish"
        @click="openZoom"
      >
        <img
          :src="objectUrl"
          :alt="displayName"
          class="max-h-80 w-full bg-ink-900 object-contain"
          @error="renderFailed = true"
        >
      </button>

      <img
        v-else-if="isImage"
        :src="objectUrl"
        :alt="displayName"
        class="max-h-80 w-full rounded-lg border border-line bg-ink-900 object-contain"
        @error="renderFailed = true"
      >

      <audio
        v-else-if="isAudio"
        class="w-full"
        controls
        preload="metadata"
        :src="objectUrl"
      />

      <p
        v-else
        class="text-xs text-slate-400"
      >
        Bu faylni bu yerda ko‘rsatib bo‘lmaydi — yuklab olib oching.
      </p>
    </template>
  </div>
</template>
