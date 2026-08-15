<script setup lang="ts">
import { computed, ref } from 'vue'

import { RecordingCard, recordingItemTitle, updateRecordingVisibility } from '@/entities/recording'
import { useRecordingList } from '@/features/recording-list/model/useRecordingList'
import RecordingPlayerModal from '@/features/recording-player/ui/RecordingPlayerModal.vue'
import { SessionReviewModal } from '@/features/session-review'
import { toUserMessage } from '@/shared/api'
import { AppIcon, BaseButton, BaseCard, DataStatus } from '@/shared/ui'

/**
 * DARS YOZUVLARI RO'YXATI — xodim ko'rinishi.
 *
 * ★ ESKI ILOVADAN (`academic.html`, `#recordings` bo'limi, 678–702-qatorlar):
 * kartochka ichida "Yozuvlar" sarlavhasi, o'ngda qidiruv maydoni
 * ("Nomi yoki ustoz bo'yicha qidirish..."), guruh tanlagichi
 * ("Barcha guruhlar") va "↺ Yangilash" tugmasi; pastda kartochkalar setkasi.
 *
 * ★ ESKISIDA BO'LMAGAN, LEKIN MAJBURIY QO'SHILGAN NARSA — SANA ORALIG'I.
 * Eski server butun tarixni birdan berardi. v2 da `GET /api/v1/recordings`
 * `from`/`to` SIZ chaqirilsa **500** qaytaradi va oraliq 92 kundan oshmasligi
 * kerak (ikkalasi ham jonli tekshirilgan). Ya'ni oraliq — interfeys bezagi
 * emas, endpointning shartidir; shuning uchun u ko'rinadigan filtr qilindi.
 *
 * ★ "Dars turi" tanlagichi (eski "Ustoz darsi / Yordamchi darsi") TUSHIRIB
 * QOLDIRILDI: v2 ro'yxat qatorida dars turi kelmaydi va tanlagich hech
 * narsani filtrlay olmasdi.
 *
 * NEGA WIDGET: bu ko'rinishni IKKI sahifa ishlatadi — o'quv bo'limining
 * "Dars yozuvlari" bo'limi va guruh ichidagi "Yozuvlar" tabi. Ikkalasi
 * `pages/` da yashaydi, ya'ni umumiy qism eng yuqori mos qatlamda turadi.
 */
const props = withDefaults(
  defineProps<{
    /** Berilsa guruh tanlagichi chizilmaydi va ro'yxat shu guruh bilan cheklanadi. */
    fixedGroupId?: number | null
  }>(),
  { fixedGroupId: null },
)

const list = useRecordingList({ fixedGroupId: props.fixedGroupId })

const showGroupFilter = computed(() => props.fixedGroupId === null)

/** Ochiq pleyer: `null` — yopiq. */
const playingId = ref<number | null>(null)
const playingTitle = ref('')

function play(recordingId: number): void {
  const item = list.items.value.find((row) => row.recording.id === recordingId)
  playingTitle.value = item === undefined ? 'Dars yozuvi' : recordingItemTitle(item)
  playingId.value = recordingId
}

function closePlayer(): void {
  playingId.value = null
}

/* ==========================================================================
   R29 — SIFAT TAHLILI
   ========================================================================== */

/**
 * Ochiq tahlil oynasi: `null` — yopiq.
 *
 * ⚠️ BU YOZUV ID'SI EMAS, DARS ID'SI. Tahlil DARSGA bog'langan (sabab:
 * `SessionReview` entity'si izohi) — qayta yozib olingan darsda ikkita
 * yozuv bo'lsa ham, ikkalasi AYNI tahlilni ochadi.
 */
const reviewSessionId = ref<number | null>(null)
const reviewTitle = ref('')
const reviewGroupName = ref('')
const reviewScheduledStart = ref('')

function openReview(sessionId: number): void {
  const item = list.items.value.find((row) => row.recording.sessionId === sessionId)
  reviewTitle.value = item === undefined ? '' : recordingItemTitle(item)
  reviewGroupName.value = item?.groupName ?? ''
  reviewScheduledStart.value = item?.scheduledStart ?? ''
  reviewSessionId.value = sessionId
}

/* ==========================================================================
   R5 — KO'RINISH KALITI
   ========================================================================== */

/**
 * Amaldagi so'rov xatosi (403 / 409).
 *
 * ★ NEGA ALOHIDA XABAR MAYDONI, `alert()` EMAS: server SABABNI yozadi va
 * u foydalanuvchiga kerak. Ikki eng muhim holat:
 *   • `403` — yozuvni O'QUV BO'LIMI yopgan, ustoz qayta ocha olmaydi;
 *   • `409` — yozuv hali tayyor emas.
 * Ikkalasi ham "tugma ishlamadi" emas, TUSHUNTIRISH talab qiladi.
 */
const visibilityError = ref<string | null>(null)

async function toggleVisibility(recordingId: number, visible: boolean): Promise<void> {
  visibilityError.value = null
  try {
    await updateRecordingVisibility(recordingId, visible)
    // ★ Ro'yxat SERVERDAN qayta o'qiladi, mahalliy qiymat qo'lda
    //   o'zgartirilmaydi: ko'rinish uchta kalitning ko'paytmasi va
    //   mijozda uni "hisoblab" qo'yish jimgina yolg'on bo'lardi.
    list.refetch()
  } catch (cause) {
    visibilityError.value = toUserMessage(cause)
  }
}
</script>

