import { ApiError, getAccessToken, refreshAccessToken } from '@/shared/api'
import { apiUrl } from '@/shared/config/env'
import type { ProblemDetails } from '@/shared/types'

/**
 * ========================================================================
 * PROGRESS BILAN FAYL YUKLASH (`XMLHttpRequest`)
 * ========================================================================
 *
 * 🔴 NEGA `fetch` EMAS: `fetch` da YUKLASH (upload) progressi YO'Q —
 * `ReadableStream` faqat javobni o'qishga beriladi. 1 GB dars videosini
 * ko'rsatkichsiz yuborish "ilova qotib qoldi" degan taassurot beradi va
 * xodim sahifani yangilab, yuklashni boshidan boshlaydi. Shuning uchun bu
 * yagona joyda `XMLHttpRequest` ishlatiladi (`shared/api/http.ts` qolgan
 * hamma so'rov uchun kuchda qoladi).
 *
 * NIMANI TAKRORLAMAYDI: xato shakli (`ApiError` + RFC 7807) va token
 * saqlagichi `shared/api` dan olinadi — ya'ni `toUserMessage(error)` bu
 * yerdagi xatolarni ham o'zi tushunadi va xato matnlari ikki xil bo'lib
 * ketmaydi.
 *
 * ── ⚠️ 401 VA UZUN YUKLASH ────────────────────────────────────────────
 *
 * Access token 15 daqiqa yashaydi, 1 GB video esa sekin internetda undan
 * uzoq yuklanadi. Shuning uchun 401 da token yangilanib, so'rov BIR MARTA
 * qaytariladi — lekin bu FAYLNI QAYTA YUBORISH degani (HTTP'da yuborilgan
 * tanani "davom ettirish" yo'li yo'q). To'liq yechim — server tomonida
 * uzoq muddatli yuklash tokeni yoki bo'lakli (resumable) yuklash; ikkisi
 * ham backend ishi va hisobotda qayd etilgan.
 *
 * ── 🔴 `xhr.status === 0` — IKKI MA'NOLI HOLAT ────────────────────────
 *
 * nginx yuklash `location` ida `proxy_request_buffering off` bilan ishlaydi
 * (`infra/nginx/zinnur.conf`). Bunda backend so'rovni
 * ERTA rad etsa (401, 413) nginx tanani hali qabul qilayotgan bo'ladi va
 * ulanish uziladi — brauzer status kod O'RNIGA "network error" ko'rsatadi.
 * Ya'ni status 0 ni faqat "internet yo'q" deb tarjima qilish YOLG'ON
 * bo'lardi: eng ehtimolli sabab — fayl chegaradan katta. Xabar uchala
 * sababni ham aytadi.
 */

/** Yuklash holati — foiz VA baytlar (foizning o'zi "qancha qoldi"ni aytmaydi). */
export interface UploadProgress {
  loaded: number
  total: number
  /** 0…100, butun son. `total` noma'lum bo'lsa 0. */
  percent: number
}

export interface UploadRequest {
  /** API yo'li (`/api/v1/lessons/12/assets`) — to'liq manzil emas. */
  path: string
  form: FormData
  onProgress?: (progress: UploadProgress) => void
  /** Bekor qilish (`xhr.abort()`) va komponent yo'q qilinganda tozalash. */
  signal?: AbortSignal
}

/**
 * FOYDALANUVCHI bekor qilgan yuklash.
 *
 * Alohida sinf: bekor qilish XATO EMAS va ro'yxatda qizil qator sifatida
 * ko'rinmasligi kerak ("Bekor qilindi" deb ko'rinadi).
 */
export class UploadCancelledError extends Error {
  constructor() {
    super('Yuklash bekor qilindi.')
    this.name = 'UploadCancelledError'
    Object.setPrototypeOf(this, UploadCancelledError.prototype)
  }
}

export function isUploadCancelled(error: unknown): error is UploadCancelledError {
  return error instanceof UploadCancelledError
}

function parseProblem(raw: string, contentType: string): ProblemDetails | null {
  if (raw.length === 0) return null
  try {
    if (contentType.includes('json')) {
      const parsed: unknown = JSON.parse(raw)
      if (typeof parsed === 'object' && parsed !== null && !Array.isArray(parsed)) {
        return parsed as ProblemDetails
      }
      return null
    }
  } catch {
    // Buzuq JSON — pastda oddiy matn sifatida olinadi.
  }
  return { title: raw.slice(0, 300) }
}

