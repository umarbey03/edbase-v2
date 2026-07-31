import { computed, onScopeDispose, ref } from 'vue'
import type { ComputedRef, Ref } from 'vue'

import type { User } from '@/entities/user'
import { useAuthStore } from '@/features/auth/model/auth.store'
import { isApiError, toUserMessage } from '@/shared/api'
import { readTelegramInitData } from '@/shared/lib/telegram-web-app'

/**
 * TELEGRAM MINI APP KIRISH OQIMI (holat mashinasi).
 *
 * Ekranning O'ZI `ui/TelegramAuthScreen.vue` da; bu yerda faqat holat va
 * so'rov. Ajratish sababi: xato kodlariga qanday MATN mos kelishi —
 * dizayn masalasi, qachon so'rov yuborilishi esa mantiq.
 */

/** `initData` umuman yo'q — serverga so'rov yuborishning ma'nosi yo'q. */
export const INIT_DATA_YOQ = -1

export type TelegramAuthStage =
  /** So'rov ketmoqda (yoki hozir ketadi) — yuklanish ekrani. */
  | 'kirilmoqda'
  /** Foydalanuvchining harakatini kutamiz (chiqishdan keyin). */
  | 'kutilmoqda'
  | 'xato'
  | 'kirildi'

/**
 * "Foydalanuvchi O'ZI chiqdi" belgisi — MODUL darajasida, ya'ni sahifa
 * yangilanguncha yashaydi.
 *
 * NEGA KERAK: Mini App'da kirish AVTOMATIK. Chiqish tugmasi bosilgandan keyin
 * kirish ekrani yana ochilsa, u darhol qayta kirib olardi va "chiqish"
 * hech qanday ta'sir ko'rsatmagandek tuyulardi. Belgi qo'yilgandan keyin
 * ekran avtomatik kirmaydi — tugmani bosishni kutadi.
 *
 * `localStorage` GA YOZILMAYDI: ilova qayta ochilganda (yangi sessiya)
 * odatdagi avtomatik kirish tiklanishi kerak.
 */
let manualLogout = false

/** Foydalanuvchi Mini App ichida "Chiqish" ni bosdi. */
export function markMiniAppLogout(): void {
  manualLogout = true
}

export interface TelegramAuthFlow {
  stage: Ref<TelegramAuthStage>
  /** Oxirgi xatoning HTTP kodi (`0` — tarmoq, `INIT_DATA_YOQ` — ma'lumot yo'q). */
  status: Ref<number>
  /** Foydalanuvchiga ko'rsatiladigan matn — `toUserMessage` dan. */
  message: Ref<string>
  /** 429 dan keyin qayta urinishgacha qolgan soniya (`0` — cheklov yo'q). */
  cooldown: Ref<number>
  canRetry: ComputedRef<boolean>
  /** Ekran ochilganda bir marta chaqiriladi. */
  begin: () => Promise<void>
  /** "Qayta urinish" tugmasi. */
  retry: () => Promise<void>
}

export function useTelegramAuth(onSuccess: (user: User) => void | Promise<void>): TelegramAuthFlow {
  const auth = useAuthStore()

  const stage = ref<TelegramAuthStage>('kirilmoqda')
  const status = ref(0)
  const message = ref('')
  const cooldown = ref(0)

  let cooldownTimer: number | null = null

  function stopCooldown(): void {
    if (cooldownTimer === null) return
    window.clearInterval(cooldownTimer)
    cooldownTimer = null
  }

  /*
    429 dan keyin tugma `Retry-After` soniyasigacha o'chirilib turadi.
    Aks holda o'quvchi tugmani ketma-ket bosib, oynani yana uzaytirardi —
    bu `ApiError.userMessage` dagi izohda tasvirlangan aynan o'sha xatti-harakat.
  */
  function startCooldown(seconds: number): void {
    stopCooldown()
    cooldown.value = seconds
    if (seconds <= 0) return
    cooldownTimer = window.setInterval(() => {
      cooldown.value -= 1
      if (cooldown.value <= 0) stopCooldown()
    }, 1000)
  }

  onScopeDispose(stopCooldown)

  function fail(code: number, text: string, retryAfter: number | null): void {
    status.value = code
    message.value = text
    stage.value = 'xato'
    startCooldown(retryAfter ?? 0)
  }

  async function attempt(): Promise<void> {
    stopCooldown()
    cooldown.value = 0
    stage.value = 'kirilmoqda'

    const initData = readTelegramInitData()
    if (initData.length === 0) {
      // Telegram muhiti aniqlangan, lekin imzolangan ma'lumot yo'q — bu
      // odatda ilova botdagi tugmadan emas, boshqa yo'l bilan ochilganini
      // bildiradi. Serverga bo'sh satr yuborib 401 olishning ma'nosi yo'q.
      fail(
        INIT_DATA_YOQ,
        'Telegram kirish ma’lumotini bermadi.',
        null,
      )
      return
    }

    try {
      const user = await auth.loginWithTelegram(initData)
      manualLogout = false
      stage.value = 'kirildi'
      await onSuccess(user)
    } catch (error) {
      // Xato MATNINI o'zimiz yig'maymiz: server `ProblemDetails.detail` da
      // o'zbekcha sababni beradi va `toUserMessage` uni to'g'ri o'qiydi.
      fail(
        isApiError(error) ? error.status : 0,
        toUserMessage(error),
        isApiError(error) ? error.retryAfterSeconds : null,
      )
    }
  }

  function begin(): Promise<void> {
    if (manualLogout) {
      stage.value = 'kutilmoqda'
      return Promise.resolve()
    }
    return attempt()
  }

  function retry(): Promise<void> {
    if (cooldown.value > 0) return Promise.resolve()
    manualLogout = false
    return attempt()
  }

  const canRetry = computed(() => stage.value !== 'kirilmoqda' && cooldown.value === 0)

  return { stage, status, message, cooldown, canRetry, begin, retry }
}
