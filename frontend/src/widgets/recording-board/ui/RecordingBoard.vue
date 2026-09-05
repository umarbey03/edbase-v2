<script setup lang="ts">
import { computed, ref } from 'vue'

import {
  isAwaitingComposition,
  RecordingCard,
  recordingItemTitle,
  updateRecordingVisibility,
} from '@/entities/recording'
import { useRecordingList } from '@/features/recording-list/model/useRecordingList'
import RecordingPlayerModal from '@/features/recording-player/ui/RecordingPlayerModal.vue'
import { SessionReviewModal } from '@/features/session-review'
import { toUserMessage } from '@/shared/api'
import { useConfirm } from '@/shared/lib/useConfirm'
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
const confirm = useConfirm()

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
   TUNGI MONTAJ — RO'YXAT DARAJASIDAGI TUSHUNTIRISH
   ==========================================================================

   🔴 NIMA UCHUN BU BLOK BOR (kartochkadagi nishon YETMAYDI). Yangi quvur
   ikkita narsani ko'rsatadiki, ularning IKKALASI ham "nosozlik" ga
   o'xshaydi va ikkalasi ham ONGLI:

     1. Dars tugagan, lekin video hali yo'q — u KECHASI tayyorlanadi.
        Kartochkada bu "Tungi montaj navbatida" deb turadi; izohsiz xodim
        buni "yozuv ishlamabdi" deb o'qiydi.
     2. Solishtiruv bosqichida BITTA darsning IKKITA qatori ro'yxatda
        yonma-yon turadi (bir xil nom, bir xil sana, ikki xil usul).

   Blok FAQAT ro'yxatda tungi montaj qatori bo'lganda chiziladi: 33 ta
   guruhning hammasi standart quvurda ekan, doimiy izoh shunchaki
   shovqin bo'lardi. */

/** Joriy (filtrlangan) ro'yxatda tungi montaj qatori bormi. */
const hasNightPipeline = computed(() =>
  list.items.value.some((item) => item.recording.pipeline === 'TrackComposition'),
)

/**
 * Montaj NAVBATIDA yoki montaj JARAYONIDA turgan yozuvlar soni.
 *
 * ⚠️ ATAYLAB "tayyor emas" DEB SANALMAYDI: `Collecting` (dars hozir
 * ketyapti) va `Failed` (haqiqiy xato) bu songa kirmaydi — birinchisi eski
 * quvurda ham bor, ikkinchisi esa BOSHQA amal talab qiladi (sababni o'qish,
 * kutish emas).
 */
const awaitingCompositionCount = computed(
  () => list.items.value.filter((item) => isAwaitingComposition(item.recording)).length,
)

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

/* ==========================================================================
   R5 — "HAMMASINI OCH/YOP" (loyiha egasi, 2026-08-15)
   ========================================================================== */

/**
 * ★ NEGA KERAK BO'LIB QOLDI: yozuv default holatda ENDI YASHIRIN
 * (`SessionRecording.IsVisibleToStudents` standarti `false`ga o'zgardi).
 * Bitta-bitta ochish tezlikda ishlaydigan bo'lim uchun o'nlab bosishni
 * talab qilardi — bu tugma AYNAN o'sha standart o'zgarishini xavfsiz
 * qiladi (izoh: `SessionRecording.IsVisibleToStudents`, "endi bu vosita
 * bor" bandi).
 *
 * ★ RO'YXATDAGI (joriy filtr — qidiruv/guruh/sana oralig'i) yozuvlar
 * ustida ishlaydi, BARCHA YOZUVLAR emas: xodim ko'rib turgan narsani
 * boshqaradi, ko'rinmas qatorlarga tegmaydi — bu ham xavfsizroq (tasodifan
 * boshqa oy yozuvlarini ochib qo'ymaslik), ham tushunarliroq.
 */
const hasHidden = computed(() =>
  list.items.value.some((item) => item.recording.isPlayable && !item.recording.isVisibleToStudents),
)
const hasVisible = computed(() =>
  list.items.value.some((item) => item.recording.isPlayable && item.recording.isVisibleToStudents),
)

const bulkPending = ref(false)

/**
 * Faqat TAYYOR (`isPlayable`) va HALI maqsadli holatda BO'LMAGAN
 * yozuvlarga tegadi — allaqachon mos holatdagi qatorga PATCH yuborish
 * ortiqcha so'rov va xatoni oshirish xavfi (masalan tayyor bo'lmagan
 * yozuvni ochishga urinish 409 qaytaradi).
 */
