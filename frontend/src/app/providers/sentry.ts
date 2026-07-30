import type { App } from 'vue'
import type { Router } from 'vue-router'

import { env } from '@/shared/config/env'

/**
 * Frontend xato kuzatuvi (Sentry).
 *
 * ┌─────────────────────────────────────────────────────────────────────┐
 * │ SENTRY DINAMIK YUKLANADI — bu ataylab qilingan.                    │
 * └─────────────────────────────────────────────────────────────────────┘
 * Sentry brauzer SDK'si ~150 KB (gzip'da ~49 KB). Statik import qilinsa u
 * ASOSIY bundle'ga tushadi va HAR foydalanuvchi, HAR sahifa yuklashida
 * uni yuklab oladi — hatto Sentry o'chiq bo'lganda ham.
 *
 * Bizning foydalanuvchilar Telegram Mini App'ni MOBIL internetda ochadi
 * (O'zbekiston, 3G/4G). 49 KB — sezilarli. Shuning uchun:
 *   - DSN bo'lmasa: Sentry umuman YUKLANMAYDI (0 bayt)
 *   - DSN bo'lsa: alohida bo'lakda, fon rejimida yuklanadi
 *
 * NIMA UCHUN KERAK: eski tizimda frontend xatolari HECH QAYERDA qayd
 * etilmasdi. O'quvchi "ishlamayapti" deb aytardi va sababni topishning
 * iloji yo'q edi.
 *
 * MAXFIYLIK: PII yuborilmaydi (`sendDefaultPii: false`) va quyida
 * qo'shimcha tozalash bor — token/parol hech qachon Sentry'ga ketmaydi.
 */
export async function setupSentry(app: App, router: Router): Promise<boolean> {
  if (env.sentryDsn.length === 0) return false

  // Dinamik import — bu bo'lak faqat shu yerga kelinganda yuklanadi
  const Sentry = await import('@sentry/vue')

  Sentry.init({
    app,
    dsn: env.sentryDsn,
    environment: env.sentryEnvironment,

    // Backend bilan BIR XIL reliz — frontend va backend xatolari
    // bitta hodisaga bog'lanadi.
    release: env.release,

    // Shaxsiy ma'lumot (IP, cookie, foydalanuvchi nomi) YUBORILMAYDI
    sendDefaultPii: false,

    // 100% xato, lekin performance izlarining 10%i — aks holda
    // Sentry kvotasi tez tugaydi.
    tracesSampleRate: env.sentryEnvironment === 'production' ? 0.1 : 1.0,

    integrations: [Sentry.browserTracingIntegration({ router })],

    // Shovqinli, foydasiz xatolar
    ignoreErrors: [
      // Foydalanuvchi sahifani yopganda ketadigan uzilishlar
      'AbortError',
      'Non-Error promise rejection captured',
      // Brauzer kengaytmalari
      /^chrome-extension:/,
      /^moz-extension:/,
      // Tarmoq uzilishi — bizning bugimiz emas
      'Failed to fetch',
      'NetworkError',
      'Load failed',
    ],

    beforeSend(event) {
      // ---- SIRLARNI TOZALASH ----
      // URL'dagi `?access_token=...` (SignalR ulanishida bor)
      if (event.request?.url) {
        event.request.url = redactUrl(event.request.url)
      }

      // Breadcrumb'lardagi URL'lar ham
      event.breadcrumbs = event.breadcrumbs?.map((crumb) => {
        if (typeof crumb.data?.url === 'string') {
          return { ...crumb, data: { ...crumb.data, url: redactUrl(crumb.data.url) } }
        }
        return crumb
      })

      // Authorization va cookie hech qachon ketmasin
      if (event.request?.headers) {
        for (const key of ['Authorization', 'authorization', 'Cookie', 'cookie']) {
          delete event.request.headers[key]
        }
      }

      return event
    },
  })

  return true
}

/** `?access_token=xyz` va shunga o'xshash maxfiy parametrlarni yashiradi. */
function redactUrl(raw: string): string {
  try {
    const url = new URL(raw, window.location.origin)
    for (const key of ['access_token', 'token', 'refresh_token', 'password', 'secret']) {
      if (url.searchParams.has(key)) url.searchParams.set(key, '[yashirildi]')
    }
    return url.toString()
  } catch {
    // URL sifatida talqin qilinmasa — ehtiyot uchun butunlay olib tashlaymiz
    return raw.includes('token') ? '[yashirildi]' : raw
  }
}
