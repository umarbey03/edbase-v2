<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue'

import { MAX_AUDIO_BYTES } from '@/entities/assignment'
import { formatFileSize } from '@/shared/lib/text'
import { AppIcon, BaseButton } from '@/shared/ui'

import { formatDuration, useVoiceRecorder } from '../model/useVoiceRecorder'

/**
 * Ovozli javob yozish paneli (2026-10-29).
 *
 * Holatlar: bo'sh → yozilmoqda (qizil nuqta, taymer, "To'xtatish") →
 * yozildi (tinglash, davomiylik, "Qayta yozish", "O'chirish").
 *
 * ★ BOSHQARILADIGAN KOMPONENT: yozilgan fayl `v-model` orqali OTA
 * oynada turadi va u AYNI `files` ro'yxatiga qo'shiladi — ya'ni yuklash
 * yo'li, `validateAttachments` va `MAX_ATTACHMENTS` sanog'i oddiy
 * tanlangan fayl bilan BIR XIL. O'quvchi yozuvni ro'yxatdagi "×" bilan
 * olib tashlasa, model `null` bo'ladi va panel o'zi bo'sh holatga qaytadi.
 *
 * 🔴 OYNA YOPILSA YOZUV TO'XTAYDI: komponent ota oynadagi
 * `v-if="assignment !== null"` ichida turadi, ya'ni yopilishda yo'q
 * qilinadi va `useVoiceRecorder` mikrofonni qo'yib yuboradi.
 */
const props = withDefaults(
  defineProps<{
    /** Yangi yozuv uchun joy yo'q (`MAX_ATTACHMENTS` to'lgan). */
    disabled?: boolean
  }>(),
  { disabled: false },
)

const recording = defineModel<File | null>({ required: true })

/**
 * Mikrofon ochiq (so'ralmoqda yoki yozilmoqda). Ota oyna shu paytda
 * "Yuborish" ni to'sadi — aks holda chala yozuv javobga kirmay qolardi.
 */
const emit = defineEmits<{ busy: [value: boolean] }>()

/** ~3 daqiqa: talaffuz mashqi uchun yetarli, hajm esa 10 MB dan ancha kichik. */
const MAX_SECONDS = 180

const durationSeconds = ref(0)
/** Ekran o'quvchi uchun holat xabari (`aria-live`). */
const announcement = ref('')

const recorder = useVoiceRecorder({
  maxSeconds: MAX_SECONDS,
  maxBytes: MAX_AUDIO_BYTES,
  onRecorded: (result) => {
    durationSeconds.value = result.durationSeconds
    recording.value = result.file
    announcement.value = recorder.autoStopped.value
      ? `Vaqt chegarasi tugadi, yozuv saqlandi. Davomiyligi ${formatDuration(result.durationSeconds)}.`
      : `Yozuv saqlandi. Davomiyligi ${formatDuration(result.durationSeconds)}.`
  },
})

const isRecording = computed(() => recorder.state.value === 'recording')
const isRequesting = computed(() => recorder.state.value === 'requesting')

/*
  ⚠️ `<audio>` uchun obyekt URL'i qo'lda boshqariladi: `computed` ichida
  `URL.createObjectURL` chaqirilsa eski URL bo'shatilmay qolardi (har qayta
  yozishda bir necha yuz KB xotira oqib ketardi).
*/
const playbackUrl = ref<string | null>(null)

watch(
  recording,
  (file) => {
    if (playbackUrl.value !== null) URL.revokeObjectURL(playbackUrl.value)
    playbackUrl.value = file === null ? null : URL.createObjectURL(file)
  },
  { immediate: true },
)

onBeforeUnmount(() => {
  if (playbackUrl.value !== null) URL.revokeObjectURL(playbackUrl.value)
})

watch(
  () => recorder.state.value,
  (state) => {
    if (state === 'recording') announcement.value = 'Yozish boshlandi.'
    emit('busy', state !== 'idle')
  },
)

async function startRecording(): Promise<void> {
  announcement.value = ''
  await recorder.start()
}

function rerecord(): void {
  recording.value = null
  void startRecording()
}

function remove(): void {
  recording.value = null
  recorder.clearError()
  announcement.value = 'Yozuv o‘chirildi.'
}
</script>

<template>
  <div class="rounded-lg border border-line bg-ink-950 p-3">
    <!-- Bo'sh holat -->
    <div
      v-if="!isRecording && recording === null"
      class="flex flex-wrap items-center gap-x-3 gap-y-1.5"
    >
      <BaseButton
        variant="secondary"
        size="sm"
        :loading="isRequesting"
        :disabled="props.disabled"
        @click="startRecording"
      >
        <template #icon>
          <AppIcon
            name="mic"
            :size="15"
          />
        </template>
        {{ isRequesting ? 'Mikrofon so‘ralmoqda…' : 'Ovoz yozish' }}
      </BaseButton>
      <span
        class="text-[11px] text-dim"
        v-text="props.disabled ? 'Fayllar soni to‘lgan.' : '3 daqiqagacha.'"
      />
    </div>

    <!-- Yozilmoqda -->
    <div
      v-else-if="isRecording"
      class="flex items-center gap-3"
    >
      <span
        class="size-2.5 shrink-0 animate-pulse rounded-full bg-red-400 motion-reduce:animate-none"
        aria-hidden="true"
      />
      <span class="text-xs font-medium text-slate-200">Yozilmoqda</span>
      <span
        class="text-xs tabular-nums text-slate-400"
        role="timer"
        :aria-label="`O‘tgan vaqt ${formatDuration(recorder.elapsedSeconds.value)}`"
      >
        {{ formatDuration(recorder.elapsedSeconds.value) }} / {{ formatDuration(MAX_SECONDS) }}
      </span>
      <BaseButton
        class="ml-auto"
        variant="danger"
        size="sm"
        @click="recorder.stop"
      >
        <template #icon>
          <AppIcon
            name="pause"
            :size="15"
          />
        </template>
        To‘xtatish
      </BaseButton>
    </div>

    <!-- Yozildi -->
    <div
      v-else-if="recording !== null"
      class="space-y-2"
    >
      <audio
        v-if="playbackUrl !== null"
        class="h-10 w-full"
        controls
        preload="metadata"
        :src="playbackUrl"
        aria-label="Yozilgan ovozni tinglash"
      />
      <div class="flex flex-wrap items-center gap-2">
        <span class="mr-auto text-[11px] tabular-nums text-dim">
          Ovozli javob · {{ formatDuration(durationSeconds) }} · {{ formatFileSize(recording.size) }}
        </span>
        <BaseButton
          variant="secondary"
          size="sm"
          @click="rerecord"
        >
          <template #icon>
            <AppIcon
              name="refresh"
              :size="15"
            />
          </template>
          Qayta yozish
        </BaseButton>
        <BaseButton
          variant="ghost"
          size="sm"
          @click="remove"
        >
          <template #icon>
            <AppIcon
              name="trash"
              :size="15"
            />
          </template>
          O‘chirish
        </BaseButton>
      </div>
    </div>

    <p
      v-if="recorder.errorMessage.value !== null"
      class="mt-2 text-[11px] leading-relaxed text-rose-400"
      role="alert"
      v-text="recorder.errorMessage.value"
    />

    <span
      class="sr-only"
      aria-live="polite"
      v-text="announcement"
    />
  </div>
</template>
