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

  return { apiUrl, hubUrl, isDev: import.meta.env.DEV }
}

export const env: AppEnv = createEnv()

/** REST yo'lini to'liq manzilga aylantiradi: `/api/v1/auth/me` -> `http://.../api/v1/auth/me` */
export function apiUrl(path: string): string {
  return `${env.apiUrl}${path.startsWith('/') ? path : `/${path}`}`
}
