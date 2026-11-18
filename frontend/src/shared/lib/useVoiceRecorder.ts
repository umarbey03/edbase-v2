import { onBeforeUnmount, readonly, ref } from 'vue'

/**
 * ========================================================================
 * ILOVA ICHIDA OVOZ YOZISH (`MediaRecorder`) — 2026-10-29
 * ========================================================================
 *
 * ★ NIMA UCHUN: kurs talaffuzga qurilgan va vazifalarning katta qismi
 * OVOZLI javob talab qiladi. Ilgari o'quvchi telefon diktofonida yozib,
 * so'ng faylni qidirib topib biriktirishi kerak edi — ko'pchilik shu
 * yerda adashardi (diktofon fayli qayerda saqlangani noma'lum, ba'zi
 * diktofonlar `.amr` beradi va server uni rad etadi).
 *
 * ★ FORMAT TANLOVI SERVERGA BOG'LIQ: `SubmissionAttachmentReader` turni
 * fayl MAZMUNIDAN (sehrli baytlar, `MediaSignatures`) aniqlaydi va
 * ovoz uchun WebM (EBML), Ogg (`OggS`) va MP4/M4A (`ftyp`) konteynerlarini
 * qabul qiladi. Brauzerlar `MediaRecorder` da aynan shu uchtasini beradi:
 *   - Chrome / Edge / Android: `audio/webm;codecs=opus`
 *   - Firefox: `audio/ogg;codecs=opus` (webm ham bor)
 *   - iOS / macOS Safari: `audio/mp4` (AAC, `ftyp` konteyner — server
 *     buni Audio ustunligi qoidasi bilan OVOZ deb taniydi).
 * ⚠️ `audio/aac` (ADTS) ATAYLAB ro'yxatda YO'Q: server uni MPEG freym
 * sarlavhasi deb `mp3` sifatida qabul qiladi — o'tadi, lekin noto'g'ri
 * yorliq bilan. Kerak emas: Safari `audio/mp4` ni baribir qo'llaydi.
 * Ya'ni SERVERDA O'ZGARISH KERAK EMAS.
 *
 * ★ HAJM: server chegarasi ovoz uchun 10 MB. 48 kbit/s Opus'da 3 daqiqa
 * ~1.1 MB — chegaradan 9 baravar kichik. Safari bitreyt so'rovini
 * e'tiborsiz qoldirishi mumkin, shuning uchun bo'laklar (`timeslice`)
 * hajmi YIG'IB boriladi va chegaraga yaqinlashsa yozuv o'zi to'xtaydi.
 *
 * 🔴 MIKROFON HAR DOIM QO'YIB YUBORILADI: to'xtatishda, xatoda va
 * komponent yo'q qilinganda (oyna yopildi). Aks holda brauzer manzil
 * satrida qizil "mikrofon yoqilgan" belgisi qolib ketadi va o'quvchi
 * "platforma meni eshityapti" deb o'ylaydi. `getUserMedia` kutilayotgan
 * paytda oyna yopilsa ham — kelgan oqim DARHOL to'xtatiladi (`disposed`).
 */

export type VoiceRecorderState = 'idle' | 'requesting' | 'recording'

export interface VoiceRecording {
  file: File
  /** Soniyalarda, o'zimiz o'lchagan (WebM sarlavhasida davomiylik YO'Q). */
  durationSeconds: number
}

interface MimeChoice {
  /** `MediaRecorder` ga beriladigan to'liq tur. */
  recorderType: string
  /** `File.type` — `validateAttachments` `audio/` prefiksiga qaraydi. */
  fileType: string
  extension: string
}

/**
 * Ustuvorlik tartibida. Birinchi qo'llangani olinadi.
 * Hammasi server tanigan konteynerlar (yuqoridagi izoh).
 */
const MIME_CHOICES: readonly MimeChoice[] = [
  { recorderType: 'audio/webm;codecs=opus', fileType: 'audio/webm', extension: 'webm' },
  { recorderType: 'audio/ogg;codecs=opus', fileType: 'audio/ogg', extension: 'ogg' },
  { recorderType: 'audio/mp4', fileType: 'audio/mp4', extension: 'm4a' },
  { recorderType: 'audio/webm', fileType: 'audio/webm', extension: 'webm' },
]

