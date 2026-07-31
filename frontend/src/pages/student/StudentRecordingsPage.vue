<script setup lang="ts">
import { computed, ref } from 'vue'

import {
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
  <div>
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

    <DataStatus
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
      <div class="flex flex-col gap-2.5">
        <article
          v-for="item in list.items.value"
          :key="item.recording.id"
          class="flex items-center justify-between gap-2.5 rounded-[15px] border border-line bg-ink-900 p-3.5"
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
          <BaseButton
            v-else
            class="shrink-0"
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
