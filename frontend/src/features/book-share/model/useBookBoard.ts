import { computed, onBeforeUnmount, ref, shallowRef } from 'vue'
import type { ComputedRef, Ref, ShallowRef } from 'vue'

import { resolveBookUrl } from '@/entities/book'
import { toUserMessage } from '@/shared/api'
import type { BookDto } from '@/shared/types'

import { openPdfFromUrl, renderPdfPage } from './pdf'
import type { PdfDocument } from './pdf'

/**
 * ════════════════════════════════════════════════════════════════════════
 *  KITOB TAXTASI (2026-09-09) — PDF sahifa + chizma → bitta canvas
 * ════════════════════════════════════════════════════════════════════════
 *
 * NIMA UCHUN KERAK: telefondan dars o'tayotgan ustoz EKRAN ULASHA OLMAYDI
 * (mobil brauzerda `getDisplayMedia` yo'q) va o'quvchilarga kitobni
 * ko'rsatishga qiynalardi. Bu taxta kitob sahifasini ustozning O'Z
 * qurilmasida canvas'ga chizadi, ustiga qalam/marker bilan belgilashga
 * imkon beradi, canvas esa `useLiveKitRoom.shareCanvas` orqali EKRAN
 * ULASHUVI treki sifatida uzatiladi. O'quvchi tomonida hech narsa
 * o'zgarmaydi — u oddiy ekran ulashuvini ko'radi, yozuvga ham tushadi.
 *
 * ★ CANVAS DOM'DAN TASHQARIDA yaratiladi (`document.createElement`):
 *   panel yopilganda ham ulashuv davom etishi kerak — canvas komponent
 *   bilan birga yo'q bo'lib ketmasin. Panel uni ko'rsatish uchun o'z
 *   konteyneriga ULAYDI (`mountPreview`), yopilganda qaytarib oladi.
 *
 * ★ CHIZMALAR SAHIFA KOORDINATASIDA (0..1): kattalashtirish/surish
 *   o'zgarganda chizma sahifa bilan birga yuradi, "havoda" qolmaydi.
 *   Har sahifaning o'z chizmalari — varaqlab qaytganda ular joyida.
 *
 * ★ BITTA SAHIFA XOTIRADA: 2000px kenglikdagi rastr ~22 MB; 28 sahifani
 *   birdan saqlash telefonni cho'ktirardi. Varaqlashda oldingisi tashlanadi.
 */

/**
 * Uzatiladigan kadr o'lchami — TIK 3:4 (2026-09-09, telefon sinovi).
 *
 * Ilgari 4:3 landshaft edi: telefonni tik tutgan ustozda kadr ekran
 * O'RTASIDA kichkina chiziq bo'lib qolardi (390px kenglikda ~290px
 * balandlik), sahifa esa undan ham kichik. Kitob sahifasi o'zi tik
 * (A4), shuning uchun tik kadr:
 *   • ustoz telefonida sahifa butun kenglikni egallaydi;
 *   • o'quvchi tomonida hech narsa yo'qolmaydi — sahna `object-contain`
 *     bilan baribir balandlikka sig'dirardi, yon chiziqlar avval ham bor edi.
 * Yotiq telefonda kadr balandlikka sig'adi — kattalashtirish bilan ishlanadi.
 */
export const BOARD_WIDTH = 960
export const BOARD_HEIGHT = 1280

/** Sahifa rastri kengligi — zoom ×3 da ham matn aniq qolsin. */
const PAGE_RASTER_WIDTH = 2000

export const ZOOM_MIN = 1
export const ZOOM_MAX = 4
const ZOOM_STEP = 0.5

export type BoardTool = 'move' | 'pen' | 'highlighter'

/** Qalam ranglari: qizil, ko'k, qora, yashil, to'q sariq, binafsha — oq sahifada barchasi kontrastli. */
export const PEN_COLORS = ['#e11d48', '#2563eb', '#111827', '#16a34a', '#ea580c', '#7c3aed'] as const
export const HIGHLIGHT_COLOR = '#facc15'

export interface BoardPoint {
  x: number
  y: number
}

export interface BoardStroke {
  tool: Exclude<BoardTool, 'move'>
  color: string
  /** Sahifa kengligiga nisbatan chiziq qalinligi (0.004 ≈ 5px 1280 da). */
  width: number
  points: BoardPoint[]
}

