import { closeMiniApp, openTelegramLink } from '@/shared/lib/telegram-web-app'

/**
 * BOTGA QAYTISH — 409 ("Telegram akkaunt bog'lanmagan") holatidagi yagona
 * foydali harakat: o'quvchi botga borib «📱 Raqamni ulashish» tugmasini
 * bosishi kerak.
 *
 * ┌──────────────────────────────────────────────────────────────────────────┐
 * │ BOT NOMINI QAYERDAN OLAMIZ                                               │
 * └──────────────────────────────────────────────────────────────────────────┘
 * Backendda `telegram.bot_username` sozlamasi bor, lekin uni o'qish yo'llari
 * (`GET /api/v1/settings*`) serverda `[Authorize(Roles = "Admin")]` bilan
 * yopilgan — jonli API'da tekshirildi: anonim so'rov 401, o'quvchi tokeni
 * bilan 403. Ya'ni 409 ekranidagi (hali KIRMAGAN) o'quvchi bu qiymatni
 * printsipial ravishda ola olmaydi.
 *
 * Shu sababli ikki bosqichli yechim:
 *
 *  1) ASOSIY — `WebApp.close()`. Mini App'ni yopganda Telegram foydalanuvchini
 *     ilova OCHILGAN chatga qaytaradi. Ilovaga kirish yo'li esa botning
 *     "Ilovani ochish" tugmasi (`telegram.mini_app_url` sozlamasining
 *     ta'rifida shunday yozilgan), ya'ni amalda bu AYNAN bot chati — o'sha
 *     yerda «📱 Raqamni ulashish» tugmasi ham turadi. Bu yo'l HECH QANDAY
 *     sozlamaga muhtoj emas va shuning uchun har doim ishlaydi.
 *
 *  2) ANIQROQ — `VITE_TELEGRAM_BOT_USERNAME` build-time o'zgaruvchisi berilgan
 *     bo'lsa, `t.me/<bot>` havolasi Telegram ichida ochiladi. Bu 1-yo'l
 *     noto'g'ri ishlaydigan chekka holatni qoplaydi: Mini App guruhdagi
 *     inline tugmadan ochilgan bo'lsa, `close()` bot chatiga emas, o'sha
 *     guruhga qaytaradi.
 *
 * NEGA BUILD-TIME O'ZGARUVCHI: bot nomi RELIZ bilan birga o'zgaradigan,
 * maxfiy bo'lmagan qiymat (yiliga bir marta ham o'zgarmaydi) — uni olish
 * uchun ochiq API endpoint qo'shish, ya'ni serverga anonim yo'l ochish
 * mutanosib emas. O'zgaruvchi IXTIYORIY: berilmasa 1-yo'l ishlaydi va
 * hech narsa buzilmaydi (`Sentry DSN` bilan bir xil yondashuv).
 *
 * ⚠️ BACKEND UCHUN TAKLIF (o'zim qilmadim, hisobotda ham bor): agar bot
 * nomi muhitdan emas, BAZADAN boshqarilishi kerak bo'lsa — `GET
 * /api/v1/telegram/mini-app/config` kabi anonim endpoint qo'shilishi lozim
 * bo'ladi, u FAQAT `{ "botUsername": "..." }` qaytaradi (token, webhook siri
 * yoki boshqa maxfiy sozlama EMAS).
 */
const BOT_USERNAME = (import.meta.env.VITE_TELEGRAM_BOT_USERNAME ?? '')
  .trim()
  // Sozlamada ham, odatda, `@` siz yoziladi — lekin yozilib qolsa havola
  // `t.me/@bot` bo'lib buzilardi.
  .replace(/^@/, '')

/** Bot havolasi ma'lum bo'lsa `true` — tugma matni shunga qarab o'zgaradi. */
export function hasBotLink(): boolean {
  return BOT_USERNAME.length > 0
}

/**
 * Foydalanuvchini botga qaytaradi.
 *
 * `false` — na havola ochildi, na ilova yopildi (SDK yuklanmagan). Bunda UI
 * qo'lda bajariladigan ko'rsatma ko'rsatishi kerak.
 */
export function goToBot(): boolean {
  if (BOT_USERNAME.length > 0 && openTelegramLink(`https://t.me/${BOT_USERNAME}`)) return true
  return closeMiniApp()
}
