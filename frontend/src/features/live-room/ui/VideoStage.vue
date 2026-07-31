<script setup lang="ts">
import { computed } from 'vue'

import { roleLabel } from '@/entities/user'
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

/** Filmstrip'da ko'rsatiladigan maksimum katakcha (DOM'ni cheklash uchun). */
const FILMSTRIP_LIMIT = 24

const screenTile = computed(() => props.tiles.find((tile) => tile.isScreenShare) ?? null)

const hostTile = computed(() => {
  if (props.hostUserId === null) return null
  return props.tiles.find((tile) => !tile.isScreenShare && tile.userId === props.hostUserId) ?? null
})

/** Asosiy sahna: ekran ulashuvi > ustoz > birinchi kamerali > birinchi. */
const mainTile = computed<ParticipantTile | null>(() => {
  return (
    screenTile.value ??
    hostTile.value ??
    props.tiles.find((tile) => tile.videoTrack !== null) ??
    props.tiles[0] ??
    null
  )
})

const filmstripAll = computed(() =>
  props.tiles.filter(
    (tile) => tile.key !== mainTile.value?.key && (tile.cameraEnabled || tile.isScreenShare),
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
  <section class="flex min-h-0 flex-1 flex-col gap-3">
    <!-- Asosiy sahna -->
    <div
      class="relative min-h-0 flex-1 overflow-hidden rounded-2xl bg-ink-900 ring-1 ring-inset ring-line"
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
      <div class="scrollbar-slim flex gap-2 overflow-x-auto pb-1">
        <VideoTile
          v-for="tile in filmstrip"
          :key="tile.key"
          :track="tile.videoTrack"
          :name="tile.name"
          :is-local="tile.isLocal"
          :is-screen-share="tile.isScreenShare"
          :is-speaking="tile.isSpeaking"
          :mic-enabled="tile.micEnabled"
          :role-label="tileRole(tile)"
        />
        <div
          v-if="hiddenCount > 0"
          class="flex aspect-video w-24 shrink-0 items-center justify-center rounded-xl bg-ink-850 text-xs font-medium text-slate-400 ring-1 ring-inset ring-line"
        >
          +{{ hiddenCount }}
        </div>
      </div>
    </div>
  </section>
</template>
