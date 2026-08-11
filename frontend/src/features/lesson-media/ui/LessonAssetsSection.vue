<script setup lang="ts">
import { useMutation } from '@tanstack/vue-query'
import { computed, ref, watch } from 'vue'

import {
  allowedAssetKind,
  assetAcceptFor,
  assetDurationLabel,
  assetTitleLabel,
  buildLessonAssetForm,
  deleteLessonAsset,
  lessonAssetUploadPath,
  MAX_LESSON_ASSETS,
  reorderLessonAssets,
} from '@/entities/course'
import { toUserMessage } from '@/shared/api'
import { useConfirm } from '@/shared/lib/useConfirm'
import { formatFileSize } from '@/shared/lib/text'
import type { LessonAssetDto, LessonKindName } from '@/shared/types'
import { AppIcon, BaseButton, BaseField, EmptyState, IconButton } from '@/shared/ui'

import { probeMedia } from '../lib/media-probe'
import { uploadWithProgress } from '../lib/upload-with-progress'
import type { UploadProgress } from '../lib/upload-with-progress'
import { useUploadLimits } from '../model/limits'
import { useUploadQueue } from '../model/upload-queue'
import AssetPreviewDialog from './AssetPreviewDialog.vue'
import UploadQueueList from './UploadQueueList.vue'

/**
 * ========================================================================
 * DARS MEDIASI BO'LIMI: video qismlari (odatiy) / rasmlar (imtihon)
 * ========================================================================
 *
 * Talab (loyiha egasi): *"videolarni qisimlarga bo'lib ham yuklashi mumkin,
 * ya'ni bitta darsda bir nechta video bo'lishi ham mumkin"*.
 *
 * ── ★ NIMA UCHUN "TANLANGAN FAYLLAR" BOSQICHI BOR ─────────────────────
 *
 * Fayl tanlangach u DARHOL yuborilmaydi: avval har biriga NOM (`title`)
 * yozish imkoni beriladi. Sabab qat'iy — SERVERDA ASSET NOMINI TAHRIRLASH
 * ENDPOINTI YO'Q (`POST assets`, `GET`, `DELETE`, `POST reorder` bor,
 * `PATCH` yo'q). Ya'ni nom faqat YUKLASH PAYTIDA yozilishi mumkin va
 * xato yozilgan nomni tuzatishning yagona yo'li — 1 GB videoni o'chirib,
 * qaytadan yuklash. Shu sababli nom kiritish yuborishdan OLDIN so'raladi.
 * (Endpoint qo'shilishi hisobotda alohida taklif qilingan.)
 *
 * ── 🔴 HAJM YUBORISHDAN OLDIN TEKSHIRILADI ────────────────────────────
 *
 * Chegaradan katta fayl navbatga UMUMAN tushmaydi: xodim 1.5 GB ni yigirma
 * daqiqa yuklab, oxirida 413 olishi shu blokning eng qimmat xatosi bo'lardi
 * (nginx `proxy_request_buffering off` bilan bu hatto "network error"
 * ko'rinishida chiqadi — 13-bo'lim, 40-tuzoq). Chegara SOZLAMADAN keladi
 * (`useUploadLimits`), kodda qotmaydi.
 *
 * ── TARTIB ────────────────────────────────────────────────────────────
 *
 * Ikki yo'l bilan: TORTIB (sichqoncha) va YUQORI/PAST tugmalari. Ikkinchisi
 * MAJBURIY — HTML5 drag-and-drop teginishli ekranda ishlamaydi va
 * klaviatura bilan ham boshqarilmaydi. Ikkalasi ham BITTA joyga keladi
 * (`commitOrder`) va serverga TO'LIQ ro'yxat yuboradi (7-tuzoq).
 */
const props = defineProps<{
  lessonId: number
  lessonKind: LessonKindName
  assets: readonly LessonAssetDto[]
}>()

const emit = defineEmits<{ 'update:assets': [value: LessonAssetDto[]] }>()

const confirm = useConfirm()
const limits = useUploadLimits()

const assetKind = computed(() => allowedAssetKind(props.lessonKind))
const isExam = computed(() => props.lessonKind === 'Exam')

const sectionTitle = computed(() => (isExam.value ? 'Rasmlar' : 'Video qismlari'))
const itemWord = computed(() => (isExam.value ? 'rasm' : 'video'))

/** Umumiy amal xatosi (o'chirish/tartib) — validatsiya emas, banner. */
const actionError = ref<string | null>(null)

/* ==================================================== fayl tanlash (staging) */

interface StagedFile {
  key: string
  file: File
  title: string
}

