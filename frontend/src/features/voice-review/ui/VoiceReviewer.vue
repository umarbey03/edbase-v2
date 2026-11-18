<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue'

import { formatDuration, isVoiceRecordingSupported, useVoiceRecorder } from '@/shared/lib/useVoiceRecorder'
import { AppIcon, BaseButton, BaseModal } from '@/shared/ui'

import { renderVoiceReview, type ReviewComment } from '../lib/render-voice-review'

/**
 * ════════════════════════════════════════════════════════════════════════
 *  OVOZ USTIGA OVOZ IZOHI (loyiha egasi, 2026-11-18)
 * ════════════════════════════════════════════════════════════════════════
 *
 * *"Kurator o'quvchining ovozli xabarini eshitib boradi, qaysidir joyida
 * feedback berish kerak bo'lsa to'xtatadi, izoh beradi, keyin davom
 * ettiradi. Yuborganda avval o'quvchining ovozi boshlanadi, orasida
 * kurator izohlari bo'ladi."*
 *
 * Oqim: ▶ eshitish → «Shu yerda izoh» (ovoz to'xtaydi, mikrofon yoqiladi)
 * → «Izohni tugatish» → ▶ davom … → «Yuborish». Yakuniy fayl brauzerda
 * yig'iladi (`renderVoiceReview`) va oddiy ovozli xabar bo'lib ketadi.
 */
const props = defineProps<{
  /** O'quvchining ovozi; `null` — oyna yopiq. */
  source: Blob | null
}>()

const emit = defineEmits<{ close: [], done: [file: File, durationSeconds: number] }>()

/** Bitta izoh — 2 daqiqagacha; jami natija chat chegarasidan oshmasin. */
const COMMENT_MAX_SECONDS = 120
const MAX_TOTAL_SECONDS = 270

const open = computed(() => props.source !== null)
const audio = ref<HTMLAudioElement | null>(null)
const sourceUrl = ref<string | null>(null)
const duration = ref(0)
const position = ref(0)
const playing = ref(false)
const comments = ref<(ReviewComment & { url: string })[]>([])
const busy = ref(false)
const error = ref<string | null>(null)

let pendingAt = 0
let discard = false

const recorder = useVoiceRecorder({
  maxSeconds: COMMENT_MAX_SECONDS,
  maxBytes: 4 * 1024 * 1024,
  onRecorded: ({ file, durationSeconds }) => {
    if (discard || durationSeconds < 1) {
      discard = false
      return
    }
    comments.value = [...comments.value, {
      at: pendingAt,
      blob: file,
      durationSeconds,
      url: URL.createObjectURL(file),
    }].sort((a, b) => a.at - b.at)
  },
})

const supported = isVoiceRecordingSupported()
const recording = computed(() => recorder.state.value !== 'idle')
const totalSeconds = computed(
  () => duration.value + comments.value.reduce((sum, c) => sum + c.durationSeconds, 0),
)
const tooLong = computed(() => totalSeconds.value > MAX_TOTAL_SECONDS)

function cleanup(): void {
  audio.value?.pause()
  if (sourceUrl.value !== null) URL.revokeObjectURL(sourceUrl.value)
  comments.value.forEach((c) => URL.revokeObjectURL(c.url))
  sourceUrl.value = null
  comments.value = []
  duration.value = 0
  position.value = 0
  playing.value = false
  error.value = null
  busy.value = false
}

watch(
  () => props.source,
  async (blob) => {
    cleanup()
    if (blob === null) return
    sourceUrl.value = URL.createObjectURL(blob)
    // WebM yozuvida davomiylik sarlavhada yo'q (`Infinity`) — dekodlab o'lchaymiz.
    try {
      const context = new OfflineAudioContext(1, 1, 16_000)
      const buffer = await context.decodeAudioData(await blob.arrayBuffer())
      duration.value = buffer.duration
    } catch {
      error.value = 'Bu ovozni brauzer o‘qiy olmadi. Chrome yoki Edge’da ochib ko‘ring.'
    }
  },
  { immediate: true },
)

