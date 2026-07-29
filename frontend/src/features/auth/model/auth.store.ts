import { defineStore } from 'pinia'
import { computed, ref, shallowRef } from 'vue'

import { fetchMe, login as loginRequest, logout as logoutRequest } from '@/entities/user'
import type { User } from '@/entities/user'
import { clearTokens, getRefreshToken, onAuthExpired, refreshAccessToken, setTokens } from '@/shared/api'
import type { LoginRequest } from '@/shared/types'

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

  async function login(payload: LoginRequest): Promise<User> {
    const response = await loginRequest(payload)
    setTokens({ accessToken: response.accessToken, refreshToken: response.refreshToken })
    user.value = response.user
    isReady.value = true
    return response.user
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
    login,
    logout,
    reloadProfile,
  }
})
