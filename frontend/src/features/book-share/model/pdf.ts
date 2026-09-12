import * as pdfjs from 'pdfjs-dist'
import type { PDFDocumentProxy } from 'pdfjs-dist'
import workerUrl from 'pdfjs-dist/build/pdf.worker.min.mjs?url'

/**
 * PDF.JS O'ROVI (2026-09-09).
 *
 * ★ WORKER — ALOHIDA FAYL (`?url`): pdf.js sahifani ishchi oqimda
 *   ochadi, aks holda 3 MB lik kitob asosiy oqimni bir soniyaga qotirardi
 *   va jonli darsdagi video "tishlab" qolardi. Vite worker faylini
 *   `dist/assets` ga o'zi ko'chiradi.
 *
 * ★ `disableRange` + `disableStream`: fayl BIR MARTA butunligicha
 *   yuklanadi. Sabab — kirish `?ticket=` bilan va chipta 15 daqiqa
 *   yashaydi; bo'lak-bo'lak (Range) so'rov 16-daqiqada 401 olardi va
 *   ustoz sahifa varaqlaganda "yuklanmadi" ko'rardi. Darslik 2–5 MB —
 *   butunlay olish arzon.
 */
/*
  ★ `?v=2` — KESH BUZUVCHI. Birinchi chiqarilishda nginx `.mjs` ni
  `application/octet-stream` bilan bergan va brauzer bu javobni
  `Cache-Control: immutable` (1 yil) bilan saqlab qolgan; fayl nomi
  mazmun xeshi bo'lgani uchun MIME tuzatilgach ham nom o'zgarmadi va
  brauzer keshdagi buzuq javobni ishlataverdi ("Setting up fake worker
  failed"). So'rov parametri manzilni o'zgartiradi — kesh chetlab o'tiladi.
  Worker mazmuni yana o'zgarsa Vite xeshni o'zi almashtiradi; bu raqam
  faqat shu bitta hodisa uchun.
*/
pdfjs.GlobalWorkerOptions.workerSrc = `${workerUrl}?v=2`

export type PdfDocument = PDFDocumentProxy

export function openPdfFromUrl(url: string): Promise<PdfDocument> {
  return pdfjs.getDocument({ url, disableRange: true, disableStream: true }).promise
}

/** Yuklashdan OLDIN sahifa sonini aniqlash (kutubxona sahifasi). */
export async function countPdfPages(file: File): Promise<number> {
  const data = await file.arrayBuffer()
  const doc = await pdfjs.getDocument({ data }).promise
  try {
    return doc.numPages
  } finally {
    await doc.destroy()
  }
}

/**
 * Sahifani `targetWidth` piksel kenglikda rastrga chizadi.
 *
 * Kenglik ATAYLAB katta (2000px): ustoz kattalashtirganda (zoom ×3)
 * matn xira bo'lmasin. Bir sahifa ~2000×2800 RGBA = 22 MB xotira —
 * faqat JORIY sahifa saqlanadi (`useBookBoard`), oldingisi tashlanadi.
 */
export async function renderPdfPage(
  doc: PdfDocument,
  pageNumber: number,
  targetWidth: number,
): Promise<HTMLCanvasElement> {
  const page = await doc.getPage(pageNumber)
  try {
    const base = page.getViewport({ scale: 1 })
    const viewport = page.getViewport({ scale: targetWidth / base.width })

    const canvas = document.createElement('canvas')
    canvas.width = Math.ceil(viewport.width)
    canvas.height = Math.ceil(viewport.height)

    const context = canvas.getContext('2d')
    if (context === null) throw new Error('Canvas 2D konteksti ochilmadi.')

    await page.render({ canvasContext: context, viewport, canvas }).promise
    return canvas
  } finally {
    page.cleanup()
  }
}
