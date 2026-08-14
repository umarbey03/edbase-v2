import { onBeforeUnmount, onMounted, ref, toValue } from 'vue'
import type { MaybeRefOrGetter, Ref } from 'vue'

/**
 * "EKRANNING QOLGAN QISMI" — element balandligini O'LCHAB beradi.
 *
 * ★ NEGA COMPOSABLE (2026-08-13, R28): hisob ilgari `ChatFillColumn` ning
 * ICHIDA edi va faqat SUHBAT ustuniga yarardi. Ustoz "Chatlar" hubi ikki
 * panelli bo'lgach (o'quvchi chati qoidasi) balandlikni SETKA konteyneri
 * chegaralashi kerak bo'ldi — u esa `display:grid`, ya'ni `flex flex-col`
 * li o'ramga sig'maydi. Ikkinchi nusxa yozish o'rniga o'lchov shu yerga
 * ko'chirildi: qoida bitta joyda qoladi, komponent esa uni ishlatadi.
 *
 * ★ NEGA O'LCHANADI, SANALMAYDI. O'quvchi sahifasida ayirma QO'LDA
 * sanalgan (`calc(100dvh - 128px)`) va buni QILSA BO'LADI, chunki u yerdagi
 * ayirma FAQAT karkasning o'zgarmas qismlaridan yig'ilgan. Xodim tomonida
 * bu mumkin emas:
 *   • `TeacherGroupPage` da chat tabining ustida sahifa sarlavhasi, o'tilgan
 *     darslar xulosasi, SHARTLI "keyingi dars" banneri va 8 ta tab turadi —
 *     tablar tor ekranda ikkinchi qatorga o'tadi, banner esa jadval
 *     kelgandan KEYIN paydo bo'ladi;
 *   • ya'ni har qanday qo'lda sanalgan raqam kamida bitta holatda xato
 *     bo'lardi va xato JIMGINA ko'rinardi (panel ekran tagidan chiqib
 *     ketardi — 2026-08-13 dagi shikoyat).
 * Shuning uchun element o'zining hujjat boshidan uzoqligini O'ZI o'lchaydi.
 *
 * 🔴 `dvh`, `vh` EMAS: iOS Safari'da `100vh` ko'rinadigan maydondan katta
 * (`docs/MOSLASHUVCHANLIK.md`, 3-bo'lim, 2- va 9-qatorlar — bu xato aynan
 * chatda TIRIK edi). Qoida shu faylda BITTA joyda yozilgan, ya'ni uni qayta
 * buzish uchun shu satrni tahrirlash kerak bo'ladi.
 *
 * 🔴 SHART: sahifa HUJJATNING O'ZI bilan skrollanishi kerak (xodim karkasi
 * shunday — `AppShell` ichida skroll konteyneri yo'q). Agar element
 * kelajakda `overflow-auto` li o'ram ichiga tushsa, `window.scrollY` bilan
 * hisob xato beradi va o'sha o'ramning `scrollTop` iga o'tish kerak bo'ladi.
 */
export interface FillHeightOptions {
  /**
   * Element ostida qoladigan bo'shliq — karkas `main` ining pastki
   * to'ldirmasi (`py-5` = 20px, `lg:py-6.5` = 26px). Eng kattasidan
   * ozgina ko'p olingan: kam olsak sahifada bir necha piksellik ortiqcha
   * skroll paydo bo'lardi.
   */
  gap?: MaybeRefOrGetter<number>
  /**
   * Past ekran (telefon yotiq holati) uchun pol. Bunda sahifa skrollanadi,
   * lekin ustun o'qib bo'lmaydigan darajada siqilmaydi.
   */
  minHeight?: MaybeRefOrGetter<number>
}

/**
 * Qaytadi: CSS balandlik satri yoki `undefined` — hali o'lchanmagan. Shu
 * holatda element mazmuni bo'yicha o'sadi, ya'ni eng yomon holatda ham chat
 * YO'QOLMAYDI (o'lchov `onMounted` dagi kadrda, brauzer chizishidan OLDIN
 * qo'llanadi — sakrash ko'rinmaydi).
 */
export function useFillHeight(
  root: Ref<HTMLElement | null>,
  options: FillHeightOptions = {},
): Ref<string | undefined> {
  const height = ref<string | undefined>(undefined)

  let frame = 0
  let observer: ResizeObserver | null = null

  function measure(): void {
    const element = root.value
    if (element === null) return

    /*
      Elementning HUJJAT boshidan uzoqligi. `window.scrollY` qo'shilgani uchun
      natija skroll holatiga BOG'LIQ EMAS — element tepasidagi hamma narsa
      (karkas sarlavhasi, sahifa sarlavhasi, tablar, banner) bitta raqamga
      yig'iladi.
    */
    const top = Math.round(element.getBoundingClientRect().top + window.scrollY)
    const minHeight = toValue(options.minHeight) ?? 320
    const gap = toValue(options.gap) ?? 28
    const next = `max(${minHeight}px, calc(100dvh - ${top + gap}px))`

    // ★ TENG QIYMAT YOZILMAYDI — `ResizeObserver` halqasini shu shart uzadi:
    // biz balandlikni o'zgartiramiz → ota-onaning balandligi o'zgaradi →
    // kuzatuvchi yana chaqiriladi → o'sha qiymat chiqadi → yozuv bo'lmaydi.
    if (next !== height.value) height.value = next
  }

  /*
    O'lchov KADRGA bir marta. `ResizeObserver` ichida DOM'ga darhol yozish
    brauzerda "loop completed with undelivered notifications" xatosini
    keltirib chiqaradi; `requestAnimationFrame` yozuvni keyingi kadrga suradi.
  */
  function schedule(): void {
    if (frame !== 0) return
    frame = requestAnimationFrame(() => {
      frame = 0
      measure()
    })
  }

  onMounted(() => {
    schedule()
    window.addEventListener('resize', schedule)

    /*
      OTA-ONA kuzatiladi: element tepasidagi narsalar (masalan, jadval kelgach
      paydo bo'ladigan "keyingi dars" banneri yoki telefonda yashiriladigan
      sahifa sarlavhasi) balandlikni o'zgartirganda uning o'lchami o'zgaradi.
      `window.resize` bunday holatda ishga tushmaydi.
    */
    const parent = root.value?.parentElement ?? null
    if (parent !== null) {
      observer = new ResizeObserver(schedule)
      observer.observe(parent)
    }
  })

  onBeforeUnmount(() => {
    window.removeEventListener('resize', schedule)
    observer?.disconnect()
    if (frame !== 0) cancelAnimationFrame(frame)
  })

  return height
}
