<script setup lang="ts">
import { useQuery } from '@tanstack/vue-query'
import { computed, onBeforeUnmount, ref, watch } from 'vue'

import { saveBlob } from '@/shared/lib/download'
import { formatFileSize } from '@/shared/lib/text'
import type { SubmissionFileDto } from '@/shared/types'
import { AppIcon, BaseButton, BaseSpinner } from '@/shared/ui'

import { fetchSubmissionFile } from '../api/assignment-api'
import { submissionFileError } from '../model/types'

/**
 * O'quvchi biriktirgan BITTA fayl: rasm ko'rinishi, audio pleyer yoki
 * yuklab olish tugmasi.
 *
 * ★ FAYL HIMOYALANGAN. `GET /submissions/files/{id}` `Authorization` talab
 * qiladi, brauzer esa `<img src>` va `<audio src>` so'rovlarida uni
 * yubormaydi. Shuning uchun mazmun Blob sifatida olinadi va `objectUrl`
 * yasaladi (`api/assignment-api.ts` dagi izohga qarang).
 *
 * ★ XOTIRA. `URL.createObjectURL` bilan yaratilgan manzil brauzerda Blob'ni
 * USHLAB TURADI va sahifa yopilgunicha o'zi bo'shalmaydi. Ustoz navbatda 50
 * ta ishni ketma-ket ko'radi — har biri 5 MB rasm bo'lsa, tozalashsiz seans
 * oxirida brauzer yuzlab megabayt ushlagan bo'lardi. Manzil SHU komponentga
 * tegishli va u yo'q qilinganda (`onBeforeUnmount`) hamda fayl
 * almashtirilganda darhol bekor qilinadi.
 *
 * `objectKey` (ombordagi ichki kalit) UI'da UMUMAN ko'rsatilmaydi — u
 * infratuzilma tafsiloti va ustozga hech narsa aytmaydi.
 */
const props = withDefaults(
  defineProps<{
    file: SubmissionFileDto
    /**
     * Rasm bosilganda kattalashtirilsinmi.
     *
     * Oyna ICHIDA (`BaseModal`) `false` bo'ladi: ikkinchi oynani ustiga
     * ochsak, `Esc` ikkala qatlamni ham yopardi — ikkala `BaseModal` ham
     * `document` da bitta tinglovchi qo'yadi va biri ikkinchisini to'sa
     * olmaydi. Kattalashtirish tekshirish navbatida (to'liq ekranda) bor.
     */
    zoomable?: boolean
  }>(),
  { zoomable: true },
)

/** Rasmni to'liq ko'rish uchun ota komponentga uzatiladi. */
const emit = defineEmits<{ zoom: [url: string] }>()

/*
  Blob TanStack Query keshida yashaydi: bitta ish ikki joyda ochilsa
  (navbat va baholash oynasi) fayl QAYTA yuklanmaydi.

  `staleTime: Infinity` — fayl mazmuni o'zgarmaydi, uni qayta so'rash
  mantiqsiz. `gcTime` esa ATAYLAB qisqa (2 daqiqa): kesh Blob'larni ushlab
  turadi, ya'ni uzoq umr xotira sarfi demakdir.
*/
const query = useQuery({
  queryKey: computed(() => ['submission-file', props.file.id]),
  queryFn: ({ signal }) => fetchSubmissionFile(props.file.id, { signal }),
  staleTime: Number.POSITIVE_INFINITY,
  gcTime: 2 * 60_000,
})

const objectUrl = ref<string | null>(null)
/** Rasm yuklandi, lekin brauzer uni chiza olmadi (buzilgan yoki qo'llab-quvvatlanmaydigan format). */
const renderFailed = ref(false)

function release(): void {
  if (objectUrl.value === null) return
  URL.revokeObjectURL(objectUrl.value)
  objectUrl.value = null
}

watch(
  () => query.data.value,
  (downloaded) => {
    // Eski manzil AVVAL bekor qilinadi: aks holda navbatda keyingi ishga
    // o'tganda oldingi faylning Blob'i xotirada qolib ketardi.
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

const fileName = computed(() => query.data.value?.fileName ?? `fayl-${props.file.id}`)

function download(): void {
  const downloaded = query.data.value
  if (downloaded === undefined) return
  saveBlob(downloaded.blob, downloaded.fileName)
}

function openZoom(): void {
  if (objectUrl.value !== null) emit('zoom', objectUrl.value)
}

/* ------------------------------------------------------------------ audio */

const audioElement = ref<HTMLAudioElement | null>(null)

/**
 * Eshitish tezligi — eski ilovadagi `qv-rate` tugmalari (0.75x…1.5x).
 * Talaffuzni baholayotgan ustoz sekinlashtirib tinglaydi, tanish javobni esa
 * tezlatib o'tkazadi. Tezlik `playbackRate` orqali beriladi: fayl QAYTA
 * yuklanmaydi.
 */
const RATES = [0.75, 1, 1.25, 1.5] as const
const rate = ref<number>(1)

function setRate(value: number): void {
  rate.value = value
  const element = audioElement.value
  if (element !== null) element.playbackRate = value
}
</script>

<template>
  <div class="rounded-lg border border-line bg-ink-950 p-3">
    <div class="mb-2 flex flex-wrap items-center justify-between gap-2">
      <p class="min-w-0 flex-1 truncate text-xs text-slate-300">
        <span v-text="fileName" />
        <span
          class="ml-1.5 tabular-nums text-dim"
          v-text="formatFileSize(props.file.sizeBytes)"
        />
      </p>
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
    </div>

    <div
      v-if="query.isPending.value"
      class="flex min-h-24 items-center justify-center gap-2 text-xs text-slate-400"
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
      <!--
        Rasm bosilsa kattalashadi (eski ilovadagi `qv-lb` lightbox'i).
        `@error` — server yuborgan bayt haqiqiy rasm bo'lmasa bo'sh joy
        emas, tushunarli xabar va yuklab olish tugmasi qolsin.
      -->
      <button
        v-if="isImage && props.zoomable"
        type="button"
        class="block w-full cursor-zoom-in overflow-hidden rounded-lg border border-line"
        title="Kattalashtirish"
        @click="openZoom"
      >
        <img
          :src="objectUrl"
          :alt="fileName"
          class="max-h-80 w-full bg-ink-900 object-contain"
          @error="renderFailed = true"
        >
      </button>

      <img
        v-else-if="isImage"
        :src="objectUrl"
        :alt="fileName"
        class="max-h-80 w-full rounded-lg border border-line bg-ink-900 object-contain"
        @error="renderFailed = true"
      >

      <div v-else-if="isAudio">
        <audio
          ref="audioElement"
          class="w-full"
          controls
          preload="metadata"
          :src="objectUrl"
        />
        <div class="mt-2 flex flex-wrap gap-1.5">
          <BaseButton
            v-for="value in RATES"
            :key="value"
            size="sm"
            :variant="rate === value ? 'primary' : 'secondary'"
            @click="setRate(value)"
          >
            {{ value }}x
          </BaseButton>
        </div>
      </div>

      <p
        v-else
        class="text-xs text-slate-400"
      >
        Bu faylni bu yerda ko‘rsatib bo‘lmaydi — yuklab olib oching.
      </p>
    </template>
  </div>
</template>
