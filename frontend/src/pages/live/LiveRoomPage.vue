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
import { homeRouteFor } from '@/entities/user'
import { useAuthStore } from '@/features/auth/model/auth.store'
import ChatPanel from '@/features/chat/ui/ChatPanel.vue'
import { useLiveHub } from '@/features/live-hub/model/useLiveHub'
import { useLiveKitRoom } from '@/features/live-room/model/useLiveKitRoom'
import MediaControlBar from '@/features/live-room/ui/MediaControlBar.vue'
import VideoStage from '@/features/live-room/ui/VideoStage.vue'
import RecordingIndicator from '@/features/session-recording/ui/RecordingIndicator.vue'
import SessionRecordingControl from '@/features/session-recording/ui/SessionRecordingControl.vue'
import { toUserMessage } from '@/shared/api'
import { formatCountdown } from '@/shared/lib/datetime'
import { useBreakpoint } from '@/shared/lib/useBreakpoint'
import { useConfirm } from '@/shared/lib/useConfirm'
import { AppIcon, BaseBadge, BaseButton } from '@/shared/ui'

/*
  🔴 JONLI DARS SAHNASI QORONG'I QOLADI.

  Ilova 2026-08-10 da yagona YORUG' temaga o'tdi, lekin video sahnasi bundan
  MUSTASNO. Uch sabab:
   1) ko'z charchaydi — kino qoidasi: video atrofi to'q bo'ladi;
   2) ekran ulashishda oq ramka video kontrastini "yeydi";
   3) hech bir jonli dars mahsuloti (Zoom, Meet, LiveKit) yorug' emas.

  ★ BU ROL TEMASI EMAS, SIRT TEMASI — atribut nomi ham boshqa
  (`data-surface`, `data-theme` EMAS), shuning uchun karkas qo'ygan
  `data-theme` bilan to'qnashmaydi va uni saqlab-tiklash kerak emas
  (ilgari shunday qilinardi, chunki xona `teacher` temasini o'zlashtirardi).
  `style.css` da `[data-surface='stage']` ostida `ink-*`, `slate-*` va
  semantik shkalalar to'q sirt uchun qayta belgilanadi; aksent indigo qoladi.

  ★ ATRIBUT `<html>` GA QO'YILADI, sahifa ildiz `<div>` iga EMAS.
  Sabab — `DAVOM_ETTIRISH.md` 6-bo'lim, 9-tuzoq: `SessionRecordingControl`
  ning xato toasti `<Teleport to="body">` bilan chiziladi va sahifa
  daraxtidan TASHQARIDA turadi; `<div>` da bo'lganda u to'q sahna ustida
  YORUG' tema tokenlarida chiqardi. `<html>` da bo'lganda `<body>` fonining
  o'zi ham to'q bo'ladi — iPhone'dagi overscroll oq chaqnab ketmaydi.

  Bir vaqtda faqat BITTA jonli xona mount bo'ladi (marshrut daraxti shunday),
  shuning uchun mount'da qo'yib unmount'da olib tashlash yetarli.
*/
const STAGE_THEME_COLOR = '#0f1115'
let previousThemeColor: string | null = null

onMounted(() => {
  document.documentElement.dataset['surface'] = 'stage'

  /*
    Mobil brauzer manzil paneli ham to'q bo'ladi. Karkaslar chiqishda
    `theme-color` ni YORUG' qiymatga tiklaydi (`#f4f6fb`), ya'ni bu qator
    bo'lmasa telefon ekranining yuqori chizig'i oq bo'lib, to'q xona
    "qirqilgan" ko'rinardi.
  */
  const meta = document.querySelector<HTMLMetaElement>('meta[name="theme-color"]')
  if (meta !== null) {
    previousThemeColor = meta.content
    meta.content = STAGE_THEME_COLOR
  }
})

onBeforeUnmount(() => {
  delete document.documentElement.dataset['surface']
  const meta = document.querySelector<HTMLMetaElement>('meta[name="theme-color"]')
  if (meta !== null && previousThemeColor !== null) meta.content = previousThemeColor
})

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
  micPending,
  cameraPending,
  screenPending,
  audioBlocked,
  mediaError,
  connectionError: mediaConnectionError,
  connect: connectMedia,
  leave: leaveMedia,
  toggleMic,
  toggleCamera,
  toggleScreenShare,
  enableAudio,
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

