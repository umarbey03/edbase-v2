import { onBeforeUnmount, onMounted } from 'vue'
import type { Ref } from 'vue'

/**
 * Tekshirish navbatining klaviatura yorliqlari.
 *
 * ★ TUGMALAR O'YLAB TOPILMAGAN — eski ilovadan olingan
 * (`Zinnur-platform/app/templates/teacher.html`, 1452–1470-qatorlar):
 *
 *    1–5    baho qo'yish (aslida 1–9, tugma bor bo'lsa)
 *    Enter  baholash va keyingisiga o'tish
 *    →      o'tkazib yuborish
 *    Space  audio javobni ijro/pauza
 *    Esc    navbatni yopish
 *
 * Ustozlar bu kombinatsiyani yodlab olishgan: kuniga yuzlab ish tekshirilganda
 * qo'l sichqonchaga umuman uzatilmaydi. Shuning uchun bironta tugma
 * "yaxshilash" niyatida ham almashtirilmadi.
 */
export interface QueueShortcutHandlers {
  /** `1`–`9`: raqamli tugma bosildi. */
  onDigit: (value: number) => void
  onSave: () => void
  onNext: () => void
  onToggleAudio: () => void
  onClose: () => void
}

const DIGIT = /^[1-9]$/

/**
 * `active` — yorliqlar hozir ishlashi kerakmi.
 *
 * Ustidan oyna ochilganda (masalan kattalashtirilgan rasm) chaqiruvchi buni
 * `false` qiladi: aks holda `Esc` bir vaqtda ikkala qatlamni yopardi va
 * ustoz butun navbatdan chiqib ketardi.
 */
export function useQueueShortcuts(active: Ref<boolean>, handlers: QueueShortcutHandlers): void {
  function handleKeydown(event: KeyboardEvent): void {
    if (!active.value) return

    /*
      Modifikatorli kombinatsiyalar TEGILMAYDI: `Cmd/Ctrl+1` — brauzerning
      tab yorlig'i, `Alt+→` — orqaga/oldinga. Ularni ushlab qolsak
      foydalanuvchi brauzerni boshqara olmay qolardi.
    */
    if (event.ctrlKey || event.metaKey || event.altKey) return

    if (event.key === 'Escape') {
      handlers.onClose()
      return
    }

    /*
      ★ MATN MAYDONIDA raqam BAHO QO'YMAYDI.

      Ustoz izohga "5 ta xato bor" deb yozganda har bir raqam bahoni
      almashtirib ketardi. Eski ilova ham aynan shunday himoyalangan
      (`if(tag==='input'||tag==='textarea'||tag==='select') … return`).

      Yagona istisno — `Enter`: izohni yozib bo'lgach qo'lni maydondan
      olmasdan saqlash mumkin bo'lsin. Izoh maydoni ATAYLAB bir qatorli
      `<input>` (eski ilovadagi `qv-fb` kabi), shuning uchun `Enter` ni
      to'sish hech qanday matnni yo'qotmaydi.
    */
    const target = event.target
    const inTextField =
      target instanceof HTMLElement
      && (target.tagName === 'INPUT'
        || target.tagName === 'TEXTAREA'
        || target.tagName === 'SELECT'
        || target.isContentEditable)

    if (inTextField) {
      if (event.key === 'Enter') {
        event.preventDefault()
        handlers.onSave()
      }
      return
    }

    if (DIGIT.test(event.key)) {
      event.preventDefault()
      handlers.onDigit(Number(event.key))
      return
    }

    if (event.key === 'Enter') {
      event.preventDefault()
      handlers.onSave()
      return
    }

    if (event.key === 'ArrowRight') {
      event.preventDefault()
      handlers.onNext()
      return
    }

    if (event.code === 'Space') {
      // `preventDefault` shart: usiz `Space` sahifani pastga suradi.
      event.preventDefault()
      handlers.onToggleAudio()
    }
  }

  onMounted(() => document.addEventListener('keydown', handleKeydown))

  /*
    ★ TINGLOVCHI MAJBURIY OLINADI. Navbat yopilgach `document` da qolib
    ketsa, ustoz vazifalar ro'yxatida raqam bosganda ko'rinmas navbatga
    baho qo'yilardi (va `Enter` uni saqlab yuborardi).
  */
  onBeforeUnmount(() => document.removeEventListener('keydown', handleKeydown))
}
