<script setup lang="ts">
import { useQuery } from '@tanstack/vue-query'
import { computed, ref } from 'vue'

import {
  fetchRecordingSection,
  formatRecordingDuration,
  formatRecordingSize,
  recordingItemTitle,
  recordingStatusLabel,
  recordingStatusTone,
} from '@/entities/recording'
import { useRecordingList } from '@/features/recording-list/model/useRecordingList'
import RecordingPlayerModal from '@/features/recording-player/ui/RecordingPlayerModal.vue'
import { formatDateTime } from '@/shared/lib/datetime'
import { AppIcon, BaseBadge, BaseButton, DataStatus } from '@/shared/ui'

/**
 * O'QUVCHI — DARS YOZUVLARI.
 *
 * ★ ESKI ILOVADAN OLINGAN MATNLAR (`student.html`, 1811–1855-qatorlar):
 *   • guruh tanlagichi (`#rec-gsel`) — guruhlar tugmalar qatorida;
 *   • "N ta yozuv" hisoblagichi (`learn-rec-meta`);
 *   • bo'sh holat — "Bu guruhda hali yozuv yo'q";
 *   • qator: nom, ostida sana · davomiylik, o'ngda "▶ Ko'rish" tugmasi;
 *   • pleyer sarlavhasi — "Dars yozuvi".
 *
 * ★ ESKI ILOVADA BU EKRAN AMALDA OCHILMAS EDI: `student.html` da JS funksiyalari
 * (`initRecordings`, `loadRecordings`, `watchRecording`) bor, lekin ular
 * murojaat qiladigan `#rec-box`, `#rec-gsel`, `#rec-player` elementlari
 * markupda YO'Q va `initRecordings()` hech qayerdan chaqirilmaydi. Ya'ni
 * bugungi o'quvchi bu bo'limni ko'rmaydi. Shunga qaramay ekran qayta tiklandi:
 * server o'quvchiga yozuvlarni ATAYLAB beradi (`GET /recordings` va
 * `/recordings/{id}/link` o'quvchi tokeni bilan 200 — jonli tekshirilgan).
 *
 * ★ 5 TA TABGA TEGILMAGAN: bu sahifa "O'quv" tabining ICHKI sahifasi
 * (`oquv/yozuvlar`), xuddi "Vazifalarim" va "Testlarim" kabi. Eski ilovada ham
 * yozuvlar hisoblagichi AYNAN "O'quv" ekranida turgan (`learn-rec-meta`).
 *
 * ⚠️ QARZDOR O'QUVCHI: ro'yxat ochiladi (server uni bloklamaydi), lekin
 * "Ko'rish" bosilganda `/recordings/{id}/link` **403** qaytaradi va sababni
 * `detail` da yozadi ("To'lov qarzi … so'm — ruxsat etilgan chegara … so'm.
 * Shu sababli video darslarga kirish vaqtincha yopilgan…"). Bu matn pleyer
 * ichida o'zgarishsiz ko'rsatiladi — jonli tekshirilgan xatti-harakat.
 */
const list = useRecordingList()

/* ==========================================================================
   R5 — BO'LIM YOPILGAN HOLATI
   ========================================================================== */

/**
 * 🔴 KIRISH KARTOCHKASINI YASHIRISH YETARLI EMAS.
 *
 * "O'quv" ekranidagi kartochka bo'lim yopilganda chizilmaydi
 * (`StudentLearnPage`), LEKIN bu sahifaga BOSHQA yo'llar bilan ham
 * kelish mumkin: xatcho'p, brauzer tarixi, orqaga tugmasi. Shunda
 * o'quvchi bo'sh ro'yxat va "Bu guruhda hali yozuv yo'q" degan matnni
 * ko'rardi — bu YOLG'ON: yozuvlar bor, ular vaqtincha yopilgan.
 *
 * ★ SHU SABABLI ALOHIDA MATN: "hali yozuv yo'q" va "bo'lim yopilgan" —
 * ikki xil holat va o'quvchi ular orasidagi farqni bilishi kerak
 * (birinchisida kutadi, ikkinchisida o'quv bo'limiga murojaat qiladi).
 *
 * ⚠️ Bu MASLAHAT, chegara emas: yozuvni ochish serverda mustaqil
 * tekshiriladi (`/recordings/{id}/link` -> 403).
 */
