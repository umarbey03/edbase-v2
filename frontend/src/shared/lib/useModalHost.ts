import { nextTick, onScopeDispose, toValue, watch } from 'vue'
import type { MaybeRefOrGetter, Ref } from 'vue'

/**
 * QATLAMLI OYNA MEXANIZMI — modal/drawer uchun umumiy "ichki mexanika".
 *
 * Nima beradi:
 *   1. `body` skroll qulfi — SANOQ (counter) bilan;
 *   2. fokusni eslab qolish va yopilgach QAYTARISH;
 *   3. fokus tuzog'i (Tab halqasi panel ichida qoladi);
 *   4. ESC — FAQAT eng ustidagi qatlamni yopadi;
 *   5. ichma-ich drawer ochilishini dev'da ushlash.
 *
 * ★ NEGA SANOQ KERAK (haqiqiy xato ssenariysi):
 * drawer ochiq turganda uning ichida tasdiqlash oynasi ochilib yopilsa, oyna
 * `body.style.overflow` ni "tiklab" drawer ostidagi sahifani skrollga qo'yib
 * yuborardi — foydalanuvchi panelni surganda orqadagi jadval ham surilib
 * ketardi. Sanoq bilan qulf FAQAT oxirgi qatlam yopilganda ochiladi va
 * `overflow` ning ENG BOSHIDAGI qiymati (odatda bo'sh satr) qaytariladi.
 *
 * ★ NEGA ESC UCHUN QATLAM STEKI KERAK: har oyna `document` ga o'z
 * `keydown` ishlovchisini qo'ysa, ESC bosilganda IKKALASI ham yopiladi
 * (`stopPropagation` bir xil elementdagi boshqa ishlovchini to'xtatmaydi).
 * Shu yerda bitta umumiy ishlovchi bor va u faqat stekning tepasidagi
 * qatlamga murojaat qiladi.
 *
 * ✅ QATLAM QO'YADIGAN HAMMA KOMPONENT SHU YERDAN O'TADI (2026-08-11 dan):
 * `BaseDrawer` (`kind: 'drawer'`), `ConfirmDialog` va `BaseModal`
 * (`kind: 'dialog'`). Ya'ni skroll qulfi, ESC va fokus mantig'ining
 * NUSXASI KODDA QOLMADI.
 *
 * 🔴 YANGI OYNA YOZSANGIZ — `body.style.overflow` ga, `document`ga
 * `keydown` ishlovchisiga va `panel.focus()` ga QO'L URMANG, shu
 * composable'ni chaqiring. Nusxa mantiq aynan uchta xatoni qaytaradi
 * (`BaseModal` da shu uchtasi bor edi): sanoqsiz qulf ostidagi sahifani
 * skrollga qo'yib yuboradi, o'z ESC ishlovchisi qo'shni qatlamni ham
 * yopadi, fokus esa formaning birinchi maydoniga tushmaydi.
 */

type LayerKind = 'dialog' | 'drawer'

export interface ModalHostOptions {
  /** Qatlam ochiqmi. */
  open: MaybeRefOrGetter<boolean>
  /** Yopish so'rovi (ESC yoki tashqi mantiq) — komponent `close` emit qiladi. */
  onClose: () => void
  /** Panel elementi: fokus tuzog'i va boshlang'ich fokus shu ichida ishlaydi. */
  panel: Readonly<Ref<HTMLElement | null>>
  /**
   * Qatlam turi. `drawer` — ekranni egallovchi yon panel; ikkinchisi ochilsa
   * dev'da ogohlantirish chiqadi (ichma-ich drawer TAQIQLANGAN).
   */
  kind?: LayerKind
  /** ESC bilan yopilsinmi. Saqlanmagan forma uchun `false` berish mumkin. */
  closeOnEscape?: boolean
  /**
   * Ochilganda fokus qaratiladigan element SELEKTORI (panel ichida
   * qidiriladi). Topilmasa panelning o'zi fokus oladi.
   *
   * Standart — `MODAL_AUTOFOCUS_CLASS`: kerakli tugmaga shu klassni qo'shish
   * yetadi, selektorni uzatish shart emas.
   *
   * NEGA SELEKTOR, nega `ref` emas: fokus kerak bo'ladigan element odatda
   * `BaseButton`, ya'ni KOMPONENT — uning `ref` i DOM elementi emas, `$el`
   * orqali olish esa tur xavfsizligini yo'qotadi.
   *
   * NEGA `data-*` ATRIBUT emas, KLASS: `strictTemplates: true` da komponentga
   * e'lon qilinmagan atribut berish tur xatosi beradi (`data-autofocus`
   * `BaseButton` prop'i emas), `class` esa har komponentda ruxsat etilgan.
   */
  initialFocusSelector?: string
}

/**
 * Oyna ochilganda fokus oladigan elementni belgilaydigan klass.
 * Ko'rinishga ta'sir qilmaydi — faqat "ilgak".
 */
export const MODAL_AUTOFOCUS_CLASS = 'js-modal-autofocus'

interface Layer {
  id: symbol
  kind: LayerKind
  panel: Readonly<Ref<HTMLElement | null>>
  close: () => void
  closeOnEscape: boolean
}

/* ------------------------------ skroll qulfi ------------------------------ */

let scrollLockDepth = 0
let savedBodyOverflow: string | null = null

function acquireScrollLock(): void {
  if (scrollLockDepth === 0) {
    savedBodyOverflow = document.body.style.overflow
    document.body.style.overflow = 'hidden'
  }
  scrollLockDepth += 1
}

function releaseScrollLock(): void {
  scrollLockDepth = Math.max(0, scrollLockDepth - 1)
  if (scrollLockDepth === 0 && savedBodyOverflow !== null) {
    document.body.style.overflow = savedBodyOverflow
    savedBodyOverflow = null
  }
}

