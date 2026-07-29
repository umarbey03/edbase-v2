/**
 * Token saqlagichi.
 *
 * Qoida:
 *  - `accessToken` FAQAT xotirada (modul o'zgaruvchisi). localStorage'ga yozilmaydi —
 *    XSS holatida uzoq muddatli zarar bermasligi uchun.
 *  - `refreshToken` localStorage'da — sahifa yangilanganda sessiyani tiklash uchun.
 *
 * Bu modul `shared` qatlamida turadi, chunki `http.ts` unga bog'liq. Pinia store esa
 * (yuqori qatlam) shu saqlagichga YOZADI — natijada `shared -> features` bog'liqligi
 * yuzaga kelmaydi.
 */

const REFRESH_TOKEN_KEY = 'zinnur.refreshToken'

export interface AuthTokens {
  accessToken: string
  refreshToken: string
}

type AuthExpiredListener = () => void

let accessToken: string | null = null
let refreshToken: string | null = readStoredRefreshToken()
const authExpiredListeners = new Set<AuthExpiredListener>()

/** Private/incognito rejimda localStorage `throw` qilishi mumkin — himoyalangan o'ram. */
function safeStorage(): Storage | null {
  try {
    return window.localStorage
  } catch {
    return null
  }
}

function readStoredRefreshToken(): string | null {
  const value = safeStorage()?.getItem(REFRESH_TOKEN_KEY) ?? null
  return value !== null && value.length > 0 ? value : null
}

export function getAccessToken(): string | null {
  return accessToken
}

export function getRefreshToken(): string | null {
  return refreshToken
}

export function setTokens(tokens: AuthTokens): void {
  accessToken = tokens.accessToken
  refreshToken = tokens.refreshToken
  try {
    safeStorage()?.setItem(REFRESH_TOKEN_KEY, tokens.refreshToken)
  } catch {
    // Saqlab bo'lmasa ham sessiya joriy tab uchun ishlayveradi.
  }
}

export function clearTokens(): void {
  accessToken = null
  refreshToken = null
  try {
    safeStorage()?.removeItem(REFRESH_TOKEN_KEY)
  } catch {
    /* e'tiborsiz qoldiriladi */
  }
}

/** Refresh ham ishlamay qolganda (sessiya tugadi) chaqiriladi. */
export function onAuthExpired(listener: AuthExpiredListener): () => void {
  authExpiredListeners.add(listener)
  return () => authExpiredListeners.delete(listener)
}

export function notifyAuthExpired(): void {
  for (const listener of authExpiredListeners) {
    try {
      listener()
    } catch (error) {
      console.error('[auth] sessiya tugash ishlovchisida xato', error)
    }
  }
}