export interface UseBookBoardResult {
  canvas: HTMLCanvasElement
  book: Ref<BookDto | null>
  pageNumber: Ref<number>
  pageCount: Ref<number>
  zoom: Ref<number>
  tool: Ref<BoardTool>
  color: Ref<string>
  loading: Ref<boolean>
  error: Ref<string | null>
  hasStrokes: ComputedRef<boolean>
  canUndo: ComputedRef<boolean>
  openBook: (book: BookDto) => Promise<void>
  closeBook: () => void
  goToPage: (page: number) => Promise<void>
  nextPage: () => Promise<void>
  prevPage: () => Promise<void>
  zoomIn: () => void
  zoomOut: () => void
  resetView: () => void
  /** Ko'rish maydonini canvas piksellarida suradi (move quroli). */
  panBy: (dx: number, dy: number) => void
  /** Pointer hodisalari — koordinata CANVAS piksellarida (`toCanvasPoint`). */
  strokeStart: (point: BoardPoint) => void
  strokeMove: (point: BoardPoint) => void
  strokeEnd: () => void
  undo: () => void
  clearPage: () => void
  /** Preview konteyneriga ulash/ajratish. */
  mountPreview: (host: HTMLElement) => void
  unmountPreview: () => void
  /** DOM elementidagi nuqtani canvas pikseliga o'giradi. */
  toCanvasPoint: (event: PointerEvent, element: HTMLElement) => BoardPoint
  dispose: () => void
}

function requireContext(canvas: HTMLCanvasElement): CanvasRenderingContext2D {
  const ctx = canvas.getContext('2d')
  if (ctx === null) throw new Error('Canvas 2D konteksti ochilmadi.')
  return ctx
}

