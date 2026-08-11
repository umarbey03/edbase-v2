import type { Router } from 'vue-router'

import {
  applyMiniAppChrome,
  ensureTelegramWebApp,
  isTelegramMiniApp,
  onBackButtonClick,
  setBackButton,
} from '@/shared/lib/telegram-web-app'

/**
 * TELEGRAM MINI APP KARKASI.
 *
 * Bu yerda faqat Telegram MIJOZINING o'z elementlari sozlanadi (sarlavha
 * rangi, "orqaga" tugmasi, to'liq ekran). Ilovaning ichki ko'rinishi
 * `StudentShell` da va u O'ZGARMAYDI — o'quvchi paneli allaqachon Mini App
 * uchun qilingan (520px ustun, pastda 5 tab).
 *
 * NEGA `app/` QATLAMIDA: bu yagona joy bo'lib, u ham `vue-router` ni, ham
 * platforma adapterini ko'radi. `shared` router'ni bilmaydi, `widgets` esa
 * bir vaqtning o'zida faqat bittasi mount bo'ladi — "orqaga" tugmasi esa
 * BARCHA marshrutlarda ishlashi kerak.
 */

/**
 * Ilova sahifasining foni (`style.css` dagi `--color-ink-950`). Telegram
 * sarlavhasi va foni shu rangga bo'yaladi.
 *
 * ★ QAYSI TEMA USTUN: Telegram foydalanuvchining O'Z temasini
 * (`themeParams`) taklif qiladi, biz uni O'QIMAYMIZ. Ilovaning rangi
 * tanib olinadigan belgi — foydalanuvchining Telegram temasi uni bosib
 * ketmasligi kerak. Aksincha: Telegram elementlari BIZNING rangimizga
 * moslashadi.
 *
 * ★ 2026-08-10: qiymat eski navy `#051e2d` dan yorug' `#f4f6fb` ga
 * o'tdi (`index.html` dagi `theme-color` va `StudentShell` bilan BIR XIL
 * bo'lishi shart — aks holda Telegram sarlavhasi to'q, ilova ichi yorug'
 * bo'lib, ekran ikkiga bo'linib ko'rinadi).
 */
const STUDENT_BACKGROUND = '#f4f6fb'

/**
 * "Orqaga" tugmasi KO'RINMAYDIGAN marshrutlar — pastki tab paneli
 * bandlari (`entities/user/model/navigation.ts` dagi `STUDENT_NAV`).
 *
 * Sabab: bu beshtasi bir-birining "ichida" emas, yonma-yon turadi va
 * ular orasida o'tish uchun tab paneli bor. Ularda tizim "orqaga" tugmasi
 * chiqsa, u tarixdagi tasodifiy tabga qaytarardi.
 */
const ROOT_ROUTES = new Set([
  'student-home',
  'student-calendar',
  'student-learn',
  'student-rating',
  'student-chat',
  'login',
])

export function registerTelegramShell(router: Router): void {
  if (!isTelegramMiniApp()) return

  void ensureTelegramWebApp().then((webApp) => {
    // SDK yuklanmadi (tarmoq yoki vaqt tugadi) — kirish oqimi baribir
    // ishlaydi, faqat bezaklar bo'lmaydi. Bu xato emas.
    if (webApp === null) return

    applyMiniAppChrome(webApp, STUDENT_BACKGROUND)

    /*
      Tugma ishlovchisi BIR MARTA ulanadi va butun ilova umri davomida
      qoladi. Har navigatsiyada qayta ulansa, Telegram SDK'sida ishlovchilar
      to'planib, bitta bosish bir necha marta orqaga qaytarardi.
    */
    onBackButtonClick(() => {
      void router.back()
    })

    // Ko'rinish esa HAR navigatsiyada qayta hisoblanadi.
    const sync = (): void => {
      const name = router.currentRoute.value.name
      setBackButton(typeof name === 'string' && !ROOT_ROUTES.has(name))
    }
    router.afterEach(sync)
    sync()
  })
}
