import { fileURLToPath, URL } from 'node:url'

import tailwindcss from '@tailwindcss/vite'
import vue from '@vitejs/plugin-vue'
import { defineConfig } from 'vite'

// Tailwind v4 — PostCSS/tailwind.config.js ISHLATILMAYDI, faqat `@tailwindcss/vite` plagini
// va `src/style.css` ichidagi `@import "tailwindcss";`.
export default defineConfig({
  plugins: [vue(), tailwindcss()],
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
    },
  },
  server: {
    host: true,
    port: 5173,
    strictPort: false,
  },
  preview: {
    host: true,
    port: 5173,
  },
  build: {
    target: 'es2022',
    sourcemap: false,
    chunkSizeWarningLimit: 900,
    rollupOptions: {
      output: {
        // livekit-client va signalr og'ir kutubxonalar — ularni alohida chunk'ga ajratamiz,
        // shunda login sahifasi ularni yuklab o'tirmaydi (faqat /live/:id sahifasida kerak).
        manualChunks(id: string): string | undefined {
          if (!id.includes('node_modules')) return undefined
          if (id.includes('livekit-client')) return 'livekit'
          if (id.includes('@microsoft/signalr')) return 'signalr'
          // Sentry ALOHIDA bo'lakda: `main.ts` da DINAMIK import qilinadi va
          // faqat `VITE_SENTRY_DSN` berilganda yuklanadi. Bu qatorsiz u
          // `vendor` ga tushib, HAR foydalanuvchiga ~49 KB (gzip) qo'shimcha
          // yuklardi — mobil internetda (Telegram Mini App) sezilarli.
          if (id.includes('@sentry')) return 'sentry'
          if (id.includes('@tanstack')) return 'query'
          if (id.includes('/vue/') || id.includes('vue-router') || id.includes('pinia')) return 'vue'
          return 'vendor'
        },
      },
    },
  },
})
