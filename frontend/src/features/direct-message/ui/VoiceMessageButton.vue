<script setup lang="ts">
import { computed } from 'vue'

import { formatDuration, isVoiceRecordingSupported, useVoiceRecorder } from '@/shared/lib/useVoiceRecorder'
import { AppIcon } from '@/shared/ui'

/**
 * ════════════════════════════════════════════════════════════════════════
 *  OVOZLI XABAR TUGMASI — yozishma uchun (24/7 chat, 2026-11-18)
 * ════════════════════════════════════════════════════════════════════════
 *
 * Telegram'dagidek: mikrofonni bosing — yozadi, «Yuborish» — darhol
 * yuboriladi, «×» — tashlab yuboriladi. Yozuv fayl ro'yxatiga tushmaydi:
 * chatda ovozli xabar "biriktirma" emas, alohida xabar.
 *
 * ★ Brauzer yozishni qo'llamasa (HTTP, eski brauzer) tugma chizilmaydi —
 *   ovozni fayl sifatida biriktirish yo'li baribir ochiq.
 */
const props = withDefaults(defineProps<{ disabled?: boolean }>(), { disabled: false })
const emit = defineEmits<{ recorded: [file: File, durationSeconds: number] }>()

/** Bitta ovozli xabar — 5 daqiqagacha (savol-javob uchun yetarli, fayl yengil). */
const MAX_SECONDS = 300
const MAX_BYTES = 8 * 1024 * 1024

let discard = false

const recorder = useVoiceRecorder({
  maxSeconds: MAX_SECONDS,
  maxBytes: MAX_BYTES,
  onRecorded: (result) => {
    if (discard) {
      discard = false
      return
    }
    // Tasodifiy bosish (1 soniyadan qisqa) — xabar emas.
    if (result.durationSeconds < 1) return
    emit('recorded', result.file, Math.round(result.durationSeconds))
  },
})

const supported = isVoiceRecordingSupported()
const recording = computed(() => recorder.state.value === 'recording')
const requesting = computed(() => recorder.state.value === 'requesting')

function startRecording(): void {
  discard = false
  void recorder.start()
}

function send(): void {
  discard = false
  recorder.stop()
}

function cancel(): void {
  discard = true
  recorder.stop()
}
</script>

<template>
  <div
    v-if="supported"
    class="flex items-center"
  >
    <button
      v-if="!recording"
      type="button"
      class="grid size-10 place-items-center rounded-full text-slate-300 hover:bg-ink-800 hover:text-slate-100 disabled:opacity-50"
      :disabled="props.disabled || requesting"
      aria-label="Ovozli xabar yozish"
      title="Ovozli xabar"
      @click="startRecording"
    >
      <AppIcon
        name="mic"
        :size="19"
      />
    </button>

    <div
      v-else
      class="flex items-center gap-2 rounded-full bg-rose-500/15 py-1 pl-3 pr-1"
      role="status"
      aria-live="polite"
    >
      <span class="size-2 animate-pulse rounded-full bg-rose-400" />
      <span class="text-sm tabular-nums text-rose-100">{{ formatDuration(recorder.elapsedSeconds.value) }}</span>
      <button
        type="button"
        class="grid size-8 place-items-center rounded-full text-rose-100 hover:bg-rose-500/20"
        aria-label="Yozuvni bekor qilish"
        @click="cancel"
      >
        <AppIcon
          name="close"
          :size="16"
        />
      </button>
      <button
        type="button"
        class="grid size-8 place-items-center rounded-full bg-brand-500 text-on-brand hover:bg-brand-600"
        aria-label="Ovozli xabarni yuborish"
        @click="send"
      >
        <AppIcon
          name="send"
          :size="15"
        />
      </button>
    </div>

    <p
      v-if="recorder.errorMessage.value !== null"
      class="ml-2 max-w-xs text-[11px] text-rose-300"
      role="alert"
    >
      {{ recorder.errorMessage.value }}
    </p>
  </div>
</template>
