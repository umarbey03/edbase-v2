<script setup lang="ts">
import { computed } from 'vue'

import { formatDateTime } from '@/shared/lib/datetime'
import { AppIcon, BaseBadge, BaseButton } from '@/shared/ui'

import {
  formatRecordingDuration,
  formatRecordingSize,
  hasPipelineBadge,
  recordingCompositionNote,
  recordingDisplayStatusLabel,
  recordingDisplayStatusTone,
  recordingPipelineLabel,
  recordingPipelineTone,
  reviewVerdictLabel,
  reviewVerdictTone,
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
 *
 * ★★ QAYTA TIKLANDI (R29, 2026-08-14): "Ko'rilmagan / Tasdiqlandi /
 *    Muammo bor" nishoni va tahlil tugmasi. Ilgari bu yerda ular
 *    "v2 backendida bunday maydon ham, endpoint ham yo'q" degan izoh
 *    bilan olib tashlangan edi — endi ikkalasi ham bor
 *    (`SessionReview` entity'si va `/live-sessions/{id}/review`).
 *
 *    ⚠️ ESKISIDAN FARQ: nishon "AI tahlili" emas, O'QUV BO'LIMI XODIMI
 *    yozgan xulosa. Eski ilovadagi nom chalg'ituvchi edi.
 *
 *    🔴 NISHON O'QUVCHIDA CHIZILMAYDI va bu shart komponent ichida
 *    tekshirilmaydi: server o'quvchiga `hasReview: false` beradi, ya'ni
 *    `v-if` o'z-o'zidan yopiladi. Chegara SERVERDA (`RecordingService`)
 *    — bu yerda faqat uning natijasi o'qiladi.
 *
 *    TEXNIK holat nishoni ("Yozilmoqda", "Xato") HAM QOLADI: u boshqa
 *    savolga javob beradi ("fayl bormi?"), sifat nishoni esa
 *    ("dars qanday o'tdi?"). Ikkalasi bir vaqtda ma'noli.
 *
 * ★★ QO'SHILDI (yozuv quvuri v2): «Tungi montaj» nishoni va montaj holati.
 *    Holat nishoni endi `recordingDisplayStatusLabel` dan chiqadi, xom
 *    `status` dan emas — yangi quvurda dars tugagach ham `status`
 *    `'Active'` bo'lib qolaveradi va kartochka ertalabgacha "Yozilmoqda"
 *    deb turardi. Sabab va jadval `model/types.ts` da.
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
    /**
     * Xodim ko'rinishimi: sifat nishoni va ko'rinish kaliti FAQAT shunda
     * chiziladi (R29 / R5).
     *
     * ⚠️ QULAYLIK BAYROG'I, RUXSAT EMAS. O'quvchiga server `hasReview`
     * ni `false` beradi va ko'rinish endpointi `403` qaytaradi — ya'ni
     * `staff` noto'g'ri `true` bo'lsa ham hech narsa oshkor bo'lmaydi va
     * hech narsa o'zgarmaydi.
     */
    staff?: boolean
  }>(),
  { groupName: '', scheduledStart: '', staff: false },
)

const emit = defineEmits<{
  play: [recordingId: number]
  /**
   * R29: sifat tahlilini ochish. DARS id'si uzatiladi, yozuv id'si emas —
   * tahlil DARSGA bog'langan (sabab: `SessionReview` entity'si izohi).
   */
  review: [sessionId: number]
  /** R5: ko'rinishni almashtirish. Ikkinchi argument — YANGI holat. */
  visibility: [recordingId: number, visible: boolean]
}>()

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

/**
 * Holat nishoni — MONTAJNI HISOBGA OLGAN holda (SPEC 7.1).
 *
 * 🔴 Xom `recording.status` TO'G'RIDAN-TO'G'RI ISHLATILMAYDI: tungi montaj
 * quvurida dars tugagach ham u `'Active'` bo'lib qolaveradi (fayl ertalab
 * yakunlanadi), ya'ni ertalab soat 8 da olti soat oldin tugagan dars
 * "Yozilmoqda" deb turardi. Qoida bitta joyda — `model/types.ts`.
 */
