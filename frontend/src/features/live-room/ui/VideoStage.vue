<script setup lang="ts">
import { computed, ref, watch } from 'vue'

import type { ParticipantMediaSource } from '@/entities/session'
import { roleLabel } from '@/entities/user'
import { useBreakpoint } from '@/shared/lib/useBreakpoint'
import type { UserRoleName } from '@/shared/types'
import { AppIcon, BaseButton, BaseSpinner } from '@/shared/ui'

import type { MediaStatus, ParticipantTile } from '../model/useLiveKitRoom'
import VideoTile from './VideoTile.vue'

/**
 * Video sahna ALOHIDA komponent — bu ataylab qilingan.
 * Chat sekundiga bir necha marta yangilanadi; agar sahna ota-komponent shablonida
 * "ichma-ich" yozilganida, har bir yangi xabar 50 ta video katakchani ham qayta
 * patch qilishga majbur qilardi. Alohida komponentda esa prop'lar o'zgarmagani
 * uchun Vue yangilanishni butunlay o'tkazib yuboradi.
 *
 * ══════════════════════════════════════════════════════════════════════
 * IKKI KO'RINISH (2026-09-09, loyiha egasi: "Telegram bilan bir xil
 * funksionallikda bo'lsin — jadval ko'rinishida barcha o'quvchilar
 * ko'rinib turishi kerak").
 *
 *  • TO'R (grid) — standart: HAMMA ishtirokchi teng kataklarda, kamerasi
 *    o'chiq bo'lsa avatar bilan. Ilgari faqat ustoz (yoki gapirayotgan)
 *    katta chiqar, qolganlar esa kamerasi yoniq bo'lsagina pastdagi tor
 *    lentada ko'rinardi — ustoz sinfni KO'RMASDI.
 *  • SAHNA (spotlight) — ekran ulashuvi bor yoki foydalanuvchi katakni
 *    QADAGAN (bosgan) bo'lsa: bittasi katta, qolganlari lentada. Ekran
 *    ulashuvida matn o'qilishi kerak, to'rda u mayda bo'lib qolardi.
 * ══════════════════════════════════════════════════════════════════════
 */
const props = defineProps<{
  tiles: readonly ParticipantTile[]
  /** Presence'dan aniqlangan ustoz/kurator `userId` si. */
  hostUserId: number | null
  status: MediaStatus
  roleByUserId: ReadonlyMap<number, UserRoleName>
  connectionError: string | null
  /** Ustoz tugmalari (mikrofon/kamera o'chirish) — faqat darsning hostida. */
  canModerate: boolean
  /** Server javobi kutilayotgan amallar: `${userId}:Microphone` / `${userId}:Camera`. */
  moderating: ReadonlySet<string>
}>()

const emit = defineEmits<{
  retry: []
  moderate: [userId: number, source: ParticipantMediaSource]
}>()

/*
  Yotiq telefonda filmstrip PASTDAN O'NG TOMONGA ko'chadi.

  Sabab arifmetik: gorizontal filmstrip 16:9 katakchalar bilan ~90px
  balandlik + oraliq oladi. 390px balandlikdagi yotiq ekranda bu asosiy
  videoning to'rtdan birini yeb qo'yadi. Yon ustunda esa u atigi ~110px
  KENGLIK oladi — yotiq ekranda kenglik mo'l (700px+), balandlik esa taqchil.

  ★ Tartib o'zgarmaydi: asosiy sahna avval, filmstrip keyin — faqat
  yo'nalish ustundan qatorga aylanadi (yuqori→past o'rniga chap→o'ng).
*/
const { isShortLandscape, isDesktop } = useBreakpoint()

/** Filmstrip'da ko'rsatiladigan maksimum katakcha (DOM'ni cheklash uchun). */
const FILMSTRIP_LIMIT = 24

/**
 * To'rda bir vaqtda chiziladigan maksimum katak. 200 kishilik xonada
 * 200 ta `<video>` brauzerni cho'ktiradi (har biri dekoder oladi);
 * `livekit.yaml` dagi `subscription_limit_video: 12` ham shuni aytadi —
 * baribir 12 tadan ortiq video kelmaydi. Qolganlar "+N" katagida.
 */
const GRID_LIMIT = 25

const screenTile = computed(() => props.tiles.find((tile) => tile.isScreenShare) ?? null)

const hostTile = computed(() => {
  if (props.hostUserId === null) return null
  return props.tiles.find((tile) => !tile.isScreenShare && tile.userId === props.hostUserId) ?? null
})