onBeforeUnmount(cleanup)

function toggle(): void {
  const el = audio.value
  if (el === null || recording.value) return
  if (el.paused) void el.play()
  else el.pause()
}

function seek(event: Event): void {
  const el = audio.value
  if (el === null) return
  el.currentTime = Number((event.target as HTMLInputElement).value)
}

function startComment(): void {
  const el = audio.value
  el?.pause()
  pendingAt = el?.currentTime ?? 0
  discard = false
  void recorder.start()
}

function finishComment(): void {
  discard = false
  recorder.stop()
}

function cancelComment(): void {
  discard = true
  recorder.stop()
}

function removeComment(index: number): void {
  const [removed] = comments.value.splice(index, 1)
  if (removed !== undefined) URL.revokeObjectURL(removed.url)
}

function jumpTo(at: number): void {
  if (audio.value !== null) audio.value.currentTime = at
}

async function send(): Promise<void> {
  if (props.source === null || comments.value.length === 0) return
  busy.value = true
  error.value = null
  try {
    const result = await renderVoiceReview(props.source, comments.value)
    emit('done', result.file, result.durationSeconds)
  } catch {
    error.value = 'Ovozni yig‘ib bo‘lmadi. Qayta urinib ko‘ring.'
  } finally {
    busy.value = false
  }
}

function markerLeft(at: number): string {
  return duration.value > 0 ? `${Math.min(100, (at / duration.value) * 100)}%` : '0%'
}
</script>