const AUDIO_BITS_PER_SECOND = 48_000
const TIMESLICE_MS = 1000

/** Brauzer ovoz yozishni umuman qo'llaydimi (HTTPS + API + mos format). */
export function isVoiceRecordingSupported(): boolean {
  return pickMime() !== null
}

function pickMime(): MimeChoice | null {
  if (typeof window === 'undefined') return null
  if (!window.isSecureContext) return null
  if (typeof navigator.mediaDevices?.getUserMedia !== 'function') return null
  if (typeof MediaRecorder === 'undefined') return null

  // Eski Safari'da `isTypeSupported` yo'q edi — bu holda formatni
  // aniqlay olmaymiz, demak server qabul qilishiga ham kafolat yo'q.
  if (typeof MediaRecorder.isTypeSupported !== 'function') return null

  return MIME_CHOICES.find((choice) => MediaRecorder.isTypeSupported(choice.recorderType)) ?? null
}

function pad(value: number): string {
  return String(value).padStart(2, '0')
}

/** `ovoz-2026-10-29-1530.webm` — o'quvchi va ustoz fayl ro'yxatida tanisin. */
function buildFileName(extension: string, at: Date): string {
  const date = `${at.getFullYear()}-${pad(at.getMonth() + 1)}-${pad(at.getDate())}`
  return `ovoz-${date}-${pad(at.getHours())}${pad(at.getMinutes())}.${extension}`
}

/** `getUserMedia` / `MediaRecorder` xatosini o'quvchi tushunadigan matnga. */
function describeError(error: unknown): string {
  const name = error instanceof DOMException || error instanceof Error ? error.name : ''

  switch (name) {
    case 'NotAllowedError':
    case 'SecurityError':
      return 'Mikrofonga ruxsat berilmadi. Brauzer sozlamalarida (manzil satridagi belgi) '
        + 'mikrofonga ruxsat bering yoki ovozni fayl sifatida biriktiring.'
    case 'NotFoundError':
    case 'OverconstrainedError':
      return 'Mikrofon topilmadi. Mikrofonni ulang yoki ovozni telefon diktofonida yozib, '
        + '“Fayl biriktirish” orqali yuklang.'
    case 'NotReadableError':
    case 'AbortError':
      return 'Mikrofon band — uni boshqa ilova (qo‘ng‘iroq, jonli dars) ishlatayotgan '
        + 'bo‘lishi mumkin. Uni yopib, qayta urinib ko‘ring.'
    default:
      return 'Ovoz yozib bo‘lmadi. Qayta urinib ko‘ring yoki ovozni fayl sifatida biriktiring.'
  }
}

