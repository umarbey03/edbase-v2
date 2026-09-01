<script setup lang="ts">
import { computed, ref } from 'vue'

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
 */
const props = defineProps<{
  tiles: readonly ParticipantTile[]
  /** Presence'dan aniqlangan ustoz/kurator `userId` si. */
  hostUserId: number | null
  status: MediaStatus
  roleByUserId: ReadonlyMap<number, UserRoleName>
  connectionError: string | null
}>()

const emit = defineEmits<{ retry: [] }>()

/*
  Yotiq telefonda filmstrip PASTDAN O'NG TOMONGA ko'chadi.

  Sabab arifmetik: gorizontal filmstrip 16:9 katakchalar bilan ~90px
  balandlik + oraliq oladi. 390px balandlikdagi yotiq ekranda bu asosiy
  videoning to'rtdan birini yeb qo'yadi. Yon ustunda esa u atigi ~110px
  KENGLIK oladi — yotiq ekranda kenglik mo'l (700px+), balandlik esa taqchil.

  ★ Tartib o'zgarmaydi: asosiy sahna avval, filmstrip keyin — faqat
  yo'nalish ustundan qatorga aylanadi (yuqori→past o'rniga chap→o'ng).
*/
const { isShortLandscape } = useBreakpoint()

/** Filmstrip'da ko'rsatiladigan maksimum katakcha (DOM'ni cheklash uchun). */
const FILMSTRIP_LIMIT = 24

/*
  ════════════════════════════════════════════════════════════════════════
  ASOSIY SAHNA — UZOQDAGI ISHTIROKCHI USTUVOR (2026-09-01)
  ════════════════════════════════════════════════════════════════════════

  🔴 ILGARI NIMA NOTO'G'RI EDI. Tanlov shunday yozilgandi:

      ekran ulashuvi ?? ustoz ?? birinchi kamerali ?? birinchi

  va u `props.tiles` ustida ishlardi. Ro'yxatda esa O'ZINGIZNING
  katakchalaringiz BIRINCHI turadi (`useLiveKitRoom.rebuildTiles` avval
  `localParticipant` ni qo'shadi). Ya'ni `find()` deyarli har doim
  siznikini topardi va natija ikki xil buzilishga olib kelardi:

    • siz ekran ulashsangiz — asosiy sahnada O'Z ekraningiz, ya'ni
      "oyna ichida oyna" cheksizligi (jonli sinovda ko'rildi);
    • xonada ustoz bo'lmasa (yoki presence hali yuklanmagan bo'lsa)
      — asosiy sahnada O'Z kamerangiz.

  Ikkalasi ham foydasiz: odam o'zini emas, QARSHI TARAFNI ko'rishi kerak.

  ★ YANGI QOIDA — avval UZOQDAGILAR orasidan tanlanadi:

      uzoqdagi ekran ulashuvi
        > uzoqdagi ustoz kamerasi
        > uzoqdagi har qanday video
        > uzoqdagi har qanday ishtirokchi
        > (xonada yolg'iz qolsangiz) o'z ekraningiz, so'ng o'z kamerangiz

  O'z kamerangiz asosiy sahnaga FAQAT xonada boshqa hech kim bo'lmaganda
  tushadi — u paytda muqobili bo'sh ekran bo'lardi.

  ★ O'Z KAMERANGIZ BURCHAKDA (PiP): u filmstrip'dan olib tashlanib,
  asosiy sahnaning o'ng pastki burchagiga kichik oyna bo'lib chiqadi —
  Zoom/Meet naqshi. Shunda "o'zim qanday ko'rinyapman" savoli ham
  javobsiz qolmaydi, lekin joyni ham egallamaydi.
*/
const remoteTiles = computed(() => props.tiles.filter((tile) => !tile.isLocal))
const localCamera = computed(
  () => props.tiles.find((tile) => tile.isLocal && !tile.isScreenShare) ?? null,
)
const localScreen = computed(
  () => props.tiles.find((tile) => tile.isLocal && tile.isScreenShare) ?? null,
)

/*
  QO'LDA QADASH (pin). Avtomatik qoida ko'p hollarda to'g'ri tanlaydi,
  lekin hammasini bilolmaydi: ustoz bitta o'quvchini kattalashtirib
  ko'rmoqchi bo'lishi mumkin, yoki o'quvchi ekran ulashuv o'rniga
  ustozning yuzini ko'rmoqchi bo'lishi mumkin.

  ★ KALIT BO'YICHA SAQLANADI, INDEKS BO'YICHA EMAS: xonaga yangi odam
  kirsa massiv tartibi o'zgaradi va indeks boshqa odamga ko'rsatib
  qolardi. Qadalgan ishtirokchi chiqib ketsa `pinnedTile` `null` bo'ladi
  va sahna JIMGINA avtomatik qoidaga qaytadi — "qora ekran" holati
  yuzaga kelmaydi.
*/
const pinnedKey = ref<string | null>(null)

const pinnedTile = computed<ParticipantTile | null>(() =>
  pinnedKey.value === null
    ? null
    : (props.tiles.find((tile) => tile.key === pinnedKey.value) ?? null),
)

const autoMainTile = computed<ParticipantTile | null>(() => {
  const hostId = props.hostUserId

  return (
    remoteTiles.value.find((tile) => tile.isScreenShare) ??
    (hostId === null
      ? undefined
      : remoteTiles.value.find((tile) => !tile.isScreenShare && tile.userId === hostId)) ??
    remoteTiles.value.find((tile) => tile.videoTrack !== null) ??
    remoteTiles.value[0] ??
    localScreen.value ??
    localCamera.value ??
    props.tiles[0] ??
    null
  )
})

