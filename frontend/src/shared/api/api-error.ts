import type { ProblemDetails } from '@/shared/types'

/** RFC 7807 ProblemDetails'dan hosil qilingan turlangan xato (SPEC 5). */
export class ApiError extends Error {
  readonly status: number
  readonly problem: ProblemDetails | null
  readonly traceId: string | null

  constructor(status: number, problem: ProblemDetails | null, fallbackMessage: string) {
    super(problem?.detail ?? problem?.title ?? fallbackMessage)
    this.name = 'ApiError'
    this.status = status
    this.problem = problem
    this.traceId = problem?.traceId ?? null
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
    if (this.status === 403) return 'Bu amal uchun ruxsatingiz yo‘q.'
    if (this.status === 404) return 'So‘ralgan ma’lumot topilmadi.'
    if (this.status === 429) return 'Juda tez-tez so‘rov yubordingiz. Biroz kuting.'
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
