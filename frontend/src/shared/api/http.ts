import { apiUrl } from '@/shared/config/env'
import type { AuthResponse, ProblemDetails } from '@/shared/types'

import { ApiError } from './api-error'
import { clearTokens, getAccessToken, getRefreshToken, notifyAuthExpired, setTokens } from './tokens'

export type HttpMethod = 'GET' | 'POST' | 'PUT' | 'PATCH' | 'DELETE'

export type QueryValue = string | number | boolean | null | undefined

export interface RequestOptions {
  method?: HttpMethod
  /**
   * So'rov tanasi. `undefined` bo'lsa tana umuman yuborilmaydi.
   *
   * IKKI SHAKL qo'llanadi:
   *  • `FormData` — `multipart/form-data` (fayl yuklash, masalan o'quvchining
   *    vazifa javobi). Seriyalanmaydi, o'zi uzatiladi;
   *  • boshqa har qanday qiymat — JSON.
   */
  body?: unknown
  query?: Readonly<Record<string, QueryValue>>
  signal?: AbortSignal
  /** `false` bo'lsa Authorization sarlavhasi qo'shilmaydi (login/refresh uchun). */
  auth?: boolean
  headers?: Readonly<Record<string, string>>
}

/** SPEC 5: refresh yo'li. Bu yo'lning o'zi hech qachon qayta urinilmaydi. */
const REFRESH_PATH = '/api/v1/auth/refresh'

/**
 * BITTA-UCHISH (single-flight) refresh.
 *
 * Muammo: jonli dars sahifasi bir vaqtda 3–4 ta so'rov yuboradi (sessiya, xabarlar,
 * LiveKit token). Access token muddati tugagan bo'lsa, hammasi bir vaqtda 401 oladi.
 * Har biri alohida refresh qilsa — server refresh tokenni rotatsiya qilgani uchun
 * ikkinchisi va uchinchisi 401 olib, foydalanuvchi tizimdan chiqib ketadi.
 *
 * Yechim: qaytayotgan Promise'ni modul darajasida saqlaymiz. Ikkinchi 401 xuddi
 * shu Promise'ni kutadi — server bilan FAQAT BITTA refresh so'rovi bo'ladi.
 */
let refreshInFlight: Promise<string> | null = null

function buildUrl(path: string, query?: Readonly<Record<string, QueryValue>>): string {
  const base = apiUrl(path)
  if (!query) return base
  const search = new URLSearchParams()
  for (const [key, value] of Object.entries(query)) {
    if (value === undefined || value === null) continue
    search.append(key, String(value))
  }
  const qs = search.toString()
  return qs.length > 0 ? `${base}?${qs}` : base
}

async function send(url: string, options: RequestOptions, token: string | null): Promise<Response> {
  const headers = new Headers(options.headers)
  /*
    `Accept` faqat CHAQIRUVCHI aytmagan bo'lsa qo'yiladi.

    Deyarli hamma endpoint JSON qaytaradi, lekin bittasi — test natijalari CSV
    eksporti (`/tests/{id}/results/export`) — fayl beradi. Sarlavhani shartsiz
    ustiga yozsak, `http.download` o'z turini ayta olmasdi.
  */
  if (!headers.has('Accept')) headers.set('Accept', 'application/json')
  if (token !== null) headers.set('Authorization', `Bearer ${token}`)

  let body: BodyInit | undefined
  if (options.body !== undefined) {
    if (options.body instanceof FormData) {
      /*
        ★ MULTIPART: `Content-Type` QO'LDA QO'YILMAYDI.

        multipart sarlavhasi `multipart/form-data; boundary=----WebKitFormBoundaryXYZ`
        ko'rinishida bo'lishi shart, `boundary` esa tanani yig'ayotgan BRAUZERDA
        hosil bo'ladi — biz uni oldindan bila olmaymiz. Sarlavhani o'zimiz
        yozsak, boundary tushib qoladi va server tanani qismlarga ajrata
        olmaydi (ASP.NET "Missing content-type boundary" bilan 400 beradi).
        Shuning uchun bu yerda faqat TANA beriladi, sarlavhani `fetch` qo'yadi.

        `FormData` ni QAYTA yuborsa ham bo'ladi: 401 dan keyin so'rov
        takrorlanganda `fetch` undan yangi oqim hosil qiladi (bir marta
        o'qiladigan `ReadableStream` dan farqli o'laroq) — ya'ni token
        yangilanishi fayl yuklashni buzmaydi.
      */
      body = options.body
    } else {
      headers.set('Content-Type', 'application/json')
      body = JSON.stringify(options.body)
    }
  }

  const init: RequestInit = {
    method: options.method ?? 'GET',
    headers,
    credentials: 'omit',
    mode: 'cors',
  }
  if (body !== undefined) init.body = body
  if (options.signal !== undefined) init.signal = options.signal

  try {
    return await fetch(url, init)
  } catch (error) {
    // Bekor qilingan so'rov xato emas — yuqoriga o'zgarishsiz uzatiladi.
    if (error instanceof DOMException && error.name === 'AbortError') throw error
    throw new ApiError(0, null, 'Serverga ulanib bo‘lmadi.')
  }
}

