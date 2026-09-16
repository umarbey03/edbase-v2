import {
  AudioPresets,
  ConnectionError,
  ConnectionErrorReason,
  ConnectionQuality,
  ConnectionState,
  createLocalVideoTrack,
  DisconnectReason,
  LocalVideoTrack,
  Room,
  RoomEvent,
  Track,
  VideoPreset,
  VideoPresets,
} from 'livekit-client'
import type {
  LocalTrackPublication,
  Participant,
  RemoteParticipant,
  RemoteTrack,
  RemoteTrackPublication,
  TrackPublication,
} from 'livekit-client'
import { onBeforeUnmount, ref, shallowRef } from 'vue'
import type { Ref, ShallowRef } from 'vue'

import { fetchLiveKitJoin } from '@/entities/session'
import { isApiError, toUserMessage } from '@/shared/api'

import { createLiveEventReporter, createScreenWakeLock } from './liveClientEvents'
import { clearMediaIntent, readMediaIntent, writeMediaIntent } from './liveMediaIntent'

export type MediaStatus =
  | 'idle'
  | 'loading'
  | 'connecting'
  | 'connected'
  | 'reconnecting'
  | 'disconnected'
  | 'failed'

/**
 * Ishtirokchining SERVERGACHA bo'lgan aloqa sifati.
 *
 * ★ NIMA UCHUN LiveKit turi emas, O'Z turimiz: `ConnectionQuality` — sonli
 * enum va uni shablonga chiqarish "quality === 2" degan o'qib bo'lmaydigan
 * shartlarga olib borardi.
 */
export type LinkQuality = 'excellent' | 'good' | 'poor' | 'lost' | 'unknown'

function toLinkQuality(value: ConnectionQuality): LinkQuality {
  switch (value) {
    case ConnectionQuality.Excellent: return 'excellent'
    case ConnectionQuality.Good: return 'good'
    case ConnectionQuality.Poor: return 'poor'
    case ConnectionQuality.Lost: return 'lost'
    default: return 'unknown'
  }
}

/** Sahnada chiziladigan bitta katakcha (kamera yoki ekran). */
export interface ParticipantTile {
  /** `v-for` uchun barqaror kalit. */
  key: string
  /** LiveKit identity — SPEC 7 bo'yicha bu `userId` ning satr ko'rinishi. */
  identity: string
  userId: number | null
  name: string
  isLocal: boolean
  isScreenShare: boolean
  micEnabled: boolean
  cameraEnabled: boolean
  isSpeaking: boolean
  videoTrack: Track | null
  /** Shu ishtirokchining aloqa sifati — katakchada zaif signal belgisi uchun. */
  quality: LinkQuality
}

export interface UseLiveKitRoomResult {
  status: Ref<MediaStatus>
  tiles: ShallowRef<ParticipantTile[]>
  isHost: Ref<boolean>
  roomName: Ref<string | null>
  endsAt: Ref<string | null>
  isMicOn: Ref<boolean>
  isCameraOn: Ref<boolean>
  isScreenSharing: Ref<boolean>
  /**
   * Brauzer ekran ulashishni umuman qo'llab-quvvatlaydimi (telefonda — YO'Q).
   *
   * `Ref` EMAS, oddiy `boolean`: qiymat sahifa hayoti davomida o'zgarmaydi
   * (izoh — `SCREEN_SHARE_SUPPORTED` da).
   */
  screenShareSupported: boolean
  /** Har bir tugma uchun ALOHIDA — faqat bosilgani kutish holatiga tushadi. */
  micPending: Ref<boolean>
  cameraPending: Ref<boolean>
  screenPending: Ref<boolean>
  /** Brauzer ovozni avtomatik chalishga ruxsat bermadi (bosish talab qilinadi). */
  audioBlocked: Ref<boolean>
  /** O'ZINGIZNING serverga ulanish sifatingiz (indikator uchun). */
  localQuality: Ref<LinkQuality>
  /**
   * Zaif kanal haqida ogohlantirish — XATO EMAS.
   *
   * `mediaError` dan ataylab ayrim: u "amal bajarilmadi" degani, bu esa
   * "hammasi ishlayapti, lekin internetingiz zaif" degani. Ikkalasini
   * bitta maydonga qo'shsak, foydalanuvchi tuzatib bo'lmaydigan xatoni
   * ko'rib qo'ng'iroq qilardi.
   */
  linkWarning: Ref<string | null>
  mediaError: Ref<string | null>
  connectionError: Ref<string | null>
  /**
   * Ustoz SERVER orqali mikrofon/kamerani o'chirdi (2026-09-09). Alohida
   * xabar: `mediaError` "qurilma ishlamadi" degani, bu esa "ishlayapti,
   * lekin ustoz o'chirdi" — ikkalasi bir rangda chiqsa o'quvchi kamerasini
   * "buzilgan" deb o'ylab, brauzer sozlamasini titkilay boshlardi.
   */
  moderationNotice: Ref<string | null>
  dismissModerationNotice: () => void
  /**
   * KITOB TAXTASI (2026-09-09): canvas EKRAN ULASHUVI treki sifatida
   * uzatiladi — telefondan ekran ulasha olmaydigan ustoz uchun.
   * Oddiy ekran ulashuvi bilan bir vaqtda bo'lmaydi (bittasi ikkinchisini to'xtatadi).
   */
  isCanvasSharing: Ref<boolean>
  canvasSharePending: Ref<boolean>
  shareCanvas: (canvas: HTMLCanvasElement) => Promise<void>
  stopCanvasShare: () => Promise<void>
  connect: () => Promise<void>
  leave: () => Promise<void>
  /**
   * Sahifa NEGA yopilayotganini belgilaydi — `page-closed` diagnostika
   * hodisasiga tushadi. Birinchi aytilgan sabab saqlanadi.
   */
  noteExit: (reason: string) => void
  toggleMic: () => Promise<void>
  toggleCamera: () => Promise<void>
  toggleScreenShare: () => Promise<void>
  enableAudio: () => Promise<void>
  dismissMediaError: () => void
  dismissLinkWarning: () => void
}

function parseUserId(identity: string): number | null {
  const parsed = Number.parseInt(identity, 10)
  return Number.isNaN(parsed) ? null : parsed
}

function describeMediaError(error: unknown): string {
  if (error instanceof DOMException || (error instanceof Error && 'name' in error)) {
    switch (error.name) {
      case 'NotAllowedError':
        return 'Brauzer ruxsat bermadi. Manzil satridagi qulf belgisidan mikrofon/kameraga ruxsat bering.'
      case 'NotFoundError':
        return 'Qurilma topilmadi. Mikrofon yoki kamera ulanganini tekshiring.'
      case 'NotReadableError':
        return 'Qurilma band. Uni ishlatayotgan boshqa dasturni yoping.'
      case 'NotSupportedError':
        return 'Brauzeringiz bu imkoniyatni qo‘llab-quvvatlamaydi.'
      default:
        break
    }
  }
  return toUserMessage(error)
}

/**
 * ════════════════════════════════════════════════════════════════════════
 * 🔴 EKRAN ULASHISH TELEFONDA UMUMAN MUMKIN EMAS (2026-09-03)
 * ════════════════════════════════════════════════════════════════════════
 *
 * `navigator.mediaDevices.getDisplayMedia` — ekran ulashishning YAGONA
 * yo'li — quyidagilarda MAVJUD EMAS:
 *   • iOS/iPadOS: hamma brauzerda (Safari, Chrome, Telegram ichidagi
 *     ko'rinish — hammasi WebKit ustida ishlaydi, ya'ni "Chrome o'rnataman"
 *     yechim emas);
 *   • Android: Chrome, Firefox va boshqalarida (Android'da ekran yozib
 *     olish tizim darajasidagi ruxsat, veb API'da ochilmagan).
 *
 * Bu BIZNING kodimizdagi nosozlik emas — platforma cheklovi va uni
 * frontend'dan aylanib o'tib bo'lmaydi.
 *
 * ★ NIMA UCHUN TEKSHIRUV KERAK: tekshiruvsiz LiveKit'ning
 *   `setScreenShareEnabled()` metodi ichkarida yiqilardi va ustoz
 *   "navigator.mediaDevices.getDisplayMedia is not a function" degan
 *   INGLIZCHA texnik matnni ko'rardi. Ustozlar shikoyati aynan shu edi:
 *   "telefonda ekranni ulashib bo'lmayapti" — sabab hech qayerda
 *   aytilmagan.
 *
 * ★ BIR MARTA HISOBLANADI: brauzer imkoniyati sahifa hayoti davomida
 *   o'zgarmaydi, ya'ni har bosishda qayta tekshirish keraksiz.
 */
const SCREEN_SHARE_SUPPORTED =
  typeof navigator !== 'undefined' &&
  navigator.mediaDevices !== undefined &&
  typeof navigator.mediaDevices.getDisplayMedia === 'function'

/**
 * Qo'llab-quvvatlanmagan holatda bosilganda ko'rsatiladigan matn.
 *
 * ★ IKKI SABAB AJRATILADI. `navigator.mediaDevices` XAVFSIZ BO'LMAGAN
 * ulanishda (https siz, IP orqali) ham `undefined` bo'ladi — bu qurilma
 * emas, MANZIL muammosi va yechimi butunlay boshqa. Bitta umumiy matn
 * yozilsa, http orqali kirgan odam "telefon qo'llab-quvvatlamaydi" degan
 * NOTO'G'RI javob olardi va sababni hech qachon topmasdi.
 */
function screenShareUnsupportedText(): string {
  if (typeof window !== 'undefined' && window.isSecureContext === false) {
    return 'Ekranni ulashish faqat xavfsiz (https) ulanishda ishlaydi. Saytga rasmiy manzil orqali kiring.'
  }
  return (
    'Telefon va planshet brauzerlari ekranni ulashishni qo‘llab-quvvatlamaydi. ' +
    'Ekranni ulashish uchun darsga kompyuterdan kiring.'
  )
}

/**
 * LiveKit ulanish xatosini O'ZBEKCHA matnga aylantiradi.
 *
 * NIMA UCHUN KERAK: `toUserMessage()` `Error.message` ni o'zgarishsiz
 * qaytaradi va foydalanuvchi ekranida LiveKit SDK'sining inglizcha ichki
 * matni chiqib qolardi — jonli sinovda AYNAN shu kuzatilgan:
 *     "could not establish pc connection"
 * Bu o'quvchiga hech narsa aytmaydi va nima qilishni ham ko'rsatmaydi.
 */
function describeConnectError(error: unknown): string {
  // Token so'rovi 10 s javobsiz qolib bekor qilindi — brauzerning inglizcha
  // "signal is aborted without reason" matni foydalanuvchiga chiqmasin.
  if (typeof error === 'object' && error !== null && (error as { name?: unknown }).name === 'AbortError') {
    return 'Server javob bermadi. Internet aloqangizni tekshiring.'
  }
  if (error instanceof ConnectionError) {
    switch (error.reason) {
      case ConnectionErrorReason.NotAllowed:
        return 'Darsga kirishga ruxsat berilmadi. Sahifani yangilab, qaytadan kiring.'
      case ConnectionErrorReason.ServerUnreachable:
        return 'Video serverga yetib bo‘lmadi. Internet aloqangizni tekshiring.'
      case ConnectionErrorReason.Cancelled:
      case ConnectionErrorReason.LeaveRequest:
        return 'Ulanish bekor qilindi.'
      default:
        break
    }
  }
  /*
    Eng ko'p uchraydigan holat: signalling (WebSocket) ochildi, lekin MEDIA
    ulanishi (ICE/DTLS) o'rnatilmadi. SDK buni "could not establish pc
    connection" deb yozadi. Foydalanuvchiga aynan nima to'sqinlik qilayotganini
    aytamiz — bu holat deyarli har doim tarmoq/firewall bilan bog'liq.
  */
  const raw = error instanceof Error ? error.message : ''
  if (raw.includes('pc connection') || raw.includes('PeerConnection')) {
    return 'Video oqimi ulanmadi (tarmoq UDP trafigini to‘sayotgan bo‘lishi mumkin). Boshqa tarmoqdan urinib ko‘ring yoki administratorga xabar bering.'
  }
  return toUserMessage(error)
}

