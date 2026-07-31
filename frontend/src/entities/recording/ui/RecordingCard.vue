<script setup lang="ts">
import { computed } from 'vue'

import { formatDateTime } from '@/shared/lib/datetime'
import { AppIcon, BaseBadge, BaseButton } from '@/shared/ui'

import {
  formatRecordingDuration,
  formatRecordingSize,
  recordingStatusLabel,
  recordingStatusTone,
} from '../model/types'
import type { Recording } from '../model/types'

/**
 * Bitta yozuv kartochkasi.
 *
 * ★ TUZILISHI ESKI ILOVADAN (`academic.html`, `_recRows()`, 6066–6175-qatorlar):
 * yuqorida bosiladigan "afisha" maydoni + o'rtada oltin doiradagi ▶ tugmasi,
 * chap yuqorida guruh nishoni, o'ng pastda "⏱ davomiylik", pastki blokda
 * sarlavha, sana va o'ngda "Ko'rish" tugmasi. Setka ham o'sha:
 * `repeat(auto-fill, minmax(290px, 1fr))` — u ota komponentda.
 *
 * ESKISIDAN OLIB TASHLANGANLARI VA SABABI:
 *  • ustoz avatari va ismi — `RecordingListItemDto` da dars EGASI yo'q;
 *  • "Ko'rilmagan / Tasdiqlandi / Muammo bor" nishoni va AI tahlil tugmasi —
 *    v2 backendida bunday maydon ham, endpoint ham yo'q. O'rniga TEXNIK holat
 *    ("Yozilmoqda", "Xato") ko'rsatiladi: u haqiqiy va foydali.
 *
 * ▶ tugmasi ichidagi matn `text-on-brand` — oltin fonda `text-white` kontrasti
 * ~1.9:1 bo'lardi (eski ilovada ham u yerda to'q ko'k `#0f2d48` turgan).
 */
const props = withDefaults(
  defineProps<{
    recording: Recording
    title: string
    /** Bo'sh satr bo'lsa guruh nishoni chizilmaydi (guruh ichidagi ro'yxatda ortiqcha). */
    groupName?: string
    /** Dars jadval bo'yicha qachon boshlangani. Bo'sh bo'lsa yozuv sanasi ishlatiladi. */
    scheduledStart?: string
  }>(),
  { groupName: '', scheduledStart: '' },
)

const emit = defineEmits<{ play: [recordingId: number] }>()

const duration = computed(() => formatRecordingDuration(props.recording.durationSeconds))
const size = computed(() => formatRecordingSize(props.recording.sizeBytes))

/*
  Sana: darsning jadvaldagi vaqti afzal (xodim darsni AYNAN shu vaqt bilan
  eslaydi). U berilmagan joyda — yozuv yaratilgan payt.
*/
const dateLabel = computed(() =>
  formatDateTime(
    props.scheduledStart.length > 0 ? props.scheduledStart : props.recording.createdAt,
  ),
)

const playable = computed(() => props.recording.isPlayable)

/** Xato matni serverdan keladi (masalan egress rad etgani) — o'zimiz yozmaymiz. */
const errorText = computed(() => props.recording.error ?? '')

function play(): void {
  if (!playable.value) return
  emit('play', props.recording.id)
}
</script>

<template>
  <article
    class="flex flex-col overflow-hidden rounded-xl border border-line bg-ink-900 transition-colors hover:border-line-strong"
  >
    <!--
      Afisha maydoni. Eskisida balandligi 150px va gradient fon edi; bu yerda
      ham shunday. Yozuv tayyor bo'lmasa bosilmaydi — shuning uchun `button`
      `disabled` bo'ladi va kursor o'zgarmaydi.
    -->
    <button
      type="button"
      class="group relative flex h-[150px] w-full items-center justify-center bg-gradient-to-br from-ink-800 to-ink-950 disabled:cursor-default"
      :disabled="!playable"
      :title="playable ? 'Yozuvni ko‘rish' : recordingStatusLabel(recording.status)"
      @click="play"
    >
      <span
        v-if="groupName.length > 0"
        class="absolute left-3 top-3 z-10 max-w-[60%] truncate rounded-full bg-black/55 px-2 py-0.5 text-[11px] font-semibold text-slate-100"
        v-text="groupName"
      />

      <BaseBadge
        class="absolute right-3 top-3 z-10"
        :tone="recordingStatusTone(recording.status)"
      >
        {{ recordingStatusLabel(recording.status) }}
      </BaseBadge>

      <span
        v-if="playable"
        class="flex size-12 items-center justify-center rounded-full bg-brand-500 text-on-brand shadow-lg transition-transform duration-200 group-hover:scale-110"
        aria-hidden="true"
      >
        <AppIcon
          name="play"
          :size="18"
        />
      </span>
      <span
        v-else
        class="flex size-12 items-center justify-center rounded-full bg-ink-800 text-slate-500"
        aria-hidden="true"
      >
        <AppIcon
          name="clock"
          :size="18"
        />
      </span>

      <span
        v-if="duration.length > 0"
        class="absolute bottom-3 right-3 z-10 rounded-full bg-black/65 px-2 py-0.5 text-[11px] font-semibold text-slate-100"
      >
        ⏱ {{ duration }}
      </span>
    </button>

    <div class="flex flex-1 flex-col gap-3 p-4">
      <div>
        <h3
          class="line-clamp-2 text-[13px] font-bold leading-snug"
          :title="title"
          v-text="title"
        />
        <p class="mt-1.5 flex items-center gap-1.5 text-[11px] text-slate-400">
          <AppIcon
            name="calendar"
            :size="12"
          />
          <span v-text="dateLabel" />
          <template v-if="size.length > 0">
            <span aria-hidden="true">·</span>
            <span v-text="size" />
          </template>
        </p>

        <!--
          Xato matni ATAYLAB to'liq ko'rsatiladi: "Yozuv xizmati rad etdi: …"
          kabi xabar bo'lmasa, xodim yozuv nega yo'qligini bila olmasdi va
          o'quvchiga javob bera olmasdi.
        -->
        <p
          v-if="errorText.length > 0"
          class="mt-2.5 rounded-md border-l-2 border-rose-500 bg-rose-500/10 px-2 py-1.5 text-[11px] leading-relaxed text-rose-200"
          v-text="errorText"
        />
      </div>

      <div class="mt-auto flex items-center justify-between gap-2 border-t border-line pt-2.5">
        <span
          class="text-[11px] text-slate-500"
          v-text="recording.attempts > 1 ? `${recording.attempts} urinish` : ''"
        />
        <BaseButton
          size="sm"
          :disabled="!playable"
          @click="play"
        >
          <template #icon>
            <AppIcon
              name="play"
              :size="13"
            />
          </template>
          Ko‘rish
        </BaseButton>
      </div>
    </div>
  </article>
</template>