/* ------------------------------ qatlam steki ------------------------------ */

const layers: Layer[] = []
let keydownAttached = false

function topLayer(): Layer | undefined {
  return layers[layers.length - 1]
}

/**
 * Ochiq qatlamlar soni — tashxis va qo'lda tekshiruv uchun (brauzer
 * konsolida `body.style.overflow` bilan solishtirish qulay).
 */
export function openLayerCount(): number {
  return layers.length
}

/*
  Fokus oladigan elementlar. `[tabindex="-1"]` chetda: panelning o'zi shunday
  (u programma orqali fokus oladi, lekin Tab halqasiga kirmaydi).
*/
const FOCUSABLE_SELECTOR = [
  'a[href]',
  'button:not([disabled])',
  'input:not([disabled]):not([type="hidden"])',
  'select:not([disabled])',
  'textarea:not([disabled])',
  'summary',
  'audio[controls]',
  'video[controls]',
  '[contenteditable="true"]',
  '[tabindex]:not([tabindex="-1"])',
].join(',')

function focusableInside(root: HTMLElement): HTMLElement[] {
  return Array.from(root.querySelectorAll<HTMLElement>(FOCUSABLE_SELECTOR)).filter(
    (element) =>
      element.tabIndex !== -1 &&
      // Yashirin element (`display:none`, yopilgan bo'lim) halqaga kirmasin.
      (element.offsetWidth > 0 || element.offsetHeight > 0 || element.getClientRects().length > 0),
  )
}

function trapTab(event: KeyboardEvent, layer: Layer): void {
  const panel = layer.panel.value
  if (panel === null) return

  const items = focusableInside(panel)
  const first = items[0]
  const last = items[items.length - 1]

  // Ichida fokus oladigan element bo'lmasa — fokus paneldan chiqmasin.
  if (first === undefined || last === undefined) {
    event.preventDefault()
    panel.focus()
    return
  }

  const active = document.activeElement
  const outside = !panel.contains(active)

  if (event.shiftKey) {
    if (outside || active === first || active === panel) {
      event.preventDefault()
      last.focus()
    }
    return
  }

  if (outside || active === last || active === panel) {
    event.preventDefault()
    first.focus()
  }
}

function handleKeydown(event: KeyboardEvent): void {
  const layer = topLayer()
  if (layer === undefined) return

  if (event.key === 'Escape') {
    if (!layer.closeOnEscape) return
    // `stopPropagation` — ostidagi sahifadagi ESC ishlovchilari (masalan
    // qidiruv maydonini tozalash) ishga tushmasin.
    event.preventDefault()
    event.stopPropagation()
    layer.close()
    return
  }

  if (event.key === 'Tab') trapTab(event, layer)
}

function attachKeydown(): void {
  if (keydownAttached) return
  document.addEventListener('keydown', handleKeydown)
  keydownAttached = true
}

function detachKeydown(): void {
  if (!keydownAttached || layers.length > 0) return
  document.removeEventListener('keydown', handleKeydown)
  keydownAttached = false
}

/* -------------------------------- composable ------------------------------ */

export function useModalHost(options: ModalHostOptions): void {
  const kind: LayerKind = options.kind ?? 'dialog'
  const layer: Layer = {
    id: Symbol('modal-layer'),
    kind,
    panel: options.panel,
    close: options.onClose,
    closeOnEscape: options.closeOnEscape ?? true,
  }

  let previouslyFocused: HTMLElement | null = null
  let isOpen = false

  function mountLayer(): void {
    if (isOpen) return
    isOpen = true

    if (kind === 'drawer' && layers.some((item) => item.kind === 'drawer') && import.meta.env.DEV) {
      console.warn(
        '[useModalHost] Ichma-ich drawer ochildi. Bu TAQIQLANGAN: ikki qatlam ' +
          '85% panel foydalanuvchini yo‘qotadi (qaysi panel "orqada" ekani ' +
          'ko‘rinmaydi). Ichki oqim uchun `ConfirmDialog` yoki `BaseModal` ishlating.',
      )
    }

    previouslyFocused = document.activeElement instanceof HTMLElement ? document.activeElement : null
    acquireScrollLock()
    layers.push(layer)
    attachKeydown()

    void nextTick(() => {
      const panel = options.panel.value
      if (panel === null) return
      const selector = options.initialFocusSelector ?? `.${MODAL_AUTOFOCUS_CLASS}`
      const target = panel.querySelector<HTMLElement>(selector)
      ;(target ?? panel).focus()
    })
  }

  function unmountLayer(): void {
    if (!isOpen) return
    isOpen = false

    const index = layers.indexOf(layer)
    if (index !== -1) layers.splice(index, 1)
    detachKeydown()
    releaseScrollLock()

    /*
      Fokusni qaytarish: oyna yopilgach foydalanuvchi klaviaturada aynan
      o'zi bosgan tugmadan davom etsin. Element sahifadan olib tashlangan
      bo'lsa (`isConnected === false`) — brauzer fokusni `body` ga qo'yadi,
      biz esa uni majburlab tortmaymiz.
    */
    if (previouslyFocused !== null && previouslyFocused.isConnected) previouslyFocused.focus()
    previouslyFocused = null
  }

  watch(
    () => toValue(options.open),
    (open) => {
      if (open) mountLayer()
      else unmountLayer()
    },
    { immediate: true },
  )

  // Komponent OCHIQ holatda yo'q qilinsa (sahifa almashsa) — tozalaymiz,
  // aks holda `body` skrolli abadiy qulflangan qoladi.
  onScopeDispose(() => {
    unmountLayer()
  })
}
