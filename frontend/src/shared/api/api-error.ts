import type { ProblemDetails } from '@/shared/types'

/** RFC 7807 ProblemDetails'dan hosil qilingan turlangan xato (SPEC 5). */
export class ApiError extends Error {
  readonly status: number
  readonly problem: ProblemDetails | null
  readonly traceId: string | null

  /**
   * `Retry-After` sarlavhasidagi soniya (429 javobida). `null` — server
   * aytmagan. Sarlavha sana ko'rinishida ham kelishi mumkin, lekin bizning
   * server doim soniya yuboradi (`Program.cs` dagi `OnRejected`).
   */
  readonly retryAfterSeconds: number | null

  constructor(
    status: number,
    problem: ProblemDetails | null,
    fallbackMessage: string,
    retryAfterSeconds: number | null = null,
  ) {
    super(problem?.detail ?? problem?.title ?? fallbackMessage)
    this.name = 'ApiError'
    this.status = status
    this.problem = problem
    this.traceId = problem?.traceId ?? null
    this.retryAfterSeconds = retryAfterSeconds
    // `Error` dan meros olishda prototip zanjiri buzilmasligi uchun (bundler target'i
    // pastroq bo'lsa ham `instanceof ApiError` ishlashi kerak).
    Object.setPrototypeOf(this, ApiError.prototype)
  }

  get isUnauthorized(): boolean {
    return this.status === 401
  }

  get isForbidden(): boolean {
    return this.status === 403
  }

  get isNetworkError(): boolean {
    return this.status === 0
  }

  /** Validatsiya xatolarini bitta o'qiladigan satrga yig'adi. */
  get validationSummary(): string | null {
    const errors = this.problem?.errors
    if (!errors) return null
    const parts: string[] = []
    for (const messages of Object.values(errors)) {
      for (const message of messages) parts.push(message)
    }
    return parts.length > 0 ? parts.join(' ') : null
  }

  /** Foydalanuvchiga ko'rsatish uchun o'zbekcha matn. */
  get userMessage(): string {
    if (this.isNetworkError) return 'Serverga ulanib bo‘lmadi. Internet aloqasini tekshiring.'
    if (this.status === 401) return 'Sessiya muddati tugagan. Qaytadan kiring.'
    /*
      403 — SERVER SABABINI ko'rsatamiz, bor bo'lsa.

      Server aniq matn beradi: "Faqat o'z guruhingizga vazifa bera olasiz",
      "Bu kurs sizning guruhingizga biriktirilmagan", "Profilingiz faol emas".
      Ilgari hammasi "Bu amal uchun ruxsatingiz yo'q" bilan almashtirilardi va
      foydalanuvchi NIMA qilishini bilmasdi — xodim esa "tizim ishlamayapti"
      deb qo'ng'iroq qilardi.

      `ForbiddenException` matnlari ATAYLAB foydalanuvchiga mo'ljallangan
      (ichki tafsilot yoki stack trace emas), shuning uchun ko'rsatish xavfsiz.
    */
    if (this.status === 403) {
      const reason = this.problem?.detail ?? ''
      return reason.length > 0 ? reason : 'Bu amal uchun ruxsatingiz yo‘q.'
    }

    if (this.status === 404) return 'So‘ralgan ma’lumot topilmadi.'

    /*
      429 — server `Retry-After` da ANIQ soniyani yuboradi. "Biroz kuting"
      o'rniga aniq vaqt aytilsa, foydalanuvchi qayta-qayta bosib oynani
      yana uzaytirmaydi.
    */
    if (this.status === 429) {
      return this.retryAfterSeconds === null
        ? 'Juda tez-tez so‘rov yubordingiz. Biroz kuting.'
        : `Juda tez-tez so‘rov yubordingiz. ${this.retryAfterSeconds} soniyadan so‘ng urinib ko‘ring.`
    }

    /*
      503 — "xizmat vaqtincha mavjud emas" (masalan fayl ombori sozlanmagan).
      Bu bizning bug'imiz EMAS va server xabarini ATAYLAB foydalanuvchiga
      ko'rsatadi: `ExceptionHandlingMiddleware` faqat AYNAN 500 ni yashiradi,
      qolgan 5xx `detail` i o'zgarishsiz qoladi. Uni umumiy "serverda xatolik"
      matni bilan almashtirsak, o'quvchi yagona foydali maslahatni ("hozir
      matnli javob yuborishingiz mumkin") ko'rmasdi.
    */
    const detail = this.problem?.detail ?? ''
    if (this.status === 503 && detail.length > 0) return detail

    if (this.status >= 500) return 'Serverda xatolik yuz berdi. Birozdan so‘ng urinib ko‘ring.'
    return this.validationSummary ?? this.message
  }
}

export function isApiError(value: unknown): value is ApiError {
  return value instanceof ApiError
}

/** Ixtiyoriy `unknown` xatodan foydalanuvchiga ko'rsatiladigan matn oladi. */
export function toUserMessage(error: unknown): string {
  if (isApiError(error)) return error.userMessage
  if (error instanceof Error && error.message.length > 0) return error.message
  return 'Kutilmagan xatolik yuz berdi.'
}