const mainTile = computed<ParticipantTile | null>(() => pinnedTile.value ?? autoMainTile.value)

/** O'z kamerasi burchakdagi kichik oynada — asosiy sahnada bo'lmasa. */
const selfPip = computed<ParticipantTile | null>(() => {
  const self = localCamera.value
  if (self === null || self.key === mainTile.value?.key) return null

  // Kamera o'chiq bo'lsa PiP chizilmaydi — bo'sh avatar burchakni
  // egallab, hech qanday ma'lumot bermasdi.
  return self.videoTrack !== null ? self : null
})

function togglePin(tile: ParticipantTile): void {
  pinnedKey.value = pinnedKey.value === tile.key ? null : tile.key
}

/*
  Filmstrip'dan O'Z KAMERANGIZ chiqarib tashlandi — u endi PiP'da.
  O'z EKRAN ULASHUVINGIZ esa qoladi: ulashayotgan odam nima
  ko'rsatayotganini tekshira olishi kerak.
*/
const filmstripAll = computed(() =>
  props.tiles.filter(
    (tile) =>
      tile.key !== mainTile.value?.key &&
      tile.key !== selfPip.value?.key &&
      (tile.cameraEnabled || tile.isScreenShare),
  ),
)

const filmstrip = computed(() => filmstripAll.value.slice(0, FILMSTRIP_LIMIT))
const hiddenCount = computed(() => Math.max(0, filmstripAll.value.length - FILMSTRIP_LIMIT))

function tileRole(tile: ParticipantTile): string {
  if (tile.userId === null) return ''
  const role = props.roleByUserId.get(tile.userId)
  return role !== undefined ? roleLabel(role) : ''
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
    :class="isShortLandscape ? 'gap-2' : 'flex-col gap-3'"
  >
    <!-- Asosiy sahna -->
    <div
      class="relative min-h-0 min-w-0 flex-1 overflow-hidden rounded-2xl bg-ink-900 ring-1 ring-inset ring-line"
    >
      <VideoTile
        v-if="mainTile"
        :key="mainTile.key"
        large
        :track="mainTile.videoTrack"
        :name="mainTile.name"
        :is-local="mainTile.isLocal"
        :is-screen-share="mainTile.isScreenShare"
        :is-speaking="mainTile.isSpeaking"
        :mic-enabled="mainTile.micEnabled"
        :role-label="tileRole(mainTile)"
      />

      <!--
        O'Z KAMERANGIZ — BURCHAKDA (PiP).

        ★ `pointer-events-none`: bu oyna videoning ustida suzadi va uni
        bosish kerak emas; usiz u asosiy sahnadagi bosishlarni yutardi.
        ★ O'lchov ekranga qarab: yotiq telefonda 96px, aks holda 128px
        (kengroq ekranda 160px). Katta qilib bo'lmaydi — u asosiy
        videoning burchagini yopadi.
      -->
      <div
        v-if="selfPip"
        class="pointer-events-none absolute bottom-3 right-3 overflow-hidden rounded-xl shadow-lg ring-1 ring-white/15"
        :class="isShortLandscape ? 'w-24' : 'w-32 sm:w-40'"
      >
        <VideoTile
          :key="selfPip.key"
          compact
          :track="selfPip.videoTrack"
          :name="selfPip.name"
          is-local
          :is-speaking="selfPip.isSpeaking"
          :mic-enabled="selfPip.micEnabled"
          class="!w-full"
        />
      </div>

      <!--
        QADALGANIDA — chiqish yo'li KO'RINIB tursin. Usiz foydalanuvchi
        katakchani tasodifan bosib qadab qo'yib, sahna nega "qotib
        qolgan"ini tushunmasdi.
      -->
      <button
        v-if="pinnedTile"
        type="button"
        class="absolute right-3 top-3 flex items-center gap-1.5 rounded-lg bg-black/60 px-2 py-1 text-[11px] font-medium text-white/90 backdrop-blur transition-colors hover:bg-black/75"
        title="Qadashni bekor qilish — sahna avtomatik tanlovga qaytadi"
        @click="pinnedKey = null"
      >
        <AppIcon
          name="close"
          :size="12"
        />
        Qadaldi
      </button>

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

    <!-- Filmstrip -->
    <div
      v-if="filmstrip.length > 0"
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
        <!--
          Katakcha bosilsa asosiy sahnaga QADALADI (yana bosilsa ozod
          bo'ladi). `<button>` ataylab — klaviatura va skrinrider uchun
          `div` + `@click` yaramaydi.
        -->
        <button
          v-for="tile in filmstrip"
          :key="tile.key"
          type="button"
          class="shrink-0 rounded-xl transition-opacity hover:opacity-80 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-brand-400"
          :title="`${tile.name} — asosiy ekranga chiqarish`"
          @click="togglePin(tile)"
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
          />
        </button>
        <div
          v-if="hiddenCount > 0"
          class="flex aspect-video shrink-0 items-center justify-center rounded-xl bg-ink-850 text-xs font-medium text-slate-400 ring-1 ring-inset ring-line"
          :class="isShortLandscape ? 'w-[104px]' : 'w-24'"
        >
          +{{ hiddenCount }}
        </div>
      </div>
    </div>
  </section>
</template>