/** 0 dan boshqa har qanday status — `ApiError` (RFC 7807 tanasi bilan). */
function toApiError(xhr: XMLHttpRequest): ApiError {
  const contentType = xhr.getResponseHeader('content-type') ?? ''
  const problem = parseProblem(xhr.responseText ?? '', contentType)
  const retryAfterRaw = xhr.getResponseHeader('retry-after')
  const retryAfter = retryAfterRaw === null ? Number.NaN : Number(retryAfterRaw)

  return new ApiError(
    xhr.status,
    problem,
    `HTTP ${xhr.status}`,
    Number.isFinite(retryAfter) && retryAfter >= 0 ? Math.ceil(retryAfter) : null,
  )
}

/** Tarmoq uzilishi / erta rad etilish (yuqoridagi izoh: status 0). */
function networkError(): ApiError {
  return new ApiError(
    0,
    null,
    'Yuklash uzildi. Ehtimol sabablar: fayl hajmi ruxsat etilgan chegaradan katta '
      + '(server ulanishni erta yopadi), internet aloqasi uzilgan yoki sessiya '
      + 'muddati tugagan. Fayl hajmini tekshirib, qaytadan urinib ko‘ring.',
  )
}

function sendOnce<T>(request: UploadRequest, token: string | null): Promise<T> {
  return new Promise<T>((resolve, reject) => {
    if (request.signal?.aborted === true) {
      reject(new UploadCancelledError())
      return
    }

    const xhr = new XMLHttpRequest()
    xhr.open('POST', apiUrl(request.path), true)
    xhr.responseType = 'text'

    /*
      `timeout` QO'YILMAYDI (standart 0 = cheksiz). 1 GB fayl sekin mobil
      internetda yarim soatdan ko'p yuklanishi mumkin; har qanday "aql
      bovar qiladigan" timeout aynan eng muhim holatda uzib qo'yardi.
      Foydalanuvchi kutishni O'ZI to'xtatadi ("Bekor qilish").
    */
    xhr.setRequestHeader('Accept', 'application/json')
    if (token !== null) xhr.setRequestHeader('Authorization', `Bearer ${token}`)

    /*
      🔴 `Content-Type` QO'LDA QO'YILMAYDI: `multipart/form-data` sarlavhasi
      `boundary` bilan bo'lishi shart va uni brauzer `FormData` dan o'zi
      hosil qiladi (`shared/api/http.ts` dagi ayni izoh).
    */

    const onAbort = (): void => xhr.abort()
    request.signal?.addEventListener('abort', onAbort, { once: true })

    const cleanup = (): void => request.signal?.removeEventListener('abort', onAbort)

    xhr.upload.onprogress = (event: ProgressEvent): void => {
      if (request.onProgress === undefined) return
      const total = event.lengthComputable ? event.total : 0
      request.onProgress({
        loaded: event.loaded,
        total,
        percent: total > 0 ? Math.min(100, Math.round((event.loaded / total) * 100)) : 0,
      })
    }

    xhr.onload = (): void => {
      cleanup()
      if (xhr.status >= 200 && xhr.status < 300) {
        const raw = xhr.responseText ?? ''
        if (raw.length === 0) {
          resolve(undefined as T)
          return
        }
        try {
          resolve(JSON.parse(raw) as T)
        } catch {
          reject(new ApiError(xhr.status, null, 'Server javobini o‘qib bo‘lmadi.'))
        }
        return
      }
      reject(toApiError(xhr))
    }

    xhr.onerror = (): void => {
      cleanup()
      reject(networkError())
    }

    xhr.onabort = (): void => {
      cleanup()
      reject(new UploadCancelledError())
    }

    xhr.send(request.form)
  })
}

/**
 * Faylni yuboradi va javob tanasini qaytaradi (`201 + DTO`).
 *
 * 401 da tokenni yangilab BIR MARTA qaytaradi (yuqoridagi izoh). Bekor
 * qilinganda `UploadCancelledError`, boshqa hollarda `ApiError` tashlaydi —
 * ya'ni chaqiruvchi `toUserMessage(error)` bilan ishlaydi.
 */
export async function uploadWithProgress<T>(request: UploadRequest): Promise<T> {
  const token = getAccessToken()

  try {
    return await sendOnce<T>(request, token)
  } catch (error) {
    if (!(error instanceof ApiError) || error.status !== 401) throw error
    if (request.signal?.aborted === true) throw new UploadCancelledError()

    /*
      Token yangilanmasa (sessiya tugagan) `refreshAccessToken` ning o'zi
      seansni yopadi va `notifyAuthExpired` ishga tushadi — bu yerda
      qo'shimcha hech narsa qilinmaydi, asl 401 yuqoriga uzatiladi.
    */
    const fresh = await refreshAccessToken().catch(() => null)
    if (fresh === null) throw error

    // Progress noldan boshlanadi: fayl QAYTA yuboriladi.
    request.onProgress?.({ loaded: 0, total: 0, percent: 0 })
    return await sendOnce<T>(request, fresh)
  }
}
