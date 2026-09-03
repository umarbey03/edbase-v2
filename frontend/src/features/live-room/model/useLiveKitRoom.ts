import {
  ConnectionError,
  ConnectionErrorReason,
  ConnectionState,
  createLocalVideoTrack,
  DisconnectReason,
  Room,
  RoomEvent,
  Track,
  VideoPresets,
} from 'livekit-client'
import type {
  LocalTrackPublication,
  LocalVideoTrack,
  Participant,
  RemoteParticipant,
  RemoteTrack,
  RemoteTrackPublication,
  TrackPublication,
} from 'livekit-client'
import { onBeforeUnmount, ref, shallowRef } from 'vue'
import type { Ref, ShallowRef } from 'vue'

import { fetchLiveKitJoin } from '@/entities/session'
import { toUserMessage } from '@/shared/api'

export type MediaStatus =
  | 'idle'
  | 'loading'
  | 'connecting'
  | 'connected'
  | 'reconnecting'
  | 'disconnected'
  | 'failed'

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
  mediaError: Ref<string | null>
  connectionError: Ref<string | null>
  connect: () => Promise<void>
  leave: () => Promise<void>
  toggleMic: () => Promise<void>
  toggleCamera: () => Promise<void>
  toggleScreenShare: () => Promise<void>
  enableAudio: () => Promise<void>
  dismissMediaError: () => void
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

  let room: Room | null = null
  let disposed = false
  let rebuildFrame: number | null = null
  let rebuildTimer: number | null = null

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
    return localCameraTrack.value !== null || participant.isCameraEnabled
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
      })
    }

    // MAHALLIY ishtirokchi uchun: e'lon qilingan trek hali yo'q bo'lsa,
    // to'g'ridan-to'g'ri mahalliy trekdan chizamiz (izoh `localCameraTrack` da).
    const publishedCamera = videoTrackOf(participant.getTrackPublication(Track.Source.Camera))
    const cameraTrack = publishedCamera ?? (isLocal ? localCameraTrack.value : null)
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

  function onTrackMuteChanged(_publication: TrackPublication, _participant: Participant): void {
    scheduleRebuild()
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

    const target = room
    room = null
    if (target !== null) {
      unbindEvents(target)
      // Kamera/mikrofon indikatori o'chsin va MediaStream oqmasin.
      void target.disconnect(true).catch(() => undefined)
    }

    dropLocalCamera()
    detachAllAudio()
    tiles.value = []
    isMicOn.value = false
    isCameraOn.value = false
    isScreenSharing.value = false
    micPending.value = false
    cameraPending.value = false
    screenPending.value = false
    audioBlocked.value = false

    status.value = 'disconnected'
    connectionError.value = describeDisconnect(reason)
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
      .off(RoomEvent.Disconnected, onDisconnected)
      .off(RoomEvent.MediaDevicesError, onMediaDevicesError)
      .off(RoomEvent.AudioPlaybackStatusChanged, onAudioPlaybackChanged)
    // Qolgan har qanday tinglovchi ham qolib ketmasin.
    target.removeAllListeners()
  }

  /* -------------------------------- ulanish -------------------------------- */

  async function connect(): Promise<void> {
    if (disposed || room !== null) return
    // Ikki marta bosilgan "Qayta urinish" ikkita `Room` yaratmasligi uchun.
    if (status.value === 'loading' || status.value === 'connecting') return

    status.value = 'loading'
    connectionError.value = null

    try {
      // SPEC 5: POST /api/v1/live-sessions/{id}/token -> LiveKitJoinDto
      const join = await fetchLiveKitJoin(sessionId)
      if (disposed) return

      isHost.value = join.isHost
      roomName.value = join.roomName
      endsAt.value = join.endsAt

      const target = new Room({
        // `adaptiveStream` — ko'rinmayotgan yoki kichkina katakchalar uchun past
        // sifatli qatlam so'raladi. 200 ta ishtirokchida bu shart.
        adaptiveStream: true,
        // `dynacast` — hech kim ko'rmayotgan qatlamlar serverda o'chiriladi.
        dynacast: true,
        videoCaptureDefaults: { resolution: VideoPresets.h720.resolution },
        publishDefaults: {
          // Simulcast: bir nechta sifat qatlami yuboriladi, LiveKit har bir
          // ko'ruvchiga mos qatlamni tanlaydi.
          simulcast: true,
          videoSimulcastLayers: [VideoPresets.h180, VideoPresets.h360],
        },
      })

      room = target
      bindEvents(target)

      status.value = 'connecting'
      await target.connect(join.serverUrl, join.token, { autoSubscribe: true })

      if (disposed) {
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
      rebuildTiles()
    } catch (error) {
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
      status.value = 'failed'
      connectionError.value = describeConnectError(error)
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

  function toggleMic(): Promise<void> {
    return runToggle(
      isMicOn,
      micPending,
      (participant, next) => participant.setMicrophoneEnabled(next),
      readMicOn,
    )
  }

  /** Mahalliy kamera trekini to'xtatib, sahnadan olib tashlaydi. */
  function dropLocalCamera(): void {
    const track = localCameraTrack.value
    localCameraTrack.value = null
    if (track !== null) track.stop()
  }

  function toggleCamera(): Promise<void> {
    return runToggle(
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
        const track = await createLocalVideoTrack({
          resolution: VideoPresets.h720.resolution,
        })
        localCameraTrack.value = track
        rebuildTiles()

        // 2-QADAM: endi e'lon qilamiz. Bu yiqilsa ham foydalanuvchi
        //          o'z videosini ko'rib turadi va xato xabari chiqadi.
        try {
          await participant.publishTrack(track, { source: Track.Source.Camera })
        } catch (error) {
          dropLocalCamera()
          throw error
        }
      },
      readCameraOn,
    )
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
      (participant, next) => participant.setScreenShareEnabled(next, { audio: true }),
      (participant) => participant.isScreenShareEnabled,
    )
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

    // Kamera treki `Room` dan MUSTAQIL yaratilgani uchun uni O'ZIMIZ
    // to'xtatishimiz shart — aks holda kameraning chirog'i yonib qolardi.
    dropLocalCamera()
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
    mediaError,
    connectionError,
    connect,
    leave,
    toggleMic,
    toggleCamera,
    toggleScreenShare,
    enableAudio,
    dismissMediaError,
  }
}