let stagedSequence = 0

const fileInput = ref<HTMLInputElement | null>(null)
const staged = ref<StagedFile[]>([])
/** Chegaradan o'tmagan fayllar — SERVERGA YUBORILMAGANI aytiladi. */
const rejected = ref<Array<{ name: string; message: string }>>([])

/**
 * Nom yuklash paytida kerak bo'ladi, lekin navbat faqat `File` ni uzatadi.
 * `WeakMap` — fayl ob'ekti yo'q bo'lgach yozuv ham o'zi tozalanadi.
 */
const titleByFile = new WeakMap<File, string>()

function suggestTitle(index: number): string {
  // Rasmlar odatda nomlanmaydi (varaqlar tartib bilan ketadi), video
  // qismlari esa deyarli doim "1-qism, 2-qism".
  if (isExam.value) return ''
  return `${props.assets.length + index + 1}-qism`
}

function openPicker(): void {
  fileInput.value?.click()
}

function onFilesPicked(event: Event): void {
  const input = event.target as HTMLInputElement
  const picked = Array.from(input.files ?? [])
  // Bir xil faylni QAYTA tanlash mumkin bo'lishi kerak (xato bilan o'chirib
  // yuborgan bo'lsa), shuning uchun `value` tozalanadi.
  input.value = ''
  if (picked.length === 0) return

  actionError.value = null
  const room = MAX_LESSON_ASSETS - props.assets.length - staged.value.length
  if (room <= 0) {
    actionError.value = `Bitta darsga ko‘pi bilan ${MAX_LESSON_ASSETS} ta fayl biriktiriladi.`
    return
  }

  for (const [index, file] of picked.slice(0, room).entries()) {
    const problem = limits.assetSizeError(file, assetKind.value)
    if (problem !== null) {
      rejected.value.push({ name: file.name, message: problem })
      continue
    }
    stagedSequence += 1
    staged.value.push({
      key: `s${stagedSequence}`,
      file,
      title: suggestTitle(staged.value.length + index),
    })
  }

  if (picked.length > room) {
    actionError.value =
      `Faqat ${room} ta fayl qo‘shildi: bitta darsga ko‘pi bilan `
      + `${MAX_LESSON_ASSETS} ta fayl biriktiriladi.`
  }
}

function removeStaged(key: string): void {
  staged.value = staged.value.filter((item) => item.key !== key)
}

/* ================================================================ yuklash */

/**
 * Bitta faylni yuboradi.
 *
 * Metama'lumot (davomiylik/o'lcham) brauzerda o'qiladi — serverda media
 * dekoder yo'q (47-tuzoq). O'qib bo'lmasa `null` ketadi va yuklash DAVOM
 * ETADI: bu ko'rsatish uchun ma'lumot, shart emas.
 */
async function uploadOne(
  file: File,
  onProgress: (progress: UploadProgress) => void,
  signal: AbortSignal,
): Promise<void> {
  const meta = await probeMedia(file, assetKind.value)
  if (signal.aborted) return

  const form = buildLessonAssetForm(file, {
    title: titleByFile.get(file) ?? null,
    durationSec: meta.durationSec,
    width: meta.width,
    height: meta.height,
  })

  const created = await uploadWithProgress<LessonAssetDto>({
    path: lessonAssetUploadPath(props.lessonId),
    form,
    onProgress,
    signal,
  })

  // Server yangi faylni OXIRIGA qo'yadi (`NextPositionAsync`), shuning uchun
  // ro'yxat ham oxiriga qo'shiladi — qayta so'rov shart emas.
  emit('update:assets', [...props.assets, created])
}

const queue = useUploadQueue({
  upload: uploadOne,
  // Ikkinchi qatlam himoyasi: navbatga to'g'ridan-to'g'ri tushgan fayl ham
  // (yoki chegara shu orada pasaygan bo'lsa) tekshiruvdan o'tadi.
  validate: (file) => limits.assetSizeError(file, assetKind.value),
})

function startUpload(): void {
  const items = staged.value
  if (items.length === 0) return
  for (const item of items) titleByFile.set(item.file, item.title.trim())
  queue.enqueue(items.map((item) => item.file))
  staged.value = []
}

/*
  Dars TURI almashsa tanlangan (lekin hali yuborilmagan) fayllar YARAMAYDI:
  imtihon darsiga video, odatiy darsga rasm biriktirilmaydi. Ularni jimgina
  yuborish 400 berardi, ro'yxatda qoldirish esa foydalanuvchini chalg'itardi.
*/
watch(
  () => props.lessonKind,
  () => {
    staged.value = []
    rejected.value = []
  },
)