/**
 * Uzilish sababini foydalanuvchi tushunadigan matnga aylantiradi.
 *
 * NIMA UCHUN KERAK: ilgari `Disconnected` hodisasi kelganda holat jimgina
 * `disconnected` ga o'tardi va ekranda HECH QANDAY xabar chiqmasdi —
 * foydalanuvchi uchun bu "video shunchaki yo'qoldi" ko'rinishida edi.
 */
function describeDisconnect(reason: DisconnectReason | undefined): string {
  switch (reason) {
    case DisconnectReason.DUPLICATE_IDENTITY:
      return 'Siz boshqa oynada shu darsga kirdingiz. Bu oynadagi ulanish yopildi.'
    case DisconnectReason.PARTICIPANT_REMOVED:
      return 'Sizni darsdan chiqarishdi.'
    case DisconnectReason.ROOM_DELETED:
    case DisconnectReason.ROOM_CLOSED:
      return 'Dars xonasi yopildi.'
    case DisconnectReason.SERVER_SHUTDOWN:
      return 'Video server qayta ishga tushmoqda. Bir oz kutib, qayta urinib ko‘ring.'
    case DisconnectReason.JOIN_FAILURE:
      return 'Video serverga ulanib bo‘lmadi. Internet aloqangizni tekshiring.'
    default:
      return 'Video aloqasi uzildi. “Qayta urinish” tugmasini bosing.'
  }
}

/*
  ════════════════════════════════════════════════════════════════════════
  AVTOMATIK QAYTA ULANISH (2026-09-14)
  ════════════════════════════════════════════════════════════════════════

  🔴 NIMA UCHUN: LiveKit qisqa uzilishni o'zi tiklaydi, lekin undan uzoqroq
     uzilishda `Disconnected` beradi — va ilgari sahifa shu yerda TO'XTARDI:
     "Qayta urinish" tugmasi bosilishini kutardi. O'lchov (5 kunlik log):
     tarmoq uzilgan o'quvchi xonaga qaytguncha MEDIAN 34 s, p90 189 s. Internet
     bir necha soniya yo'qoladi, ovoz esa daqiqalab yo'q bo'lardi.

  ★ QAYTA ULANMAYDIGAN SABABLAR — foydalanuvchi yoki server ATAYLAB uzgan:
    boshqa oynada kirdi (ikki oyna bir-birini abadiy haydab chiqarardi),
    chiqarib yuborildi, xona yopildi/o'chirildi.

  🔴 `CLIENT_INITIATED` BU RO'YXATDA YO'Q VA BU ATAYLAB: bizning kodimiz
     `disconnect()` dan OLDIN har doim tinglovchilarni olib tashlaydi, ya'ni
     bu sabab bilan kelgan hodisa — livekit-client'ning O'ZI sahifa
     muzlaganda (`freeze`) yoki yashirilganda (`pagehide`) uzgani. Telefonda
     boshqa ilovaga o'tish aynan shu — eng ko'p uchraydigan holat.
*/

export function shouldReconnect(reason: DisconnectReason | undefined): boolean {
  switch (reason) {
    case DisconnectReason.DUPLICATE_IDENTITY:
    case DisconnectReason.PARTICIPANT_REMOVED:
    case DisconnectReason.ROOM_DELETED:
    case DisconnectReason.ROOM_CLOSED:
      return false
    default:
      return true
  }
}

/** Urinishlar orasidagi kutish: 1 → 2 → 4 → 8 → 10 s (undan keyin 10 s). */
const RECONNECT_DELAYS_MS = [1_000, 2_000, 4_000, 8_000] as const
const RECONNECT_MAX_DELAY_MS = 10_000

/**
 * ★ ±30% TASODIFIY SILJISH: bir qurilmadagi ikki oyna (yoki bir sinfdagi
 *   hamma telefon) internet qaytgan AYNI lahzada urinmasin.
 */
export function reconnectDelay(attempt: number): number {
  const base = RECONNECT_DELAYS_MS[attempt] ?? RECONNECT_MAX_DELAY_MS
  return Math.round(base * (0.7 + Math.random() * 0.6))
}

/**
 * Sahifa FONDA turganda tekshiruvlar orasidagi kutish.
 *
 * Fonda urinishning o'zi qilinmaydi (sabab — `attemptReconnect` izohi), bu
 * faqat zaxira taymer: `visibilitychange` kelmaydigan WebView'da sahifa
 * qachon ko'rinib qolganini shu oraliqda bilib olamiz. Uzoq (20 s), chunki
 * fonda tez-tez uyg'onish batareyani yeydi va hech narsa bermaydi.
 */
const HIDDEN_RECONNECT_DELAY_MS = 20_000

/** Token so'rovi shundan uzoq osilib qolsa — bekor qilinadi va qayta uriniladi. */
const JOIN_FETCH_TIMEOUT_MS = 10_000

/**
 * Shuncha vaqt ulanolmasak — sariq "qayta ulanmoqda" o'rniga qizil xabar va
 * "Qayta urinish" tugmasi chiqadi. Urinishlar FONDA davom etadi.
 */
const RECONNECT_BUTTON_AFTER_MS = 30_000

const RECONNECT_STILL_TRYING_TEXT =
  'Aloqa hali tiklanmadi — qayta ulanishga urinib turibmiz. Internetingizni tekshiring.'

/**
 * Qayta urinishning ma'nosi yo'q xato: ruxsat yo'q (403), dars topilmadi
 * (404), dars tugagan yoki boshlanmagan (409), sessiya tugagan (401).
 * Tarmoq xatosi (0), server xatosi (5xx) va 429 — vaqtinchalik.
 */
export function isFatalJoinError(error: unknown): boolean {
  if (isApiError(error)) {
    return error.status === 401 || error.status === 403 || error.status === 404 || error.status === 409
  }
  if (error instanceof ConnectionError) {
    return error.reason === ConnectionErrorReason.NotAllowed
  }
  return false
}

/*
  ZAIF KANAL MATNLARI — AYBLAMAYDI VA NIMA QILISHNI AYTADI.

  ⚠️ "Internetingiz yomon" deb yozilmaydi: foydalanuvchi buni ayb deb
  o'qiydi va qo'llab-quvvatlashga yozadi. Matn HOLATNI va CHORANI aytadi.
*/
/**
 * Kamera qanday o'lchamda OLINADI (yuborish qatlamlari alohida).
 *
 * 🔴 O'QUVCHIDA 240p (2026-09-14 gacha 360p), USTOZDA 720p. Manba o'lchami simulcast
 * qatlamlarining eng yuqorisini belgilaydi: 720p olinsa, 720p ham
 * yuboriladi. O'quvchining kichkina katakchasi uchun bu behuda
 * yuklashdir va u ovozdan joy oladi.
 */
function captureResolution(isHost: boolean) {
  return isHost ? VideoPresets.h720.resolution : STUDENT_CAMERA_CAPTURE
}

/*
  ════════════════════════════════════════════════════════════════════
  O'QUVCHI KAMERASI — OVOZGA JOY QOLDIRADI (2026-09-14)
  ════════════════════════════════════════════════════════════════════

  Ustoz o'quvchidan "kamerani yoq, gapir" deydi — aynan shu lahzada ovoz
  uzilmasligi kerak. Ilgari o'quvchi 360p + 180p yuborardi (~610 kbit/s),
  zaif o'quvchilarda o'lchangan kanal medianasi esa 279 kbit/s — kamera
  yoqilishi bilan ovoz bo'g'ilardi.

  ★ BITTA QATLAM, 240p/15 kadr, 150 kbit/s: o'quvchining yuzi kichik
    katakchada ko'rinadi, bundan ortig'i ovozdan joy oladi.
  ★ `priority: 'low'` — brauzer kanal torayganda AVVAL videoni qisadi.
    Ovozning ustuvorligi livekit-client'da standart holda `high`.
*/
const STUDENT_CAMERA_CAPTURE = { width: 426, height: 240, frameRate: 15 }

const STUDENT_CAMERA_ENCODING = {
  maxBitrate: 150_000,
  maxFramerate: 15,
  priority: 'low' as const,
}

/**
 * Ustoz ovozi: 48 → 32 kbit/s. Opus 32 kbit/s da nutq to'liq, musiqa ham
 * qabul qilinarli; har o'quvchining yuklab olish kanalida (RED bilan)
 * ~30 kbit/s bo'shaydi. `priority` oshkora — kelajakda tushib qolmasin.
 */
const HOST_AUDIO_PRESET = { maxBitrate: 32_000, priority: 'high' as const }

/*
  ════════════════════════════════════════════════════════════════════
  EKRAN ULASHISH — ZAIF TELEFONGA HAM SIG'ADIGAN QATLAM BO'LISHI SHART
  ════════════════════════════════════════════════════════════════════

  O'lchov (LiveKit logi, 2026-09-09..14): faqat mikrofon yuborayotgan
  o'quvchida `expectedUsage` 1.74 Mbit/s ga chiqqan — bu ustozning
  ekrani, unga YUBORILAYOTGAN. Sabab livekit-client standartida:
  ekran o'lchami hech bir presetga sig'masa (Retina/2K ekran) `original`
  tanlanadi — 7 Mbit/s va 30 kadr, eng past qatlam esa uning to'rtdan
  biri, ya'ni ~1.75 Mbit/s. O'quvchi kanalining medianasi 279 kbit/s —
  ya'ni ENG PAST qatlam ham sig'masdi va ovoz shu bilan birga bo'g'ilardi.

  ★ UCH QATLAM: 360p/3 kadr (~150 kbit/s) — zaif telefon; 720p/5 kadr —
    o'rtacha; 1080p/15 kadr — kuchli kanal va DARS YOZUVI (egress eng
    yuqori qatlamni oladi, ya'ni yozuv sifati pasaymaydi).
  ★ Slayd va hujjat uchun 15 kadr yetarli — ekranda harakat kam.
*/
const SCREEN_SHARE_ENCODING = { maxBitrate: 1_500_000, maxFramerate: 15 }

const SCREEN_SHARE_LAYERS = [
  new VideoPreset(640, 360, 150_000, 3),
  new VideoPreset(1280, 720, 500_000, 5),
]

const SCREEN_SHARE_CAPTURE = { width: 1920, height: 1080, frameRate: 15 }

const WEAK_LINK_CAMERA_OFF_TEXT =
  'Internet aloqangiz zaiflashdi — ovoz uzilmasligi uchun kamera vaqtincha '
  + 'o‘chirildi. Aloqa tiklanganda uni qayta yoqishingiz mumkin.'

const WEAK_LINK_AUDIO_ONLY_TEXT =
  'Internet aloqangiz zaif — ovozingiz uzilib eshitilishi mumkin. '
  + 'Imkon bo‘lsa Wi-Fi‘ga ulaning yoki routerga yaqinroq turing.'

const WEAK_LINK_HOST_TEXT =
  'Internet aloqangiz zaiflashdi — o‘quvchilar ovozingizni uzuq eshitishi '
  + 'mumkin. Kamerani vaqtincha o‘chirsangiz ovoz barqarorlashadi.'