/*
  YOTIQ TELEFON — SHU EKRANNING ENG OG'IR HOLATI.

  Tik holatda balandlik 700–900px: yuqori panel, holat chiziqlari, video va
  boshqaruv paneli bemalol joylashadi. Telefon YOTIQ tutilganda balandlik
  ~390px ga tushadi, lekin bu qatlamlarning hammasi joyida qoladi — video
  100px ga siqilib, dars ko'rib bo'lmaydigan holga keladi.

  ★ `isShortLandscape` = `(orientation: landscape) and (max-height: 500px)`,
  ya'ni FAQAT telefon. iPad yotiq holatda 768–1024px balandlikda bo'ladi va
  bu shartga TUSHMAYDI — planshet siqilgan chrome olmaydi.

  ★ CSS media query bilan qilib bo'lmasdi: `style.css` ga `@custom-variant`
  qo'shish kerak edi, u esa bu vazifa doirasidan tashqarida. `useBreakpoint`
  `matchMedia` ustida ishlaydi va boshlang'ich qiymatni mount'dan OLDIN
  o'qiydi — birinchi kadrda "sakrash" bo'lmaydi.

  `isDesktop` esa faqat xavfsiz zona uchun kerak (pastdagi `chatSheetStyle`).
*/
const { isDesktop, isShortLandscape } = useBreakpoint()

const chatOpen = ref(false)
const chatUnread = ref(0)
const actionBusy = ref(false)
const actionError = ref<string | null>(null)
const nowMs = ref(Date.now())
let clockTimer: number | null = null

// Darsdan chiqqanda qayerga qaytish ROLGA bog'liq: o'quvchi "Darslarim" ga,
// ustoz "Guruhlarim" ga, o'quv bo'limi esa boshqaruv paneliga.
const homeRoute = computed(() => homeRouteFor(auth.role))

const session = computed(() => sessionQuery.data.value ?? null)
const headerTitle = computed(() => {
  const current = session.value
  return current !== null ? sessionTitle(current) : 'Jonli dars'
})
const groupName = computed(() => session.value?.groupName ?? '')
const isLive = computed(() => session.value?.status === 'Live')
const canManageSession = computed(() => session.value?.isHost === true || isHost.value)

