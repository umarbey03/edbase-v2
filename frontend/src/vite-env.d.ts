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
}

interface ImportMeta {
  readonly env: ImportMetaEnv
}