export function useLiveKitRoom(sessionId: number): UseLiveKitRoomResult {
  const status = ref<MediaStatus>('idle')
  const isHost = ref(false)
  const roomName = ref<string | null>(null)
  const endsAt = ref<string | null>(null)
  const isMicOn = ref(false)
  const isCameraOn = ref(false)
  const isScreenSharing = ref(false)
  const micPending = ref(false)
  const cameraPending = ref(false)
  const screenPending = ref(false)
  const audioBlocked = ref(false)
  const mediaError = ref<string | null>(null)
  const connectionError = ref<string | null>(null)
  const localQuality = ref<LinkQuality>('unknown')
  const linkWarning = ref<string | null>(null)

  /**
   * Ishtirokchilarning aloqa sifati — `identity` bo'yicha.
   *
   * ⚠️ ODDIY `Map`, REAKTIV EMAS VA BU ATAYLAB: qiymat katakchalarga
   * `rebuildTiles()` orqali ko'chadi, ya'ni butun sahna baribir bitta
   * joydan yangilanadi. Reaktiv qilinsa, 200 kishilik xonada har sifat
   * hodisasi alohida render keltirib chiqarardi.
   */
  const qualityByIdentity = new Map<string, LinkQuality>()

  /**
   * Zaif kanalda kamera BIR MARTA o'chiriladi.
   *
   * 🔴 BAYROQSIZ BO'LMAYDI: sifat `poor` da uzoq turadi va har hodisada
   * kamerani o'chirsak, foydalanuvchi uni qayta yoqolmaydi — tugma bilan
   * jang boshlanardi. Bayroq sifat tiklanganda tushiriladi.
   */
  let cameraDroppedForWeakLink = false

  /**
   * Foydalanuvchi ogohlantirishni YOPGAN.
   *
   * ⚠️ BUSIZ BANNER QAYTA-QAYTA CHIQARDI: sifat hodisasi bir necha
   * soniyada bir keladi va har biri matnni qayta qo'yardi — yopib
   * bo'lmaydigan xabar eng bezovta qiladigan naqsh. Bayroq aloqa
   * tiklanganda tushadi, ya'ni KEYINGI uzilishda banner yana chiqadi.
   */
  let linkWarningDismissed = false
  const moderationNotice = ref<string | null>(null)

  /**
   * `shallowRef` — katakchalar massivi butunligicha almashtiriladi.
   * LiveKit `Track` obyektlari ichida MediaStreamTrack bor; ularni Vue proksisiga
   * o'rash MUMKIN EMAS (`attach()` ishlamay qoladi va xotira oqadi).
   */
  const tiles = shallowRef<ParticipantTile[]>([])

  /**
   * ★ O'Z VIDEOSINI KO'RISH (local preview) — LiveKit'dan MUSTAQIL.
   *
   * MUAMMO: kamera treki sahnaga faqat `localParticipant.getTrackPublication()`
   * orqali tushardi. Bu obyekt esa trek SERVERGA e'lon qilinib, server TASDIQ
   * qaytargandan keyingina paydo bo'ladi. Ya'ni SFU sekin javob bersa yoki
   * umuman javob bermasa — kamera YONIQ, `getUserMedia` MUVAFFAQIYATLI, lekin
   * ekranda HECH NIMA yo'q edi. Foydalanuvchi shikoyati aynan shu.
   *
   * YECHIM: trekni avval MAHALLIY yaratamiz va darhol chizamiz, e'lon qilish
   * esa keyin fonda ketadi. Endi o'z videongiz server bilan bog'liq emas.
   */
  const localCameraTrack = shallowRef<LocalVideoTrack | null>(null)

  /**
   * Kitob taxtasi treki (canvas → LiveKit). `isCanvasSharing` — UI uchun
   * ko'zgu; haqiqiy manba shu o'zgaruvchi. `canvasPublishing` — e'lon
   * hali tasdiqlanmagan oraliq (`rebuildTiles` dagi tekshiruv uchun).
   */
  let canvasTrack: LocalVideoTrack | null = null
  let canvasPublishing = false
  const isCanvasSharing = ref(false)
  const canvasSharePending = ref(false)

  let room: Room | null = null
  let disposed = false
  let rebuildFrame: number | null = null
  let rebuildTimer: number | null = null

  /*
    ── QAYTA ULANISH HOLATI ─────────────────────────────────────────────
    `disconnectedAt !== null` — qayta ulanish SIKLI ketmoqda. `reconnectEnabled`
    — birinchi urinish qilingan va foydalanuvchi o'zi "Chiqish" bosmagan.
    `connectInFlight` — ikki urinish (taymer + "Qayta urinish") ustma-ust
    ikkita `Room` yaratmasin.
  */
  let reconnectEnabled = false
  let disconnectedAt: number | null = null
  /**
   * Qizil xato taymerining boshlanishi. `disconnectedAt` dan farqi va nima
   * uchun ikkita sana kerakligi — `armRetryWindow()` izohida.
   */
  let retryWindowStartedAt: number | null = null
  let reconnectAttempt = 0
  let reconnectTimer: number | null = null
  let connectInFlight = false
  /** Oxirgi urinish nima uchun yiqilgani — 30 soniyadan keyin ko'rsatiladi. */
  let lastFailureText: string | null = null
  /** Urinish osilib qolsa ham 30 soniyada qizil xabar va tugma chiqsin. */
  let statusTimer: number | null = null
  /**
   * "Chiqish" yoki sahifa yopilishi har oshirganda ESKI urinish natijasi
   * e'tiborsiz qoladi — aks holda chiqib ketgan foydalanuvchi (yoki dars
   * tugagach ustoz) jimgina xonaga qaytib kirardi.
   */
  let generation = 0

  /*
    ── FOYDALANUVCHINING NIYATI ─────────────────────────────────────────
    🔴 NIMA UCHUN: qayta kirganda mikrofon O'CHIQ kelardi. O'lchov: uzilishdan
       oldin mikrofoni bor 460 holatdan 128 tasida o'quvchi 10 daqiqa o'tib
       ham uni yoqmagan — ustoz "gapir" deydi, o'quvchi esa jim.
    Niyatni FAQAT foydalanuvchining o'z tugmasi o'zgartiradi; ustoz o'chirsa
    (moderatsiya) niyat ham o'chadi — qayta ulanish uni qaytarib yoqmaydi.
  */
  let wantMic = false
  let wantCamera = false
  /*
    ── NIYAT SAHIFA QAYTA QURILISHIDAN OMON QOLADI (2026-09-16) ──────────
    🔴 Yuqoridagi izoh faqat UZILISH haqida edi. O'lchov esa ko'rsatdiki,
       asosiy yo'qotish uzilishda emas: `LiveRoomPage` dars davomida
       o'rtacha 3.2 marta qayta quriladi va o'shanda bu ikki o'zgaruvchi
       kompozabl bilan birga o'ladi. Sabab va raqamlar — `liveMediaIntent.ts`.
    ★ `intentStorageEnabled` — SSG/prerender paytida `window` yo'q, va
      `sessionId` haqiqiy dars bo'lmasa saqlashning ma'nosi yo'q.
  */
  const intentStorageEnabled =
    typeof window !== 'undefined' && Number.isInteger(sessionId) && sessionId > 0

  /** Saqlangan niyatni O'ZGARUVCHILARGA joylaydi (`connect()` uni tiklaydi). */
  function loadIntent(): void {
    if (!intentStorageEnabled) return
    const stored = readMediaIntent(sessionId)
    if (stored === null) return
    wantMic = stored.mic
    wantCamera = stored.camera
  }

  /**
   * Niyat HAR o'zgarganda chaqiriladi.
   *
   * ⚠️ `wantMic` / `wantCamera` ga yozadigan HAR bir joy buni chaqirishi
   *    shart — aks holda saqlangan qiymat haqiqatdan ajralib qoladi va
   *    ustoz o'chirgan mikrofon qayta qurilishdan keyin o'zi yonardi.
   *    Joylar: tugmalar (`toggleMic` / `toggleCamera`), moderatsiya
   *    (`handleRemoteModeration`), `restoreMedia` va `leave`.
   */
  function rememberIntent(): void {
    if (!intentStorageEnabled) return
    writeMediaIntent(sessionId, { mic: wantMic, camera: wantCamera })
  }

  loadIntent()

  /**
   * Har mute hodisasida oshadi. Tugma bosilgan paytda ustoz o'chirsa, tugma
   * natijasi (optimistik `true`) ustozning qarorini ustidan yozmasin.
   */
  let micModerationSeq = 0
  let cameraModerationSeq = 0

  /** Ekran ulashish uzilishda to'xtadi — qayta ulangach ustozga aytiladi. */
  let screenShareLost = false

  /*
    ── SAHIFA NEGA YOPILDI (2026-09-16) ─────────────────────────────────
    🔴 `page-closed` hodisasi 2026-09-15 kechasi 305 marta yozilgan, lekin
       SABABSIZ — va aynan sabab kerak edi: ulardan faqat 79 tasi haqiqiy
       "Chiqish" ekanligi boshqa hodisalardan TAXMIN qilindi. Qolgan ~226
       tasining sababi noma'lum qoldi, ya'ni tuzatish o'rniga yana
       taxmin qilishga to'g'ri kelardi.
    Endi chiqishni BOSHLAGAN joy o'zini nomlaydi: "Chiqish" tugmasi —
    `left`, marshrutdan chiqish — `navigate:<sahifa>`. Hech kim aytmasa
    `unknown` qoladi va bu ham ma'lumot: demak komponentni Vue'ning o'zi
    (yoki tashqi kod) yo'q qilgan.
  */
  let exitReason: string | null = null

  /**
   * Chiqish sababini belgilaydi — `page-closed` hodisasiga tushadi.
   *
   * ★ BIRINCHI AYTGAN YUTADI. "Chiqish" tugmasi bosilganda ketma-ket
   *   ikkita signal keladi: `leave()` -> `left`, keyin marshrut
   *   o'zgarishi -> `navigate:student-home`. Keyingisi ustidan yozsa,
   *   jurnalda hamma chiqish `navigate:` bo'lib ko'rinardi va tugma
   *   orqali chiqish bilan tasodifiy chiqishni ajratib bo'lmasdi.
   */
  function noteExit(reason: string): void {
    if (exitReason === null) exitReason = reason
  }

  const reporter =
    typeof window !== 'undefined' && Number.isInteger(sessionId) && sessionId > 0
      ? createLiveEventReporter(sessionId, () => ({ micOn: isMicOn.value, cameraOn: isCameraOn.value }))
      : null

  function report(type: string, extra: Parameters<NonNullable<typeof reporter>['report']>[1] = {}): void {
    reporter?.report(type, extra)
  }

  const wakeLock = typeof window !== 'undefined'
    ? createScreenWakeLock((detail) => report('wake-lock', { detail }))
    : null

  /**
   * Tugma "kutish" (spinner) holatining ENG UZOQ muddati.
   *
   * NIMA UCHUN KERAK — 2026-07-31 dagi o'lchov: LiveKit `publishTrack()` va
   * `setMicrophoneEnabled()` server TASDIG'INI kutadi. Server tasdiqni umuman
   * qaytarmasa, promise na `resolve`, na `reject` bo'ladi — ya'ni `finally`
   * bloki ham HECH QACHON ishga tushmaydi va tugma spinner ostida MANGU
   * qotib qoladi. Foydalanuvchi shikoyati aynan shu edi: "video oqib turibdi,
   * lekin kamera tugmasi aylanaveradi".
   *
   * Endi kutish chegaralangan: amal fonda davom etaveradi, tugma esa blokdan
   * chiqadi va foydalanuvchi nima bo'layotganini KO'RADI.
   */
  const TOGGLE_DEADLINE_MS = 6_000

  /** Ochiq chegara taymerlari — komponent yopilganda tozalanishi SHART. */
  const deadlineTimers = new Set<number>()

  /** Ovoz elementlari uchun ko'rinmas idish — DOM'da bitta joyda turadi. */
  let audioHost: HTMLDivElement | null = null
  const attachedAudioTracks = new Set<RemoteTrack>()

  function ensureAudioHost(): HTMLDivElement {
    if (audioHost === null) {
      audioHost = document.createElement('div')
      audioHost.style.display = 'none'
      audioHost.setAttribute('aria-hidden', 'true')
      document.body.appendChild(audioHost)
    }
    return audioHost
  }

  function attachAudio(track: RemoteTrack): void {
    if (track.kind !== Track.Kind.Audio || attachedAudioTracks.has(track)) return
    const element = track.attach()
    element.autoplay = true
    // `<video>` elementlari HAR DOIM muted; ovoz faqat shu `<audio>` lardan chiqadi.
    ensureAudioHost().appendChild(element)
    attachedAudioTracks.add(track)
  }

  function detachAudio(track: RemoteTrack): void {
    if (!attachedAudioTracks.has(track)) return
    for (const element of track.detach()) element.remove()
    attachedAudioTracks.delete(track)
  }

  function detachAllAudio(): void {
    for (const track of attachedAudioTracks) {
      for (const element of track.detach()) element.remove()
    }
    attachedAudioTracks.clear()
    audioHost?.remove()
    audioHost = null
  }

  /**
   * ★★ KAMERA HOLATINING YAGONA HAQIQAT MANBAI.
   *
   * MUAMMO (foydalanuvchi shikoyati, ekran surati bilan): sahnada o'z videosi
   * JONLI oqib turibdi, lekin boshqaruv panelidagi kamera tugmasi QIZIL
   * (o'chirilgan) ko'rinadi — holat ekrandagi haqiqat bilan mos emas.
   *
   * SABABI — holat ikki xil manbadan o'qilardi:
   *   • sahnadagi video `localCameraTrack` dan chiziladi (serverdan MUSTAQIL);
   *   • tugma holati esa `localParticipant.isCameraEnabled` dan olinardi, bu
   *     esa trek serverda E'LON qilinganidan keyin va faqat ulanish TIRIK
   *     bo'lgandagina `true` bo'ladi.
   * Xona qayta ulanganda LiveKit qiymati nolga tushadi, mahalliy trek esa
   * tirik qoladi — ikki manba ajralib ketadi va tugma yolg'on ko'rsatadi.
   *
   * YECHIM: bitta funksiya IKKALASINI ham hisobga oladi. Tugma foydalanuvchi
   * KO'RAYOTGAN narsani aks ettiradi: kamera treki bor ekan — kamera YONIQ.
   */
  function readCameraOn(participant: Room['localParticipant']): boolean {
    // ★ `isMuted` HAM tekshiriladi: ustoz server orqali kamerani o'chirsa
    //   LiveKit mahalliy trekni MUTE qiladi (to'xtatmaydi) — trek obyekti
    //   tirik, lekin kadr qora. Bu holda tugma "yoniq" deb yolg'on ko'rsatmasin.
    const local = localCameraTrack.value
    return (local !== null && !local.isMuted) || participant.isCameraEnabled
  }

  /**
   * ★★ MIKROFON HOLATINING YAGONA HAQIQAT MANBAI.
   *
   * MUAMMO (o'lchov, 2026-07-31): mikrofon yoqilgandan keyin ovoz HAQIQATAN
   * yuborilardi (`outbound-rtp kind=audio`, `bytesSent` o'sib borardi), lekin
   * xona qayta ulangan zahoti tugma QIZIL (o'chiq) holatga tushib, shu holda
   * QOLIB KETARDI. Foydalanuvchi buni "mikrofon umuman ishlamayapti" deb
   * tushunadi — shikoyat aynan shu edi.
   *
   * SABABI: `isMicrophoneEnabled` faqat trek `Source.Microphone` sifatida
   * ro'yxatdan o'tgan bo'lsa `true` qaytaradi. Qayta ulanishdan keyin LiveKit
   * trekni QAYTA e'lon qiladi, lekin manba (source) bog'lanishi tiklanmasligi
   * mumkin — natijada ovoz KETAYOTGAN bo'lsa ham bayroq `false` bo'lib qoladi.
   *
   * YECHIM: e'lon qilingan ovoz treklariga ham qaraymiz. Ekran ulashish ovozi
   * hisobga OLINMAYDI — u mikrofon emas.
   */
  function readMicOn(participant: Room['localParticipant']): boolean {
    if (participant.isMicrophoneEnabled) return true
    for (const publication of participant.trackPublications.values()) {
      if (publication.kind !== Track.Kind.Audio) continue
      if (publication.source === Track.Source.ScreenShareAudio) continue
      if (!publication.isMuted) return true
    }
    return false
  }

  /* ------------------------------ katakchalar ------------------------------ */

  function videoTrackOf(publication: TrackPublication | undefined): Track | null {
    if (publication === undefined) return null
    if (publication.kind !== Track.Kind.Video) return null
    if (publication.isMuted) return null
    return publication.track ?? null
  }

  function appendTiles(participant: Participant, isLocal: boolean, out: ParticipantTile[]): void {
    const identity = participant.identity
    const name = participant.name !== undefined && participant.name.length > 0 ? participant.name : identity
    const userId = parseUserId(identity)
    const micEnabled = participant.isMicrophoneEnabled
    const isSpeaking = participant.isSpeaking

    const quality = qualityByIdentity.get(identity) ?? 'unknown'

    const screenTrack = videoTrackOf(participant.getTrackPublication(Track.Source.ScreenShare))
    if (screenTrack !== null) {
      out.push({
        key: `${identity}:screen`,
        identity,
        userId,
        name,
        isLocal,
        isScreenShare: true,
        micEnabled,
        cameraEnabled: true,
        isSpeaking: false,
        videoTrack: screenTrack,
        quality,
      })
    }

    // MAHALLIY ishtirokchi uchun: e'lon qilingan trek hali yo'q bo'lsa,
    // to'g'ridan-to'g'ri mahalliy trekdan chizamiz (izoh `localCameraTrack` da).
    const publishedCamera = videoTrackOf(participant.getTrackPublication(Track.Source.Camera))
    const localCamera = localCameraTrack.value
    const cameraTrack =
      publishedCamera ?? (isLocal && localCamera !== null && !localCamera.isMuted ? localCamera : null)
    out.push({
      key: `${identity}:cam`,
      identity,
      userId,
      name,
      isLocal,
      isScreenShare: false,
      micEnabled,
      cameraEnabled: cameraTrack !== null,
      isSpeaking,
      videoTrack: cameraTrack,
      quality,
    })
  }

  function rebuildTiles(): void {
    if (rebuildFrame !== null) {
      cancelAnimationFrame(rebuildFrame)
      rebuildFrame = null
    }
    if (rebuildTimer !== null) {
      window.clearTimeout(rebuildTimer)
      rebuildTimer = null
    }

    const current = room
    if (current === null) return

    // Taxta treki TASHQARIDAN olib tashlangan bo'lsa (ustoz oddiy ekran
    // ulashuvi tugmasini bosib o'chirdi, server uzdi) — holatni tozalaymiz,
    // aks holda «Kitob» tugmasi «efirda» deb yolg'on ko'rsatardi.
    if (canvasTrack !== null && !canvasPublishing) {
      const published = current.localParticipant.getTrackPublication(Track.Source.ScreenShare)
      if (published?.track !== canvasTrack) {
        canvasTrack.stop()
        canvasTrack = null
        isCanvasSharing.value = false
      }
    }

    const next: ParticipantTile[] = []
    appendTiles(current.localParticipant, true, next)
    for (const participant of current.remoteParticipants.values()) {
      appendTiles(participant, false, next)
    }
    tiles.value = next

    /*
      OPTIMISTIK HOLATNI YO'Q QILMASLIK.
      Tugma bosilganda holat DARHOL o'zgaradi (`toggleMic` va boshqalar), lekin
      LiveKit javobi bir necha yuz millisekunddan keyin keladi. Agar shu oraliqda
      hodisa kelib `rebuildTiles()` ishga tushsa, u LiveKit'ning HALI ESKI
      qiymatini qaytarib yozar va tugma "o'zi orqaga sakragan" bo'lib ko'rinardi.
      Shuning uchun kutilayotgan (pending) tugmalarga TEGMAYMIZ.
    */
    if (!micPending.value) isMicOn.value = readMicOn(current.localParticipant)
    // Kamera uchun ATAYLAB `readCameraOn` — izohi funksiyaning o'zida.
    if (!cameraPending.value) isCameraOn.value = readCameraOn(current.localParticipant)
    if (!screenPending.value) isScreenSharing.value = current.localParticipant.isScreenShareEnabled
  }

  /**
   * 200 kishilik xonada ulanish paytida o'nlab hodisa ketma-ket keladi
   * (`ParticipantConnected` × 200, `TrackSubscribed` × N...). Har biriga alohida
   * qayta render qilish o'rniga kadrga BITTA marta qayta quramiz.
   *
   * ⚠️ ZAXIRA TAYMER SHART: brauzer FONDAGI tabda `requestAnimationFrame` ni
   * UMUMAN chaqirmaydi. Ilgari faqat rAF ishlatilardi va foydalanuvchi boshqa
   * tabda turganda yangi ishtirokchi ham, yangi video trek ham sahnaga
   * QO'SHILMASDI — u tabga qaytgach ham hodisa allaqachon o'tib ketgani uchun
   * ekran bo'sh qolardi. Taymer shu "jimgina ishlamaslik"ni yopadi.
   */
  function scheduleRebuild(): void {
    if (disposed) return
    if (rebuildFrame === null) rebuildFrame = requestAnimationFrame(rebuildTiles)
    if (rebuildTimer === null) rebuildTimer = window.setTimeout(rebuildTiles, 250)
  }

  /* ---------------------------- hodisa ishlovchilari ------------------------ */

  function onTrackSubscribed(
    track: RemoteTrack,
    _publication: RemoteTrackPublication,
    _participant: RemoteParticipant,
  ): void {
    attachAudio(track)
    scheduleRebuild()
  }

  function onTrackUnsubscribed(
    track: RemoteTrack,
    _publication: RemoteTrackPublication,
    _participant: RemoteParticipant,
  ): void {
    detachAudio(track)
    scheduleRebuild()
  }

  function onParticipantConnected(_participant: RemoteParticipant): void {
    scheduleRebuild()
  }

  function onParticipantDisconnected(_participant: RemoteParticipant): void {
    scheduleRebuild()
  }

  function onTrackMuteChanged(publication: TrackPublication, participant: Participant): void {
    handleRemoteModeration(publication, participant)
    scheduleRebuild()
  }

  /**
   * USTOZ SERVER ORQALI O'CHIRDI (2026-09-09) — o'quvchi tomonidagi javob.
   *
   * LiveKit `MutePublishedTrack` ni olganda mahalliy trekni MUTE qiladi va
   * `TrackMuted` hodisasini beradi. Bu hodisa o'quvchi O'ZI tugmani
   * bosganda ham keladi — farqi: o'z bosishida tegishli `pending` bayrog'i
   * `true` (amal hali kutilmoqda), ustoz o'chirganda esa hech qanday amal
   * kutilmayapti.
   *
   * ★ KAMERA UCHUN TREK TO'XTATILADI (faqat mute emas): mute holatida
   *   kameraning chirog'i yonib turadi va o'quvchi "hali ko'rishyapti" deb
   *   xavotirlanadi. `toggleCamera(false)` bilan AYNI yo'l — unpublish +
   *   stop. Qayta yoqish o'quvchining o'z qo'lida (pastki panel).
   * ★ Mikrofon uchun `setMicrophoneEnabled(false)` — SDK holatini bizning
   *   tugma bilan bir xil qiladi; trek allaqachon jim, bu chaqiruv arzon.
   */
  function handleRemoteModeration(publication: TrackPublication, participant: Participant): void {
    const current = room
    if (current === null || participant !== current.localParticipant) return
    if (!publication.isMuted) return

    // Niyat `pending` dan QAT'I NAZAR o'chadi: o'z o'chirishida ham to'g'ri,
    // yoqish kutilayotganda kelgan mute esa faqat ustozniki bo'lishi mumkin.
    if (publication.source === Track.Source.Camera) {
      wantCamera = false
      cameraModerationSeq += 1
      rememberIntent()
    }
    if (publication.source === Track.Source.Microphone) {
      wantMic = false
      micModerationSeq += 1
      rememberIntent()
    }

    if (publication.source === Track.Source.Camera && !cameraPending.value) {
      const track = localCameraTrack.value
      localCameraTrack.value = null
      if (track !== null) {
        void current.localParticipant.unpublishTrack(track, true).catch(() => undefined)
      }
      isCameraOn.value = false
      wantCamera = false
      rememberIntent()
      moderationNotice.value = 'Ustoz kamerangizni o‘chirdi. Kerak bo‘lsa pastki paneldan qayta yoqing.'
      return
    }

    if (publication.source === Track.Source.Microphone && !micPending.value) {
      isMicOn.value = false
      wantMic = false
      rememberIntent()
      void current.localParticipant.setMicrophoneEnabled(false).catch(() => undefined)
      moderationNotice.value = 'Ustoz mikrofoningizni o‘chirdi. Gapirish uchun pastki paneldan qayta yoqing.'
    }
  }

  function dismissModerationNotice(): void {
    moderationNotice.value = null
  }

  function onLocalTrackChanged(
    _publication: LocalTrackPublication,
    _participant: Participant,
  ): void {
    scheduleRebuild()
  }

  function onActiveSpeakersChanged(_speakers: Participant[]): void {
    scheduleRebuild()
  }

  function onConnectionStateChanged(state: ConnectionState): void {
    if (disposed) return
    // Qayta ulanish siklida holatni SIKL boshqaradi — yangi `Room` ning
    // "Connecting" hodisasi qizil tugmani har urinishda yashirmasin.
    if (disconnectedAt !== null && (state === ConnectionState.Connecting || state === ConnectionState.Disconnected)) {
      scheduleRebuild()
      return
    }
    switch (state) {
      case ConnectionState.Connected:
        status.value = 'connected'
        connectionError.value = null
        break
      case ConnectionState.Connecting:
        status.value = 'connecting'
        break
      case ConnectionState.Reconnecting:
      case ConnectionState.SignalReconnecting:
        status.value = 'reconnecting'
        break
      case ConnectionState.Disconnected:
        status.value = 'disconnected'
        break
      default:
        break
    }
    scheduleRebuild()
  }

  /**
   * ★★ ENG MUHIM TUZATISH — "Qayta urinish" JIMGINA ISHLAMASDI.
   *
   * Ilgari bu ishlovchi faqat `status` va `tiles` ni tozalardi, lekin `room`
   * o'zgaruvchisi ESKI, o'lik `Room` obyektiga ishora qilib qolardi.
   * `connect()` esa birinchi qatorida `if (room !== null) return` qiladi —
   * ya'ni foydalanuvchi "Qayta urinish" ni bosganda HECH NIMA BO'LMASDI:
   * na yangi ulanish, na xato xabari. Video butunlay o'lik qolardi.
   *
   * Endi `room` bo'shatiladi, tinglovchilar olib tashlanadi va foydalanuvchi
   * uzilish sababini O'ZBEKCHA ko'radi.
   */
  function onDisconnected(reason?: DisconnectReason): void {
    if (disposed) return

    if (isScreenSharing.value || isCanvasSharing.value) screenShareLost = true

    const target = room
    room = null
    if (target !== null) {
      unbindEvents(target)
      // Kamera/mikrofon indikatori o'chsin va MediaStream oqmasin.
      void target.disconnect(true).catch(() => undefined)
    }

    dropLocalCamera()
    dropCanvasTrack()
    detachAllAudio()

    // Sifat holati UZILISHDA tozalanadi: qayta ulanganda eski "poor"
    // qiymati qolib, kamera sababsiz o'chirilib ketardi.
    qualityByIdentity.clear()
    localQuality.value = 'unknown'
    linkWarning.value = null
    cameraDroppedForWeakLink = false
    linkWarningDismissed = false

    tiles.value = []
    isMicOn.value = false
    isCameraOn.value = false
    isScreenSharing.value = false
    micPending.value = false
    cameraPending.value = false
    screenPending.value = false
    audioBlocked.value = false
    moderationNotice.value = null

    const reasonName =
      reason === undefined ? 'UNKNOWN' : ((DisconnectReason[reason] as string | undefined) ?? String(reason))

    if (reconnectEnabled && shouldReconnect(reason)) {
      report('disconnected', { reason: reasonName, detail: 'reconnecting' })
      beginReconnect()
      return
    }

    report('disconnected', { reason: reasonName, detail: 'final' })
    reconnectEnabled = false
    wakeLock?.release()
    status.value = 'disconnected'
    connectionError.value = describeDisconnect(reason)
  }

  /* ------------------------------ qayta ulanish ----------------------------- */

  function clearReconnectTimer(): void {
    if (reconnectTimer !== null) {
      window.clearTimeout(reconnectTimer)
      reconnectTimer = null
    }
  }

  /** Siklni boshlaydi (allaqachon ketayotgan bo'lsa — hech narsa qilmaydi). */
  function beginReconnect(): void {
    if (disconnectedAt === null) {
      disconnectedAt = Date.now()
      reconnectAttempt = 0
      armRetryWindow()
    }
    updateReconnectStatus()
    scheduleReconnect()
  }

  /**
   * "Qizil xato + Qayta urinish tugmasi" hisobini SHU LAHZADAN boshlaydi.
   *
   * ★ NEGA `disconnectedAt` DAN AJRATILDI (2026-09-16). `disconnectedAt` —
   *   uzilishning HAQIQIY boshlanishi va u `reconnected` hodisasidagi
   *   "necha millisekund yo'qoldi" o'lchoviga kiradi; uni surib bo'lmaydi,
   *   aks holda metrika yolg'on bo'lardi. Ammo sahifa FONDA turgan vaqt
   *   "muvaffaqiyatsiz urinish" emas — telefon sahifani to'xtatib qo'ygan.
   *   Shuning uchun UI taymeri alohida hisoblanadi va sahifa qaytganda
   *   qaytadan boshlanadi: o'quvchi qaytishi bilanoq qizil xatoni emas,
   *   "Qayta ulanmoqda…" ni ko'radi.
   */
  function armRetryWindow(): void {
    retryWindowStartedAt = Date.now()
    if (statusTimer !== null) window.clearTimeout(statusTimer)
    statusTimer = window.setTimeout(() => {
      statusTimer = null
      updateReconnectStatus()
    }, RECONNECT_BUTTON_AFTER_MS)
  }

  function stopReconnect(): void {
    clearReconnectTimer()
    if (statusTimer !== null) {
      window.clearTimeout(statusTimer)
      statusTimer = null
    }
    disconnectedAt = null
    retryWindowStartedAt = null
    reconnectAttempt = 0
    lastFailureText = null
  }

  function scheduleReconnect(delayMs: number = reconnectDelay(reconnectAttempt)): void {
    if (disposed || disconnectedAt === null) return
    clearReconnectTimer()
    reconnectTimer = window.setTimeout(() => {
      reconnectTimer = null
      void attemptReconnect()
    }, delayMs)
  }

  async function attemptReconnect(): Promise<void> {
    if (disposed || disconnectedAt === null || room !== null || connectInFlight) return

    /*
      ════════════════════════════════════════════════════════════════════
      FONDAGI SAHIFADA URINMAYMIZ (2026-09-16)
      ════════════════════════════════════════════════════════════════════

      🔴 O'LCHOV (2026-09-15 kechasi, 7 dars): 53 ta qayta ulanish
         urinishidan 44 tasi sahifa FONDA turganida qilingan va ulardan
         43 tasi yiqilgan. Ekran ko'rinib turgan 9 tasidan 7 tasi
         muvaffaqiyatli bo'lgan.

         Sabab: Android va Telegram WebView fondagi sahifaning tarmog'ini
         va taymerlarini to'xtatadi — `getUserMedia` ham, WebSocket ham
         ochilmaydi.

      ZARARI shunchaki behuda urinish emas: har yiqilish `reconnectAttempt`
      ni oshirardi, ya'ni o'quvchi telefoniga qaytganda backoff allaqachon
      eng katta (10 s) qiymatda turardi va 30 soniyalik taymer ham
      allaqachon ishlab, QIZIL xato ko'rsatilardi — vaholanki hali birorta
      HAQIQIY urinish bo'lmagan.

      ★ Endi fonda urinish o'tkazib yuboriladi va NAVBATDAGI tekshiruv
        sekin oraliqda rejalashtiriladi (butunlay to'xtatib qo'ymaymiz:
        `visibilitychange` ba'zi WebView'larda umuman kelmaydi, u holda
        bu zaxira yo'l ishlaydi). Urinish soni OSHMAYDI.
    */
    if (typeof document !== 'undefined' && document.hidden) {
      scheduleReconnect(HIDDEN_RECONNECT_DELAY_MS)
      return
    }

    reconnectAttempt += 1
    report('reconnect-attempt', { attempt: reconnectAttempt })
    updateReconnectStatus()
    await openRoom()
  }

  /**
   * Sariq "qayta ulanmoqda" — birinchi 30 soniya. Undan keyin qizil xabar va
   * "Qayta urinish" tugmasi (sahifa `disconnected` holatida uni chizadi),
   * urinishlar esa fonda davom etadi.
   */
  function updateReconnectStatus(): void {
    if (disconnectedAt === null || retryWindowStartedAt === null) return
    if (Date.now() - retryWindowStartedAt >= RECONNECT_BUTTON_AFTER_MS) {
      status.value = 'disconnected'
      // Aniq sabab (masalan UDP to'silgan) bo'lsa — o'shani aytamiz.
      connectionError.value = lastFailureText !== null
        ? `${lastFailureText} Qayta ulanishga urinib turibmiz.`
        : RECONNECT_STILL_TRYING_TEXT
    } else {
      status.value = 'reconnecting'
      connectionError.value = null
    }
  }

  /** Internet qaytdi yoki sahifa yana ko'rindi — kutmasdan urinamiz. */
  function onNetworkMayBeBack(): void {
    // 0–1 s tasodifiy kutish — izoh `reconnectDelay` da.
    if (disconnectedAt !== null && room === null && !connectInFlight) {
      /*
        BACKOFF NOLGA QAYTADI. Sahifa fonda turganda (yoki internet
        yo'qligida) o'tgan vaqt urinish emas edi — shuning uchun o'quvchi
        qaytganda 10 soniyalik kutish ham, qizil xato ham ko'rsatilmaydi:
        hisob shu lahzadan qaytadan boshlanadi.
      */
      reconnectAttempt = 0
      armRetryWindow()
      updateReconnectStatus()
      scheduleReconnect(Math.round(Math.random() * 1_000))
    }
  }

  function onPageVisibility(): void {
    if (document.visibilityState === 'visible') onNetworkMayBeBack()
  }

  /** Sahifa brauzer keshidan (bfcache) qaytdi — ulanish allaqachon o'lgan. */
  function onPageShow(event: PageTransitionEvent): void {
    if (event.persisted) onNetworkMayBeBack()
  }

  if (typeof window !== 'undefined') {
    window.addEventListener('online', onNetworkMayBeBack)
    window.addEventListener('pageshow', onPageShow)
    document.addEventListener('visibilitychange', onPageVisibility)
  }

  /**
   * Qayta ulangach foydalanuvchining NIYATINI tiklaydi: mikrofon va kamera.
   *
   * ⚠️ EKRAN ULASHISH VA KITOB TAXTASI TIKLANMAYDI: ekran tanlash oynasi faqat
   *    foydalanuvchi bosganda ochiladi, canvas esa sahifa komponentiga
   *    tegishli. Ustozga buni aytamiz — jimgina yo'qolmasin.
   */
  async function restoreMedia(): Promise<void> {
    const wantedMic = wantMic
    const wantedCamera = wantCamera
    const micSeq = micModerationSeq
    const cameraSeq = cameraModerationSeq
    const tasks: Promise<void>[] = []
    if (wantedMic && !isMicOn.value) tasks.push(toggleMic())
    if (wantedCamera && !isCameraOn.value) tasks.push(toggleCamera())
    await Promise.allSettled(tasks)

    // Tiklash paytida aloqa yana uzildi — niyat keyingi ulanishga saqlanadi.
    if (room === null) {
      // Shu orada ustoz o'chirgan bo'lsa — uning qarori saqlanadi.
      if (micModerationSeq === micSeq) wantMic = wantedMic
      if (cameraModerationSeq === cameraSeq) wantCamera = wantedCamera
      rememberIntent()
      return
    }

    if (screenShareLost) {
      screenShareLost = false
      mediaError.value = 'Aloqa uzilgani uchun ekran ulashish to‘xtadi — kerak bo‘lsa qayta yoqing.'
    }

    report('media-restored', { detail: `mic=${String(wantMic)} camera=${String(wantCamera)}` })
  }

  /**
   * ════════════════════════════════════════════════════════════════════
   * ALOQA SIFATI — ZAIF KANALDA OVOZNI QUTQARISH
   * ════════════════════════════════════════════════════════════════════
   *
   * 🔴 NIMA UCHUN BU UMUMAN KERAK (2026-09-12 da o'lchandi). LiveKit
   *    jurnalida 48 soatda 7171 ta "channel congestion" hodisasi bor va
   *    ularning 217 dan 1 qismigina tinglovchi tomonida — qolgani
   *    YUBORUVCHI tomonida. Kanal bahosi 100 Mbit/s dan 38 kbit/s ga
   *    tushgan holatlar bor, ya'ni bitta Opus oqimi ham sig'maydi va
   *    o'quvchining ovozi uzilib qoladi.
   *
   *    Bunda ilova HECH NARSA demasdi: `ConnectionQuality` hodisasi
   *    umuman o'qilmasdi. O'quvchi sababni bilmasdi, ustoz esa
   *    "platforma buzuq" deb xabar qilardi.
   *
   * ★ IKKI ISH QILINADI:
   *     1) sifat sahnaga chiqariladi (har katakchada va o'zingiz uchun);
   *     2) o'z kanalingiz `poor` bo'lsa KAMERA o'chiriladi — video
   *        kanalning katta qismini yeydi, ovoz esa darsning O'ZI.
   *
   * ⚠️ USTOZGA TEGILMAYDI: uning videosi darsning mazmuni (doska,
   *    ko'rsatma), va o'lchov bo'yicha ustozlarda bu muammo umuman
   *    uchramagan — siqilgan 25 ishtirokchining hammasi o'quvchi edi.
   */
  function onConnectionQualityChanged(
    quality: ConnectionQuality,
    participant: Participant,
  ): void {
    const mapped = toLinkQuality(quality)
    qualityByIdentity.set(participant.identity, mapped)

    if (room !== null && participant.identity === room.localParticipant.identity) {
      const previous = localQuality.value
      localQuality.value = mapped
      // Faqat YOMONLASHISH va TIKLANISH lahzasi — har hodisa emas.
      if (mapped !== previous && (mapped === 'poor' || mapped === 'lost' || previous === 'poor' || previous === 'lost')) {
        report('quality', { detail: mapped })
      }
      applyWeakLinkPolicy(mapped)
    }

    scheduleRebuild()
  }

  /** Zaif kanalda kamerani o'chiradi, tiklanganda bayroqni bo'shatadi. */
  function applyWeakLinkPolicy(quality: LinkQuality): void {
    if (quality === 'excellent' || quality === 'good') {
      cameraDroppedForWeakLink = false
      linkWarningDismissed = false
      linkWarning.value = null
      return
    }

    if (quality !== 'poor') return

    /** Yopilgan ogohlantirish qayta chiqmaydi (aloqa tiklanmaguncha). */
    function warn(text: string): void {
      if (!linkWarningDismissed) linkWarning.value = text
    }

    if (isHost.value) {
      // Ustozning kamerasiga TEGILMAYDI — u darsning mazmuni (doska,
      // ko'rsatma). Unga faqat sabab aytiladi, chorani o'zi tanlaydi.
      warn(WEAK_LINK_HOST_TEXT)
      return
    }

    if (cameraDroppedForWeakLink) return

    cameraDroppedForWeakLink = true

    if (!isCameraOn.value) {
      warn(WEAK_LINK_AUDIO_ONLY_TEXT)
      return
    }

    warn(WEAK_LINK_CAMERA_OFF_TEXT)
    void toggleCamera()
  }

  function dismissLinkWarning(): void {
    linkWarningDismissed = true
    linkWarning.value = null
  }

  function onMediaDevicesError(error: Error): void {
    mediaError.value = describeMediaError(error)
  }

  /**
   * Brauzer siyosati bo'yicha ovoz foydalanuvchi ishtirokisiz chalinmasligi
   * mumkin. Ilgari bu holat hech qayerda ko'rsatilmasdi — ustoz gapirardi,
   * o'quvchi esa "ovoz yo'q" deb o'ylardi. Endi banner chiqadi.
   */
  function onAudioPlaybackChanged(): void {
    audioBlocked.value = room !== null && !room.canPlaybackAudio
  }

  function bindEvents(target: Room): void {
    target
      .on(RoomEvent.TrackSubscribed, onTrackSubscribed)
      .on(RoomEvent.TrackUnsubscribed, onTrackUnsubscribed)
      .on(RoomEvent.ParticipantConnected, onParticipantConnected)
      .on(RoomEvent.ParticipantDisconnected, onParticipantDisconnected)
      .on(RoomEvent.TrackMuted, onTrackMuteChanged)
      .on(RoomEvent.TrackUnmuted, onTrackMuteChanged)
      .on(RoomEvent.LocalTrackPublished, onLocalTrackChanged)
      .on(RoomEvent.LocalTrackUnpublished, onLocalTrackChanged)
      .on(RoomEvent.ActiveSpeakersChanged, onActiveSpeakersChanged)
      .on(RoomEvent.ConnectionStateChanged, onConnectionStateChanged)
      .on(RoomEvent.ConnectionQualityChanged, onConnectionQualityChanged)
      .on(RoomEvent.Disconnected, onDisconnected)
      .on(RoomEvent.MediaDevicesError, onMediaDevicesError)
      .on(RoomEvent.AudioPlaybackStatusChanged, onAudioPlaybackChanged)
  }

  function unbindEvents(target: Room): void {
    target
      .off(RoomEvent.TrackSubscribed, onTrackSubscribed)
      .off(RoomEvent.TrackUnsubscribed, onTrackUnsubscribed)
      .off(RoomEvent.ParticipantConnected, onParticipantConnected)
      .off(RoomEvent.ParticipantDisconnected, onParticipantDisconnected)
      .off(RoomEvent.TrackMuted, onTrackMuteChanged)
      .off(RoomEvent.TrackUnmuted, onTrackMuteChanged)
      .off(RoomEvent.LocalTrackPublished, onLocalTrackChanged)
      .off(RoomEvent.LocalTrackUnpublished, onLocalTrackChanged)
      .off(RoomEvent.ActiveSpeakersChanged, onActiveSpeakersChanged)
      .off(RoomEvent.ConnectionStateChanged, onConnectionStateChanged)
      .off(RoomEvent.ConnectionQualityChanged, onConnectionQualityChanged)
      .off(RoomEvent.Disconnected, onDisconnected)
      .off(RoomEvent.MediaDevicesError, onMediaDevicesError)
      .off(RoomEvent.AudioPlaybackStatusChanged, onAudioPlaybackChanged)
    // Qolgan har qanday tinglovchi ham qolib ketmasin.
    target.removeAllListeners()
  }

  /* -------------------------------- ulanish -------------------------------- */

  async function connect(): Promise<void> {
    // "Qayta urinish" qayta ulanish SIKLI ichida bosildi — navbatdagi
    // urinishni kutmasdan hozir qilamiz (ikkinchi sikl ochilmaydi).
    if (disconnectedAt !== null) {
      scheduleReconnect(0)
      return
    }
    await openRoom()
  }

  /**
   * Bitta ulanish urinishi — birinchi kirish ham, qayta ulanish ham shu yerdan.
   *
   * ★ SIKL ICHIDA HOLAT `loading`/`connecting` GA O'TMAYDI: aks holda har
   *   urinishda banner "Darsga ulanmoqda…" va "qayta ulanmoqda…" orasida
   *   miltillab turardi.
   */
  async function openRoom(): Promise<void> {
    // Ikki marta bosilgan "Qayta urinish" ikkita `Room` yaratmasligi uchun.
    if (disposed || room !== null || connectInFlight) return

    const inCycle = disconnectedAt !== null
    const gen = generation
    connectInFlight = true
    reconnectEnabled = true

    if (!inCycle) {
      status.value = 'loading'
      connectionError.value = null
    }

    // ⚠️ `AbortSignal.timeout` EMAS: eski iOS WebView'larda u yo'q va
    //    sinxron TypeError cheksiz "vaqtinchalik xato" siklini boshlardi.
    const abort = new AbortController()
    const abortTimer = window.setTimeout(() => abort.abort(), JOIN_FETCH_TIMEOUT_MS)

    try {
      // SPEC 5: POST /api/v1/live-sessions/{id}/token -> LiveKitJoinDto
      const join = await fetchLiveKitJoin(sessionId, abort.signal)
      window.clearTimeout(abortTimer)
      if (disposed || gen !== generation) return

      isHost.value = join.isHost
      roomName.value = join.roomName
      endsAt.value = join.endsAt

      const target = new Room({
        // `adaptiveStream` — ko'rinmayotgan yoki kichkina katakchalar uchun past
        // sifatli qatlam so'raladi. 200 ta ishtirokchida bu shart.
        adaptiveStream: true,
        // `dynacast` — hech kim ko'rmayotgan qatlamlar serverda o'chiriladi.
        dynacast: true,
        videoCaptureDefaults: { resolution: captureResolution(join.isHost) },
        publishDefaults: {
          // Simulcast: bir nechta sifat qatlami yuboriladi, LiveKit har bir
          // ko'ruvchiga mos qatlamni tanlaydi.
          simulcast: true,

          /*
            ════════════════════════════════════════════════════════════
            QATLAMLAR ROLGA QARAB — O'QUVCHIDA IKKITA, USTOZDA UCHTA
            ════════════════════════════════════════════════════════════

            Simulcast HAMMA qatlamni BIR VAQTDA yuboradi. Ilgari hamma
            720p + 360p + 180p yuborardi, ya'ni ~2.4 Mbit/s yuklash —
            o'lchangan kanal bahosining medianasi esa 279 kbit/s.
            O'quvchi uchun 360p + 180p yetarli va u ovozga joy qoldiradi.
          */
          videoSimulcastLayers: join.isHost
            ? [VideoPresets.h180, VideoPresets.h360]
            : [VideoPresets.h180],

          // Ekran ulashish qatlamlari — sabab `SCREEN_SHARE_ENCODING` izohida.
          screenShareEncoding: SCREEN_SHARE_ENCODING,
          screenShareSimulcastLayers: SCREEN_SHARE_LAYERS,

          /*
            ════════════════════════════════════════════════════════════
            🔴 OVOZ PRESETI — ENG MUHIM QATOR
            ════════════════════════════════════════════════════════════

            LiveKit standarti — `music` (48 kbit/s). Dars uchun bu
            ortiqcha: o'lchov bo'yicha siqilish hodisalarining 11% ida
            ishtirokchi FAQAT ovoz yuborayotgan edi va kanal baribir
            38 kbit/s ga tushgan — ya'ni 48 sig'maydi, 24 sig'adi.
            Opus 24 kbit/s da nutqni juda yaxshi uzatadi.

            ★ USTOZDA 32 kbit/s (2026-09-14 gacha `music`, 48): darsda
              tinglash materiali qo'yilishi mumkin (til kurslari), shuning
              uchun `speech` emas. Lekin ustozning ovozi HAR o'quvchining
              yuklab olish kanalidan o'tadi — sabab `HOST_AUDIO_PRESET` da.
          */
          audioPreset: join.isHost ? HOST_AUDIO_PRESET : AudioPresets.speech,

          /*
            RED — ovoz paketlarining ORTIQCHA nusxasi. Paket yo'qolganda
            ovoz uzilmaydi. LiveKit'da standart holda yoniq, lekin bu
            yerda OSHKORA yozilgan: u aynan zaif kanal uchun eng muhim
            himoya va uni kelajakda kimdir bilmasdan o'chirmasin.
          */
          red: true,
          dtx: true,
        },
      })

      room = target
      bindEvents(target)

      if (!inCycle) status.value = 'connecting'
      await target.connect(join.serverUrl, join.token, { autoSubscribe: true })

      if (disposed || gen !== generation) {
        if (room === target) {
          room = null
          unbindEvents(target)
        }
        await target.disconnect(true)
        return
      }

      // Mavjud ishtirokchilarning ovozini ulaymiz (biz ulangunimizcha
      // obuna bo'lingan treklar uchun hodisa kelmaydi).
      for (const participant of target.remoteParticipants.values()) {
        for (const publication of participant.trackPublications.values()) {
          const track = publication.track
          if (track !== undefined && publication.kind === Track.Kind.Audio) {
            attachAudio(track)
          }
        }
      }

      /*
        Ovoz bloklanganini HODISANI KUTMASDAN tekshiramiz. Brauzer ulanishdan
        OLDIN ham "avtomatik ijro taqiqlangan" holatida bo'lishi mumkin —
        u holda `AudioPlaybackStatusChanged` umuman kelmaydi va "Ovozni
        yoqish" tugmasi hech qachon ko'rinmasdi.
      */
      onAudioPlaybackChanged()

      status.value = 'connected'
      connectionError.value = null
      rebuildTiles()
      wakeLock?.acquire()

      if (disconnectedAt !== null) {
        report('reconnected', { attempt: reconnectAttempt, detail: `${String(Date.now() - disconnectedAt)}ms` })
        stopReconnect()
        void restoreMedia()
      } else {
        report('connected')
        /*
          BIRINCHI ULANISH — LEKIN SAHIFA QAYTA QURILGAN BO'LISHI MUMKIN.

          🔴 Ilgari bu shoxda niyat UMUMAN tiklanmasdi: kompozabl uchun
             bu "birinchi ulanish", ya'ni tiklaydigan holat yo'q edi.
             Amalda esa o'quvchining sahifasi dars davomida o'rtacha
             3.2 marta qayta quriladi va HAR SAFAR mikrofon o'chib
             qolardi (o'lchov: `liveMediaIntent.ts`).

          Endi saqlangan niyat (10 daqiqagacha) shu yerda tiklanadi.
          Niyat bo'lmasa `restoreMedia()` hech narsa qilmaydi, shuning
          uchun oddiy birinchi kirish uchun xatti-harakat o'zgarmaydi.
        */
        if (wantMic || wantCamera) void restoreMedia()
      }
    } catch (error) {
      window.clearTimeout(abortTimer)
      // Foydalanuvchi shu orada chiqib ketdi — `teardown` hammasini tozalagan,
      // bu urinishning xatosi endi hech narsani boshlamasin.
      if (disposed || gen !== generation) return
      // MUHIM: muvaffaqiyatsiz `Room` ni tozalab, `room` ni `null` qilamiz —
      // aks holda "Qayta urinish" tugmasi hech qachon ishlamas edi
      // (`connect()` boshida `room !== null` bo'lib chiqib ketardi).
      const failed = room
      room = null
      if (failed !== null) {
        unbindEvents(failed)
        try {
          await failed.disconnect(true)
        } catch {
          /* e'tiborsiz */
        }
      }
      // Ulanish yiqilganda kamera treki `Room` dan MUSTAQIL tirik qolardi —
      // kameraning chirog'i yonib turar, lekin ekranni xato qoplamasi yopardi.
      dropLocalCamera()
      isCameraOn.value = false
      isMicOn.value = false
      isScreenSharing.value = false
      if (disposed) return

      const text = describeConnectError(error)

      // Vaqtinchalik xato (internet yo'q, server qayta ishga tushmoqda) —
      // tugmani kutmasdan qayta urinamiz. Birinchi kirishda ham.
      // `reconnectEnabled` — urinish paytida `onDisconnected` uzilishni YAKUNIY
      // deb belgilagan bo'lishi mumkin (boshqa oyna, chiqarib yuborish).
      if (!isFatalJoinError(error) && reconnectEnabled) {
        lastFailureText = text
        report('connect-failed', { attempt: reconnectAttempt, detail: text })
        beginReconnect()
        return
      }

      report('connect-stopped', { detail: text })
      stopReconnect()
      reconnectEnabled = false
      wakeLock?.release()
      status.value = 'failed'
      connectionError.value = text
    } finally {
      // Faqat SHU avlodning bayrog'i: `leave()` dan keyin boshlangan yangi
      // urinishning bayrog'ini eski urinish tushirib yubormasin.
      if (gen === generation) connectInFlight = false
    }
  }

  /* ------------------------------- boshqaruv ------------------------------- */

  /**
   * ★ OPTIMISTIK YANGILANISH NAQSHI.
   *
   * MUAMMO (foydalanuvchi shikoyati): "tugmalarning bosilishi bilinmayapti".
   * Sababi — holat FAQAT LiveKit javobidan keyin o'zgarardi. `getUserMedia`
   * ruxsat so'rashi, qurilmani ochishi va trekni e'lon qilishi 300 ms dan
   * bir necha sekundgacha vaqt oladi; shu oraliqda tugma umuman o'zgarmasdi
   * va foydalanuvchi "bosilmadi" deb yana bosardi.
   *
   * YECHIM: holat DARHOL o'zgaradi (`state.value = next`), tugma kutish
   * ko'rsatkichini yoqadi, amal muvaffaqiyatsiz bo'lsa holat LiveKit'ning
   * HAQIQIY qiymatiga ORQAGA QAYTADI va xato o'zbekcha ko'rsatiladi.
   *
   * Har tugmaning O'Z `pending` bayrog'i bor — ilgari bitta umumiy `isBusy`
   * hammasini birdan o'chirardi va "hech narsa bosilmayapti" hissi kuchayardi.
   */
  /**
   * `work` ni CHEKLANGAN vaqt ichida kutadi.
   *
   * Qaytadi: `true` — amal ulgurdi; `false` — muddat tugadi (amal fonda
   * DAVOM ETADI, biz shunchaki kutishni to'xtatamiz). Amal xato bersa —
   * istisno tashlanadi, chunki chaqiruvchi holatni orqaga qaytarishi kerak.
   *
   * Taymer HAR IKKI yo'lda ham tozalanadi va `deadlineTimers` da hisobga
   * olinadi — komponent yopilganda osilib qolmasligi uchun.
   */
  function waitWithDeadline(work: Promise<unknown>, ms: number): Promise<boolean> {
    return new Promise<boolean>((resolve, reject) => {
      const timer = window.setTimeout(() => {
        deadlineTimers.delete(timer)
        resolve(false)
      }, ms)
      deadlineTimers.add(timer)

      const clear = (): void => {
        window.clearTimeout(timer)
        deadlineTimers.delete(timer)
      }
      work.then(
        () => {
          clear()
          resolve(true)
        },
        (error: unknown) => {
          clear()
          reject(error instanceof Error ? error : new Error(String(error)))
        },
      )
    })
  }

  async function runToggle(
    state: Ref<boolean>,
    pending: Ref<boolean>,
    apply: (participant: Room['localParticipant'], next: boolean) => Promise<unknown>,
    read: (participant: Room['localParticipant']) => boolean,
  ): Promise<void> {
    const current = room
    if (current === null) {
      // Ilgari bu holat JIMGINA `return` bilan tugardi — tugma umuman
      // javob bermagandek ko'rinardi. Endi sabab aytiladi.
      mediaError.value = 'Video aloqasi hali tayyor emas. Ulanish tiklanishini kuting.'
      return
    }
    if (pending.value) return

    const next = !state.value
    state.value = next // ← DARHOL: foydalanuvchi bosilganini shu zahoti ko'radi
    pending.value = true
    mediaError.value = null

    try {
      const finished = await waitWithDeadline(
        apply(current.localParticipant, next),
        TOGGLE_DEADLINE_MS,
      )
      if (!finished) {
        /*
          Server tasdiqni belgilangan vaqtda qaytarmadi. Tugmani MANGU
          spinner'da ushlab turish — eng yomon variant (foydalanuvchi
          shikoyati aynan shu edi). Shuning uchun tugmani ozod qilamiz,
          lekin JIMGINA emas: oqim boshqalarga yetmayotgan bo'lishi
          mumkinligini ochiq aytamiz.
        */
        mediaError.value =
          'Video server tasdiqni qaytarmadi — oqimingiz boshqa ishtirokchilarga yetib bormayotgan bo‘lishi mumkin.'
      }
    } catch (error) {
      // Orqaga qaytarish: LiveKit'dagi HAQIQIY holatni olamiz, `!next` ni emas —
      // amal yarim bajarilgan bo'lishi ham mumkin.
      state.value = room === null ? false : read(current.localParticipant)
      mediaError.value = describeMediaError(error)
    } finally {
      pending.value = false
      scheduleRebuild()
    }
  }

  async function toggleMic(): Promise<void> {
    // Xona yo'q paytdagi bosish (qayta ulanish ketmoqda) niyatni O'ZGARTIRMAYDI —
    // `runToggle` u holda hech narsa qilmaydi, holat esa shunchaki `false`.
    // Amal paytida aloqa uzilsa ham niyat buzilmasin: faqat AYNI xona hali
    // tirik bo'lsa yangilanadi. ⚠️ `state === Connected` TEKSHIRILMAYDI:
    // "qayta ulanmoqda" paytida o'chirilgan mikrofon keyingi uzilishdan
    // keyin o'zi yoqilib ketardi. Uzilish `room` ni SINXRON bo'shatadi.
    const target = room
    const seq = micModerationSeq
    await runToggle(
      isMicOn,
      micPending,
      (participant, next) => participant.setMicrophoneEnabled(next),
      readMicOn,
    )
    if (target !== null && room === target && micModerationSeq === seq) {
      wantMic = isMicOn.value
      rememberIntent()
    }
  }

  /** Taxta trekini (uzilish/tozalashda) mahalliy to'xtatadi — server bilan gaplashmasdan. */
  function dropCanvasTrack(): void {
    const track = canvasTrack
    canvasTrack = null
    canvasPublishing = false
    isCanvasSharing.value = false
    canvasSharePending.value = false
    if (track !== null) track.stop()
  }

  /** Mahalliy kamera trekini to'xtatib, sahnadan olib tashlaydi. */
  function dropLocalCamera(): void {
    const track = localCameraTrack.value
    localCameraTrack.value = null
    if (track !== null) track.stop()
  }

  async function toggleCamera(): Promise<void> {
    const target = room
    const seq = cameraModerationSeq
    await runToggle(
      isCameraOn,
      cameraPending,
      async (participant, next) => {
        if (!next) {
          const track = localCameraTrack.value
          localCameraTrack.value = null
          if (track !== null) {
            // `stopOnUnpublish = true` — kameraning yonayotgan chirog'i o'chadi.
            await participant.unpublishTrack(track, true)
          } else {
            await participant.setCameraEnabled(false)
          }
          return
        }

        // 1-QADAM: trekni mahalliy yaratamiz va DARHOL sahnaga qo'yamiz.
        //          Bu qadam serverga umuman bog'liq emas.
        // ⚠️ `videoCaptureDefaults` BU YO'LGA TA'SIR QILMAYDI — trek qo'lda
        //    yaratilyapti. Shuning uchun o'lcham AYNI yordamchidan olinadi,
        //    aks holda o'quvchi tugma orqali yoqqanda yana 720p ketardi.
        const track = await createLocalVideoTrack({
          resolution: captureResolution(isHost.value),
        })
        localCameraTrack.value = track
        rebuildTiles()

        // 2-QADAM: endi e'lon qilamiz. Bu yiqilsa ham foydalanuvchi
        //          o'z videosini ko'rib turadi va xato xabari chiqadi.
        try {
          // O'quvchi — bitta yengil qatlam (sabab `STUDENT_CAMERA_ENCODING` da).
          await participant.publishTrack(
            track,
            isHost.value
              ? { source: Track.Source.Camera }
              : { source: Track.Source.Camera, simulcast: false, videoEncoding: STUDENT_CAMERA_ENCODING },
          )
        } catch (error) {
          dropLocalCamera()
          throw error
        }
      },
      readCameraOn,
    )
    if (target !== null && room === target && cameraModerationSeq === seq) {
      wantCamera = isCameraOn.value
      rememberIntent()
    }
  }

  function toggleScreenShare(): Promise<void> {
    // Ekranni faqat host ulashadi (tugma ham faqat unda ko'rinadi).
    if (!isHost.value) return Promise.resolve()

    /*
      ★ TUGMA YASHIRILMAYDI — SABAB AYTILADI. Bu to'liq ekran tugmasidan
      (`MediaControlBar.canFullscreen`) ATAYLAB farq qiladi:

        • to'liq ekran — qulaylik; qo'llab-quvvatlanmasa tugmani umuman
          chizmaslik to'g'ri, chunki foydalanuvchi uni qidirmaydi;
        • ekran ulashish — DARSNING ASOSIY VOSITASI. Tugma jimgina
          yo'q bo'lsa ustoz uni qidiraveradi, topolmaydi va "ilova buzuq"
          degan xulosaga keladi. Aynan shu holat 2026-09-03 da
          ustozlardan shikoyat bo'lib keldi.

      Shuning uchun tugma joyida qoladi va bosilganda ANIQ, o'zbekcha
      javob beradi: nima uchun mumkin emas va nima qilish kerak.
    */
    if (!SCREEN_SHARE_SUPPORTED) {
      mediaError.value = screenShareUnsupportedText()
      return Promise.resolve()
    }

    return runToggle(
      isScreenSharing,
      screenPending,
      async (participant, next) => {
        // Taxta ulashilayotgan bo'lsa — u ham "ekran ulashuvi": bitta
        // manba bo'lishi kerak, avval taxtani to'xtatamiz.
        if (canvasTrack !== null) await stopCanvasShare()
        await participant.setScreenShareEnabled(next, {
          audio: true,
          resolution: SCREEN_SHARE_CAPTURE,
        })
      },
      (participant) => participant.isScreenShareEnabled,
    )
  }

  /* --------------------------------------------------- kitob taxtasi */

  /**
   * Canvas'ni EKRAN ULASHUVI sifatida uzatadi.
   *
   * ★ `captureStream(5)` — 5 kadr/soniya yetarli: sahifa statik, chizma
   *   sekin. Yuqori kadr tezligi telefon batareyasini yeydi.
   * ★ `simulcast: false` — matnli kadr uchun bitta sifatli qatlam
   *   afzal: pastki qatlamda harflar o'qilmas bo'lib qolardi.
   * ★ Manba `ScreenShare`: o'quvchi klienti va yozuv (egress) uni oddiy
   *   ekran ulashuvi deb biladi — hech qayerda maxsus ishlov yo'q.
   */
  async function shareCanvas(canvas: HTMLCanvasElement): Promise<void> {
    const current = room
    if (current === null) {
      mediaError.value = 'Video aloqasi hali tayyor emas. Ulanish tiklanishini kuting.'
      return
    }
    if (canvasTrack !== null || canvasSharePending.value) return

    canvasSharePending.value = true
    mediaError.value = null
    try {
      if (current.localParticipant.isScreenShareEnabled) {
        await current.localParticipant.setScreenShareEnabled(false)
      }

      const stream = canvas.captureStream(5)
      const media = stream.getVideoTracks()[0]
      if (media === undefined) {
        throw new Error('Brauzer canvas oqimini bera olmadi (captureStream).')
      }

      // `userProvidedTrack = true` — LiveKit bu trekni O'ZI yaratmagan:
      // qayta ulanishda `getUserMedia` bilan "qayta ochishga" urinmasin
      // (canvas oqimini kameradek qayta ochib bo'lmaydi — xato berardi).
      const track = new LocalVideoTrack(media, undefined, true)
      canvasTrack = track
      canvasPublishing = true
      isCanvasSharing.value = true

      try {
        await current.localParticipant.publishTrack(track, {
          source: Track.Source.ScreenShare,
          name: 'book-board',
          simulcast: false,
          videoEncoding: { maxBitrate: 1_200_000, maxFramerate: 5 },
          // ⚠️ `ScreenShare` manbasida livekit-client `videoEncoding` ni
          //    E'TIBORSIZ qoldirib `screenShareEncoding` ni oladi. Busiz
          //    taxta xona standartini (15 kadr) meros qilib olardi.
          screenShareEncoding: { maxBitrate: 1_200_000, maxFramerate: 5 },
        })
      } catch (error) {
        canvasTrack = null
        isCanvasSharing.value = false
        track.stop()
        throw error
      } finally {
        canvasPublishing = false
      }
    } catch (error) {
      mediaError.value = describeMediaError(error)
    } finally {
      canvasSharePending.value = false
      scheduleRebuild()
    }
  }

  async function stopCanvasShare(): Promise<void> {
    const track = canvasTrack
    canvasTrack = null
    canvasPublishing = false
    isCanvasSharing.value = false
    if (track === null) return
    const current = room
    if (current !== null) {
      // `stopOnUnpublish = true` — MediaStreamTrack ham to'xtaydi.
      await current.localParticipant.unpublishTrack(track, true).catch(() => undefined)
    } else {
      track.stop()
    }
    scheduleRebuild()
  }

  /** Ovoz bloklangan bo'lsa — foydalanuvchi bosgan zahoti ochamiz. */
  async function enableAudio(): Promise<void> {
    const current = room
    if (current === null) return
    try {
      await current.startAudio()
      audioBlocked.value = !current.canPlaybackAudio
    } catch (error) {
      mediaError.value = describeMediaError(error)
    }
  }

  /* -------------------------------- tozalash ------------------------------- */

  async function teardown(): Promise<void> {
    if (rebuildFrame !== null) {
      cancelAnimationFrame(rebuildFrame)
      rebuildFrame = null
    }
    if (rebuildTimer !== null) {
      window.clearTimeout(rebuildTimer)
      rebuildTimer = null
    }
    // Kutish chegarasi taymerlari komponentdan uzoq yashab qolmasin.
    for (const timer of deadlineTimers) window.clearTimeout(timer)
    deadlineTimers.clear()

    const target = room
    room = null
    tiles.value = []
    isMicOn.value = false
    isCameraOn.value = false
    isScreenSharing.value = false
    micPending.value = false
    cameraPending.value = false
    screenPending.value = false
    audioBlocked.value = false
    moderationNotice.value = null

    // Kamera treki `Room` dan MUSTAQIL yaratilgani uchun uni O'ZIMIZ
    // to'xtatishimiz shart — aks holda kameraning chirog'i yonib qolardi.
    dropLocalCamera()
    dropCanvasTrack()
    detachAllAudio()

    if (target === null) return
    unbindEvents(target)
    try {
      // `stopTracks: true` — kamera/mikrofon indikatori o'chadi, MediaStream oqmaydi.
      await target.disconnect(true)
    } catch {
      /* e'tiborsiz */
    }
  }

  async function leave(): Promise<void> {
    // Foydalanuvchi O'ZI chiqdi — qayta ulanish ham, niyat ham tugaydi.
    noteExit('left')
    report('left')
    generation += 1
    connectInFlight = false
    stopReconnect()
    reconnectEnabled = false
    wantMic = false
    wantCamera = false
    // Foydalanuvchi O'ZI chiqdi — saqlangan niyat ham o'chadi, aks holda
    // darsga qayta kirganda mikrofoni o'zidan yonardi.
    if (intentStorageEnabled) clearMediaIntent(sessionId)
    screenShareLost = false
    wakeLock?.release()
    await teardown()
    /*
      ATAYLAB `idle`, `disconnected` EMAS.
      `disconnected` endi XATO holati sifatida ko'rsatiladi (qizil chiziq +
      "Qayta urinish"). Foydalanuvchi o'zi "Chiqish" bosganda esa xato yo'q —
      aks holda darsdan chiqayotganda bir lahzaga "aloqa uzildi" chaqnab
      o'tardi. Xatoni ham tozalaymiz.
    */
    connectionError.value = null
    status.value = 'idle'
  }

  function dismissMediaError(): void {
    mediaError.value = null
  }

  onBeforeUnmount(() => {
    disposed = true
    generation += 1
    stopReconnect()
    if (typeof window !== 'undefined') {
      window.removeEventListener('online', onNetworkMayBeBack)
      window.removeEventListener('pageshow', onPageShow)
      document.removeEventListener('visibilitychange', onPageVisibility)
    }
    wakeLock?.release()
    report('page-closed', { reason: exitReason ?? 'unknown' })
    reporter?.dispose()
    void teardown()
  })

  return {
    status,
    tiles,
    isHost,
    roomName,
    endsAt,
    isMicOn,
    isCameraOn,
    isScreenSharing,
    screenShareSupported: SCREEN_SHARE_SUPPORTED,
    micPending,
    cameraPending,
    screenPending,
    audioBlocked,
    localQuality,
    linkWarning,
    mediaError,
    connectionError,
    moderationNotice,
    dismissModerationNotice,
    isCanvasSharing,
    canvasSharePending,
    shareCanvas,
    stopCanvasShare,
    connect,
    leave,
    noteExit,
    toggleMic,
    toggleCamera,
    toggleScreenShare,
    enableAudio,
    dismissMediaError,
    dismissLinkWarning,
  }
}