const statusLabel = computed(() => recordingDisplayStatusLabel(props.recording))
const statusTone = computed(() => recordingDisplayStatusTone(props.recording))

/*
  QUVUR NISHONI. Standart quvur (`RoomComposite`) uchun yorliq BO'SH bo'ladi
  va nishon umuman chizilmaydi — 33 ta guruhning hammasi standart, ya'ni
  "Standart" yozuvi har kartochkada takrorlanib shovqin bo'lardi.

  ★ NIMA UCHUN BU KERAK: solishtiruv bosqichida BITTA darsning IKKITA yozuvi
  ro'yxatda yonma-yon turadi (bir xil nom, bir xil sana). Nishonsiz bu
  takrorlanish NOSOZLIKKA o'xshaydi.
*/
const showPipeline = computed(() => hasPipelineBadge(props.recording))
const pipelineLabel = computed(() => recordingPipelineLabel(props.recording.pipeline))
const pipelineTone = computed(() => recordingPipelineTone(props.recording.pipeline))

/**
 * "Fayl qani?" izohi — faqat KUTISH holatlarida (`Queued`, `Running`).
 *
 * ⚠️ XATO BLOKIDAN ALOHIDA VA BOSHQA RANGDA: bu yerda hech narsa
 * buzilmagan, shunchaki video ertalabga tayyor bo'ladi. Ikkalasini bitta
 * qizil blokka qo'shish aynan o'sha "yozuv yo'qolibdi" degan noto'g'ri
 * xulosani keltirib chiqarardi.
 */
const compositionNote = computed(() => recordingCompositionNote(props.recording.compositionStatus))

/** Xato matni serverdan keladi (masalan egress rad etgani) — o'zimiz yozmaymiz. */
const errorText = computed(() => props.recording.error ?? '')

function play(): void {
  if (!playable.value) return
  emit('play', props.recording.id)
}

