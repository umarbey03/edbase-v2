import { computed } from 'vue'
import type { ComputedRef } from 'vue'
import { useRoute } from 'vue-router'

import { useShopMe } from '@/entities/shop'
import { useMySubscription } from '@/entities/subscription'
import { GUEST_NAV, navItemsForRole, SELF_STUDY_NAV } from '@/entities/user'
import type { NavItem } from '@/entities/user'

/**
 * O'QUVCHI NAVIGATSIYASI — IKKI KO'RINISH, BITTA MANBA.
 *
 * `STUDENT_NAV` (`entities/user/model/navigation.ts`) endi ikki joyda
 * chiziladi: telefonda `StudentTabBar` (pastki 5 tab), desktopda
 * `StudentSidebar` (yon ustun). Ro'yxatning O'ZI allaqachon bitta manbada
 * turibdi, lekin "qaysi band yonib turadi" qoidasi ham bitta bo'lishi kerak —
 * u ahamiyatsiz emas (pastdagi izohga qarang), va ikki nusxada saqlansa,
 * biri kunlardan bir kun ikkinchisidan orqada qolardi: masalan `/oquv` ga
 * yangi ichki sahifa qo'shilsa, telefonda "O'quv" yonib, desktopda o'chib
 * turardi.
 *
 * ★ NEGA `ui/` ICHIDA: bu widget'da (loyihadagi hech bir widget'da) `model/`
 * qatlami yo'q, va bu qoida FAQAT shu ikki komponentga tegishli — uni
 * `shared/` ga ko'tarish hech kim ishlatmaydigan umumiy API yaratardi.
 */
export interface StudentNav {
  /** `STUDENT_NAV` — tartib, nom va ikonka MAHSULOT QARORI bilan muzlatilgan. */
  items: ComputedRef<NavItem[]>
  /** Shu marshrut nomi hozir "faol band" sifatida ko'rsatilishi kerakmi. */
  isActive: (routeName: string) => boolean
}

export function useStudentNav(): StudentNav {
  const route = useRoute()

  // Mustaqil o'quvchida Kalendar va Chat o'rniga Profil (2026-10-23).
  const { isSelfStudy } = useMySubscription()
  // Mehmonda (guruhsiz, obunasiz) faqat Tariflar va Profil (2026-10-29).
  const { isGuest } = useShopMe()
  const items = computed(() => {
    if (isGuest.value) return GUEST_NAV
    return isSelfStudy.value ? SELF_STUDY_NAV : navItemsForRole('Student')
  })

  /*
    ★ `active-class` / `exact-active-class` YETMAYDI: "O'quv" bandi `/oquv`
    dan tashqari uning ICHKI sahifalarida ham yonib turishi kerak
    (`student-assignments`, `student-tests`, `student-test-take`,
    `student-recordings`) — eski ilovada vazifa, test va yozuvlar aynan
    "O'quv" ning ichida edi. Shuning uchun qoida qo'lda yoziladi.
  */
  function isActive(routeName: string): boolean {
    if (route.name === routeName) return true
    if (routeName !== 'student-learn') return false
    return typeof route.name === 'string' && route.name.startsWith('student-')
      && route.path.startsWith('/oquv')
  }

  return { items, isActive }
}
