<script setup lang="ts">
import { onBeforeUnmount, ref, watch } from 'vue'

import { BaseButton, BaseModal, BaseSpinner } from '@/shared/ui'

import { useRecordingLink } from '../model/useRecordingLink'

/**
 * Yozuv pleyeri.
 *
 * ★ ESKI ILOVADAN AYNAN (`academic.html`, 6383–6401-qatorlar): oyna sarlavhasi
 * "Dars yozuvi", ichida `controls playsinline` bilan `<video>`, pastda
 * "⏱️ Tezlik:" va 1.0x / 1.25x / 1.5x / 2.0x tugmalari hamda "Yopish".
 * Tezlik tugmalari o'quvchilar uchun eng ko'p ishlatiladigan imkoniyat edi —
 * dars 80 daqiqa, 1.5x da uni qayta ko'rish real vaqt talab qiladi.
 *
 * ESKISIDAN FARQ: `v.src = j.url` darhol qo'yilardi va manzil eskirsa video
 * jimgina to'xtardi. Bu yerda `error` hodisasi ushlanadi va manzil BIR MARTA
 * qayta so'ralib, ko'rilgan vaqt tiklanadi (`currentTime`) — presigned havola
 * 15 daqiqada eskiraydi, 80 daqiqalik darsni esa hech kim 15 daqiqada
 * ko'rmaydi.
 */
const props = defineProps<{
  /** `null` — oyna yopiq. Ochilganda yozuv id'si beriladi. */
  recordingId: number | null
  title: string
}>()

const emit = defineEmits<{ close: [] }>()

const link = useRecordingLink()
const video = ref<HTMLVideoElement | null>(null)

/**
 * Eski ilovadagi to'rtta tezlik (`setPlaybackSpeed`). Yorliqlar ham AYNAN
 * o'sha ko'rinishda — `toFixed()` bilan hisoblansa "1.50x" chiqib ketardi.
 */
const SPEEDS: readonly { value: number; label: string }[] = [
  { value: 1, label: '1.0x' },
  { value: 1.25, label: '1.25x' },
  { value: 1.5, label: '1.5x' },
  { value: 2, label: '2.0x' },
]
const speed = ref<number>(1)

/**
 * Manzil eskirgani sababli BIR MARTA qayta urinildimi.
 * Cheksiz halqa bo'lmasligi uchun: agar qayta olingan manzil ham xato bersa,
 * foydalanuvchiga xato ko'rsatiladi.
 */
let retriedAfterError = false

function applySpeed(value: number): void {
  speed.value = value
  const element = video.value
  if (element !== null) element.playbackRate = value
}

/**
 * Videoni to'xtatib, manbani UZADI.
 *
 * ★ `src` ni bo'shatish SHART: `removeAttribute('src')` + `load()` bo'lmasa
 * brauzer oyna yopilgandan keyin ham faylni yuklab olishda davom etadi
 * (1 GB lik yozuvda bu sezilarli trafik). Eski ilova ham aynan shunday
 * qilardi (`closeRecPlayer`).
 */
function detachVideo(): void {
  const element = video.value
  if (element === null) return
  element.pause()
  element.removeAttribute('src')
  element.load()
}

async function open(recordingId: number): Promise<void> {
  retriedAfterError = false
  speed.value = 1
  const url = await link.load(recordingId)
  if (url === null) return

  const element = video.value
  if (element === null) return
  element.src = url
  element.playbackRate = 1
  // Avtomatik ijro brauzer siyosati bilan bloklanishi mumkin — bu xato emas,
  // foydalanuvchi ▶ ni o'zi bosadi.
  void element.play().catch(() => undefined)
}

/**
 * `<video>` xatosi. Eng ehtimolli sabab — presigned manzil eskirgani, shuning
 * uchun avval JIMGINA qayta olinadi va ko'rilgan joy tiklanadi. Ikkinchi xato
 * allaqachon boshqa sabab (ombor yo'q, fayl o'chirilgan) — u ko'rsatiladi.
 */
async function handleVideoError(): Promise<void> {
  const recordingId = props.recordingId
  const element = video.value
  if (recordingId === null || element === null) return

  if (retriedAfterError) {
    link.error.value =
      'Videoni ochib bo‘lmadi. Havola eskirgan yoki fayl ombori javob bermayapti.'
    return
  }

  retriedAfterError = true
  const position = element.currentTime
  const url = await link.load(recordingId, true)
  if (url === null) return

  element.src = url
  // Manba almashgach `currentTime` darhol qo'yilmaydi — metama'lumot kutiladi.
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

watch(
  () => props.recordingId,
  (id) => {
    if (id === null) {
      detachVideo()
      link.reset()
      return
    }
    void open(id)
  },
)

// Sahifa almashsa oyna ochiq holda yo'q qilinishi mumkin — yuklab olishni
// to'xtatamiz, aks holda oqim fonda davom etardi.
onBeforeUnmount(detachVideo)
</script>

<template>
  <BaseModal
    :open="props.recordingId !== null"
    :title="props.title.length > 0 ? props.title : 'Dars yozuvi'"
    wide
    @close="emit('close')"
  >
    <div
      v-if="link.pending.value"
      class="flex h-48 items-center justify-center rounded-xl bg-black"
    >
      <BaseSpinner />
    </div>

    <p
      v-else-if="link.error.value !== null"
      class="rounded-xl border border-rose-500/25 bg-rose-500/10 px-5 py-6 text-center text-sm text-rose-200"
      role="alert"
      v-text="link.error.value"
    />

    <!--
      `v-show` (`v-if` EMAS): element DOM'da qolishi kerak, aks holda
      `video` ref'i `open()` chaqirilgan paytda hali `null` bo'lardi.
    -->
    <video
      v-show="!link.pending.value && link.error.value === null"
      ref="video"
      controls
      playsinline
      class="max-h-[65vh] w-full rounded-xl bg-black"
      @error="handleVideoError"
    />

    <template #footer>
      <div class="flex flex-1 flex-wrap items-center gap-2">
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
      </div>
      <BaseButton
        size="sm"
        variant="secondary"
        @click="emit('close')"
      >
        Yopish
      </BaseButton>
    </template>
  </BaseModal>
</template>
