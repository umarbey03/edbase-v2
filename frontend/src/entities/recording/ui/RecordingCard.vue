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
 * ▶ tugmasi ichidagi ikonka `text-on-brand` — brend fonidagi matn rangi
 * tokenda turadi (hozir oq, indigo fonda 5.9:1). `text-white` yozib
 * qo'yilmagan, chunki aksent almashsa faqat token o'zgarishi kerak.
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
    class="flex flex-col overflow-hidden rounded-2xl border border-line bg-ink-900 shadow-sm transition-shadow hover:shadow-md"
  >
    <!--
      Afisha maydoni. Eskisida balandligi 150px va gradient fon edi; bu yerda
      ham shunday. Yozuv tayyor bo'lmasa bosilmaydi — shuning uchun `button`
      `disabled` bo'ladi va kursor o'zgarmaydi.

      Gradient `ink-800 -> ink-750` (ilgari `ink-800 -> ink-950`): yorug'
      temada `ink-950` sahifa foni bo'lib qoldi va gradient ikki deyarli
      bir xil oq orasida yo'qolardi — afisha maydoni kartochkadan
      ajralmasdi.
    -->
    <button
      type="button"
      class="group relative flex h-[150px] w-full items-center justify-center bg-gradient-to-br from-ink-800 to-ink-750 disabled:cursor-default"
      :disabled="!playable"
      :title="playable ? 'Yozuvni ko‘rish' : recordingStatusLabel(recording.status)"
      @click="play"
    >
      <!--
        🔴 TUZATILDI (2026-08-11): `bg-black/55` (bu yerda) va `bg-black/65`
        (pastdagi davomiylik) → `bg-slate-900/70`.

        Oldingi izoh bu ikkisini "video posteri ustida turadi, poster rangi
        oldindan ma'lum emas" degan asos bilan qoldirgan edi. ASOS TO'G'RI
        EMAS: yuqoridagi `button` da haqiqiy kadr YO'Q — u
        `bg-gradient-to-br from-ink-800 to-ink-750`, ya'ni DOIM YORUG'
        (#f2f4f9 → #e9ecf5) va o'rtasida play ikonkasi turadi. Server
        posterini chizadigan kod hech qayerda yozilmagan.

        Ya'ni to'q pill yorug' gradient ustida "yamoq" bo'lib turardi —
        ilovaning boshqa hech bir joyida bunday element yo'q. `slate-900/70`
        — dizayn tizimining QORAYTIRUVCHI QATLAM (scrim) tokeni; modal va
        drawer foni ham shundan. Oq matn uning ustida 6.9:1.

        ★ Matn `text-white` da QOLADI (`slate-100` EMAS): rang qatlamga
        bog'liq, temaga emas — to'q pill ustida `slate-100` (#1b1d2a)
        ko'rinmasdi.

        ★ Agar kelajakda HAQIQIY poster (server kadri) qo'shilsa — bu
        qiymatni qaytarib to'qroq qilish kerak bo'ladi: 70% scrim oq slayd
        ustida ham yetadi, lekin qoida o'zgaradi.
      -->
      <span
        v-if="groupName.length > 0"
        class="absolute left-3 top-3 z-10 max-w-[60%] truncate rounded-full bg-slate-900/70 px-2 py-0.5 text-[11px] font-semibold text-white"
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

      <!-- Yuqoridagi izohga qarang: scrim tokeni + oq matn. -->
      <span
        v-if="duration.length > 0"
        class="absolute bottom-3 right-3 z-10 rounded-full bg-slate-900/70 px-2 py-0.5 text-[11px] font-semibold text-white"
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
