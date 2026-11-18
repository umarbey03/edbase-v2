/**
 * ════════════════════════════════════════════════════════════════════════
 *  OVOZ USTIGA OVOZ — YAKUNIY FAYLNI YIG'ISH (2026-11-18)
 * ════════════════════════════════════════════════════════════════════════
 *
 * Kurator o'quvchi ovozini eshitib boradi, kerakli joyda to'xtatib izoh
 * yozadi va davom ettiradi. Natija — BITTA ovoz fayli:
 *
 *   o'quvchi[0 → t1] · izoh1 · o'quvchi[t1 → t2] · izoh2 · … · o'quvchi[tn → oxir]
 *
 * ★ BRAUZERDA, SERVERSIZ: ffmpeg kerak emas, server faylni oddiy ovozli
 *   xabar sifatida qabul qiladi (WAV `RIFF` sarlavhasi bilan taniladi).
 * ★ WAV 16 kHz MONO: nutq uchun yetarli, har brauzer (iOS ham) ijro etadi.
 *   Hajm ~32 KB/s, ya'ni 4 daqiqalik izoh ~7.5 MB — chat chegarasi ichida.
 * ★ IZOH ATROFIDA QISQA PAUZA: o'quvchi ovozidan kurator ovoziga sakrash
 *   keskin eshitilmasin, izoh boshlangani sezilsin.
 */

export interface ReviewComment {
  /** O'quvchi ovozining qaysi soniyasida to'xtatilgan. */
  at: number
  blob: Blob
  durationSeconds: number
}

export interface RenderedReview {
  file: File
  durationSeconds: number
}

const SAMPLE_RATE = 16_000
const GAP_SECONDS = 0.35

/** Har qanday ovoz konteynerini (webm/ogg/m4a/wav) PCM'ga. */
async function decode(blob: Blob): Promise<AudioBuffer> {
  const bytes = await blob.arrayBuffer()
  // Dekodlash uchun vaqtinchalik kontekst; `OfflineAudioContext` ning o'zi ham yetadi.
  const context = new OfflineAudioContext(1, 1, SAMPLE_RATE)
  return context.decodeAudioData(bytes)
}

export async function renderVoiceReview(
  source: Blob,
  comments: readonly ReviewComment[],
): Promise<RenderedReview> {
  const student = await decode(source)
  const sorted = [...comments].sort((a, b) => a.at - b.at)
  const decoded = await Promise.all(sorted.map((c) => decode(c.blob)))

  // Ijro rejasi: [manba, boshlanish, davomiylik] bo'laklari ketma-ket.
  interface Piece { buffer: AudioBuffer, offset: number, duration: number, gapAfter: number }
  const pieces: Piece[] = []
  let cursor = 0

  sorted.forEach((comment, index) => {
    const at = Math.min(Math.max(comment.at, cursor), student.duration)
    if (at > cursor) pieces.push({ buffer: student, offset: cursor, duration: at - cursor, gapAfter: GAP_SECONDS })
    const voice = decoded[index]!
    pieces.push({ buffer: voice, offset: 0, duration: voice.duration, gapAfter: GAP_SECONDS })
    cursor = at
  })
  if (student.duration > cursor) {
    pieces.push({ buffer: student, offset: cursor, duration: student.duration - cursor, gapAfter: 0 })
  }

  const total = pieces.reduce((sum, p) => sum + p.duration + p.gapAfter, 0)
  const context = new OfflineAudioContext(1, Math.max(1, Math.ceil(total * SAMPLE_RATE)), SAMPLE_RATE)

  let when = 0
  for (const piece of pieces) {
    const node = context.createBufferSource()
    node.buffer = piece.buffer
    node.connect(context.destination)
    node.start(when, piece.offset, piece.duration)
    when += piece.duration + piece.gapAfter
  }

  const mixed = await context.startRendering()
  const wav = encodeWav(mixed.getChannelData(0), SAMPLE_RATE)
  const stamp = new Date().toISOString().slice(0, 16).replace(/[-:T]/g, '')
  return {
    file: new File([wav], `izoh-${stamp}.wav`, { type: 'audio/wav' }),
    durationSeconds: Math.round(total),
  }
}

/** 16-bit PCM WAV (RIFF) — eng oddiy, hamma joyda ijro etiladigan format. */
function encodeWav(samples: Float32Array, sampleRate: number): Blob {
  const buffer = new ArrayBuffer(44 + samples.length * 2)
  const view = new DataView(buffer)
  const text = (offset: number, value: string): void => {
    for (let i = 0; i < value.length; i++) view.setUint8(offset + i, value.charCodeAt(i))
  }

  text(0, 'RIFF')
  view.setUint32(4, 36 + samples.length * 2, true)
  text(8, 'WAVE')
  text(12, 'fmt ')
  view.setUint32(16, 16, true)
  view.setUint16(20, 1, true) // PCM
  view.setUint16(22, 1, true) // mono
  view.setUint32(24, sampleRate, true)
  view.setUint32(28, sampleRate * 2, true)
  view.setUint16(32, 2, true)
  view.setUint16(34, 16, true)
  text(36, 'data')
  view.setUint32(40, samples.length * 2, true)

  let offset = 44
  for (const sample of samples) {
    const clamped = Math.max(-1, Math.min(1, sample))
    view.setInt16(offset, clamped < 0 ? clamped * 0x8000 : clamped * 0x7fff, true)
    offset += 2
  }
  return new Blob([buffer], { type: 'audio/wav' })
}
