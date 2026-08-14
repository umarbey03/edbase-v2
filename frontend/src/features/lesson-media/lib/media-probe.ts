/**
 * MEDIA METAMA'LUMOTINI BRAUZERDA O'QISH (davomiylik, o'lcham).
 *
 * ★ NEGA KLIENTDA: serverda media dekoder YO'Q — `durationSec`, `width` va
 * `height` AYNAN shu yerdan keladi. Shu sababli ular FAQAT KO'RSATISH uchun
 * va ularga hech qanday qaror bog'lanmaydi: qiymat noto'g'ri bo'lsa ham eng
 * yomon oqibat — ro'yxatda xato davomiylik ko'rinishi.
 *
 * 🔴 SHUNING UCHUN BU FUNKSIYA HECH QACHON XATO TASHLAMAYDI. Brauzer faylni
 * dekodlay olmasa (masalan HEIC Chrome'da) yoki metama'lumot kelmasa —
 * `null` qaytadi va yuklash DAVOM ETADI. Aks holda "surat qo'shib bo'lmadi"
 * degan tushunarsiz to'siq paydo bo'lardi, holbuki server faylni bemalol
 * qabul qiladi.
 */

export interface MediaMetadata {
  /** Sekund (butun). `null` — o'qib bo'lmadi. */
  durationSec: number | null
  width: number | null
  height: number | null
}

const EMPTY: MediaMetadata = { durationSec: null, width: null, height: null }

/**
 * Metama'lumot kutish muddati.
 *
 * `loadedmetadata` KELMASLIGI mumkin (kodek qo'llanmasa hodisa umuman
 * bo'lmaydi, `error` ham har brauzerda bir xil emas) — kutish cheksiz bo'lsa
 * yuklash navbati JIMGINA to'xtab qolardi.
 */
const PROBE_TIMEOUT_MS = 4000

function round(value: number): number | null {
  return Number.isFinite(value) && value > 0 ? Math.round(value) : null
}

/** Video: davomiylik + piksel o'lchami. */
function probeVideo(url: string): Promise<MediaMetadata> {
  return new Promise<MediaMetadata>((resolve) => {
    const element = document.createElement('video')
    element.preload = 'metadata'
    // Ovoz CHIQMASIN: ba'zi brauzerlar `preload` paytida ham dekodlashni
    // boshlaydi va tanlangan videoning bir lahzasi eshitilib qolardi.
    element.muted = true

    const timer = window.setTimeout(() => finish(EMPTY), PROBE_TIMEOUT_MS)

    function finish(result: MediaMetadata): void {
      window.clearTimeout(timer)
      element.removeAttribute('src')
      resolve(result)
    }

    element.onloadedmetadata = (): void =>
      finish({
        durationSec: round(element.duration),
        width: round(element.videoWidth),
        height: round(element.videoHeight),
      })
    element.onerror = (): void => finish(EMPTY)

    element.src = url
  })
}

/** Audio: faqat davomiylik (o'lcham ma'nosiz). */
function probeAudio(url: string): Promise<MediaMetadata> {
  return new Promise<MediaMetadata>((resolve) => {
    const element = document.createElement('audio')
    element.preload = 'metadata'

    const timer = window.setTimeout(() => finish(EMPTY), PROBE_TIMEOUT_MS)

    function finish(result: MediaMetadata): void {
      window.clearTimeout(timer)
      element.removeAttribute('src')
      resolve(result)
    }

    element.onloadedmetadata = (): void =>
      finish({ durationSec: round(element.duration), width: null, height: null })
    element.onerror = (): void => finish(EMPTY)

    element.src = url
  })
}

/** Rasm: piksel o'lchami (imtihon galereyasi joyni oldindan hisoblaydi). */
function probeImage(url: string): Promise<MediaMetadata> {
  return new Promise<MediaMetadata>((resolve) => {
    const element = new Image()
    const timer = window.setTimeout(() => resolve(EMPTY), PROBE_TIMEOUT_MS)

    element.onload = (): void => {
      window.clearTimeout(timer)
      resolve({
        durationSec: null,
        width: round(element.naturalWidth),
        height: round(element.naturalHeight),
      })
    }
    element.onerror = (): void => {
      window.clearTimeout(timer)
      resolve(EMPTY)
    }

    element.src = url
  })
}

/**
 * Faylning metama'lumotini o'qiydi.
 *
 * ★ `URL.revokeObjectURL` MAJBURIY: `createObjectURL` Blob'ni brauzerda
 * ushlab turadi, ya'ni tozalanmasa 1 GB video xotirada qolib ketardi
 * (`SubmissionAttachment.vue` dagi ayni qoida).
 */
export async function probeMedia(
  file: File,
  kind: 'Video' | 'Image' | 'Audio',
): Promise<MediaMetadata> {
  const url = URL.createObjectURL(file)
  try {
    if (kind === 'Video') return await probeVideo(url)
    if (kind === 'Audio') return await probeAudio(url)
    return await probeImage(url)
  } catch {
    return EMPTY
  } finally {
    URL.revokeObjectURL(url)
  }
}

/**
 * Vazifa biriktirmasi uchun turkumni MIME'dan taxmin qiladi — faqat
 * metama'lumotni qaysi element bilan o'qishni tanlash uchun.
 *
 * ⚠️ Bu YAKUNIY tur EMAS: serverda tur fayl MAZMUNIDAN aniqlanadi va u yerda
 * `ftyp` konteyneri AUDIO deb qabul qilinadi (13-bo'lim, 46-tuzoq).
 */
export function probeKindForAttachment(file: File): 'Video' | 'Image' | 'Audio' {
  if (file.type.startsWith('audio/')) return 'Audio'
  if (file.type.startsWith('video/')) return 'Video'
  return 'Image'
}
