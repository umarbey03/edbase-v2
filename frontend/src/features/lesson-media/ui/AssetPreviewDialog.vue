<script setup lang="ts">
import { computed } from 'vue'

import { assetDurationLabel, fetchLessonAssetFile } from '@/entities/course'
import { saveBlob } from '@/shared/lib/download'
import { formatDateTime } from '@/shared/lib/datetime'
import { formatFileSize } from '@/shared/lib/text'
import type { LessonAssetDto } from '@/shared/types'
import { AppIcon, BaseButton, BaseModal, BaseSpinner } from '@/shared/ui'

import { useProtectedBlobUrl } from '../lib/useProtectedBlobUrl'

/**
 * DARS FAYLINING KARTOCHKASI.
 *
 * ══════════════════════════════════════════════════════════════════════
 * 🔴 QAROR: VIDEO PLEYERI BU YERDA QURILMADI (ataylab)
 * ══════════════════════════════════════════════════════════════════════
 *
 * Oqim endpointi (`GET /lessons/assets/{id}`) `Authorization` sarlavhasini
 * talab qiladi, brauzer esa `<video src>` da uni YUBORMAYDI (13-bo'lim,
 * 39-tuzoq). Ikki "vaqtinchalik" yechim ko'rildi va IKKALASI HAM rad etildi:
 *
 *  1) `fetch` + `Blob` + `createObjectURL` — vazifa fayllarida ishlaydi,
 *     lekin 1 GB video uchun YARAMAYDI: butun fayl xotiraga tushadi
 *     (brauzer tab'i qulaydi) va `Range` (seek) ma'nosini yo'qotadi —
 *     ya'ni server tomonidagi eng qimmat imkoniyat bekor bo'ladi.
 *  2) tokenni query parametrida yuborish — bu ANIQ vazifa sifatida
 *     boshqa ishda qurilmoqda (`(assetId, userId, exp)` ga bog'langan
 *     qisqa muddatli token). Uni bu yerda "o'zimizcha" yasash ikkita
 *     boshqa-boshqa avtorizatsiya yo'lini tug'dirardi.
 *
 * SHUNING UCHUN video uchun faqat MA'LUMOT ko'rsatiladi (nomi, hajmi,
 * davomiyligi, formati). Ro'yxatdagi "ko'rish" tugmasi video darsda
 * O'CHIQ turadi va sababi `title` da yozilgan.
 *
 * RASM (imtihon varag'i) — KO'RSATILADI: chegarasi 10 MB
 * (`lesson.image_max_mb`), ya'ni Blob yo'li bu yerda xavfsiz va loyihada
 * allaqachon isbotlangan (`useProtectedBlobUrl`).
 */
const props = defineProps<{
  /** `null` — oyna yopiq. */
  asset: LessonAssetDto | null
}>()

const emit = defineEmits<{ close: [] }>()

const isImage = computed(() => props.asset?.kind === 'Image')

/*
  Rasm FAQAT rasm bo'lganda so'raladi: `id()` video uchun `null` qaytaradi,
  ya'ni so'rov umuman yuborilmaydi (`enabled`). Video uchun tasodifan 1 GB
  yuklab olish mumkin bo'lmasligi kerak.
*/
const preview = useProtectedBlobUrl(
  'lesson-asset-file',
  () => (isImage.value ? (props.asset?.id ?? null) : null),
  fetchLessonAssetFile,
)

const rows = computed<ReadonlyArray<{ label: string; value: string }>>(() => {
  const asset = props.asset
  if (asset === null) return []

  const list: Array<{ label: string; value: string }> = [
    { label: 'Turi', value: asset.kind === 'Image' ? 'Rasm' : 'Video' },
    { label: 'Hajmi', value: formatFileSize(asset.sizeBytes) },
    { label: 'Format', value: asset.contentType },
  ]

  if (asset.kind === 'Video') {
    list.push({ label: 'Davomiyligi', value: assetDurationLabel(asset.durationSec) })
  }
  if (asset.width !== null && asset.height !== null) {
    list.push({ label: 'O‘lchami', value: `${asset.width} × ${asset.height} px` })
  }
  list.push({ label: 'Qo‘shilgan', value: formatDateTime(asset.createdAt) })
  return list
})

function download(): void {
  const blob = preview.blob.value
  if (blob === null) return
  saveBlob(blob, preview.fileName.value)
}
</script>

<template>
  <BaseModal
    :open="props.asset !== null"
    :title="props.asset?.title ?? (isImage ? 'Rasm' : 'Video qismi')"
    @close="emit('close')"
  >
    <div v-if="props.asset !== null">
      <!-- ---------------------------------------------------------- rasm -->
      <template v-if="isImage">
        <div
          v-if="preview.isPending.value"
          class="flex min-h-40 items-center justify-center gap-2 text-xs text-slate-400"
        >
          <BaseSpinner size="sm" />
          Rasm yuklanmoqda…
        </div>

        <div
          v-else-if="preview.errorMessage.value !== null"
          class="rounded-lg border border-rose-500/25 bg-rose-500/10 px-3 py-3 text-center"
          role="alert"
        >
          <p
            class="text-xs text-rose-200"
            v-text="preview.errorMessage.value"
          />
          <BaseButton
            class="mt-2.5"
            size="sm"
            variant="secondary"
            :loading="preview.isFetching.value"
            @click="preview.refetch()"
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

        <img
          v-else-if="preview.url.value !== null"
          :src="preview.url.value"
          :alt="props.asset.title ?? 'Imtihon rasmi'"
          class="max-h-[60dvh] w-full rounded-lg border border-line bg-ink-950 object-contain"
        >
      </template>

      <!-- --------------------------------------------------------- video -->
      <div
        v-else
        class="rounded-lg border border-amber-500/25 bg-amber-500/10 p-3"
      >
        <p class="text-xs leading-relaxed text-amber-200">
          Videoni bu yerda ko‘rish HOZIRCHA mumkin emas: oqim manzili
          avtorizatsiya talab qiladi, brauzer esa video so‘rovida tokenni
          yubormaydi. Ko‘rish uchun qisqa muddatli havola tayyorlanmoqda.
        </p>
      </div>

      <!-- ---------------------------------------------------- ma'lumotlar -->
      <dl class="mt-3 divide-y divide-line rounded-lg border border-line">
        <div
          v-for="row in rows"
          :key="row.label"
          class="flex items-center justify-between gap-3 px-3 py-2"
        >
          <dt
            class="text-[11px] text-slate-400"
            v-text="row.label"
          />
          <dd
            class="min-w-0 truncate text-xs tabular-nums text-slate-200"
            v-text="row.value"
          />
        </div>
      </dl>
    </div>

    <template #footer>
      <BaseButton
        variant="secondary"
        @click="emit('close')"
      >
        Yopish
      </BaseButton>
      <!-- Yuklab olish FAQAT rasm uchun: videoni Blob bilan olish xotirani yeydi. -->
      <BaseButton
        v-if="isImage"
        :disabled="preview.blob.value === null"
        @click="download"
      >
        <template #icon>
          <AppIcon
            name="download"
            :size="14"
          />
        </template>
        Yuklab olish
      </BaseButton>
    </template>
  </BaseModal>
</template>
