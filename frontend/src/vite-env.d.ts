/// <reference types="vite/client" />

// DIQQAT: bu yerda `declare module '*.vue'` shim'i ATAYLAB yo'q.
// vue-tsc (Volar) `.vue` fayllarning HAQIQIY turlarini chiqaradi; shim qo'yilsa
// barcha komponentlar `DefineComponent<any>` bo'lib qoladi va prop'lardagi
// xatolar tekshirilmay o'tib ketadi.

interface ImportMetaEnv {
  readonly VITE_API_URL: string
  readonly VITE_HUB_URL: string
}

interface ImportMeta {
  readonly env: ImportMetaEnv
}