/*
  ★ NISHON UCHTA HOLATNI KO'RSATADI, LEKIN MANBA IKKITA MAYDON:
  `hasReview === false` va `reviewStatus === 'NotReviewed'` FOYDALANUVCHI
  UCHUN bir xil ("hali xulosa yo'q"), shuning uchun ikkalasi ham
  "Ko'rilmagan" beradi (sabab: `reviewVerdictLabel` izohi).
*/
const reviewLabel = computed(() => reviewVerdictLabel(props.recording.reviewStatus))
const reviewTone = computed(() => reviewVerdictTone(props.recording.reviewStatus))
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
      :title="playable ? 'Yozuvni ko‘rish' : statusLabel"
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

      <!--
        Ikki nishon USTMA-UST, yonma-yon EMAS: chapda guruh nishoni turadi
        (`max-w-[60%]`) va uch element bitta qatorga sig'masdi — 290px li
        kartochkada ular bir-birining ustiga chiqardi.
      -->
      <span class="absolute right-3 top-3 z-10 flex flex-col items-end gap-1">
        <BaseBadge :tone="statusTone">
          {{ statusLabel }}
        </BaseBadge>
        <!--
          Quvur nishoni. `RoomComposite` da `showPipeline` `false` — standart
          yo'l ATAYLAB jimgina qoladi (sabab skriptdagi izohda).

          `title` O'RAB TURGAN `span` DA: `BaseBadge` `title` propini
          e'lon qilmaydi va `vue-tsc` uni noma'lum prop deb rad etadi.
        -->
        <span
          v-if="showPipeline"
          title="Bu yozuv dars davomida emas, kechasi montaj qilinadi. Bitta darsning ikkita yozuvi bo‘lsa — ular solishtirish uchun."
        >
          <BaseBadge :tone="pipelineTone">
            {{ pipelineLabel }}
          </BaseBadge>
        </span>
      </span>

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
          ══════════════════════════════════════════════════════════════
           TUNGI MONTAJ IZOHI — "hali yo'q", "yiqildi" EMAS
          ══════════════════════════════════════════════════════════════

          🔴 PASTDAGI XATO BLOKIDAN ATAYLAB BOSHQACHA: qizil emas, brend
             rangida va matni QACHON tayyor bo'lishini aytadi. Ikkalasi
             bir xil ko'rinsa xodim kutilayotgan yozuvni "yiqilgan" deb
             o'qirdi va aynan shu — bu quvurni yozishga sabab bo'lgan
             shikoyatning o'zi.

          Ikkalasi bir vaqtda chiqishi NAZARIY jihatdan mumkin (kutish
          holatida ham `error` da eski, o'tib ketgan xato qolgan bo'lsa).
          Bu holda ikkalasi ham ko'rsatiladi va bu TO'G'RI: serverning xato
          matnini yashirish undan ko'ra yomonroq bo'lardi.
        -->
        <p
          v-if="compositionNote.length > 0"
          class="mt-2.5 rounded-md border-l-2 border-brand-500 bg-brand-500/10 px-2 py-1.5 text-[11px] leading-relaxed text-brand-300"
          v-text="compositionNote"
        />

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

      <!--
        ══════════════════════════════════════════════════════════════════
         R29 + R5 — XODIM QATORI
        ══════════════════════════════════════════════════════════════════

        🔴 BUTUN BLOK `staff` GA BOG'LANGAN, LEKIN U YAGONA HIMOYA EMAS:
           `hasReview` o'quvchiga har doim `false`, ko'rinish endpointi esa
           unga `403` beradi. Ya'ni bayroq — QULAYLIK (foydasiz tugma
           ko'rsatilmasin), chegara emas.
      -->
      <div
        v-if="staff"
        class="flex flex-wrap items-center gap-1.5 border-t border-line pt-2.5"
      >
        <button
          type="button"
          class="inline-flex min-h-9 items-center gap-1 rounded-lg px-1.5 transition-colors hover:bg-ink-800"
          :title="recording.hasReview ? 'Sifat tahlilini ochish' : 'Tahlil yozish'"
          @click="emit('review', recording.sessionId)"
        >
          <BaseBadge :tone="reviewTone">
            {{ reviewLabel }}
          </BaseBadge>
          <AppIcon
            name="chevron-right"
            :size="12"
            class="text-dim"
          />
        </button>

        <!--
          ★ KO'RINISH KALITI. Yorliq HOZIRGI holatni emas, BOSILGANDA NIMA
            BO'LISHINI aytadi ("Yashirish" -> yashiradi): tugma yorlig'i
            buyruq bo'lishi kerak, holat ko'rsatkichi emas — aks holda
            xodim teskarisini bosardi.
        -->
        <button
          type="button"
          class="ml-auto inline-flex min-h-9 items-center gap-1 rounded-lg px-2 text-[11px] font-semibold transition-colors"
          :class="
            recording.isVisibleToStudents
              ? 'text-slate-400 hover:bg-ink-800'
              : 'text-amber-300 hover:bg-amber-500/10'
          "
          :title="
            recording.isVisibleToStudents
              ? 'Hozir o‘quvchilarga ko‘rinadi. Bosilsa — yashiriladi (masalan sifati past yoki mazmuni noto‘g‘ri bo‘lsa, o‘quvchidan vaqtincha berkitish uchun; yozuvning o‘zi o‘chmaydi).'
              : 'O‘quvchilardan yashirilgan — faqat xodimlar ko‘radi. Bosilsa — qayta ochiladi.'
          "
          @click="emit('visibility', recording.id, !recording.isVisibleToStudents)"
        >
          <AppIcon
            :name="recording.isVisibleToStudents ? 'eye' : 'eye-off'"
            :size="13"
          />
          {{ recording.isVisibleToStudents ? 'Yashirish' : 'Ochish' }}
        </button>
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
