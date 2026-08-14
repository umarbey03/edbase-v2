<script setup lang="ts">
import type { Track } from 'livekit-client'
import { computed, onBeforeUnmount, onMounted, useTemplateRef, watch } from 'vue'

import { AppIcon, BaseAvatar } from '@/shared/ui'

const props = withDefaults(
  defineProps<{
    track: Track | null
    name: string
    isLocal?: boolean
    isScreenShare?: boolean
    isSpeaking?: boolean
    micEnabled?: boolean
    /** Asosiy sahna uchun kattaroq ko'rinish. */
    large?: boolean
    /**
     * Yotiq telefondagi YON filmstrip uchun toraytirilgan katakcha
     * (104px ≈ 59px balandlik). `large` bilan birga ma'nosiz — asosiy sahna
     * baribir butun joyni oladi.
     */
    compact?: boolean
    roleLabel?: string
  }>(),
  {
    isLocal: false,
    isScreenShare: false,
    isSpeaking: false,
    micEnabled: false,
    large: false,
    compact: false,
    roleLabel: '',
  },
)

const videoEl = useTemplateRef<HTMLVideoElement>('videoEl')

// Qaysi trek AYNAN shu <video> ga ulangani — `detach` uchun kerak.
let attachedTrack: Track | null = null

function detachTrack(): void {
  const element = videoEl.value
  if (attachedTrack === null) return
  if (element !== null) {
    attachedTrack.detach(element)
    // `srcObject` ni bo'shatish — MediaStream ushlanib qolmasligi uchun.
    element.srcObject = null
  } else {
    attachedTrack.detach()
  }
  attachedTrack = null
}

function syncTrack(): void {
  const element = videoEl.value
  if (element === null) return
  if (attachedTrack === props.track) return

  detachTrack()

  if (props.track !== null) {
    props.track.attach(element)
    attachedTrack = props.track
  }
  // Ovoz alohida <audio> elementlaridan chiqadi — video HAR DOIM ovozsiz,
  // aks holda aks-sado (echo) paydo bo'ladi.
  element.muted = true
}

// `flush: 'post'` — DOM yangilangandan keyin ulaymiz.
watch(() => props.track, syncTrack, { flush: 'post' })
onMounted(syncTrack)
onBeforeUnmount(detachTrack)

const hasVideo = computed(() => props.track !== null)

/*
  O'lchov klassi. Uchta holat: asosiy sahna (butun joy), yon filmstrip
  (yotiq telefon — tor), oddiy filmstrip (o'zgarmadi: 160px, `sm` da 192px).
*/
const sizeClass = computed(() => {
  if (props.large) return 'size-full'
  return props.compact
    ? 'aspect-video w-[104px] shrink-0'
    : 'aspect-video w-40 shrink-0 sm:w-48'
})
</script>

<template>
  <div
    class="group relative overflow-hidden rounded-xl bg-ink-850 ring-1 ring-inset transition-shadow"
    :class="[props.isSpeaking ? 'ring-2 ring-emerald-400/80' : 'ring-line', sizeClass]"
  >
    <video
      ref="videoEl"
      autoplay
      playsinline
      muted
      disablePictureInPicture
      class="size-full"
      :class="[
        hasVideo ? 'opacity-100' : 'opacity-0',
        props.isScreenShare ? 'object-contain' : 'object-cover',
        props.isLocal && !props.isScreenShare ? '-scale-x-100' : '',
      ]"
    />

    <!-- Kamera o'chiq — avatar bilan o'rin egallagich -->
    <div
      v-if="!hasVideo"
      class="absolute inset-0 flex flex-col items-center justify-center gap-2 bg-ink-850"
    >
      <BaseAvatar
        :name="props.name"
        :size="props.large ? 'lg' : 'md'"
      />
      <span
        v-if="props.large"
        class="text-sm text-slate-400"
        v-text="props.roleLabel"
      />
    </div>

    <!--
      Pastki yozuv.

      ★ `from-black/75`, `text-white/90` va `text-white/60` ATAYLAB
      QOLDIRILDI (2026-08-11 "oq/qora shaffoflik" auditi). Sabab bu yerda
      HAQIQIY: qatlam `<video>` elementining USTIDA turadi, ya'ni fon —
      kameradan kelgan kadr va rangi oldindan MA'LUM EMAS (qorong'i xona,
      oq devor, ekran ulashuvida oq slayd). Tema tokeni bu joyda ma'nosiz:
      yorug' `ink-*` oq slaydda yozuvni yo'q qilardi, to'q `ink-*` esa
      qorong'i xonada "dog'" bo'lib qolardi. Qora gradient + oq matn —
      video subtitri qoidasi, u har qanday kadrda ishlaydi.
    -->
    <!-- ★ Tor katakchada (104×59px) yozuv qatori tilkaning yarmini egallab
         qolardi — shuning uchun ichki bo'shliq qisqaradi, matn esa o'sha
         (ism `truncate` bilan qisqaradi, olib tashlanmaydi). -->
    <div
      class="pointer-events-none absolute inset-x-0 bottom-0 flex items-center gap-1.5 bg-gradient-to-t from-black/75 to-transparent"
      :class="props.compact && !props.large ? 'px-1.5 py-0.5' : 'px-2 py-1.5'"
    >
      <AppIcon
        :name="props.micEnabled ? 'mic' : 'mic-off'"
        :size="14"
        :class="props.micEnabled ? 'text-emerald-400' : 'text-rose-400'"
      />
      <span
        class="truncate text-xs font-medium text-white/90"
        v-text="props.name"
      />
      <span
        v-if="props.isLocal"
        class="text-[10px] text-white/60"
      >(siz)</span>
    </div>

    <span
      v-if="props.isScreenShare"
      class="absolute rounded-md bg-brand-600/90 px-1.5 py-0.5 text-[10px] font-semibold uppercase tracking-wide text-white"
      :class="props.compact && !props.large ? 'left-1 top-1' : 'left-2 top-2'"
    >
      Ekran
    </span>
  </div>
</template>
