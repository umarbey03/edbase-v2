/**
 * Muhit o'zgaruvchilari (SPEC 8-bo'lim).
 *
 * Vite `import.meta.env` ni build paytida almashtiradi, lekin qiymat bo'sh yoki
 * noto'g'ri bo'lsa ilova jimgina buziladi. Shuning uchun bu yerda RUNTIME tekshiruvi
 * bor: nosozlik darhol, aniq xabar bilan yuzaga chiqadi.
 */

export interface AppEnv {
  /** Masalan: `http://localhost:5080` (oxirida `/` bo'lmaydi) */
  readonly apiUrl: string
  /** Masalan: `http://localhost:5080/hubs/live` */
  readonly hubUrl: string
  readonly isDev: boolean

  /**
   * Sentry DSN. BO'SH BO'LSA xato kuzatuvi butunlay o'chiq.
   *
   * Ataylab IXTIYORIY: ishlab chiquvchi kompyuterida DSN bo'lmaydi va
   * uning yo'qligi ilovani yiqitmasligi kerak.
   */
  readonly sentryDsn: string
  /** `production` | `staging` | `development` */
  readonly sentryEnvironment: string
  /** Backend bilan MOS bo'lishi kerak — xatolar bir-biriga bog'lanishi uchun. */
  readonly release: string
}

export class EnvError extends Error {
  constructor(message: string) {
    super(message)
    this.name = 'EnvError'
  }
}

function requireNonEmpty(name: string, raw: string | undefined): string {
  const value = (raw ?? '').trim()
  if (value.length === 0) {
    throw new EnvError(
      `[env] "${name}" o'zgaruvchisi berilmagan. frontend/.env faylini .env.example asosida to'ldiring.`,
    )
  }
  return value
}

function requireAbsoluteUrl(name: string, raw: string | undefined): string {
  const value = requireNonEmpty(name, raw)
  let parsed: URL
  try {
    parsed = new URL(value)
  } catch {
    throw new EnvError(`[env] "${name}" to'liq URL bo'lishi kerak (masalan http://localhost:5080).`)
  }
  if (parsed.protocol !== 'http:' && parsed.protocol !== 'https:') {
    throw new EnvError(`[env] "${name}" faqat http yoki https bo'lishi mumkin, "${parsed.protocol}" emas.`)
  }
  return value.replace(/\/+$/, '')
}

function createEnv(): AppEnv {
  const apiUrl = requireAbsoluteUrl('VITE_API_URL', import.meta.env.VITE_API_URL)
  const hubUrl = requireAbsoluteUrl('VITE_HUB_URL', import.meta.env.VITE_HUB_URL)

  // Odatiy xato: VITE_HUB_URL ga `/hubs/live` yozishni unutish. Ogohlantiramiz, lekin
  // to'xtatmaymiz — hub yo'li reverse-proxy'da boshqacha bo'lishi mumkin.
  if (!hubUrl.includes('/hubs/')) {
    console.warn('[env] VITE_HUB_URL odatda "/hubs/live" bilan tugaydi. Hozirgi qiymat:', hubUrl)
  }

  // Sentry — IXTIYORIY. Tekshirilmaydi va yo'qligi xato hisoblanmaydi.
  const sentryDsn = (import.meta.env.VITE_SENTRY_DSN ?? '').trim()
  const sentryEnvironment =
    (import.meta.env.VITE_SENTRY_ENVIRONMENT ?? '').trim()
    || (import.meta.env.DEV ? 'development' : 'production')
  const release = (import.meta.env.VITE_RELEASE ?? '').trim() || 'dev'

  return {
    apiUrl,
    hubUrl,
    isDev: import.meta.env.DEV,
    sentryDsn,
    sentryEnvironment,
    release,
  }
}

export const env: AppEnv = createEnv()

/** REST yo'lini to'liq manzilga aylantiradi: `/api/v1/auth/me` -> `http://.../api/v1/auth/me` */
export function apiUrl(path: string): string {
  return `${env.apiUrl}${path.startsWith('/') ? path : `/${path}`}`
}

/**
 * Boshqa hub manzilini MAVJUD `VITE_HUB_URL` dan hosil qiladi:
 * `http://host/hubs/live` + `group-chat` -> `http://host/hubs/group-chat`.
 *
 * NEGA YANGI MUHIT O'ZGARUVCHISI EMAS: hub'lar bitta serverda, bitta
 * `/hubs/*` prefiksi ostida yashaydi va autentifikatsiya ham AYNAN o'sha
 * prefiks uchun sozlangan (token `?access_token=` query'sida). Har hub uchun
 * alohida o'zgaruvchi qo'shsak, ularning biri deploy'da yangilanmay qolishi
 * mumkin edi — nosozlik esa faqat ish vaqtida, "chat ochilmayapti"
 * ko'rinishida bilinardi. Manba bitta bo'lgani uchun bunday ajralish
 * MUMKIN EMAS.
 *
 * Faqat OXIRGI segment almashtiriladi, ya'ni reverse-proxy'dagi ichki
 * prefikslar (`/api/hubs/live`) ham saqlanadi.
 */
export function hubUrlFor(hubName: string): string {
  const base = env.hubUrl
  const lastSlash = base.lastIndexOf('/')
  // Slash umuman bo'lmasa (kutilmagan qiymat) — asl manzilni buzmaymiz.
  return lastSlash < 0 ? base : `${base.slice(0, lastSlash + 1)}${hubName}`
}
