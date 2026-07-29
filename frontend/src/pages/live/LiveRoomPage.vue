<script setup lang="ts">
import { useQuery } from '@tanstack/vue-query'
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'

import { fetchRecentMessages } from '@/entities/message'
import {
  endLiveSession,
  fetchLiveSession,
  sessionTitle,
  startLiveSession,
} from '@/entities/session'
import { useAuthStore } from '@/features/auth/model/auth.store'
import ChatPanel from '@/features/chat/ui/ChatPanel.vue'
import { useLiveHub } from '@/features/live-hub/model/useLiveHub'
import { useLiveKitRoom } from '@/features/live-room/model/useLiveKitRoom'
import MediaControlBar from '@/features/live-room/ui/MediaControlBar.vue'
import VideoStage from '@/features/live-room/ui/VideoStage.vue'
import { toUserMessage } from '@/shared/api'
import { formatCountdown } from '@/shared/lib/datetime'
import { AppIcon, BaseBadge, BaseButton } from '@/shared/ui'

const route = useRoute()
const router = useRouter()
const auth = useAuthStore()

const rawSessionId = route.params['sessionId']
const sessionId = Number(Array.isArray(rawSessionId) ? rawSessionId[0] : rawSessionId)
const isValidSession = Number.isInteger(sessionId) && sessionId > 0

/* ------------------------------ sessiya ma'lumoti ------------------------- */

const sessionQuery = useQuery({
  queryKey: ['live-session', sessionId],
  queryFn: ({ signal }) => fetchLiveSession(sessionId, { signal }),
  enabled: isValidSession,
  staleTime: 15_000,
})

/* --------------------------------- video ---------------------------------- */

const media = useLiveKitRoom(sessionId)
const {
  status: mediaStatus,
  tiles,
  isHost,
  endsAt: mediaEndsAt,
  isMicOn,
  isCameraOn,
  isScreenSharing,
  isBusy: mediaBusy,
  mediaError,
  connectionError: mediaConnectionError,
  connect: connectMedia,
  leave: leaveMedia,
  toggleMic,
  toggleCamera,
  toggleScreenShare,
  dismissMediaError,
} = media

/* ------------------------------- chat / hub -------------------------------- */

const hub = useLiveHub({
  sessionId,
  onSessionEnded: () => {
    // Dars yakunlandi — videodan ham darhol uzilamiz.
    void leaveMedia()
  },
})
const {
  status: chatStatus,
  messages,
  participants,
  participantCount,
  raisedHands,
  roleByUserId,
  sessionEnded,
  notice: chatNotice,
  cooldownRemainingMs,
  canSend,
  isSending,
  handRaised,
  sendMessage,
  raiseHand,
  seedMessages,
  start: startHub,
  retry: retryHub,
  dismissNotice,
} = hub

/* --------------------------------- holat ---------------------------------- */

const chatOpen = ref(false)
const chatUnread = ref(0)
const actionBusy = ref(false)
const actionError = ref<string | null>(null)
const nowMs = ref(Date.now())
let clockTimer: number | null = null

const session = computed(() => sessionQuery.data.value ?? null)
const headerTitle = computed(() => {
  const current = session.value
  return current !== null ? sessionTitle(current) : 'Jonli dars'
})
const groupName = computed(() => session.value?.groupName ?? '')
const isLive = computed(() => session.value?.status === 'Live')
const canManageSession = computed(() => session.value?.isHost === true || isHost.value)

/**
 * Kim "host" (ustoz) ekanini LiveKit o'zi aytmaydi — `LiveSessionDto` da ham
 * `HostId` yo'q. Shu sababli presence ma'lumotidan foydalanamiz: SPEC 7 bo'yicha
 * LiveKit `identity` = `userId`, presence esa har bir `userId` uchun rolni beradi.
 */
const hostUserId = computed<number | null>(() => {
  const list = participants.value
  const teacher = list.find((entry) => entry.role === 'Teacher')
  if (teacher !== undefined) return teacher.userId
  const assistant = list.find((entry) => entry.role === 'Assistant')
  if (assistant !== undefined) return assistant.userId
  return isHost.value ? auth.userId : null
})