/* ============================================================== o'chirish */

const deletingId = ref<number | null>(null)

const deleteMutation = useMutation({
  mutationFn: (assetId: number) => deleteLessonAsset(assetId),
  onSuccess: (_result, assetId) => {
    emit(
      'update:assets',
      props.assets.filter((item) => item.id !== assetId),
    )
  },
  onError: (error: Error) => {
    actionError.value = toUserMessage(error)
  },
  onSettled: () => {
    deletingId.value = null
  },
})

async function askDelete(asset: LessonAssetDto, index: number): Promise<void> {
  const name = assetTitleLabel(asset, index)
  const ok = await confirm({
    title: `${isExam.value ? 'Rasmni' : 'Video qismini'} o‘chirish`,
    message:
      `“${name}” o‘chiriladi. Fayl ombordan ham olib tashlanadi — `
      + 'bu amalni QAYTARIB BO‘LMAYDI.',
    confirmLabel: 'O‘chirish',
    tone: 'danger',
    details: [`Hajmi: ${formatFileSize(asset.sizeBytes)}`],
  })
  if (!ok) return

  actionError.value = null
  deletingId.value = asset.id
  deleteMutation.mutate(asset.id)
}

/* ================================================================= tartib */

/**
 * Tartib so'rovi: YANGI ro'yxat va AVVALGISI.
 *
 * Avvalgi ro'yxat `onMutate` KONTEKSTI o'rniga to'g'ridan-to'g'ri so'rov
 * ma'lumotida uzatiladi — shunda u to'liq turlangan bo'ladi va "kontekst
 * qaysi turda edi?" degan savol umuman tug'ilmaydi.
 */
interface ReorderInput {
  ordered: LessonAssetDto[]
  previous: LessonAssetDto[]
}

const reorderMutation = useMutation({
  // 🔴 TO'LIQ ro'yxat: yetishmasa, takrorlansa yoki begona Id bo'lsa server
  // 400 beradi (`problem.errors.orderedIds`) va HECH NARSA yozilmaydi.
  mutationFn: (input: ReorderInput) =>
    reorderLessonAssets(
      props.lessonId,
      input.ordered.map((item) => item.id),
    ),
  onError: (error: Error, input: ReorderInput) => {
    /*
      OPTIMISTIK TARTIB QAYTARILADI. Ro'yxat serverga yuborilishdan OLDIN
      ekranda ko'chiriladi (tortish silliq bo'lishi uchun), xato bo'lsa esa
      AVVALGI holat tiklanadi — aks holda ekranda server QABUL QILMAGAN
      tartib qolib, keyingi ko'chirish uni yana yuborardi.
    */
    emit('update:assets', input.previous)
    actionError.value = toUserMessage(error)
  },
})

function commitOrder(ordered: LessonAssetDto[]): void {
  actionError.value = null
  const previous = [...props.assets]
  emit('update:assets', ordered)
  reorderMutation.mutate({ ordered, previous })
}

function moved(from: number, to: number): LessonAssetDto[] | null {
  if (from === to || from < 0 || to < 0) return null
  const list = [...props.assets]
  if (from >= list.length || to >= list.length) return null
  const [item] = list.splice(from, 1)
  if (item === undefined) return null
  list.splice(to, 0, item)
  return list
}

function move(from: number, delta: number): void {
  const next = moved(from, from + delta)
  if (next === null) return
  commitOrder(next)
}

/* ---- tortib tartiblash (sichqoncha; teginishda tugmalar ishlaydi) ---- */

const dragIndex = ref<number | null>(null)

function onDragStart(index: number, event: DragEvent): void {
  dragIndex.value = index
  if (event.dataTransfer === null) return
  event.dataTransfer.effectAllowed = 'move'
  // Firefox bo'sh `dataTransfer` bilan tortishni BOSHLAMAYDI — qiymat
  // ishlatilmasa ham qo'yilishi shart.
  event.dataTransfer.setData('text/plain', String(index))
}

function onDrop(index: number): void {
  const from = dragIndex.value
  dragIndex.value = null
  if (from === null) return
  const next = moved(from, index)
  if (next === null) return
  commitOrder(next)
}

/* ================================================================ ko'rish */

const previewAsset = ref<LessonAssetDto | null>(null)

/**
 * 🔴 VIDEO UCHUN "KO'RISH" O'CHIQ. Sabab `AssetPreviewDialog` boshida
 * batafsil yozilgan: oqim endpointi token talab qiladi, `<video src>` esa
 * uni yubormaydi; `Blob` yechimi 1 GB fayl uchun yaramaydi. Rasm (10 MB
 * chegarasi bilan) KO'RSATILADI.
 */
