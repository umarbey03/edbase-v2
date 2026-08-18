import type { RouteLocationRaw } from 'vue-router'

import type { IconName } from '@/shared/ui'

/**
 * QIDIRUV NATIJASIDAN MARSHRUTGA (2026-08-18).
 *
 * ★ NEGA MARSHRUT BACKENDDAN KELMAYDI: server ma'lumot turini biladi,
 * lekin ilovaning marshrut nomlarini BILMASLIGI kerak. Aks holda
 * frontendagi har URL o'zgarishi backend o'zgarishini talab qilardi.
 * Server `type` + `id` beradi, xarita esa shu yerda.
 */

/** Tur bo'yicha ikonka — natija qatorini bir qarashda tanish uchun. */
const ICONS: Record<string, IconName> = {
  users: 'user',
  groups: 'grid',
  courses: 'file-text',
  tests: 'award',
  assignments: 'clipboard',
}

export function hitIcon(type: string): IconName {
  return ICONS[type] ?? 'search'
}

/**
 * Natija bosilganda qayerga o'tiladi.
 *
 * ★ FOYDALANUVCHI — `?profil=<id>` SO'ROV PARAMETRI bilan: profil
 * alohida sahifa emas, "Foydalanuvchilar" ro'yxati ustidan ochiladigan
 * panel. Parametr qo'shilishi uni HAVOLA QILINADIGAN qildi — qidiruv
 * natijasi ham, xabardagi havola ham to'g'ridan-to'g'ri ocha oladi.
 */
export function hitRoute(type: string, id: number): RouteLocationRaw | null {
  switch (type) {
    case 'users':
      return { name: 'manage-users', query: { profil: String(id) } }

    case 'groups':
      return { name: 'teacher-group', params: { groupId: String(id) } }

    case 'courses':
      return { name: 'manage-course', params: { courseId: String(id) } }

    case 'tests':
      return { name: 'manage-test', params: { testId: String(id) } }

    case 'assignments':
      // Vazifaning O'ZIGA marshrut yo'q — ro'yxat sahifasi ochiladi va
      // qidiruv matni u yerda ham qo'llanadi.
      return { name: 'manage-assignments' }

    default:
      return null
  }
}