export function useBookBoard(): UseBookBoardResult {
  const canvas = document.createElement('canvas')
  canvas.width = BOARD_WIDTH
  canvas.height = BOARD_HEIGHT
  canvas.className = 'block size-full object-contain touch-none select-none'
  const ctx = requireContext(canvas)

  const book = ref<BookDto | null>(null)
  const pageNumber = ref(1)
  const pageCount = ref(0)
  const zoom = ref(1)
  const tool = ref<BoardTool>('move')
  const color = ref<string>(PEN_COLORS[0])
  const loading = ref(false)
  const error = ref<string | null>(null)

  // Surish — canvas piksellarida, sahifa markazidan.
  let panX = 0
  let panY = 0

  const doc: ShallowRef<PdfDocument | null> = shallowRef(null)
  const pageRaster: ShallowRef<HTMLCanvasElement | null> = shallowRef(null)

  const strokesByPage = new Map<number, BoardStroke[]>()
  const strokesVersion = ref(0)
  let activeStroke: BoardStroke | null = null

  // Sahifa yuklash poygasi: ustoz tez-tez varaqlasa eski javob yangisini
  // bosib qo'ymasin.
  let renderToken = 0

  const hasStrokes = computed(() => {
    // `strokesVersion` — Map reaktiv emas, versiya raqami uni kuzatishga majbur qiladi.
    if (strokesVersion.value < 0) return false
    return (strokesByPage.get(pageNumber.value)?.length ?? 0) > 0
  })
  const canUndo = hasStrokes

  /* ------------------------------------------------------------ chizish */

  interface Layout {
    originX: number
    originY: number
    drawW: number
    drawH: number
  }

  function layout(): Layout | null {
    const raster = pageRaster.value
    if (raster === null) return null
    const fit = Math.min(BOARD_WIDTH / raster.width, BOARD_HEIGHT / raster.height)
    const scale = fit * zoom.value
    const drawW = raster.width * scale
    const drawH = raster.height * scale
    return {
      originX: (BOARD_WIDTH - drawW) / 2 + panX,
      originY: (BOARD_HEIGHT - drawH) / 2 + panY,
      drawW,
      drawH,
    }
  }

  function draw(): void {
    // Fon — to'q, sahifa esa oq: o'quvchi ekranida qog'oz ajralib tursin.
    ctx.setTransform(1, 0, 0, 1, 0, 0)
    ctx.fillStyle = '#1b1d2a'
    ctx.fillRect(0, 0, BOARD_WIDTH, BOARD_HEIGHT)

    const raster = pageRaster.value
    const box = layout()
    if (raster === null || box === null) {
      ctx.fillStyle = '#94a3b8'
      ctx.font = '600 36px system-ui, sans-serif'
      ctx.textAlign = 'center'
      ctx.fillText(
        loading.value ? 'Sahifa yuklanmoqda…' : 'Kitob tanlanmagan',
        BOARD_WIDTH / 2,
        BOARD_HEIGHT / 2,
      )
      return
    }

    ctx.drawImage(raster, box.originX, box.originY, box.drawW, box.drawH)

    const strokes = strokesByPage.get(pageNumber.value) ?? []
    for (const stroke of strokes) drawStroke(stroke, box)
    if (activeStroke !== null) drawStroke(activeStroke, box)
  }

  function drawStroke(stroke: BoardStroke, box: Layout): void {
    if (stroke.points.length === 0) return
    ctx.save()
    ctx.lineCap = 'round'
    ctx.lineJoin = 'round'
    ctx.strokeStyle = stroke.color
    ctx.lineWidth = Math.max(1.5, stroke.width * box.drawW)
    // Marker yarim shaffof — ostidagi matn o'qiladi (`multiply` matnni
    // "qoraytirmaydi", sariq ustida qora harf qoladi).
    ctx.globalAlpha = stroke.tool === 'highlighter' ? 0.4 : 1
    ctx.globalCompositeOperation = stroke.tool === 'highlighter' ? 'multiply' : 'source-over'
    ctx.beginPath()
    const first = stroke.points[0]
    if (first === undefined) {
      ctx.restore()
      return
    }
    ctx.moveTo(box.originX + first.x * box.drawW, box.originY + first.y * box.drawH)
    if (stroke.points.length === 1) {
      // Bitta nuqta — nuqta chizamiz (bosib qo'yib yuborish ham belgi).
      ctx.lineTo(box.originX + first.x * box.drawW + 0.1, box.originY + first.y * box.drawH)
    }
    for (let i = 1; i < stroke.points.length; i++) {
      const p = stroke.points[i]
      if (p === undefined) continue
      ctx.lineTo(box.originX + p.x * box.drawW, box.originY + p.y * box.drawH)
    }
    ctx.stroke()
    ctx.restore()
  }

  let frame: number | null = null
  function scheduleDraw(): void {
    if (frame !== null) return
    frame = requestAnimationFrame(() => {
      frame = null
      draw()
    })
  }

  /*
    Ulashuv paytida kadr DOIM yangilanib turadi (2 fps): `captureStream`
    faqat canvas O'ZGARGANDA kadr beradi; tomoshabin kech ulansa yoki
    kodek kalit kadr so'rasa, o'zgarmagan canvas hech narsa yubormasdi va
    o'quvchida sahna BO'SH qolardi. Ikki kadr/soniya — statik sahifa
    uchun arzon, LiveKit uni deyarli baytlarsiz siqadi.
  */
  const heartbeat = window.setInterval(draw, 500)

  /* ------------------------------------------------------------- kitob */

  async function openBook(next: BookDto): Promise<void> {
    closeBook()
    book.value = next
    loading.value = true
    error.value = null
    const token = ++renderToken
    try {
      const url = await resolveBookUrl(next.id)
      const opened = await openPdfFromUrl(url)
      if (token !== renderToken) {
        await opened.destroy()
        return
      }
      doc.value = opened
      pageCount.value = opened.numPages
      pageNumber.value = 1
      await loadPage(1, token)
    } catch (err) {
      if (token !== renderToken) return
      error.value = toUserMessage(err)
      loading.value = false
      scheduleDraw()
    }
  }

  function closeBook(): void {
    renderToken++
    const opened = doc.value
    doc.value = null
    if (opened !== null) void opened.destroy().catch(() => undefined)
    pageRaster.value = null
    book.value = null
    pageCount.value = 0
    pageNumber.value = 1
    strokesByPage.clear()
    strokesVersion.value++
    activeStroke = null
    loading.value = false
    error.value = null
    zoom.value = 1
    panX = 0
    panY = 0
    scheduleDraw()
  }

  async function loadPage(page: number, token: number): Promise<void> {
    const opened = doc.value
    if (opened === null) return
    loading.value = true
    scheduleDraw()
    try {
      const raster = await renderPdfPage(opened, page, PAGE_RASTER_WIDTH)
      if (token !== renderToken) return
      pageRaster.value = raster
      pageNumber.value = page
      panX = 0
      panY = 0
      error.value = null
    } catch (err) {
      if (token !== renderToken) return
      error.value = toUserMessage(err)
    } finally {
      if (token === renderToken) {
        loading.value = false
        scheduleDraw()
      }
    }
  }

  async function goToPage(page: number): Promise<void> {
    if (doc.value === null) return
    const target = Math.min(Math.max(1, Math.round(page)), pageCount.value)
    if (target === pageNumber.value && pageRaster.value !== null) return
    activeStroke = null
    await loadPage(target, ++renderToken)
  }

  const nextPage = (): Promise<void> => goToPage(pageNumber.value + 1)
  const prevPage = (): Promise<void> => goToPage(pageNumber.value - 1)

  /* ------------------------------------------------------- zoom / pan */

  function clampPan(): void {
    const box = layout()
    if (box === null) {
      panX = 0
      panY = 0
      return
    }
    // Sahifa ekrandan butunlay chiqib ketmasin — chekkasi kamida 1/4
    // qismda ko'rinib tursin, aks holda ustoz "qayerdaman?" deb adashardi.
    const limitX = Math.max(0, (box.drawW - BOARD_WIDTH) / 2) + BOARD_WIDTH / 4
    const limitY = Math.max(0, (box.drawH - BOARD_HEIGHT) / 2) + BOARD_HEIGHT / 4
    panX = Math.min(limitX, Math.max(-limitX, panX))
    panY = Math.min(limitY, Math.max(-limitY, panY))
  }

  function setZoom(next: number): void {
    const clamped = Math.min(ZOOM_MAX, Math.max(ZOOM_MIN, next))
    if (clamped === zoom.value) return
    // Markaz saqlanadi: surish nisbatan o'lchanadi.
    const ratio = clamped / zoom.value
    panX *= ratio
    panY *= ratio
    zoom.value = clamped
    clampPan()
    scheduleDraw()
  }

  const zoomIn = (): void => setZoom(zoom.value + ZOOM_STEP)
  const zoomOut = (): void => setZoom(zoom.value - ZOOM_STEP)

  function resetView(): void {
    zoom.value = 1
    panX = 0
    panY = 0
    scheduleDraw()
  }

  function panBy(dx: number, dy: number): void {
    panX += dx
    panY += dy
    clampPan()
    scheduleDraw()
  }

  /* ----------------------------------------------------------- chizma */

  function toPagePoint(point: BoardPoint): BoardPoint | null {
    const box = layout()
    if (box === null) return null
    return {
      x: (point.x - box.originX) / box.drawW,
      y: (point.y - box.originY) / box.drawH,
    }
  }

  function strokeStart(point: BoardPoint): void {
    if (tool.value === 'move') return
    const p = toPagePoint(point)
    if (p === null) return
    activeStroke = {
      tool: tool.value,
      color: tool.value === 'highlighter' ? HIGHLIGHT_COLOR : color.value,
      width: tool.value === 'highlighter' ? 0.022 : 0.004,
      points: [p],
    }
    scheduleDraw()
  }

  function strokeMove(point: BoardPoint): void {
    if (activeStroke === null) return
    const p = toPagePoint(point)
    if (p === null) return
    const last = activeStroke.points[activeStroke.points.length - 1]
    // Juda zich nuqtalar chiziqni og'irlashtiradi — 1/500 sahifadan yaqinlari tashlanadi.
    if (last !== undefined && Math.hypot(p.x - last.x, p.y - last.y) < 0.002) return
    activeStroke.points.push(p)
    scheduleDraw()
  }

  function strokeEnd(): void {
    const stroke = activeStroke
    activeStroke = null
    if (stroke === null || stroke.points.length === 0) return
    const list = strokesByPage.get(pageNumber.value) ?? []
    list.push(stroke)
    strokesByPage.set(pageNumber.value, list)
    strokesVersion.value++
    scheduleDraw()
  }

  function undo(): void {
    const list = strokesByPage.get(pageNumber.value)
    if (list === undefined || list.length === 0) return
    list.pop()
    strokesVersion.value++
    scheduleDraw()
  }

  function clearPage(): void {
    strokesByPage.delete(pageNumber.value)
    strokesVersion.value++
    scheduleDraw()
  }

  /* ---------------------------------------------------------- preview */

  function mountPreview(host: HTMLElement): void {
    if (canvas.parentElement !== host) host.appendChild(canvas)
    scheduleDraw()
  }

  function unmountPreview(): void {
    canvas.parentElement?.removeChild(canvas)
  }

  function toCanvasPoint(event: PointerEvent, element: HTMLElement): BoardPoint {
    // Canvas `object-contain` bilan ko'rsatiladi — element ichida
    // harflashuv (letterbox) bo'lishi mumkin; haqiqiy chizilgan
    // to'rtburchakni hisoblaymiz.
    const rect = element.getBoundingClientRect()
    const scale = Math.min(rect.width / BOARD_WIDTH, rect.height / BOARD_HEIGHT)
    const shownW = BOARD_WIDTH * scale
    const shownH = BOARD_HEIGHT * scale
    const offsetX = rect.left + (rect.width - shownW) / 2
    const offsetY = rect.top + (rect.height - shownH) / 2
    return {
      x: (event.clientX - offsetX) / scale,
      y: (event.clientY - offsetY) / scale,
    }
  }

  function dispose(): void {
    window.clearInterval(heartbeat)
    if (frame !== null) cancelAnimationFrame(frame)
    unmountPreview()
    closeBook()
  }

  onBeforeUnmount(dispose)

  draw()

  return {
    canvas,
    book,
    pageNumber,
    pageCount,
    zoom,
    tool,
    color,
    loading,
    error,
    hasStrokes,
    canUndo,
    openBook,
    closeBook,
    goToPage,
    nextPage,
    prevPage,
    zoomIn,
    zoomOut,
    resetView,
    panBy,
    strokeStart,
    strokeMove,
    strokeEnd,
    undo,
    clearPage,
    mountPreview,
    unmountPreview,
    toCanvasPoint,
    dispose,
  }
}