const previewHint =
  'Ko‘rish tez orada — avtorizatsiya ulanmoqda (video uchun qisqa muddatli havola)'
</script>

<template>
  <section>
    <header class="mb-2 flex flex-wrap items-end justify-between gap-2">
      <div class="min-w-0">
        <h3 class="text-sm font-semibold text-slate-100">
          {{ sectionTitle }}
          <span
            v-if="props.assets.length > 0"
            class="ml-1 tabular-nums font-normal text-dim"
          >{{ props.assets.length }}</span>
        </h3>
        <p class="mt-0.5 text-[11px] leading-relaxed text-dim">
          <template v-if="isExam">
            Imtihon darsiga savol varaqlarining suratlari biriktiriladi. Tartib —
            o‘quvchi ko‘radigan tartib.
          </template>
          <template v-else>
            Bitta darsni bir nechta qismga bo‘lib yuklash mumkin. Tartib —
            o‘quvchi ko‘radigan ketma-ketlik.
          </template>
        </p>
      </div>
    </header>

    <p class="mb-2.5 text-[11px] text-dim">
      Bitta fayl uchun chegara:
      <span class="tabular-nums">
        {{ isExam ? limits.imageMaxMb.value : limits.videoMaxMb.value }} MB
      </span>
      <template v-if="limits.isApproximate.value">
        (standart qiymat — aniq chegarani administrator sozlamalarda ko‘radi)
      </template>
    </p>

    <div
      v-if="actionError !== null"
      class="mb-2.5 rounded-lg border border-rose-500/25 bg-rose-500/10 p-2.5 text-[11px] text-rose-200"
      role="alert"
      v-text="actionError"
    />

    <!-- ======================================================= ro'yxat -->
    <EmptyState
      v-if="props.assets.length === 0"
      :icon="isExam ? 'image' : 'video'"
      :title="isExam ? 'Rasm yo‘q' : 'Video yo‘q'"
      :text="`Quyidagi tugma bilan birinchi ${itemWord}ni yuklang.`"
    />

    <ul
      v-else
      class="space-y-2"
    >
      <li
        v-for="(asset, index) in props.assets"
        :key="asset.id"
        class="js-asset-row flex items-center gap-3 rounded-lg border border-line bg-ink-850 p-2.5"
        :class="dragIndex === index ? 'opacity-50' : ''"
        draggable="true"
        @dragstart="onDragStart(index, $event)"
        @dragover.prevent
        @drop.prevent="onDrop(index)"
        @dragend="dragIndex = null"
      >
        <!--
          Tortish belgisi. `aria-hidden`: klaviatura va teginish uchun
          HAQIQIY vosita — yondagi yuqori/past tugmalari.
        -->
        <span
          class="shrink-0 cursor-grab text-slate-500"
          aria-hidden="true"
          title="Tortib joyini o‘zgartirish"
        >
          <AppIcon
            name="menu"
            :size="15"
          />
        </span>

        <div class="min-w-0 flex-1">
          <p class="truncate text-[13px] font-medium text-slate-200">
            <span class="mr-1.5 tabular-nums text-dim">{{ index + 1 }}.</span>
            {{ assetTitleLabel(asset, index) }}
          </p>
          <p class="mt-0.5 text-[11px] tabular-nums text-dim">
            {{ formatFileSize(asset.sizeBytes) }}
            <template v-if="asset.kind === 'Video'">
              · {{ assetDurationLabel(asset.durationSec) }}
            </template>
            <template v-else-if="asset.width !== null && asset.height !== null">
              · {{ asset.width }}×{{ asset.height }}
            </template>
          </p>
        </div>

        <!-- 🔴 `gap-3` — 24-tuzoq (kichik oraliqda qo'shni tugma bosiladi). -->
        <div class="flex shrink-0 items-center gap-3">
          <IconButton
            icon="arrow-up"
            label="Yuqoriga"
            size="sm"
            :disabled="index === 0 || reorderMutation.isPending.value"
            @click="move(index, -1)"
          />
          <IconButton
            icon="arrow-down"
            label="Pastga"
            size="sm"
            :disabled="index === props.assets.length - 1 || reorderMutation.isPending.value"
            @click="move(index, 1)"
          />
          <IconButton
            icon="eye"
            :label="asset.kind === 'Image' ? 'Rasmni ko‘rish' : previewHint"
            size="sm"
            @click="previewAsset = asset"
          />
          <IconButton
            icon="trash"
            label="O‘chirish"
            size="sm"
            tone="danger"
            :loading="deletingId === asset.id"
            @click="askDelete(asset, index)"
          />
        </div>
      </li>
    </ul>

    <!-- ================================================ tanlangan fayllar -->
    <div
      v-if="staged.length > 0"
      class="mt-3 rounded-lg border border-line-strong bg-ink-850 p-3"
    >
      <p class="mb-2 text-xs font-semibold text-slate-200">
        Yuborishga tayyor: {{ staged.length }} ta fayl
      </p>
      <ul class="space-y-2.5">
        <li
          v-for="item in staged"
          :key="item.key"
          class="flex items-end gap-3"
        >
          <div class="min-w-0 flex-1">
            <BaseField
              :label="item.file.name"
              :hint="isExam ? 'Nom ixtiyoriy' : 'Masalan: 1-qism, Nazariya'"
            >
              <input
                v-model="item.title"
                class="zn-input"
                maxlength="200"
                :placeholder="isExam ? 'Nom (ixtiyoriy)' : 'Qism nomi'"
              >
            </BaseField>
            <p class="mt-1 text-[11px] tabular-nums text-dim">
              {{ formatFileSize(item.file.size) }}
            </p>
          </div>
          <IconButton
            icon="close"
            label="Ro‘yxatdan olib tashlash"
            size="sm"
            @click="removeStaged(item.key)"
          />
        </li>
      </ul>
      <p class="mt-2 text-[11px] leading-relaxed text-dim">
        Nom keyinchalik TAHRIRLANMAYDI (server bunday amalni qo‘llamaydi) — shu
        sababli yuborishdan oldin so‘raladi.
      </p>
      <div class="mt-2.5 flex flex-wrap gap-2">
        <BaseButton
          size="sm"
          @click="startUpload"
        >
          <template #icon>
            <AppIcon
              name="upload"
              :size="14"
            />
          </template>
          Yuklashni boshlash
        </BaseButton>
        <BaseButton
          size="sm"
          variant="ghost"
          @click="staged = []"
        >
          Bekor qilish
        </BaseButton>
      </div>
    </div>

    <!-- ================================================ rad etilgan fayllar -->
    <div
      v-if="rejected.length > 0"
      class="mt-3 rounded-lg border border-rose-500/25 bg-rose-500/10 p-2.5"
      role="alert"
    >
      <p class="text-[11px] font-semibold text-rose-200">
        Bu fayllar serverga YUBORILMADI:
      </p>
      <ul class="mt-1 space-y-1">
        <li
          v-for="(item, index) in rejected"
          :key="`${item.name}-${index}`"
          class="text-[11px] leading-relaxed text-rose-200"
        >
          <span class="font-medium">{{ item.name }}</span> — {{ item.message }}
        </li>
      </ul>
      <BaseButton
        class="mt-2"
        size="sm"
        variant="secondary"
        @click="rejected = []"
      >
        Tushunarli
      </BaseButton>
    </div>

    <!-- ======================================================= yuklash -->
    <UploadQueueList
      :items="queue.items.value"
      @cancel="queue.cancel"
      @retry="queue.retry"
    />

    <div class="mt-3 flex flex-wrap items-center gap-2">
      <!--
        `input` KO'RINMAYDI, lekin `display: none` EMAS: yashirilgan
        maydonni ba'zi brauzerlar `click()` bilan ochmaydi. `sr-only`
        elementni oqimdan chiqaradi, lekin "mavjud" qoldiradi.
      -->
      <input
        ref="fileInput"
        type="file"
        class="sr-only"
        :accept="assetAcceptFor(props.lessonKind)"
        multiple
        tabindex="-1"
        aria-hidden="true"
        @change="onFilesPicked"
      >
      <BaseButton
        size="sm"
        variant="secondary"
        @click="openPicker"
      >
        <template #icon>
          <AppIcon
            name="upload"
            :size="14"
          />
        </template>
        {{ isExam ? 'Rasm tanlash' : 'Video tanlash' }}
      </BaseButton>
      <BaseButton
        v-if="queue.items.value.length > 0"
        size="sm"
        variant="ghost"
        :disabled="queue.isBusy.value"
        @click="queue.clearFinished"
      >
        Ro‘yxatni tozalash
      </BaseButton>
      <p
        v-if="queue.activeCount.value > 0"
        class="text-[11px] text-dim"
      >
        Fayllar KETMA-KET yuboriladi — parallel yuklash internetni bo‘g‘adi.
      </p>
    </div>

    <AssetPreviewDialog
      :asset="previewAsset"
      @close="previewAsset = null"
    />
  </section>
</template>
