<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue'

import { assetDurationLabel, assetTitleLabel } from '@/entities/course'
import { useAuthStore } from '@/features/auth/model/auth.store'
import { formatPhone } from '@/shared/lib/phone'
import type { LessonAssetDto } from '@/shared/types'
import { AppIcon, BaseButton, BaseSpinner } from '@/shared/ui'

import { useLessonAssetTicket } from '../model/useLessonAssetTicket'

/**
 * ════════════════════════════════════════════════════════════════════════
 *  DARS VIDEOSI PLEYERI — Dars Dashboard uchun (ko'p qismli)
 * ════════════════════════════════════════════════════════════════════════
 *
 * `RecordingPlayerModal`dan IKKI farqi bor:
 *  1) manba `LessonAsset` (kurs kontenti), yozuv EMAS — chipta bilan
 *     ishlaydi (`useLessonAssetTicket`), presigned S3 URL bilan emas;
 *  2) BITTA modal EMAS — Dashboard ICHIDA joylashgan blok, shuning uchun
 *     o'z sarlavhasi/modal chegarasi yo'q va bir nechta VIDEO QISM orasida
 *     tanlash (chip qatori) bor — dars video "ko'p qismli bo'lishi mumkin".
 *
 * Suv belgisi va tezlik boshqaruvi — `RecordingPlayerModal` bilan AYNI
 * sabab va AYNI naqsh (pullik kurs kontenti, xuddi yozuv kabi himoyalanadi).
 */
const props = defineProps<{ assets: LessonAssetDto[] }>()

const videoAssets = computed(() =>
  props.assets.filter((a) => a.kind === 'Video').slice().sort((a, b) => a.position - b.position),
)

const activeIndex = ref(0)
const activeAsset = computed<LessonAssetDto | null>(() => videoAssets.value[activeIndex.value] ?? null)

const ticket = useLessonAssetTicket()
const video = ref<HTMLVideoElement | null>(null)

const auth = useAuthStore()

/** Suv belgisi matni — ko'ruvchining o'zi (`RecordingPlayerModal`dagi AYNI qoida va manba). */
const watermark = computed<string>(() => {
  const user = auth.user
  if (user === null) return ''
  const phone = formatPhone(user.phone)
  if (phone.length > 0) return phone
  return `${user.fullName} · #${user.id}`
})

const SPEEDS: readonly { value: number; label: string }[] = [
  { value: 1, label: '1.0x' },
  { value: 1.25, label: '1.25x' },
  { value: 1.5, label: '1.5x' },
  { value: 2, label: '2.0x' },
]
const speed = ref<number>(1)

let retriedAfterError = false

function applySpeed(value: number): void {
  speed.value = value
  const element = video.value
  if (element !== null) element.playbackRate = value
}

/** `RecordingPlayerModal.detachVideo` bilan AYNI sabab: yuklashni to'xtatadi. */
function detachVideo(): void {
  const element = video.value
  if (element === null) return
  element.pause()
  element.removeAttribute('src')
  element.load()
}

async function openAsset(asset: LessonAssetDto | null): Promise<void> {
  retriedAfterError = false
  speed.value = 1
  detachVideo()

  if (asset === null) {
    ticket.reset()
    return
  }

  const url = await ticket.load(asset.id)
  if (url === null) return

  const element = video.value
  if (element === null) return
  element.src = url
  element.playbackRate = 1
  void element.play().catch(() => undefined)
}

/** Chipta eskirgani sababli BIR MARTA qayta urinish (`RecordingPlayerModal`dagi AYNI naqsh). */
async function handleVideoError(): Promise<void> {
  const asset = activeAsset.value
  const element = video.value
  if (asset === null || element === null) return

  if (retriedAfterError) {
    ticket.error.value = 'Videoni ochib bo‘lmadi. Chipta eskirgan yoki fayl ombori javob bermayapti.'
    return
  }

  retriedAfterError = true
  const position = element.currentTime
  const url = await ticket.load(asset.id, true)
  if (url === null) return

  element.src = url
  element.addEventListener(
    'loadedmetadata',
    () => {
      element.currentTime = position
      element.playbackRate = speed.value
      void element.play().catch(() => undefined)
    },
    { once: true },
  )
}

function selectPart(index: number): void {
  if (index === activeIndex.value) return
  activeIndex.value = index
}

watch(activeAsset, (asset) => void openAsset(asset), { immediate: true })