<template>
  <BaseModal
    :open="open"
    title="Ovozli izoh"
    wide
    @close="emit('close')"
  >
    <div class="space-y-4">
      <p class="text-sm text-slate-400">
        O‘quvchi ovozini eshiting. Xato joyda <b class="text-slate-200">«Shu yerda izoh»</b> ni
        bosing — ovoz to‘xtaydi va sizning izohingiz yoziladi. Tugatgach, eshitishni davom
        ettiring. O‘quvchi avval o‘z ovozini, orasida esa izohlaringizni eshitadi.
      </p>

      <audio
        v-if="sourceUrl !== null"
        ref="audio"
        :src="sourceUrl"
        preload="auto"
        class="hidden"
        @play="playing = true"
        @pause="playing = false"
        @ended="playing = false"
        @timeupdate="position = audio?.currentTime ?? 0"
      />

      <div class="rounded-xl bg-ink-800 p-3 ring-1 ring-inset ring-line">
        <div class="flex items-center gap-3">
          <button
            type="button"
            class="grid size-11 shrink-0 place-items-center rounded-full bg-brand-500 text-on-brand hover:bg-brand-600 disabled:opacity-50"
            :disabled="recording || duration === 0"
            :aria-label="playing ? 'To‘xtatish' : 'Eshitish'"
            @click="toggle"
          >
            <AppIcon
              :name="playing ? 'pause' : 'play'"
              :size="18"
            />
          </button>
          <div class="relative min-w-0 flex-1">
            <input
              type="range"
              class="w-full accent-brand-500"
              min="0"
              :max="duration || 0"
              step="0.1"
              :value="position"
              :disabled="recording"
              aria-label="O‘quvchi ovozidagi o‘rin"
              @input="seek"
            >
            <!-- Izoh belgilari: qayerda to'xtatilgani ko'rinib tursin. -->
            <button
              v-for="(c, i) in comments"
              :key="c.url"
              type="button"
              class="absolute -top-1.5 size-3 -translate-x-1/2 rounded-full bg-amber-400 ring-2 ring-ink-800"
              :style="{ left: markerLeft(c.at) }"
              :title="`${i + 1}-izoh · ${formatDuration(c.at)}`"
              @click="jumpTo(c.at)"
            />
          </div>
          <span class="shrink-0 text-xs tabular-nums text-slate-400">
            {{ formatDuration(position) }} / {{ formatDuration(duration) }}
          </span>
        </div>

        <div class="mt-3 flex flex-wrap items-center gap-2">
          <BaseButton
            v-if="!recording"
            variant="secondary"
            :disabled="!supported || duration === 0"
            @click="startComment"
          >
            <AppIcon
              name="mic"
              :size="15"
            />
            Shu yerda izoh ({{ formatDuration(position) }})
          </BaseButton>
          <template v-else>
            <span
              class="inline-flex items-center gap-2 rounded-full bg-rose-500/15 px-3 py-1.5 text-sm text-rose-100"
              role="status"
            >
              <span class="size-2 animate-pulse rounded-full bg-rose-400" />
              Izoh yozilmoqda · {{ formatDuration(recorder.elapsedSeconds.value) }}
            </span>
            <BaseButton @click="finishComment">
              <AppIcon
                name="check"
                :size="15"
              />
              Izohni tugatish
            </BaseButton>
            <BaseButton
              variant="ghost"
              @click="cancelComment"
            >
              Bekor
            </BaseButton>
          </template>
        </div>
        <p
          v-if="!supported"
          class="mt-2 text-xs text-amber-300"
        >
          Brauzeringiz ovoz yozishni qo‘llamaydi (HTTPS va Chrome/Edge kerak).
        </p>
        <p
          v-if="recorder.errorMessage.value !== null"
          class="mt-2 text-xs text-rose-300"
          role="alert"
        >
          {{ recorder.errorMessage.value }}
        </p>
      </div>

      <ol
        v-if="comments.length > 0"
        class="space-y-2"
      >
        <li
          v-for="(c, i) in comments"
          :key="c.url"
          class="flex items-center gap-2 rounded-lg bg-ink-800/60 px-3 py-2 text-sm"
        >
          <button
            type="button"
            class="shrink-0 rounded-md bg-amber-400/15 px-2 py-0.5 text-xs font-semibold tabular-nums text-amber-200 hover:bg-amber-400/25"
            title="Shu joyga o‘tish"
            @click="jumpTo(c.at)"
          >
            {{ i + 1 }} · {{ formatDuration(c.at) }}
          </button>
          <audio
            :src="c.url"
            controls
            preload="metadata"
            class="h-8 min-w-0 flex-1"
          />
          <span class="shrink-0 text-xs tabular-nums text-slate-400">{{ formatDuration(c.durationSeconds) }}</span>
          <button
            type="button"
            class="grid size-8 shrink-0 place-items-center rounded-full text-slate-400 hover:bg-ink-700 hover:text-rose-300"
            aria-label="Izohni o‘chirish"
            @click="removeComment(i)"
          >
            <AppIcon
              name="trash"
              :size="15"
            />
          </button>
        </li>
      </ol>
      <p
        v-else
        class="text-xs text-slate-500"
      >
        Hali izoh yo‘q.
      </p>

      <p
        v-if="tooLong"
        class="text-xs text-amber-300"
      >
        Jami {{ formatDuration(totalSeconds) }} — {{ formatDuration(MAX_TOTAL_SECONDS) }} dan oshmasin.
        Izohlarni qisqartiring.
      </p>
      <p
        v-if="error !== null"
        class="text-sm text-rose-300"
        role="alert"
      >
        {{ error }}
      </p>
    </div>

    <template #footer>
      <div class="flex items-center justify-between gap-2">
        <span class="text-xs tabular-nums text-slate-500">Jami: {{ formatDuration(totalSeconds) }}</span>
        <div class="flex gap-2">
          <BaseButton
            variant="ghost"
            @click="emit('close')"
          >
            Bekor
          </BaseButton>
          <BaseButton
            :loading="busy"
            :disabled="comments.length === 0 || recording || tooLong"
            @click="send"
          >
            <AppIcon
              name="send"
              :size="15"
            />
            Yuborish
          </BaseButton>
        </div>
      </div>
    </template>
  </BaseModal>
</template>