export function useVoiceRecorder(options: {
  maxSeconds: number
  maxBytes: number
  onRecorded: (recording: VoiceRecording) => void
}) {
  const state = ref<VoiceRecorderState>('idle')
  const elapsedSeconds = ref(0)
  const errorMessage = ref<string | null>(null)
  /** Yozuv vaqt yoki hajm chegarasida o'zi to'xtadimi (ekran o'quvchiga aytiladi). */
  const autoStopped = ref(false)

  let stream: MediaStream | null = null
  let recorder: MediaRecorder | null = null
  let chunks: Blob[] = []
  let bytes = 0
  let startedAt = 0
  let timer: ReturnType<typeof setInterval> | null = null
  let disposed = false

  function releaseStream(): void {
    stream?.getTracks().forEach((track) => track.stop())
    stream = null
  }

  function clearTimer(): void {
    if (timer !== null) clearInterval(timer)
    timer = null
  }

  function measure(): number {
    return Math.min(options.maxSeconds, (performance.now() - startedAt) / 1000)
  }

  async function start(): Promise<void> {
    if (state.value !== 'idle') return

    errorMessage.value = null
    autoStopped.value = false

    const mime = pickMime()
    if (mime === null) {
      errorMessage.value = 'Brauzeringiz ovoz yozishni qo‘llamaydi. Ovozni telefon diktofonida '
        + 'yozib, “Fayl biriktirish” orqali yuklang.'
      return
    }

    state.value = 'requesting'

    let acquired: MediaStream
    try {
      acquired = await navigator.mediaDevices.getUserMedia({
        audio: { echoCancellation: true, noiseSuppression: true },
      })
    } catch (error) {
      if (disposed) return
      state.value = 'idle'
      errorMessage.value = describeError(error)
      return
    }

    // ⚠️ Ruxsat oynasi ochiq turganda vazifa oynasi yopilgan bo'lishi
    // mumkin — kelgan oqimni ushlab qolmaymiz.
    if (disposed) {
      acquired.getTracks().forEach((track) => track.stop())
      return
    }

    stream = acquired

    try {
      recorder = new MediaRecorder(acquired, {
        mimeType: mime.recorderType,
        audioBitsPerSecond: AUDIO_BITS_PER_SECOND,
      })
    } catch (error) {
      releaseStream()
      state.value = 'idle'
      errorMessage.value = describeError(error)
      return
    }

    chunks = []
    bytes = 0
    const active = recorder

    active.ondataavailable = (event: BlobEvent) => {
      if (event.data.size === 0) return
      chunks.push(event.data)
      bytes += event.data.size

      // Keyingi bo'lak chegaradan oshirib yubormasin — 90% da to'xtaymiz.
      if (bytes >= options.maxBytes * 0.9) finish(true)
    }

    active.onstop = () => {
      const durationSeconds = Math.round(measure())
      clearTimer()
      releaseStream()
      recorder = null
      state.value = 'idle'

      if (disposed) return

      const blob = new Blob(chunks, { type: mime.fileType })
      chunks = []

      if (blob.size === 0) {
        errorMessage.value = 'Yozuv bo‘sh chiqdi. Qayta urinib ko‘ring.'
        return
      }
      if (blob.size > options.maxBytes) {
        errorMessage.value = 'Yozuv juda katta chiqdi. Qisqaroq yozib ko‘ring.'
        return
      }

      const file = new File([blob], buildFileName(mime.extension, new Date()), {
        type: mime.fileType,
      })
      options.onRecorded({ file, durationSeconds })
    }

    active.onerror = () => {
      errorMessage.value = 'Yozish to‘xtab qoldi. Qayta urinib ko‘ring.'
      finish(false)
    }

    // Mikrofon uzib qo'yilsa (quloqchin chiqarildi) — bor yozuvni saqlaymiz.
    acquired.getAudioTracks().forEach((track) => {
      track.addEventListener('ended', () => finish(false))
    })

    active.start(TIMESLICE_MS)
    startedAt = performance.now()
    elapsedSeconds.value = 0
    state.value = 'recording'

    timer = setInterval(() => {
      elapsedSeconds.value = Math.floor(measure())
      if (measure() >= options.maxSeconds) finish(true)
    }, 250)
  }

  /** Yozuvni to'xtatadi; natija `onRecorded` orqali keladi. */
  function finish(auto: boolean): void {
    if (recorder === null || recorder.state === 'inactive') return
    autoStopped.value = auto
    clearTimer()
    recorder.stop()
  }

  function stop(): void {
    finish(false)
  }

  /** Natijani SAQLAMASDAN hammasini to'xtatadi (oyna yopildi). */
  function dispose(): void {
    disposed = true
    clearTimer()
    if (recorder !== null && recorder.state !== 'inactive') recorder.stop()
    recorder = null
    chunks = []
    releaseStream()
    state.value = 'idle'
  }

  function clearError(): void {
    errorMessage.value = null
  }

  onBeforeUnmount(dispose)

  return {
    state: readonly(state),
    elapsedSeconds: readonly(elapsedSeconds),
    errorMessage: readonly(errorMessage),
    autoStopped: readonly(autoStopped),
    start,
    stop,
    clearError,
  }
}

/** `95` -> `"1:35"`. */
export function formatDuration(totalSeconds: number): string {
  const seconds = Math.max(0, Math.floor(totalSeconds))
  return `${Math.floor(seconds / 60)}:${pad(seconds % 60)}`
}