<template>
  <BaseCard title="Yozuvlar">
    <template #actions>
      <div class="flex flex-wrap items-center gap-2">
        <label class="relative">
          <span class="sr-only">Qidirish</span>
          <AppIcon
            name="search"
            :size="14"
            class="pointer-events-none absolute left-3 top-1/2 -translate-y-1/2 text-slate-500"
          />
          <input
            v-model="list.search.value"
            type="search"
            class="h-9 w-full min-w-0 rounded-lg border border-line bg-ink-950 pl-9 pr-3 text-xs text-slate-100 outline-none placeholder:text-slate-500 focus:border-line-strong sm:w-[260px]"
            placeholder="Nomi yoki guruh bo‘yicha qidirish..."
          >
        </label>

        <select
          v-if="showGroupFilter"
          :value="list.groupId.value === null ? '' : String(list.groupId.value)"
          class="h-9 rounded-lg border border-line bg-ink-950 px-2.5 text-xs text-slate-100 outline-none focus:border-line-strong"
          @change="
            list.groupId.value =
              ($event.target as HTMLSelectElement).value === ''
                ? null
                : Number(($event.target as HTMLSelectElement).value)
          "
        >
          <option value="">
            Barcha guruhlar
          </option>
          <option
            v-for="option in list.groupOptions.value"
            :key="option.id"
            :value="String(option.id)"
            v-text="option.name"
          />
        </select>

        <input
          v-model="list.from.value"
          type="date"
          class="h-9 rounded-lg border border-line bg-ink-950 px-2.5 text-xs text-slate-100 outline-none focus:border-line-strong"
          title="Boshlanish sanasi"
        >
        <input
          v-model="list.to.value"
          type="date"
          class="h-9 rounded-lg border border-line bg-ink-950 px-2.5 text-xs text-slate-100 outline-none focus:border-line-strong"
          title="Tugash sanasi"
        >

        <BaseButton
          size="sm"
          variant="secondary"
          :loading="list.isFetching.value"
          @click="list.refetch()"
        >
          <template #icon>
            <AppIcon
              name="refresh"
              :size="13"
            />
          </template>
          Yangilash
        </BaseButton>
      </div>
    </template>

    <!--
      R5: ko'rinish so'rovining xatosi. Serverning matni O'ZGARISHSIZ
      ko'rsatiladi — u sababni aniq aytadi ("o'quv bo'limi yopgan",
      "yozuv hali tayyor emas").

      ⚠️ BU BLOK PASTDAGI `v-if`/`v-else` ZANJIRIDAN TASHQARIDA turishi
         SHART: zanjir ichiga tushsa `DataStatus` dagi `v-else` unga
         bog'lanib qolardi va xato bo'lmaganda RO'YXAT UMUMAN
         chizilmasdi.
    -->
    <p
      v-if="visibilityError !== null"
      class="mb-3 rounded-xl border border-rose-500/25 bg-rose-500/10 px-4 py-3 text-xs text-rose-200"
      role="alert"
      v-text="visibilityError"
    />

    <!--
      Oraliq xatosi — serverga so'rov yubormasdan. Matn AYNAN serverdagidek
      ("Oraliq 92 kundan oshmasin.") — ikki xil ta'rif chalkashtirardi.
    -->
    <p
      v-if="list.rangeError.value !== null"
      class="rounded-xl border border-amber-500/25 bg-amber-500/10 px-4 py-3 text-xs text-amber-200"
      role="alert"
      v-text="list.rangeError.value"
    />

    <DataStatus
      v-else
      :pending="list.isPending.value"
      :error="list.errorMessage.value"
      :empty="list.items.value.length === 0"
      :retrying="list.isFetching.value"
      :skeleton-rows="3"
      empty-icon="camera"
      empty-title="Hozircha yozuvlar yo‘q"
      empty-text="Tanlangan oraliqda yozib olingan dars topilmadi."
      @retry="list.refetch()"
    >
      <!-- Setka eski ilovadagidek: `minmax(290px, 1fr)`. -->
      <div class="grid grid-cols-[repeat(auto-fill,minmax(290px,1fr))] gap-5">
        <RecordingCard
          v-for="item in list.items.value"
          :key="item.recording.id"
          :recording="item.recording"
          :title="recordingItemTitle(item)"
          :group-name="showGroupFilter ? (item.groupName ?? '') : ''"
          :scheduled-start="item.scheduledStart"
          staff
          @play="play"
          @review="openReview"
          @visibility="toggleVisibility"
        />
      </div>
    </DataStatus>

    <RecordingPlayerModal
      :recording-id="playingId"
      :title="playingTitle"
      @close="closePlayer"
    />

    <!--
      R29. `@saved` da ro'yxat qayta o'qiladi: nishon SERVER ma'lumotidan
      chizilishi kerak, mahalliy taxmindan emas.
    -->
    <SessionReviewModal
      :session-id="reviewSessionId"
      :title="reviewTitle"
      :group-name="reviewGroupName"
      :scheduled-start="reviewScheduledStart"
      @close="reviewSessionId = null"
      @saved="list.refetch()"
    />
  </BaseCard>
</template>