function isProblemDetails(value: unknown): value is ProblemDetails {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

async function toApiError(response: Response): Promise<ApiError> {
  let problem: ProblemDetails | null = null
  try {
    const contentType = response.headers.get('content-type') ?? ''
    if (contentType.includes('json')) {
      const parsed: unknown = await response.json()
      if (isProblemDetails(parsed)) problem = parsed
    } else {
      const text = (await response.text()).trim()
      if (text.length > 0) problem = { title: text.slice(0, 300) }
    }
  } catch {
    // Tana o'qilmasa ham status kodining o'zi yetarli.
  }
  // `Retry-After` (429) — server ANIQ soniyani yuboradi. Sarlavha yo'q yoki
  // HTTP sana ko'rinishida bo'lsa `null` qoladi va UI umumiy matn ko'rsatadi.
  const retryAfterRaw = response.headers.get('retry-after')
  const retryAfter = retryAfterRaw === null ? Number.NaN : Number(retryAfterRaw)

  return new ApiError(
    response.status,
    problem,
    `HTTP ${response.status}`,
    Number.isFinite(retryAfter) && retryAfter >= 0 ? Math.ceil(retryAfter) : null,
  )
}

async function parseBody<T>(response: Response): Promise<T> {
  if (response.status === 204 || response.status === 205) {
    // Tanasi yo'q javob. Chaqiruvchi `void` kutadi.
    return undefined as T
  }
  const text = await response.text()
  if (text.length === 0) return undefined as T
  return JSON.parse(text) as T
}

function endSession(): void {
  clearTokens()
  notifyAuthExpired()
}

async function performRefresh(): Promise<string> {
  const refreshToken = getRefreshToken()
  if (refreshToken === null) {
    endSession()
    throw new ApiError(401, null, 'Sessiya topilmadi.')
  }

  const response = await send(
    buildUrl(REFRESH_PATH),
    { method: 'POST', body: { refreshToken }, auth: false },
    null,
  )

  if (!response.ok) {
    endSession()
    throw await toApiError(response)
  }

  const auth = await parseBody<AuthResponse>(response)
  setTokens({ accessToken: auth.accessToken, refreshToken: auth.refreshToken })
  return auth.accessToken
}

/** Tashqaridan ham chaqirsa bo'ladi (masalan ilova ishga tushganda sessiyani tiklash). */
export function refreshAccessToken(): Promise<string> {
  if (refreshInFlight !== null) return refreshInFlight
  const promise = performRefresh().finally(() => {
    if (refreshInFlight === promise) refreshInFlight = null
  })
  refreshInFlight = promise
  return promise
}

/**
 * 401 dan keyin yaroqli access token qaytaradi (yoki `null` — sessiya tugagan).
 * `tokenUsed` — muvaffaqiyatsiz so'rovda ishlatilgan token.
 */
async function ensureFreshAccessToken(tokenUsed: string | null): Promise<string | null> {
  // Parallel so'rovlardan biri allaqachon yangilab ulgurgan bo'lsa — qayta refresh shart emas.
  const current = getAccessToken()
  if (current !== null && current !== tokenUsed) return current

  try {
    return await refreshAccessToken()
  } catch {
    return null
  }
}

/**
 * So'rovni yuboradi, 401 da bir marta refresh qilib qaytaradi va muvaffaqiyatli
 * `Response` beradi (aks holda `ApiError` tashlaydi).
 *
 * TANANI O'QIMAYDI — shu sababli JSON ham, fayl ham (`download`) shu yagona
 * yo'ldan o'tadi va token yangilash mantig'i IKKI JOYDA takrorlanmaydi.
 */
async function requestRaw(path: string, options: RequestOptions = {}): Promise<Response> {
  const withAuth = options.auth !== false
  const url = buildUrl(path, options.query)
  const tokenUsed = withAuth ? getAccessToken() : null

  let response = await send(url, options, tokenUsed)

  // 401 -> bir marta refresh qilib, so'rovni QAYTA yuboramiz.
  if (response.status === 401 && withAuth && path !== REFRESH_PATH) {
    const freshToken = await ensureFreshAccessToken(tokenUsed)
    if (freshToken !== null) {
      response = await send(url, options, freshToken)
    } else {
      endSession()
    }
  }

  if (!response.ok) throw await toApiError(response)
  return response
}

async function request<T>(path: string, options: RequestOptions = {}): Promise<T> {
  return await parseBody<T>(await requestRaw(path, options))
}

/** Yuklab olingan fayl: mazmun va server aytgan nom. */
export interface DownloadedFile {
  blob: Blob
  fileName: string
}

/**
 * `Content-Disposition` dan fayl nomini oladi.
 *
 * ASP.NET `filename*=UTF-8''...` (RFC 5987) shaklini ham yuboradi — o'zbekcha
 * nomlar uchun AYNAN o'sha to'g'ri, shuning uchun u birinchi tekshiriladi.
 */
function fileNameFrom(response: Response, fallback: string): string {
  const header = response.headers.get('content-disposition')
  if (header === null) return fallback

  const extended = /filename\*=UTF-8''([^;]+)/i.exec(header)?.[1]
  if (extended !== undefined) {
    try {
      return decodeURIComponent(extended.trim())
    } catch {
      // Buzuq kodlash — pastdagi oddiy shaklga tushamiz.
    }
  }

  const plain = /filename="?([^";]+)"?/i.exec(header)?.[1]
  return plain !== undefined ? plain.trim() : fallback
}

