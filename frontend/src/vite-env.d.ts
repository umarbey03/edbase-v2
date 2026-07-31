/// <reference types="vite/client" />

// DIQQAT: bu yerda `declare module '*.vue'` shim'i ATAYLAB yo'q.
// vue-tsc (Volar) `.vue` fayllarning HAQIQIY turlarini chiqaradi; shim qo'yilsa
// barcha komponentlar `DefineComponent<any>` bo'lib qoladi va prop'lardagi
// xatolar tekshirilmay o'tib ketadi.

interface ImportMetaEnv {
  /** Sentry DSN. Bo'sh bo'lsa xato kuzatuvi o'chiq (ixtiyoriy). */
  readonly VITE_SENTRY_DSN?: string
  /** production | staging | development */
  readonly VITE_SENTRY_ENVIRONMENT?: string
  /** Backend bilan MOS reliz identifikatori. */
  readonly VITE_RELEASE?: string

  readonly VITE_API_URL: string
  readonly VITE_HUB_URL: string

  /**
   * Telegram botining foydalanuvchi nomi (`@` siz). IXTIYORIY.
   *
   * "Telegram akkaunt bog'lanmagan" (409) ekranidagi tugma shundan
   * `t.me/<nom>` havolasini yasaydi. Berilmasa tugma `WebApp.close()` bilan
   * ilovani yopadi va foydalanuvchi ilova ochilgan bot chatiga qaytadi —
   * ya'ni o'zgaruvchisiz ham oqim ishlaydi.
   *
   * Sabab va muqobillar: `features/telegram-auth/model/bot-link.ts`.
   */
  readonly VITE_TELEGRAM_BOT_USERNAME?: string
}

interface ImportMeta {
  readonly env: ImportMetaEnv
}