const sectionQuery = useQuery({
  queryKey: ['recordings', 'section'],
  queryFn: ({ signal }) => fetchRecordingSection({ signal }),
})

/** Xato bo'lsa OCHIQ deb hisoblanadi — tarmoq nosozligi bo'limni yopmasin. */
const sectionOpen = computed(() => sectionQuery.data.value?.visible ?? true)

const playingId = ref<number | null>(null)
const playingTitle = ref('')

/** Tanlangan guruh nomi — bo'sh holat matnini aniqroq qilish uchun. */
const hasGroups = computed(() => list.groupOptions.value.length > 0)

function selectGroup(id: number | null): void {
  list.groupId.value = id
}

function play(recordingId: number, title: string): void {
  playingTitle.value = title
  playingId.value = recordingId
}
</script>

<template>
  <!--
    ★ `@container` — pastdagi ro'yxat setkasi OYNANI emas, SHU USTUNNI
    o'lchaydi (xuddi "Testlarim" sahifasidagidek). Ildizda, chunki element
    o'zini so'rovga sola olmaydi: `@container` va `@2xl:` bitta tugunda
    tursa, so'rov yuqoridagi konteynerga murojaat qilardi.

    `container-type: inline-size` faqat `layout style inline-size`
    cheklovini beradi (`paint` emas), shuning uchun quyidagi guruh
    tanlagichining `-mx-4` chetga chiqishi ham, fokus halqasi ham
    qirqilmaydi.
  -->
  <div class="@container">
    <div class="mb-3 ml-1 mt-2 flex items-center justify-between gap-2">
      <h2
        class="flex items-center gap-[7px] text-xs font-bold uppercase tracking-[1.4px] text-slate-400"
      >
        <AppIcon
          name="camera"
          :size="15"
        />
        Dars yozuvlari
      </h2>
      <!-- Eski `learn-rec-meta`: "N ta yozuv". -->
      <span
        class="text-[11.5px] text-slate-400"
        v-text="`${list.items.value.length} ta yozuv`"
      />
    </div>

    <!--
      Guruh tanlagichi — eski `#rec-gsel` tugmalari. "Barchasi" varianti
      QO'SHILDI: v2 da ro'yxat serverdan BIR SO'ROVDA barcha guruhlar bilan
      keladi, ya'ni "hammasini birga ko'rish" bepul imkoniyat.
    -->
    <div
      v-if="hasGroups"
      class="scroll-x-safe scrollbar-none -mx-4 mb-3 flex gap-2 px-4"
    >
      <button
        type="button"
        class="min-h-9 shrink-0 whitespace-nowrap rounded-[18px] border px-3.5 text-[12.5px] transition-colors"
        :class="
          list.groupId.value === null
            ? 'border-brand-500 bg-brand-500/14 font-semibold text-brand-500'
            : 'border-line bg-ink-900 font-medium text-slate-400'
        "
        @click="selectGroup(null)"
      >
        Barchasi
      </button>
      <button
        v-for="option in list.groupOptions.value"
        :key="option.id"
        type="button"
        class="min-h-9 shrink-0 whitespace-nowrap rounded-[18px] border px-3.5 text-[12.5px] transition-colors"
        :class="
          list.groupId.value === option.id
            ? 'border-brand-500 bg-brand-500/14 font-semibold text-brand-500'
            : 'border-line bg-ink-900 font-medium text-slate-400'
        "
        @click="selectGroup(option.id)"
        v-text="option.name"
      />
    </div>

    <!--
      Bo'lim yopilgan — bu BO'SH ro'yxat emas, boshqa holat (izohga qarang).
    -->
    <p
      v-if="!sectionOpen"
      class="rounded-[15px] border border-line bg-ink-900 px-5 py-8 text-center text-[13px] text-slate-400"
    >
      Dars yozuvlari hozircha yopiq. Savol bo‘lsa o‘quv bo‘limiga murojaat qiling.
    </p>

    <DataStatus
      v-else
      :pending="list.isPending.value"
      :error="list.errorMessage.value"
      :empty="list.items.value.length === 0"
      :retrying="list.isFetching.value"
      :skeleton-rows="3"
      empty-icon="camera"
      empty-title="Bu guruhda hali yozuv yo‘q"
      empty-text="Dars yozib olingach shu yerda paydo bo‘ladi."
      @retry="list.refetch()"
    >
      <!--
        ★ 2026-08-13: `flex flex-col` O'RNIGA SETKA (bo'shliq AYNAN o'sha
        10px — `gap-2.5`, ya'ni bitta ustunda ko'rinish bir piksel ham
        o'zgarmaydi). Karkas ustuni 1600px bo'lgach yozuv qatori ~1536px ga
        cho'zilardi: chapda nom, o'ngda "Ko'rish" tugmasi va orada bir metr
        bo'sh joy — eng ko'zga tashlanadigan "cho'zilgan telefon" joyi.

        Qator kartochkasi GORIZONTAL (nom + sana·davomiylik·hajm + tugma),
        shuning uchun unga ixcham test kartochkasidan ko'ra kengroq joy
        kerak: 2 ustun uchun 42rem, 3 ustun uchun 64rem — 1536px da har
        biri ~505px, meta qatori bitta satrga sig'adi. To'rtinchi ustun
        (~375px) sana·davomiylik·hajm zanjirini ikkiga bo'lib yuborardi.

        ★ Telefon: eng past chegara 42rem = 672px, karkas ustuni esa `lg`
        gacha 520px — birorta so'rov yonmaydi.
      -->
      <div class="grid gap-2.5 @2xl:grid-cols-2 @5xl:grid-cols-3">
        <!--
          Bu qator kartochkasida ham hover FAQAT chegarada: bosiladigan
          element — "Ko'rish" tugmasi, kartochkaning o'zi emas.
        -->
        <article
          v-for="item in list.items.value"
          :key="item.recording.id"
          class="flex items-center justify-between gap-2.5 rounded-[15px] border border-line bg-ink-900 p-3.5 transition-colors hover:border-line-strong"
        >
          <div class="min-w-0 flex-1">
            <p
              class="truncate text-[14.5px] font-bold"
              v-text="recordingItemTitle(item)"
            />
            <p class="mt-1 text-[12px] text-slate-400">
              <span v-text="formatDateTime(item.scheduledStart)" />
              <template v-if="formatRecordingDuration(item.recording.durationSeconds).length > 0">
                ·
                <span v-text="formatRecordingDuration(item.recording.durationSeconds)" />
              </template>
              <template v-if="formatRecordingSize(item.recording.sizeBytes).length > 0">
                ·
                <span v-text="formatRecordingSize(item.recording.sizeBytes)" />
              </template>
            </p>
          </div>

          <!--
            Tayyor bo'lmagan yozuv uchun tugma o'rniga HOLAT ko'rsatiladi:
            bosiladigan, lekin doim xato beradigan tugma chalg'itardi.
          -->
          <BaseBadge
            v-if="!item.recording.isPlayable"
            :tone="recordingStatusTone(item.recording.status)"
            size="sm"
          >
            {{ recordingStatusLabel(item.recording.status) }}
          </BaseBadge>
          <!--
            ★ `tap-expand`: `size="sm"` 36px baland, WCAG 2.5.5 esa 44px
            so'raydi. `BaseButton` o'lchov xaritasiga TEGILMAYDI — u butun
            ilovaniki va o'zgarsa har panelda joylashuv siljirdi. Bu yerda
            faqat bosiladigan maydon ko'rinmas `::after` bilan har tomondan
            6px kengayadi (36 + 12 = 48px), tugma o'zi o'sha-o'sha.
          -->
          <BaseButton
            v-else
            class="tap-expand shrink-0"
            size="sm"
            @click="play(item.recording.id, recordingItemTitle(item))"
          >
            <template #icon>
              <AppIcon
                name="play"
                :size="13"
              />
            </template>
            Ko‘rish
          </BaseButton>
        </article>
      </div>
    </DataStatus>

    <RecordingPlayerModal
      :recording-id="playingId"
      :title="playingTitle"
      @close="playingId = null"
    />
  </div>
</template>
