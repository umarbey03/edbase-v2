import { QueryClient } from '@tanstack/vue-query'

import { isApiError } from '@/shared/api'

/**
 * Server holati uchun yagona `QueryClient`.
 * 4xx xatolarda qayta urinish mantiqsiz (401 ni `http.ts` o'zi hal qiladi).
 */
export const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: 30_000,
      gcTime: 5 * 60_000,
      refetchOnWindowFocus: false,
      retry(failureCount: number, error: Error): boolean {
        if (isApiError(error) && error.status >= 400 && error.status < 500) return false
        return failureCount < 2
      },
    },
    mutations: {
      retry: false,
    },
  },
})