export const http = {
  get<T>(path: string, options?: Omit<RequestOptions, 'method' | 'body'>): Promise<T> {
    return request<T>(path, { ...options, method: 'GET' })
  },
  post<T>(path: string, body?: unknown, options?: Omit<RequestOptions, 'method' | 'body'>): Promise<T> {
    return request<T>(path, { ...options, method: 'POST', body })
  },
  put<T>(path: string, body?: unknown, options?: Omit<RequestOptions, 'method' | 'body'>): Promise<T> {
    return request<T>(path, { ...options, method: 'PUT', body })
  },
  patch<T>(path: string, body?: unknown, options?: Omit<RequestOptions, 'method' | 'body'>): Promise<T> {
    return request<T>(path, { ...options, method: 'PATCH', body })
  },
  delete<T>(path: string, options?: Omit<RequestOptions, 'method' | 'body'>): Promise<T> {
    return request<T>(path, { ...options, method: 'DELETE' })
  },
  /**
   * Faylni (CSV eksport) yuklab oladi.
   *
   * NEGA `window.open` yoki oddiy `<a href>` EMAS: bu endpoint `Authorization`
   * sarlavhasini talab qiladi, brauzer navigatsiyasi esa uni yubormaydi va
   * javob 401 bo'lardi. Shu yerdan o'tganda token yangilash ham ishlaydi.
   */
  async download(
    path: string,
    fallbackFileName: string,
    options?: Omit<RequestOptions, 'method' | 'body'>,
  ): Promise<DownloadedFile> {
    const response = await requestRaw(path, {
      ...options,
      method: 'GET',
      // Xato bo'lsa server baribir `application/problem+json` qaytaradi —
      // `*/*` shuning uchun ham kerak.
      headers: { Accept: 'text/csv, */*', ...options?.headers },
    })

    return { blob: await response.blob(), fileName: fileNameFrom(response, fallbackFileName) }
  },
} as const