/* ------------------------------------------------------------- qadash */

const pinnedKey = ref<string | null>(null)

// Qadalgan ishtirokchi chiqib ketsa qadash ham ketadi — aks holda sahna
// bo'sh qolib, to'rga qaytish uchun bosadigan narsa qolmasdi.
watch(
  () => props.tiles,
  (list) => {
    if (pinnedKey.value !== null && !list.some((tile) => tile.key === pinnedKey.value)) {
      pinnedKey.value = null
    }
  },
)

const pinnedTile = computed(() =>
  pinnedKey.value === null ? null : (props.tiles.find((tile) => tile.key === pinnedKey.value) ?? null),
)

function togglePin(tile: ParticipantTile): void {
  pinnedKey.value = pinnedKey.value === tile.key ? null : tile.key
}

/* ------------------------------------------------------------ ko'rinish */

/** Sahna rejimi: qadalgan yoki ekran ulashuvi bor. */
const isSpotlight = computed(() => pinnedTile.value !== null || screenTile.value !== null)

/** Asosiy sahna: qadalgan > ekran ulashuvi > ustoz > birinchi. */
const mainTile = computed<ParticipantTile | null>(
  () => pinnedTile.value ?? screenTile.value ?? hostTile.value ?? props.tiles[0] ?? null,
)

/**
 * Lenta — asosiydan boshqa HAMMA (kamerasi o'chiqlar ham, avatar bilan).
 * Ilgari faqat kamerasi yoniqlar chiqardi — kamerasiz o'quvchi sahnada
 * umuman yo'q edi.
 */
const filmstripAll = computed(() =>
  props.tiles.filter((tile) => tile.key !== mainTile.value?.key),
)
const filmstrip = computed(() => filmstripAll.value.slice(0, FILMSTRIP_LIMIT))
const filmstripHidden = computed(() => Math.max(0, filmstripAll.value.length - FILMSTRIP_LIMIT))

/**
 * To'r: ustoz BIRINCHI, keyin qolganlar LiveKit tartibida (mahalliy
 * ishtirokchi `tiles` da doim birinchi — `rebuildTiles`). Ekran ulashuvi
 * to'rga tushmaydi — u bo'lsa sahna rejimi ishlaydi.
 */
const gridAll = computed(() => {
  const cameras = props.tiles.filter((tile) => !tile.isScreenShare)
  const host = hostTile.value
  if (host === null) return cameras
  return [host, ...cameras.filter((tile) => tile.key !== host.key)]
})
const gridTiles = computed(() => gridAll.value.slice(0, GRID_LIMIT))
const gridHidden = computed(() => Math.max(0, gridAll.value.length - GRID_LIMIT))

/**
 * Ustun soni — Telegram/Meet qoidasi: kataklar imkon qadar KVADRATGA yaqin
 * to'r hosil qilsin. Tik telefonda ko'pi bilan 2 ustun (kenglik 360–430px,
 * 3 ustunda yuz tanib bo'lmaydi); yotiq telefonda esa balandlik taqchil,
 * shuning uchun ko'proq ustun (kamroq qator).
 */
const gridColumns = computed(() => {
  const count = gridTiles.value.length + (gridHidden.value > 0 ? 1 : 0)
  if (count <= 1) return 1
  if (isShortLandscape.value) return Math.min(5, Math.max(2, Math.ceil(count / 2)))
  if (!isDesktop.value) return 2
  if (count <= 4) return 2
  if (count <= 9) return 3
  if (count <= 16) return 4
  return 5
})

const gridStyle = computed(() => ({
  gridTemplateColumns: `repeat(${gridColumns.value}, minmax(0, 1fr))`,
}))

/* ---------------------------------------------------------- yordamchilar */

function tileRole(tile: ParticipantTile): string {
  if (tile.userId === null) return ''
  const role = props.roleByUserId.get(tile.userId)
  return role !== undefined ? roleLabel(role) : ''
}

/** Ustoz tugmalari: o'zganing kamera katagida, faqat host uchun. */
function canModerateTile(tile: ParticipantTile): boolean {
  return props.canModerate && !tile.isLocal && !tile.isScreenShare && tile.userId !== null
}

function isModerating(tile: ParticipantTile, source: ParticipantMediaSource): boolean {
  return tile.userId !== null && props.moderating.has(`${tile.userId}:${source}`)
}

function moderate(tile: ParticipantTile, source: ParticipantMediaSource): void {
  if (tile.userId === null) return
  emit('moderate', tile.userId, source)
}

const isBusyState = computed(() => props.status === 'loading' || props.status === 'connecting')