const endsAtIso = computed(() => mediaEndsAt.value ?? session.value?.endsAt ?? null)
const remainingMs = computed<number | null>(() => {
  if (endsAtIso.value === null) return null
  const target = new Date(endsAtIso.value).getTime()
  if (Number.isNaN(target)) return null
  return target - nowMs.value
})
const countdown = computed(() => {
  const remaining = remainingMs.value
  return remaining !== null && remaining > 0 ? formatCountdown(remaining) : null
})
const isEndingSoon = computed(() => {
  const remaining = remainingMs.value
  return remaining !== null && remaining > 0 && remaining < 5 * 60_000
})

/** Yuqoridagi ogohlantirish chizig'i: video yoki chat aloqasi muammoli bo'lsa. */
type BannerTone = 'info' | 'warn' | 'error'
const banner = computed<{ tone: BannerTone; text: string } | null>(() => {
  if (sessionEnded.value) return null
  if (mediaStatus.value === 'reconnecting' || chatStatus.value === 'reconnecting') {
    return { tone: 'warn', text: 'Aloqa uzildi — qayta ulanmoqda…' }
  }
  if (mediaStatus.value === 'failed' || chatStatus.value === 'disconnected') {
    return { tone: 'error', text: 'Serverga ulanib bo‘lmadi. Internet aloqangizni tekshiring.' }
  }
  if (mediaStatus.value === 'loading' || mediaStatus.value === 'connecting' || chatStatus.value === 'connecting') {
    return { tone: 'info', text: 'Darsga ulanmoqda…' }
  }
  return null
})

const BANNER_CLASS: Record<BannerTone, string> = {
  info: 'bg-brand-500/15 text-brand-200 border-brand-500/25',
  warn: 'bg-amber-500/15 text-amber-200 border-amber-500/25',
  error: 'bg-rose-500/15 text-rose-200 border-rose-500/25',
}

/* -------------------------------- amallar ---------------------------------- */

async function handleLeave(): Promise<void> {
  await leaveMedia()
  await router.push({ name: 'sessions' })
}

async function handleToggleHand(): Promise<void> {
  await raiseHand(!handRaised.value)
}

async function runSessionAction(action: () => Promise<unknown>): Promise<void> {
  if (actionBusy.value) return
  actionBusy.value = true
  actionError.value = null
  try {
    await action()
    await sessionQuery.refetch()
  } catch (error) {
    actionError.value = toUserMessage(error)
  } finally {
    actionBusy.value = false
  }
}

function handleStartSession(): void {
  void runSessionAction(() => startLiveSession(sessionId))
}

function handleEndSession(): void {
  void runSessionAction(() => endLiveSession(sessionId))
}

function handleRetry(): void {
  void connectMedia()
  void retryHub()
}

function openChat(): void {
  chatOpen.value = true
  chatUnread.value = 0
}

/* ------------------------------- hayot davri ------------------------------- */

onMounted(() => {
  if (!isValidSession) return

  // Video va chat PARALLEL ishga tushadi — biri ikkinchisini kutmaydi.
  void connectMedia()
  void startHub()

  // Chat tarixi (SPEC 5). Kelmasa ham realtime chat ishlayveradi.
  void fetchRecentMessages(sessionId, 50)
    .then((history) => seedMessages(history))
    .catch(() => undefined)

  clockTimer = window.setInterval(() => {
    nowMs.value = Date.now()
  }, 1000)
})

onBeforeUnmount(() => {
  if (clockTimer !== null) {
    window.clearInterval(clockTimer)
    clockTimer = null
  }
  // `useLiveKitRoom` va `useLiveHub` o'z tozalashini o'zlari bajaradi
  // (trek'lar detach qilinadi, tinglovchilar olib tashlanadi, ulanish yopiladi).
})
</script>

