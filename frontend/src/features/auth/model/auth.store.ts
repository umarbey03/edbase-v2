import { defineStore } from 'pinia'
import { computed, ref, shallowRef } from 'vue'

import {
  fetchMe,
  loginWithTelegram as telegramLoginRequest,
  logout as logoutRequest,
  requestPhoneCode as requestPhoneCodeRequest,
  verifyPhoneCode as verifyPhoneCodeRequest,
} from '@/entities/user'
import type { User } from '@/entities/user'
import { clearTokens, getRefreshToken, onAuthExpired, refreshAccessToken, setTokens } from '@/shared/api'
import type { AuthResponse, PhoneCodeResponse } from '@/shared/types'

/**
 * Autentifikatsiya store'i.
 *
 * Token'larning O'ZI bu yerda saqlanmaydi — ular `shared/api/tokens.ts` da
 * (access xotirada, refresh localStorage'da). Store faqat foydalanuvchi profili va
 * sessiya holatini boshqaradi. Shu tufayli `http.ts` store'ga bog'lanmaydi
 * (aylanma import bo'lmaydi).
 */
export const useAuthStore = defineStore('auth', () => {
  // `shallowRef` — `User` oddiy o'zgarmas obyekt, chuqur proksi shart emas.
  const user = shallowRef<User | null>(null)
  const isReady = ref(false)

  // Sahifa yangilanganda bir nechta guard bir vaqtda `bootstrap()` chaqirishi mumkin —
  // bitta-uchish (single-flight) qilib qo'yamiz.
  let bootstrapPromise: Promise<void> | null = null

  const isAuthenticated = computed(() => user.value !== null)
  const displayName = computed(() => user.value?.fullName ?? '')
  const role = computed(() => user.value?.role ?? null)
  const userId = computed(() => user.value?.id ?? null)

  /**
   * Muvaffaqiyatli javobni SESSIYAGA aylantiradi.
   *
   * ★ IKKALA kirish eshigi (telefon + kod va Telegram Mini App) shu YAGONA
   * funksiyadan o'tadi. Har biri o'zi `setTokens` chaqirsa, kelajakda
   * sessiyaga qo'shiladigan qadam (masalan tokenni boshqacha saqlash yoki
   * audit) bittasida esdan chiqib, ikki yo'l bir-biridan uzoqlashardi —
   * eski tizimning Telegram zaifligi ham aynan shunday "ikkinchi yo'l"
   * bo'lgani uchun paydo bo'lgan edi.
   *
   * ⚠️ Uchinchi eshik (email + parol) 2026-08-13 da OLIB TASHLANDI.
   */
  function applySession(response: AuthResponse): User {
    setTokens({ accessToken: response.accessToken, refreshToken: response.refreshToken })
    user.value = response.user
    isReady.value = true
    return response.user
  }

  /**
   * 1-BOSQICH: telefon raqamiga bir martalik kod so'rash.
   *
   * 🔴 SESSIYA OCHILMAYDI va foydalanuvchi hali ANIQLANMAYDI — bu ataylab.
   * Javob raqam bazada bor yoki yo'qligidan qat'i nazar AYNI bo'ladi
   * (hisob sanashga qarshi), shuning uchun bu yerdan "foydalanuvchi
   * topildi" degan xulosa chiqarib bo'lmaydi va chiqarishga urinmaslik
   * kerak.
   */
  async function requestPhoneCode(phone: string): Promise<PhoneCodeResponse> {
    return requestPhoneCodeRequest({ phone })
  }

  /** 2-BOSQICH: kodni tasdiqlash — SESSIYA aynan shu yerda ochiladi. */
  async function verifyPhoneCode(phone: string, code: string): Promise<User> {
    return applySession(await verifyPhoneCodeRequest({ phone, code }))
  }

  /**
   * Telegram Mini App orqali kirish.
   *
   * 🔴 `initData` bu yerda ham, quyi qatlamda ham PARCHALANMAYDI va
   * saqlanmaydi: u faqat argument sifatida o'tib, so'rov tanasiga tushadi.
   * Kimligini server imzodan aniqlaydi — frontend hech qanday shaxsiy
   * ma'lumot (telefon, `telegramId`) yubormaydi.
   */
  async function loginWithTelegram(initData: string): Promise<User> {
    return applySession(await telegramLoginRequest(initData))
  }

  async function logout(): Promise<void> {
    try {
      await logoutRequest()
    } catch {
      // Server javob bermasa ham lokal sessiyani tozalaymiz.
    }
    clearTokens()
    user.value = null
  }

  async function reloadProfile(): Promise<void> {
    user.value = await fetchMe()
  }

  /** Ilova ishga tushganda sessiyani tiklaydi (refresh token bo'lsa). */
  function bootstrap(): Promise<void> {
    if (isReady.value) return Promise.resolve()
    if (bootstrapPromise !== null) return bootstrapPromise

    bootstrapPromise = (async () => {
      if (getRefreshToken() === null) return
      try {
        await refreshAccessToken()
        await reloadProfile()
      } catch {
        clearTokens()
        user.value = null
      }
    })().finally(() => {
      isReady.value = true
      bootstrapPromise = null
    })

    return bootstrapPromise
  }

  // Refresh ham ishlamay qolganda (`http.ts` xabar beradi) profilni tozalaymiz.
  onAuthExpired(() => {
    user.value = null
  })

  return {
    user,
    userId,
    role,
    displayName,
    isReady,
    isAuthenticated,
    bootstrap,
    loginWithTelegram,
    logout,
    reloadProfile,
    requestPhoneCode,
    verifyPhoneCode,
  }
})