/**
 * `disconnected` HAM xatolik qoplamasini ko'rsatadi.
 *
 * Ilgari faqat `failed` holatida qoplama chiqardi. Ulanish o'rnatilgandan
 * KEYIN uzilsa (server qayta ishga tushdi, internet uzildi, boshqa oynadan
 * kirildi) holat `disconnected` bo'lardi va ekranda faqat bo'sh "Hozircha
 * efirda hech kim yo'q" yozuvi qolardi — foydalanuvchi hech qachon
 * "Qayta urinish" tugmasini ko'rmasdi. Aynan shu "jimgina ishlamaslik".
 */
const isErrorState = computed(() => props.status === 'failed' || props.status === 'disconnected')
</script>

<template>
  <section
    class="flex min-h-0 min-w-0 flex-1"
    :class="isShortLandscape && isSpotlight ? 'gap-2' : 'flex-col gap-3'"
  >
    <!-- Asosiy maydon: to'r yoki sahna -->
    <div
      class="relative min-h-0 min-w-0 flex-1 overflow-hidden rounded-2xl bg-ink-900 ring-1 ring-inset ring-line"
    >
      <!-- ============================================ SAHNA (spotlight) -->
      <!--
        ★ `div role="button"`, `<button>` EMAS: ichida ustoz tugmalari
        (`<button>`) bor, HTML esa tugma ichida tugmaga ruxsat bermaydi —
        brauzer ichki tugmani tashqi tugmaning bir qismi deb hisoblab,
        bosishni ikkalasiga ham yuborardi. Klaviatura: Enter/Space.
      -->
      <div
        v-if="isSpotlight && mainTile"
        :key="mainTile.key"
        role="button"
        tabindex="0"
        class="block size-full cursor-zoom-out text-left outline-none focus-visible:ring-2 focus-visible:ring-brand-400"
        :title="pinnedTile !== null ? 'To‘rga qaytish' : 'Sahnaga qadash'"
        @click="togglePin(mainTile)"
        @keydown.enter.prevent="togglePin(mainTile)"
        @keydown.space.prevent="togglePin(mainTile)"
      >
        <VideoTile
          large
          :track="mainTile.videoTrack"
          :name="mainTile.name"
          :is-local="mainTile.isLocal"
          :is-screen-share="mainTile.isScreenShare"
          :is-speaking="mainTile.isSpeaking"
          :mic-enabled="mainTile.micEnabled"
          :role-label="tileRole(mainTile)"
          :pinned="pinnedTile !== null"
          :can-moderate="canModerateTile(mainTile)"
          :mic-moderating="isModerating(mainTile, 'Microphone')"
          :camera-moderating="isModerating(mainTile, 'Camera')"
          @mute-mic="moderate(mainTile, 'Microphone')"
          @camera-off="moderate(mainTile, 'Camera')"
        />
      </div>

      <!-- ============================================ TO'R (grid) -->
      <div
        v-else-if="gridTiles.length > 0"
        class="grid size-full auto-rows-fr gap-2 p-2"
        :style="gridStyle"
      >
        <!--
          Har katak — bosilsa sahnaga qadaladi (Telegram'dagi
          "kattalashtirish"). Ichidagi ustoz tugmalari `@click.stop` bilan
          o'z hodisasini to'xtatadi. `div role="button"` — sabab yuqorida.
        -->
        <div
          v-for="tile in gridTiles"
          :key="tile.key"
          role="button"
          tabindex="0"
          class="min-h-0 min-w-0 cursor-zoom-in rounded-xl text-left outline-none focus-visible:ring-2 focus-visible:ring-brand-400"
          :title="`${tile.name}: sahnaga qadash`"
          @click="togglePin(tile)"
          @keydown.enter.prevent="togglePin(tile)"
          @keydown.space.prevent="togglePin(tile)"
        >
          <VideoTile
            fill
            :track="tile.videoTrack"
            :name="tile.name"
            :is-local="tile.isLocal"
            :is-speaking="tile.isSpeaking"
            :mic-enabled="tile.micEnabled"
            :role-label="tileRole(tile)"
            :can-moderate="canModerateTile(tile)"
            :mic-moderating="isModerating(tile, 'Microphone')"
            :camera-moderating="isModerating(tile, 'Camera')"
            @mute-mic="moderate(tile, 'Microphone')"
            @camera-off="moderate(tile, 'Camera')"
          />
        </div>
        <div
          v-if="gridHidden > 0"
          class="flex min-h-0 items-center justify-center rounded-xl bg-ink-850 text-sm font-medium text-slate-400 ring-1 ring-inset ring-line"
        >
          +{{ gridHidden }}
        </div>
      </div>

      <div
        v-else
        class="flex size-full flex-col items-center justify-center gap-3 px-6 text-center"
      >
        <div class="flex size-14 items-center justify-center rounded-2xl bg-ink-800 text-slate-500">
          <AppIcon
            name="camera"
            :size="26"
          />
        </div>
        <p class="text-sm font-medium text-slate-300">
          Hozircha efirda hech kim yo‘q
        </p>
        <p class="max-w-xs text-xs text-slate-500">
          Ustoz efirga chiqishi bilan video shu yerda paydo bo‘ladi.
        </p>
      </div>

      <!-- Yuklanish qoplamasi -->
      <div
        v-if="isBusyState"
        class="absolute inset-0 flex flex-col items-center justify-center gap-3 bg-ink-950/80 backdrop-blur-sm"
      >
        <BaseSpinner
          size="lg"
          class="text-brand-400"
        />
        <p class="text-sm text-slate-300">
          Videoga ulanmoqda…
        </p>
      </div>

      <!-- Qayta ulanish -->
      <div
        v-else-if="props.status === 'reconnecting'"
        class="absolute inset-0 flex flex-col items-center justify-center gap-3 bg-ink-950/75 backdrop-blur-sm"
      >
        <BaseSpinner
          size="lg"
          class="text-amber-400"
        />
        <p class="text-sm text-amber-200">
          Video aloqa tiklanmoqda…
        </p>
      </div>

      <!-- Xatolik yoki uzilish -->
      <div
        v-else-if="isErrorState"
        class="absolute inset-0 flex flex-col items-center justify-center gap-4 bg-ink-950/90 px-6 text-center"
      >
        <div class="flex size-12 items-center justify-center rounded-2xl bg-rose-500/15 text-rose-400">
          <AppIcon
            name="wifi-off"
            :size="24"
          />
        </div>
        <div>
          <p class="text-sm font-semibold text-slate-100">
            {{ props.status === 'failed' ? 'Videoga ulanib bo‘lmadi' : 'Video aloqasi uzildi' }}
          </p>
          <p
            class="mt-1 max-w-sm text-xs text-slate-400"
            v-text="props.connectionError ?? ''"
          />
        </div>
        <BaseButton
          size="sm"
          variant="secondary"
          @click="emit('retry')"
        >
          <template #icon>
            <AppIcon
              name="refresh"
              :size="15"
            />
          </template>
          Qayta urinish
        </BaseButton>
      </div>
    </div>

    <!-- Filmstrip — faqat sahna rejimida -->
    <div
      v-if="isSpotlight && filmstrip.length > 0"
      class="shrink-0"
    >
      <div
        class="scrollbar-slim flex gap-2"
        :class="
          isShortLandscape
            ? 'h-full flex-col overflow-y-auto overscroll-contain pr-0.5'
            : 'overflow-x-auto pb-1'
        "
      >
        <div
          v-for="tile in filmstrip"
          :key="tile.key"
          role="button"
          tabindex="0"
          class="shrink-0 cursor-zoom-in rounded-xl text-left outline-none focus-visible:ring-2 focus-visible:ring-brand-400"
          :title="`${tile.name}: sahnaga qadash`"
          @click="togglePin(tile)"
          @keydown.enter.prevent="togglePin(tile)"
          @keydown.space.prevent="togglePin(tile)"
        >
          <VideoTile
            :compact="isShortLandscape"
            :track="tile.videoTrack"
            :name="tile.name"
            :is-local="tile.isLocal"
            :is-screen-share="tile.isScreenShare"
            :is-speaking="tile.isSpeaking"
            :mic-enabled="tile.micEnabled"
            :role-label="tileRole(tile)"
            :can-moderate="canModerateTile(tile)"
            :mic-moderating="isModerating(tile, 'Microphone')"
            :camera-moderating="isModerating(tile, 'Camera')"
            @mute-mic="moderate(tile, 'Microphone')"
            @camera-off="moderate(tile, 'Camera')"
          />
        </div>
        <div
          v-if="filmstripHidden > 0"
          class="flex aspect-video shrink-0 items-center justify-center rounded-xl bg-ink-850 text-xs font-medium text-slate-400 ring-1 ring-inset ring-line"
          :class="isShortLandscape ? 'w-[104px]' : 'w-24'"
        >
          +{{ filmstripHidden }}
        </div>
      </div>
    </div>
  </section>
</template>
