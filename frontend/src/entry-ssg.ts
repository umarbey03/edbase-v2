import { createSSRApp } from 'vue'
import { renderToString } from 'vue/server-renderer'
import { createMemoryHistory, createRouter } from 'vue-router'

import LandingPage from '@/pages/landing/LandingPage.vue'

/*
  ══════════════════════════════════════════════════════════════════════════
  PRERENDER KIRISH NUQTASI — LANDING SAHIFANI HTML GA AYLANTIRADI
  ══════════════════════════════════════════════════════════════════════════

  Bu modul BRAUZERGA TUSHMAYDI. U faqat build paytida, Node ichida bir
  marta ishlaydi (`scripts/prerender.mjs` chaqiradi) va natijasi
  `dist/index.html` ga yoziladi.

  ┌────────────────────────────────────────────────────────────────────┐
  │ 🔴 NIMA UCHUN UMUMAN KERAK                                         │
  └────────────────────────────────────────────────────────────────────┘
  Ilova — SPA. Ya'ni serverdan keladigan HTML ichida `<div id="app">`
  dan boshqa HECH NARSA yo'q; butun matn brauzerda JavaScript bilan
  chiziladi.

  Google JS ni ishlata oladi, lekin:
    • buni KEYINROQ, alohida navbatda qiladi — indekslash sekinlashadi;
    • bitta JS xatosi = indeksga BO'SH sahifa tushadi;
    • Yandex JS ni ancha zaif o'qiydi, O'zbekistonda esa uning ulushi
      katta — ya'ni bu bizda ikkinchi darajali muammo emas.

  Prerender'dan keyin HTML'da to'liq matn turadi va u yerdan hech qanday
  JS talab qilinmaydi.

  ┌────────────────────────────────────────────────────────────────────┐
  │ 🔴 XAVFSIZLIK — NIMA HTML GA TUSHADI VA NIMA TUSHMAYDI             │
  └────────────────────────────────────────────────────────────────────┘
  FAQAT `pages/landing` — ya'ni `content.ts` dagi statik marketing matni.

    • baza SO'ROVI yo'q — bu modul API ga umuman murojaat qilmaydi;
    • foydalanuvchi ma'lumoti yo'q — sahifa anonim mehmon uchun;
    • yangi sir ochilmaydi — Vite faqat `VITE_` prefiksli qiymatlarni
      singdiradi va ular ALLAQACHON mavjud JS bundle ichida ochiq;
    • XSS yo'q — landing'da bitta ham `v-html` yo'q, `renderToString`
      esa interpolatsiyani brauzerdagi bilan AYNI tarzda ekranlaydi.

  ⚠️ BU YERGA BOSHQA SAHIFA QO'SHISHDAN OLDIN o'ylang: autentifikatsiya
     talab qiladigan sahifa HTML ga tushsa, u ommaga ochiq faylda
     qolardi.

  ┌────────────────────────────────────────────────────────────────────┐
  │ ★ NEGA HAQIQIY `app/router` ISHLATILMAYDI                          │
  └────────────────────────────────────────────────────────────────────┘
  Haqiqiy router `beforeEach` da `auth.bootstrap()` ni chaqiradi, ya'ni
  API ga so'rov yuboradi. Build paytida server yo'q va bu bir necha
  soniyalik kutish yoki xato bilan tugardi.

  Bu yerda esa MINIMAL router: faqat `RouterLink` ishlashi uchun. Ikkala
  router ham `/` va `/login` ni AYNI tarzda hal qiladi, shuning uchun
  `RouterLink` yasaydigan `<a>` markup'i (jumladan `router-link-active`
  klasslari) prod bilan bir xil chiqadi.
*/

/**
 * Marshrut yozuvi uchun bo'sh komponent.
 *
 * ★ Komponentning O'ZI kerak emas — biz `RouterView` chizmaymiz. Router
 * bu yerda faqat `RouterLink` manzillarni hal qila olishi uchun bor.
 */
const Blank = { render: (): null => null }

/** Landing sahifani HTML satriga aylantiradi. */
export async function render(): Promise<string> {
  const app = createSSRApp(LandingPage)

  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/', name: 'landing', component: Blank },
      { path: '/login', name: 'login', component: Blank },
      // Landing'dagi havolalarning hammasi shu ikkitasiga tushadi, lekin
      // ushlovchi marshrutsiz `router.resolve` noma'lum yo'lda
      // ogohlantirish chiqaradi va build logini ifloslantiradi.
      { path: '/:pathMatch(.*)*', name: 'not-found', component: Blank },
    ],
  })

  app.use(router)

  await router.push('/')
  await router.isReady()

  return await renderToString(app)
}