async function bulkSetVisibility(visible: boolean): Promise<void> {
  const targets = list.items.value.filter(
    (item) => item.recording.isPlayable && item.recording.isVisibleToStudents !== visible,
  )
  if (targets.length === 0) return

  const ok = await confirm({
    title: visible ? 'Hammasini ochish' : 'Hammasini yopish',
    message: visible
      ? `Joriy ro‘yxatdagi ${targets.length} ta yozuv o‘quvchilarga ochiladi.`
      : `Joriy ro‘yxatdagi ${targets.length} ta yozuv o‘quvchilardan yashiriladi.`,
    confirmLabel: visible ? 'Ochish' : 'Yashirish',
    tone: visible ? 'primary' : 'danger',
    details: ['Har birini keyin alohida ham qaytarish mumkin.'],
  })
  if (!ok) return

  visibilityError.value = null
  bulkPending.value = true
  try {
    const results = await Promise.allSettled(
      targets.map((item) => updateRecordingVisibility(item.recording.id, visible)),
    )
    const firstFailure = results.find(
      (result): result is PromiseRejectedResult => result.status === 'rejected',
    )
    if (firstFailure !== undefined) visibilityError.value = toUserMessage(firstFailure.reason)
    list.refetch()
  } finally {
    bulkPending.value = false
  }
}
</script>

<template>
  <BaseCard title="Yozuvlar">
    <!--
      🔴 TUZATILDI: qidiruv/guruh/sana filtrlari ILGARI `#actions` ichida,
      sarlavha bilan BITTA qatorda turardi. "Hammasini och/yop" tugmalari
      qo'shilgach (bu ikkalasi + "Yangilash" + qidiruv + guruh + 2 sana
      maydoni = 7 element) qator kartochka kengligidan CHIQIB ketardi —
      `flex-wrap` faqat BUTUN blokni sarlavha ostiga tushirar edi, ichidagi
      tor maydonlar (sana, tanlagich) esa siqilib/kesilib qolardi.
      Endi FILTRLAR alohida panjarada (boshqa boshqaruv sahifalaridagi
      naqsh — `ManageGroupsPage`/`ManageUsersPage`), `#actions` da esa
      FAQAT UCH tugma qoladi va ular istalgan kenglikda joylashadi.
    -->
    <template #actions>
      <div class="flex flex-wrap items-center gap-2">
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

        <!--
          R5 · "Hammasini och/yop" — joriy (filtrlangan) ro'yxat ustida.
          Ikkalasi ham o'chiriladi, agar shu holatga o'tkaziladigan tayyor
          yozuv qolmagan bo'lsa (`hasHidden`/`hasVisible`).
        -->
        <BaseButton
          size="sm"
          variant="secondary"
          :disabled="!hasHidden"
          :loading="bulkPending"
          @click="bulkSetVisibility(true)"
        >
          <template #icon>
            <AppIcon
              name="eye"
              :size="13"
            />
          </template>
          Hammasini ochish
        </BaseButton>
        <BaseButton
          size="sm"
          variant="secondary"
          :disabled="!hasVisible"
          :loading="bulkPending"
          @click="bulkSetVisibility(false)"
        >
          <template #icon>
            <AppIcon
              name="eye-off"
              :size="13"
            />
          </template>
          Hammasini yopish
        </BaseButton>
      </div>
    </template>

    <!-- Filtrlar: telefonda ustun, sm dan boshlab yonma-yon (boshqa boshqaruv sahifalari bilan bir xil panjara). -->
    <div class="mb-4 grid gap-2.5 sm:grid-cols-2 lg:grid-cols-4">
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
          class="zn-input pl-9"
          placeholder="Nomi yoki guruh bo‘yicha qidirish..."
        >
      </label>

      <select
        v-if="showGroupFilter"
        :value="list.groupId.value === null ? '' : String(list.groupId.value)"
        class="zn-input"
        aria-label="Guruh bo‘yicha filtr"
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
        class="zn-input"
        aria-label="Boshlanish sanasi"
        title="Boshlanish sanasi"
      >
      <input
        v-model="list.to.value"
        type="date"
        class="zn-input"
        aria-label="Tugash sanasi"
        title="Tugash sanasi"
      >
    </div>

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
      <!--
        Tungi montaj izohi — FAQAT ro'yxatda shunday qator bo'lganda.
        Sabab va matnning ohangi skriptdagi izohda: bu OGOHLANTIRISH emas,
        shuning uchun sariq/qizil emas, brend rangida.
      -->
      <div
        v-if="hasNightPipeline"
        class="mb-4 rounded-xl border border-brand-500/25 bg-brand-500/10 px-4 py-3 text-xs leading-relaxed text-brand-300"
      >
        <p>
          <span class="font-semibold">«Tungi montaj»</span> nishoni qo‘yilgan darslarning
          videosi dars tugashi bilan emas, kechasi tayyorlanadi va ertalab ochiladi.
          <template v-if="awaitingCompositionCount > 0">
            Hozir {{ awaitingCompositionCount }} ta yozuv shu navbatda — bu xato emas,
            ularni ertalab qayta tekshiring.
          </template>
        </p>
        <p class="mt-1.5 text-dim">
          Usullarni solishtirish uchun bitta darsda ikkita yozuv bo‘lishi mumkin: biri
          standart, biri tungi montaj. Ular nishoni bilan farqlanadi.
        </p>
      </div>

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
