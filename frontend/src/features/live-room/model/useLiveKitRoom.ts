import {
  ConnectionState,
  Room,
  RoomEvent,
  Track,
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
  isBusy: Ref<boolean>
  mediaError: Ref<string | null>
  connectionError: Ref<string | null>
  connect: () => Promise<void>
  leave: () => Promise<void>
  toggleMic: () => Promise<void>
  toggleCamera: () => Promise<void>
  toggleScreenShare: () => Promise<void>
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
      default:
        break
    }
  }
  return toUserMessage(error)
}

export function useLiveKitRoom(sessionId: number): UseLiveKitRoomResult {
  const status = ref<MediaStatus>('idle')
  const isHost = ref(false)
  const roomName = ref<string | null>(null)
  const endsAt = ref<string | null>(null)
  const isMicOn = ref(false)
  const isCameraOn = ref(false)
  const isScreenSharing = ref(false)
  const isBusy = ref(false)
  const mediaError = ref<string | null>(null)
  const connectionError = ref<string | null>(null)

  /**
   * `shallowRef` — katakchalar massivi butunligicha almashtiriladi.
   * LiveKit `Track` obyektlari ichida MediaStreamTrack bor; ularni Vue proksisiga
   * o'rash MUMKIN EMAS (`attach()` ishlamay qoladi va xotira oqadi).
   */
  const tiles = shallowRef<ParticipantTile[]>([])

  let room: Room | null = null
  let disposed = false
  let rebuildHandle: number | null = null

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

    const cameraTrack = videoTrackOf(participant.getTrackPublication(Track.Source.Camera))
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
    rebuildHandle = null
    const current = room
    if (current === null) return

    const next: ParticipantTile[] = []
    appendTiles(current.localParticipant, true, next)
    for (const participant of current.remoteParticipants.values()) {
      appendTiles(participant, false, next)
    }
    tiles.value = next

    isMicOn.value = current.localParticipant.isMicrophoneEnabled
    isCameraOn.value = current.localParticipant.isCameraEnabled
    isScreenSharing.value = current.localParticipant.isScreenShareEnabled
  }

  /**
   * 200 kishilik xonada ulanish paytida o'nlab hodisa ketma-ket keladi
   * (`ParticipantConnected` × 200, `TrackSubscribed` × N...). Har biriga alohida
   * qayta render qilish o'rniga kadrga BITTA marta qayta quramiz.
   */
  function scheduleRebuild(): void {
    if (disposed || rebuildHandle !== null) return
    rebuildHandle = requestAnimationFrame(rebuildTiles)
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

  function onDisconnected(): void {
    if (disposed) return
    status.value = 'disconnected'
    tiles.value = []
  }

  function onMediaDevicesError(error: Error): void {
    mediaError.value = describeMediaError(error)
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
      if (disposed) return
      status.value = 'failed'
      connectionError.value = toUserMessage(error)
    }
  }

  /* ------------------------------- boshqaruv ------------------------------- */

  async function withBusy(action: () => Promise<void>): Promise<void> {
    if (isBusy.value) return
    isBusy.value = true
    try {
      await action()
      mediaError.value = null
    } catch (error) {
      mediaError.value = describeMediaError(error)
    } finally {
      isBusy.value = false
      scheduleRebuild()
    }
  }

  function toggleMic(): Promise<void> {
    return withBusy(async () => {
      const current = room
      if (current === null) return
      await current.localParticipant.setMicrophoneEnabled(!current.localParticipant.isMicrophoneEnabled)
    })
  }

  function toggleCamera(): Promise<void> {
    return withBusy(async () => {
      const current = room
      if (current === null) return
      await current.localParticipant.setCameraEnabled(!current.localParticipant.isCameraEnabled)
    })
  }

  function toggleScreenShare(): Promise<void> {
    return withBusy(async () => {
      const current = room
      if (current === null) return
      // Ekranni faqat host ulashadi (tugma ham faqat unda ko'rinadi).
      if (!isHost.value) return
      await current.localParticipant.setScreenShareEnabled(
        !current.localParticipant.isScreenShareEnabled,
        { audio: true },
      )
    })
  }

  /* -------------------------------- tozalash ------------------------------- */

  async function teardown(): Promise<void> {
    if (rebuildHandle !== null) {
      cancelAnimationFrame(rebuildHandle)
      rebuildHandle = null
    }

    const target = room
    room = null
    tiles.value = []

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
    status.value = 'disconnected'
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
    isBusy,
    mediaError,
    connectionError,
    connect,
    leave,
    toggleMic,
    toggleCamera,
    toggleScreenShare,
    dismissMediaError,
  }
}