<template>
  <div class="flex h-dvh flex-col overflow-hidden bg-ink-950">
    <!-- ============================ Yuqori panel =========================== -->
    <header
      class="flex shrink-0 items-center gap-3 border-b border-line bg-ink-900/80 px-3 py-2.5 backdrop-blur"
    >
      <button
        type="button"
        class="rounded-lg p-2 text-slate-400 transition-colors hover:bg-white/5 hover:text-slate-100"
        title="Orqaga"
        @click="handleLeave"
      >
        <AppIcon name="arrow-left" :size="18" />
      </button>

      <div class="min-w-0 flex-1">
        <div class="flex items-center gap-2">
          <h1 class="truncate text-sm font-semibold text-slate-100 sm:text-base" v-text="headerTitle" />
          <BaseBadge v-if="isLive" tone="live" dot>Jonli</BaseBadge>
        </div>
        <p v-if="groupName.length > 0" class="truncate text-xs text-slate-500" v-text="groupName" />
      </div>

      <div class="flex items-center gap-2">
        <span
          v-if="countdown !== null"
          class="hidden items-center gap-1.5 rounded-lg bg-ink-800 px-2.5 py-1.5 text-xs font-medium tabular-nums ring-1 ring-inset ring-line sm:inline-flex"
          :class="isEndingSoon ? 'text-amber-300' : 'text-slate-300'"
          title="Dars tugashiga qolgan vaqt"
        >
          <AppIcon name="calendar" :size="14" />
          {{ countdown }}
        </span>

        <span
          class="inline-flex items-center gap-1.5 rounded-lg bg-ink-800 px-2.5 py-1.5 text-xs font-medium text-slate-300 tabular-nums ring-1 ring-inset ring-line"
          title="Ishtirokchilar soni"
        >
          <AppIcon name="users" :size="14" />
          {{ participantCount }}
        </span>

        <BaseButton
          v-if="canManageSession && session?.status === 'Scheduled'"
          size="sm"
          variant="success"
          :loading="actionBusy"
          @click="handleStartSession"
        >
          <template #icon><AppIcon name="play" :size="14" /></template>
          <span class="hidden sm:inline">Darsni boshlash</span>
        </BaseButton>

        <BaseButton
          v-else-if="canManageSession && isLive"
          size="sm"
          variant="secondary"
          :loading="actionBusy"
          @click="handleEndSession"
        >
          <span class="hidden sm:inline">Yakunlash</span>
          <span class="sm:hidden">Stop</span>
        </BaseButton>
      </div>
    </header>

    <!-- ========================= Holat chiziqlari ========================== -->
    <div
      v-if="banner !== null"
      class="flex shrink-0 items-center gap-2 border-b px-4 py-1.5 text-xs font-medium"
      :class="BANNER_CLASS[banner.tone]"
      role="status"
    >
      <AppIcon :name="banner.tone === 'error' ? 'wifi-off' : 'refresh'" :size="14" />
      <span class="flex-1" v-text="banner.text" />
      <button
        v-if="banner.tone === 'error'"
        type="button"
        class="rounded-md px-2 py-0.5 font-semibold underline-offset-2 hover:underline"
        @click="handleRetry"
      >
        Qayta urinish
      </button>
    </div>

    <div
      v-if="mediaError !== null"
      class="flex shrink-0 items-center gap-2 border-b border-amber-500/25 bg-amber-500/10 px-4 py-1.5 text-xs text-amber-200"
      role="alert"
    >
      <span class="flex-1" v-text="mediaError" />
      <button type="button" class="rounded p-0.5 hover:text-amber-100" @click="dismissMediaError">
        <AppIcon name="close" :size="14" />
      </button>
    </div>

    <div
      v-if="actionError !== null"
      class="flex shrink-0 items-center gap-2 border-b border-rose-500/25 bg-rose-500/10 px-4 py-1.5 text-xs text-rose-200"
      role="alert"
    >
      <span class="flex-1" v-text="actionError" />
      <button type="button" class="rounded p-0.5 hover:text-rose-100" @click="actionError = null">
        <AppIcon name="close" :size="14" />
      </button>
    </div>

    <!-- ============================== Asosiy =============================== -->
    <div v-if="!isValidSession" class="flex flex-1 items-center justify-center px-6 text-center">
      <div>
        <p class="text-sm font-semibold text-slate-200">Dars manzili noto‘g‘ri</p>
        <BaseButton class="mt-4" size="sm" variant="secondary" @click="router.push({ name: 'sessions' })">
          Darslarim
        </BaseButton>
      </div>
    </div>

    <div v-else class="flex min-h-0 flex-1">
      <!-- Video + boshqaruv -->
      <main class="flex min-w-0 flex-1 flex-col gap-3 p-3">
        <VideoStage
          :tiles="tiles"
          :host-user-id="hostUserId"
          :status="mediaStatus"
          :role-by-user-id="roleByUserId"
          :connection-error="mediaConnectionError"
          @retry="handleRetry"
        />

        <div class="flex shrink-0 justify-center">
          <MediaControlBar
            :is-mic-on="isMicOn"
            :is-camera-on="isCameraOn"
            :is-screen-sharing="isScreenSharing"
            :can-share-screen="isHost"
            :hand-raised="handRaised"
            :is-busy="mediaBusy"
            :disabled="sessionEnded"
            :unread-count="chatUnread"
            @toggle-mic="toggleMic"
            @toggle-camera="toggleCamera"
            @toggle-screen="toggleScreenShare"
            @toggle-hand="handleToggleHand"
            @toggle-chat="openChat"
            @leave="handleLeave"
          />
        </div>
      </main>

      <!-- Mobil uchun fon qoplamasi -->
      <div
        v-if="chatOpen"
        class="fixed inset-0 z-30 bg-black/60 lg:hidden"
        aria-hidden="true"
        @click="chatOpen = false"
      />

      <!--
        BITTA ChatPanel nusxasi: mobilda pastdan chiquvchi panel, katta ekranda
        o'ng ustun. Ikkita nusxa qilinsa — xabarlar DOM'da ikki barobar bo'lardi.
      -->
      <div
        class="min-h-0 border-line bg-ink-900 lg:static lg:inset-auto lg:z-auto lg:flex lg:w-[380px] lg:shrink-0 lg:animate-none lg:rounded-none lg:border-l lg:border-t-0 lg:shadow-none xl:w-[420px]"
        :class="
          chatOpen
            ? 'fixed inset-x-0 bottom-0 top-14 z-40 flex animate-sheet-up flex-col rounded-t-2xl border-t shadow-2xl'
            : 'hidden'
        "
      >
        <ChatPanel
          class="w-full flex-1"
          :messages="messages"
          :current-user-id="auth.userId"
          :role-by-user-id="roleByUserId"
          :participants="participants"
          :participant-count="participantCount"
          :raised-hands="raisedHands"
          :status="chatStatus"
          :can-send="canSend"
          :is-sending="isSending"
          :cooldown-remaining-ms="cooldownRemainingMs"
          :notice="chatNotice"
          :session-ended="sessionEnded"
          :send="sendMessage"
          @retry="retryHub"
          @dismiss-notice="dismissNotice"
          @close="chatOpen = false"
          @unread-change="chatUnread = $event"
        />
      </div>
    </div>

    <!-- ========================= Dars yakunlandi ========================== -->
    <div
      v-if="sessionEnded"
      class="fixed inset-0 z-50 flex items-center justify-center bg-ink-950/90 px-6 backdrop-blur"
    >
      <div class="w-full max-w-sm rounded-2xl bg-ink-900 p-6 text-center ring-1 ring-inset ring-line">
        <div
          class="mx-auto flex size-12 items-center justify-center rounded-2xl bg-brand-500/15 text-brand-300"
        >
          <AppIcon name="check" :size="24" />
        </div>
        <h2 class="mt-4 text-lg font-semibold text-slate-100">Dars yakunlandi</h2>
        <p class="mt-1 text-sm text-slate-400">Qatnashganingiz uchun rahmat.</p>
        <BaseButton class="mt-5" block @click="router.push({ name: 'sessions' })">
          Darslarim ro‘yxatiga
        </BaseButton>
      </div>
    </div>
  </div>
</template>