// Dashboard boshqa darsga o'tganda `assets` prop'i almashadi — qism
// tanlagich HAM boshidan boshlanishi kerak, aks holda 3-qismli darsdan
// 1-qismli darsga o'tilganda indeks chegaradan chiqib qolardi.
watch(
  () => props.assets,
  () => {
    activeIndex.value = 0
  },
)

onBeforeUnmount(detachVideo)
</script>

<template>
  <div>
    <p
      v-if="videoAssets.length === 0"
      class="rounded-xl border border-line bg-ink-950 px-5 py-10 text-center text-sm text-slate-400"
    >
      Bu darsga video hali qo‘shilmagan.
    </p>

    <template v-else>
      <!-- ------------------------------------------------- qism tanlagich -->
      <div
        v-if="videoAssets.length > 1"
        class="mb-2.5 flex flex-wrap gap-1.5"
      >
        <button
          v-for="(a, index) in videoAssets"
          :key="a.id"
          type="button"
          class="rounded-lg px-3 py-1.5 text-xs font-semibold transition-colors"
          :class="
            index === activeIndex
              ? 'bg-brand-500 text-white'
              : 'bg-ink-800 text-slate-300 hover:bg-ink-700'
          "
          @click="selectPart(index)"
        >
          {{ assetTitleLabel(a, index) }}
        </button>
      </div>

      <div
        v-if="ticket.pending.value"
        class="flex h-48 items-center justify-center rounded-xl bg-black"
      >
        <BaseSpinner />
      </div>

      <p
        v-else-if="ticket.error.value !== null"
        class="rounded-xl border border-rose-500/25 bg-rose-500/10 px-5 py-6 text-center text-sm text-rose-200"
        role="alert"
        v-text="ticket.error.value"
      />

      <!-- `v-show`, RecordingPlayerModal'dagi AYNI sabab: `video` ref' ochilishda hali yo'q bo'lmasin. -->
      <div
        v-show="!ticket.pending.value && ticket.error.value === null"
        class="relative"
      >
        <video
          ref="video"
          controls
          playsinline
          class="block max-h-[65dvh] w-full rounded-xl bg-black"
          @error="handleVideoError"
        />

        <div
          v-if="watermark.length > 0"
          class="zn-watermark pointer-events-none absolute inset-0 select-none overflow-hidden rounded-xl"
          aria-hidden="true"
        >
          <div class="zn-watermark__track">
            <span
              class="zn-watermark__text"
              v-text="watermark"
            />
          </div>
        </div>
      </div>

      <div class="mt-2.5 flex flex-wrap items-center gap-2">
        <span class="text-xs font-semibold text-slate-400">⏱️ Tezlik:</span>
        <BaseButton
          v-for="option in SPEEDS"
          :key="option.value"
          size="sm"
          :variant="speed === option.value ? 'primary' : 'ghost'"
          @click="applySpeed(option.value)"
        >
          {{ option.label }}
        </BaseButton>
        <span
          v-if="activeAsset !== null"
          class="ml-auto flex items-center gap-1 text-xs text-slate-500"
        >
          <AppIcon
            name="clock"
            :size="13"
          />
          {{ assetDurationLabel(activeAsset.durationSec) }}
        </span>
      </div>
    </template>
  </div>
</template>

<style scoped>
/* Suv belgisi animatsiyasi — `RecordingPlayerModal.vue` bilan SO'ZMA-SO'Z bir xil. */
.zn-watermark__track {
  position: absolute;
  inset: 0;
  animation: zn-watermark-drift 44s ease-in-out infinite alternate both;
}

.zn-watermark__text {
  display: inline-block;
  white-space: nowrap;
  font-size: clamp(11px, 1.6vw, 15px);
  font-weight: 700;
  letter-spacing: 0.08em;
  color: rgb(255 255 255 / 42%);
  text-shadow: 0 1px 3px rgb(0 0 0 / 55%);
}

@keyframes zn-watermark-drift {
  0% {
    transform: translate(4%, 8%);
  }
  25% {
    transform: translate(48%, 22%);
  }
  50% {
    transform: translate(14%, 48%);
  }
  75% {
    transform: translate(55%, 34%);
  }
  100% {
    transform: translate(30%, 60%);
  }
}

@media (prefers-reduced-motion: reduce) {
  .zn-watermark__track {
    animation: none;
    transform: translate(30%, 20%);
  }
}
</style>