/*
  QO'L KO'TARISH — FAQAT O'QUVCHIDA (R1; loyiha egasi: "livechatda teacher
  is not needed to raise hand").

  ★ QAMROV: "bu darsning ustozimi?" EMAS, "o'quvchimi?" — ya'ni tugma
  o'quv bo'limi/administrator kuzatuvchisida ham chiqmaydi. Qo'l ko'tarish
  so'z SO'RASH signali va u XODIMGA qaratilgan; xodimning o'zi uni kimga
  ko'taradi? Bundan tashqari uning qo'li hamma ko'radigan "Qo'l ko'targanlar"
  ro'yxatiga tushib, ustozni chalg'itadigan shovqin bo'lardi.

  🔴 NIMA UCHUN `isHost` EMAS (ekran ulashishdagi naqshdan ATAYLAB farq):
   1) `isHost` — LiveKit qo'shilish javobidan keladi va ulanish tugagunicha
      `false` (`useLiveKitRoom.ts:162,575`). Unga bog'lansa ustoz tugmani
      bir necha soniya KO'RIB turardi va u keyin g'oyib bo'lardi — aynan
      olib tashlanayotgan narsa ko'z oldida miltillardi.
   2) Server bu chaqiruvni ROL da'vosi bo'yicha rad etadi (hub'da har
      chaqiruvda bazaga borish yo'q — `LiveClassHub` 5-qarori). Ikki qatlam
      turli mezonga tayansa, ular kelishmagan holatda tugma ko'rinib turib
      server xato qaytarardi.
*/
const isStudent = computed(() => auth.role === 'Student')

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
  /*
    `mediaStatus === 'disconnected'` HAM xato deb hisoblanadi.
    Ilgari faqat `failed` tekshirilardi — ya'ni ulanish o'rnatilgandan KEYIN
    uzilsa, yuqorida hech qanday chiziq chiqmasdi va "Qayta urinish" tugmasi
    ham ko'rinmasdi. Video jimgina yo'qolardi.
  */
  if (
    mediaStatus.value === 'failed' ||
    mediaStatus.value === 'disconnected' ||
    chatStatus.value === 'disconnected'
  ) {
    return {
      tone: 'error',
      text: mediaConnectionError.value ?? 'Serverga ulanib bo‘lmadi. Internet aloqangizni tekshiring.',
    }
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

/*
  TO'RTTA CHIZIQ BIR VAQTDA CHIQISHI MUMKIN (aloqa, ovoz blokka tushgan,
  media xatosi, amal xatosi) — ular bir-birini istisno qilmaydi. Har biri
  ~30px: 390px balandlikdagi yotiq telefonda bu videodan qolgan joyni ham
  yeb qo'yardi.

  ★ HECH BIRI YASHIRILMAYDI. Xato matni — foydalanuvchi uchun yagona
  ma'lumot manbai; uni "joy yetmadi" deb olib tashlash jimgina ishlamaslik
  bo'lardi. Buning o'rniga ikki bosqichli DEGRADATSIYA:
   1) chiziqlar ko'paygan sari ichki bo'shliq qisqaradi;
   2) umumiy balandlik `max-h` bilan cheklanadi va o'rov skroll bo'ladi —
      ya'ni matn baribir o'qiladi, lekin video "bosib" qolmaydi.
*/
const noticeCount = computed(
  () =>
    (banner.value !== null ? 1 : 0) +
    (audioBlocked.value ? 1 : 0) +
    (mediaError.value !== null ? 1 : 0) +
    (actionError.value !== null ? 1 : 0),
)

/** Chiziqning ichki bo'shlig'i — soniga va yo'nalishga qarab siqiladi. */
const noticeRowClass = computed(() => {
  if (isShortLandscape.value || noticeCount.value >= 3) return 'px-3 py-0.5'
  if (noticeCount.value === 2) return 'px-4 py-1'
  return 'px-4 py-1.5'
})

/*
  Xavfsiz zona (T3). Ildiz `h-dvh` — ya'ni oyna balandligiga TENG, iPhone'ning
  pastki "home indicator" chizig'i esa oynaning ICHIDA. Shu sababli boshqaruv
  paneli o'sha chiziq ostida qolib, "chiqish" tugmasini bosish qiyin edi.

  ★ Chap/o'ng inset ham qo'shildi: yotiq holatda "notch" YON tomonda bo'ladi
  va videoni qirqardi. Tik holatda bu qiymatlar 0 — hech narsa o'zgarmaydi.
  ★ Inline `style`, Tailwind klassi emas: `env()` ni utility'ga chiqarish
  uchun `style.css` ga tegish kerak bo'lardi (ilovadagi mavjud naqsh ham
  shunday — `StudentTabBar`, `BaseModal`).
*/
const rootSafeAreaStyle = {
  paddingBottom: 'env(safe-area-inset-bottom, 0px)',
  paddingLeft: 'env(safe-area-inset-left, 0px)',
  paddingRight: 'env(safe-area-inset-right, 0px)',
} as const

/*
  Chat varaqasi `fixed` bo'lganda ildizning padding'i unga TA'SIR QILMAYDI
  (fixed element viewport'ga nisbatan joylashadi) — shuning uchun xabar
  yozish maydoni yana home indicator ostida qolardi. Desktopda esa panel
  ildiz ichidagi oddiy ustun, ya'ni inset ikki marta qo'shilmasligi kerak.
*/
const chatSheetStyle = computed(() =>
  isDesktop.value ? undefined : { paddingBottom: 'env(safe-area-inset-bottom, 0px)' },
)

/* -------------------------------- amallar ---------------------------------- */

const confirm = useConfirm()

async function handleLeave(): Promise<void> {
  await leaveMedia()
  await router.push({ name: homeRoute.value })
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

/**
 * R4 — DARSNI YAKUNLASH TASDIQLANADI (bir bosishlik, qaytarib bo'lmaydi).
 *
 * ★ NEGA AYNAN BU TUGMA `danger`, "Darsni boshlash" esa tasdiqsiz:
 * boshlash — QAYTARILADIGAN amal (xato bosilsa darhol yakunlanadi va hech
 * kim zarar ko'rmaydi), yakunlash esa uch narsani BIR VAQTDA va qaytarib
 * bo'lmaydigan qilib bajaradi: davomat yopiladi, yozuv to'xtaydi, xonadagi
 * HAMMA uziladi. Tugma "Yakunlash" yozuvi bilan `SessionRecordingControl`
 * yonida turadi va telefonda ikkalasi ham qisqargan holatda ("Stop") —
 * xato bosish real ehtimol.
 *
 * ★ ISHTIROKCHILAR SONI `details` DA: "hozir 14 kishi bor" degan raqam
 * "dars tugadimi yoki men adashdimmi?" savoliga tugmani bosmasdan javob
 * beradi. Reja B2: yon ta'siri katta amalda RAQAM ko'rsatilsin.
 */
async function handleEndSession(): Promise<void> {
  if (actionBusy.value) return

  const ok = await confirm({
    title: 'Darsni yakunlash',
    message: `“${headerTitle.value}” yakunlanadi va xonadagi hamma darhol uziladi.`,
    confirmLabel: 'Yakunlash',
    tone: 'danger',
    details: [
      `Hozir xonada ${participantCount.value} ishtirokchi bor — hammasi chiqariladi.`,
      'Davomat yopiladi: shu ondan keyin kirgan o‘quvchi hisobga olinmaydi.',
      'Yozuv ketayotgan bo‘lsa to‘xtaydi va shu joyda yopiladi.',
    ],
  })
  if (!ok) return

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
  <div
    class="flex h-dvh flex-col overflow-hidden bg-ink-950"
    :style="rootSafeAreaStyle"
  >
    <!-- ============================ Yuqori panel ===========================
      "Orqaga" tugmasi 2026-08-13 da OLIB TASHLANDI (R3, loyiha egasi:
      "livechatda orqaga qaytish button kerakmas").

      NIMA UCHUN XAVFSIZ: u marshrut tarixiga qaytarmasdi — pastdagi QIZIL
      "Darsdan chiqish" tugmasi bilan AYNI `handleLeave` ni chaqirardi, ya'ni
      bitta amalning ikkinchi, kamroq ko'rinadigan nusxasi edi. Xonadan
      chiqishning uch yo'li joyida qoladi: boshqaruv panelidagi qizil tugma,
      "Dars yakunlandi" oynasi va noto'g'ri manzil ekrani.

      ★ CHAP TO'LDIRISH QO'SHILMADI (ataylab): tugma ketgach sarlavha `px-3`
      (yotiq telefonda `px-2`) chegarasiga tushdi — bu ostidagi video
      sahnasining `p-3` / `p-2` chap qirrasi bilan AYNAN bir vertikalda.
      Ilgari sarlavha tugma kengligicha ichkarida turardi va bu ikki qator
      mos kelmasdi; ya'ni tugmaning o'rnini "to'ldirish" tekislikni QAYTA
      buzardi.
    -->
    <header
      class="flex shrink-0 items-center border-b border-line bg-ink-900/80 backdrop-blur"
      :class="isShortLandscape ? 'gap-2 px-2 py-1' : 'gap-3 px-3 py-2.5'"
    >
      <!--
        Yotiq telefonda sarlavha va guruh nomi BITTA qatorga chiqadi.

        ★ Matn HAM, tartib HAM o'zgarmaydi — faqat yo'nalish: ustundan
        qatorga. Yotiq holatda kenglik 700px dan ortiq, ya'ni ikkalasi ham
        qisqarmasdan sig'adi; buning evaziga panel ~18px pastroq bo'ladi.
      -->
      <div
        class="flex min-w-0 flex-1"
        :class="isShortLandscape ? 'items-baseline gap-2' : 'flex-col'"
      >
        <div class="flex min-w-0 items-center gap-2">
          <h1
            class="truncate text-sm font-semibold text-slate-100 sm:text-base"
            v-text="headerTitle"
          />
          <BaseBadge
            v-if="isLive"
            tone="live"
            dot
          >
            Jonli
          </BaseBadge>
        </div>
        <p
          v-if="groupName.length > 0"
          class="truncate text-xs text-slate-500"
          :class="isShortLandscape ? 'min-w-0 flex-1' : ''"
          v-text="groupName"
        />
      </div>

      <div class="flex items-center gap-2">
        <!-- ================================================================
          🔴 "YOZUVDA" — ROZILIK INDIKATORI, XONADAGI HAR KIMGA.

          ★ SHARTDA `canManageSession` YO'Q va bu ENG MUHIM tafsilot: 2026-08-13
          da yozuv AVTOMATIK bo'ldi (guruhning `recordEnabled` kaliti), ya'ni
          o'quvchi hech kim tugma bosmagan holda yozib olinadi. Bungacha host
          tugmasi indikator vazifasini bajarardi — lekin FAQAT host uchun.

          Nishon HAQIQIY yozuv holatiga ulanadi (`GET .../recording-status`),
          "guruhda yozuv yoqilgan" degan sozlamaga emas — sabab
          `useRecordingIndicator` izohida.

          ★ `isLive` SHARTI: yozuv faqat jonli darsda ketadi, ya'ni
          rejalashtirilgan/yakunlangan darsda so'rov ham yuborilmaydi.
          Komponentning O'ZI ham yozuv yo'q bo'lsa hech narsa chizmaydi,
          shuning uchun bu yerda ikkinchi shart kerak emas.
        ================================================================= -->
        <RecordingIndicator
          v-if="isLive && isValidSession"
          :session-id="sessionId"
          :is-live="isLive"
        />

        <span
          v-if="countdown !== null"
          class="hidden items-center gap-1.5 rounded-lg bg-ink-800 px-2.5 py-1.5 text-xs font-medium tabular-nums ring-1 ring-inset ring-line sm:inline-flex"
          :class="isEndingSoon ? 'text-amber-300' : 'text-slate-300'"
          title="Dars tugashiga qolgan vaqt"
        >
          <AppIcon
            name="calendar"
            :size="14"
          />
          {{ countdown }}
        </span>

        <span
          class="inline-flex items-center gap-1.5 rounded-lg bg-ink-800 px-2.5 py-1.5 text-xs font-medium text-slate-300 tabular-nums ring-1 ring-inset ring-line"
          title="Ishtirokchilar soni"
        >
          <AppIcon
            name="users"
            :size="14"
          />
          {{ participantCount }}
        </span>

        <!--
          Yozuvni boshlash/to'xtatish — FAQAT dars egasiga va boshqaruvchiga,
          faqat dars JONLI paytida. O'quvchi bu chaqiruvlardan 403, jonli
          bo'lmagan darsda esa hamma 409 oladi (jonli tekshirilgan).
        -->
        <SessionRecordingControl
          v-if="canManageSession && isLive && isValidSession"
          :session-id="sessionId"
          :is-live="isLive"
        />

        <BaseButton
          v-if="canManageSession && session?.status === 'Scheduled'"
          size="sm"
          variant="success"
          :loading="actionBusy"
          @click="handleStartSession"
        >
          <template #icon>
            <AppIcon
              name="play"
              :size="14"
            />
          </template>
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

    <!-- ========================= Holat chiziqlari ==========================
      O'ROV: to'rtta chiziq birdan chiqqanda ular videodan joy o'g'irlamasin
      (`noticeRowClass` izohiga qarang). `max-h` + skroll — yashirish EMAS,
      cheklash: matn to'liq qoladi, kerak bo'lsa surib o'qiladi.
    -->
    <div
      class="scrollbar-slim flex shrink-0 flex-col overflow-y-auto overscroll-contain"
      :class="isShortLandscape ? 'max-h-[25dvh]' : 'max-h-[30dvh]'"
    >
      <div
        v-if="banner !== null"
        class="flex shrink-0 items-center gap-2 border-b text-xs font-medium"
        :class="[BANNER_CLASS[banner.tone], noticeRowClass]"
        role="status"
      >
        <AppIcon
          :name="banner.tone === 'error' ? 'wifi-off' : 'refresh'"
          :size="14"
        />
        <span
          class="flex-1"
          v-text="banner.text"
        />
        <button
          v-if="banner.tone === 'error'"
          type="button"
          class="tap-expand rounded-md px-2 py-0.5 font-semibold underline-offset-2 hover:underline"
          @click="handleRetry"
        >
          Qayta urinish
        </button>
      </div>

      <!--
        Brauzer ovozni avtomatik chalishga ruxsat bermaganda chiqadi.
        Bu holat ilgari HECH QAYERDA ko'rinmasdi: ustoz gapirardi, o'quvchi esa
        sukunatni "mikrofon ishlamayapti" deb tushunardi.
      -->
      <div
        v-if="audioBlocked"
        class="flex shrink-0 items-center gap-2 border-b border-brand-500/25 bg-brand-500/10 text-xs text-brand-200"
        :class="noticeRowClass"
        role="alert"
      >
        <AppIcon
          name="mic-off"
          :size="14"
        />
        <span class="flex-1">Brauzer ovozni bloklab qo‘ydi.</span>
        <button
          type="button"
          class="tap-expand rounded-md px-2 py-0.5 font-semibold underline-offset-2 hover:underline"
          @click="enableAudio"
        >
          Ovozni yoqish
        </button>
      </div>

      <div
        v-if="mediaError !== null"
        class="flex shrink-0 items-center gap-2 border-b border-amber-500/25 bg-amber-500/10 text-xs text-amber-200"
        :class="noticeRowClass"
        role="alert"
      >
        <span
          class="flex-1"
          v-text="mediaError"
        />
        <button
          type="button"
          class="tap-expand rounded p-0.5 hover:text-amber-100"
          @click="dismissMediaError"
        >
          <AppIcon
            name="close"
            :size="14"
          />
        </button>
      </div>

      <div
        v-if="actionError !== null"
        class="flex shrink-0 items-center gap-2 border-b border-rose-500/25 bg-rose-500/10 text-xs text-rose-200"
        :class="noticeRowClass"
        role="alert"
      >
        <span
          class="flex-1"
          v-text="actionError"
        />
        <button
          type="button"
          class="tap-expand rounded p-0.5 hover:text-rose-100"
          @click="actionError = null"
        >
          <AppIcon
            name="close"
            :size="14"
          />
        </button>
      </div>
    </div>

    <!-- ============================== Asosiy =============================== -->
    <div
      v-if="!isValidSession"
      class="flex flex-1 items-center justify-center px-6 text-center"
    >
      <div>
        <p class="text-sm font-semibold text-slate-200">
          Dars manzili noto‘g‘ri
        </p>
        <BaseButton
          class="mt-4"
          size="sm"
          variant="secondary"
          @click="router.push({ name: homeRoute })"
        >
          Darslarim
        </BaseButton>
      </div>
    </div>

    <div
      v-else
      class="flex min-h-0 flex-1"
    >
      <!--
        Video + boshqaruv.

        `relative` — yotiq telefonda boshqaruv paneli video USTIDA suzadi
        (pastdagi izohga qarang), shuning uchun tayanch nuqta kerak.
      -->
      <main
        class="relative flex min-w-0 flex-1 flex-col"
        :class="isShortLandscape ? 'p-2' : 'gap-3 p-3'"
      >
        <VideoStage
          :tiles="tiles"
          :host-user-id="hostUserId"
          :status="mediaStatus"
          :role-by-user-id="roleByUserId"
          :connection-error="mediaConnectionError"
          @retry="handleRetry"
        />

        <!--
          Yotiq telefonda boshqaruv paneli OQIMDAN CHIQADI va video ustida
          suzadi. Sabab: 44px tugmalar + o'rov = ~60px, bu 390px balandlikning
          15% i. Panelning o'zida `bg-ink-900/90 backdrop-blur` bor, ya'ni
          video ustida ham o'qiladi — bu Zoom/Meet dagi bir xil naqsh.

          ★ `pointer-events-none` o'rovda, `pointer-events-auto` panelda:
          aks holda ko'rinmas to'liq kenglikdagi qatlam videoning pastki
          qismidagi bosishlarni yutib qo'yardi.
          ★ Tik holatda hech narsa o'zgarmaydi — panel avvalgidek oqimda.
        -->
        <div
          class="flex justify-center"
          :class="
            isShortLandscape
              ? 'pointer-events-none absolute inset-x-0 bottom-2 z-10 px-2'
              : 'shrink-0'
          "
        >
          <MediaControlBar
            class="pointer-events-auto"
            :is-mic-on="isMicOn"
            :is-camera-on="isCameraOn"
            :is-screen-sharing="isScreenSharing"
            :can-share-screen="isHost"
            :hand-raised="handRaised"
            :can-raise-hand="isStudent"
            :mic-pending="micPending"
            :camera-pending="cameraPending"
            :screen-pending="screenPending"
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

      <!--
        Mobil uchun fon qoplamasi. `bg-black/60` ATAYLAB qoldirildi (yorug'
        temaga o'tkazilmadi): bu ekran `[data-surface='stage']` ostida —
        sahna baribir to'q, ostida esa video oqadi. Yorug' `slate-900/35`
        qatlami u yerda videoni oqartirib, chat panelini "havoda" qoldirardi.
      -->
      <div
        v-if="chatOpen"
        class="fixed inset-0 z-30 bg-black/60 lg:hidden"
        aria-hidden="true"
        @click="chatOpen = false"
      />

      <!--
        BITTA ChatPanel nusxasi: mobilda pastdan chiquvchi panel, katta ekranda
        o'ng ustun. Ikkita nusxa qilinsa — xabarlar DOM'da ikki barobar bo'lardi.

        UCH BOSQICH (planshet bosqichi 2026-08-13 da qo'shildi):
         • < md (768px) — butun ekranni egallovchi varaqa, pastdan chiqadi.
         • md…lg — O'NG TOMONDAN chiquvchi 380px lik varaqa: chapda video
           KO'RINIB TURADI. Ilgari iPad tik holatida (768px) chat butun
           videoni bosib qolardi, holbuki ekranda ikkalasiga ham joy bor.
         • ≥ lg (1024px) — oqimdagi doimiy o'ng ustun (o'zgarmadi).

        ★ NEGA md'da DOIMIY USTUN EMAS: yon menyu ham, jadvallar ham `lg:` da
        ochiladi (loyiha egasining 2026-08-13 dagi qarori, `style.css`
        "CHEGARALAR" izohi). Planshetda chatni doimiy ustunga aylantirish
        `md` ni ikkinchi "desktop" chegarasiga aylantirardi — o'rniga
        planshet oraliq xulqni oladi: qoplama, lekin YARIM ekranli.

        ★ `max-md:` va `md:` — bir-birini ISTISNO qiluvchi media so'rovlar,
        shuning uchun Tailwind tartibiga bog'liq emas; `lg:` esa ikkalasidan
        keyin chiqadi va ustunni tiklaydi.
      -->
      <div
        class="min-h-0 border-line bg-ink-900 lg:static lg:inset-auto lg:z-auto lg:flex lg:w-[380px] lg:shrink-0 lg:animate-none lg:rounded-none lg:border-l lg:border-t-0 lg:shadow-none xl:w-[420px]"
        :class="
          chatOpen
            ? 'fixed z-40 flex flex-col shadow-2xl max-md:inset-0 max-md:animate-sheet-up md:inset-y-0 md:right-0 md:w-[380px] md:animate-drawer-in md:border-l'
            : 'hidden'
        "
        :style="chatSheetStyle"
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
          <AppIcon
            name="check"
            :size="24"
          />
        </div>
        <h2 class="mt-4 text-lg font-semibold text-slate-100">
          Dars yakunlandi
        </h2>
        <p class="mt-1 text-sm text-slate-400">
          Qatnashganingiz uchun rahmat.
        </p>
        <BaseButton
          class="mt-5"
          block
          @click="router.push({ name: homeRoute })"
        >
          Darslarim ro‘yxatiga
        </BaseButton>
      </div>
    </div>
  </div>
</template>
