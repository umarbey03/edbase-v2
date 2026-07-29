import type { Router } from 'vue-router'

import { onAuthExpired } from '@/shared/api'

export { queryClient } from './query-client'

/**
 * Refresh ham ishlamaganda foydalanuvchini login sahifasiga qaytaradi.
 * Router `shared` qatlamiga bog'lanmasligi uchun ulanish shu yerda amalga oshiriladi.
 */
export function registerSessionExpiryRedirect(router: Router): void {
  onAuthExpired(() => {
    const current = router.currentRoute.value
    if (current.name === 'login') return
    void router.replace({
      name: 'login',
      query: { redirect: current.fullPath, sabab: 'sessiya-tugadi' },
    })
  })
}
