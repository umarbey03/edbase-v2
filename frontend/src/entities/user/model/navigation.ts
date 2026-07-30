import { lookup } from '@/shared/lib/lookup'
import type { UserRoleName } from '@/shared/types'
import type { IconName } from '@/shared/ui'

/**
 * Rol -> menyu va bosh sahifa xaritasi.
 *
 * NEGA `entities/user` da? Chunki buni IKKI joy o'qiydi: router guard'i
 * (`app/`) va yon menyu (`widgets/`). FSD'da widget `app/` dan import
 * qila olmaydi, shuning uchun umumiy bilim eng pastki mos qatlamga —
 * foydalanuvchi entity'siga qo'yilgan. Bu yerda faqat MA'LUMOT bor
 * (marshrut nomlari satr sifatida), router bog'liqligi yo'q.
 */
export interface NavItem {
  /** `vue-router` dagi marshrut nomi. */
  routeName: string
  label: string
  icon: IconName
}

/**
 * O'QUVCHI — pastki 5 tab (Telegram Mini App karkasi).
 *
 * ★ TARTIB, NOM va IKONKA eski ilovadan (`student.html`, `<nav class="tabbar">`)
 * AYNAN ko'chirilgan va O'ZGARTIRILMAYDI. Bugungi o'quvchilar shu beshtasini
 * yodlab olishgan; nomni "yaxshilash" yoki tartibni almashtirish ular uchun
 * qayta o'rganish demakdir.
 *
 * Xodim menyusidan farqli o'laroq bu ro'yxat yon menyuda emas, `StudentTabBar`
 * da chiziladi — lekin manba bitta bo'lishi uchun shu yerda turadi.
 */
const STUDENT_NAV: NavItem[] = [
  { routeName: 'student-home', label: 'Bosh sahifa', icon: 'home' },
  { routeName: 'student-calendar', label: 'Kalendar', icon: 'calendar' },
  { routeName: 'student-learn', label: 'O‘quv', icon: 'book' },
  { routeName: 'student-rating', label: 'Reyting', icon: 'chart' },
  { routeName: 'student-chat', label: 'Chat', icon: 'message-circle' },
]

const TEACHER_NAV: NavItem[] = [
  { routeName: 'teacher-home', label: 'Bosh sahifa', icon: 'graduation' },
  { routeName: 'teacher-groups', label: 'Guruhlarim', icon: 'users' },
  { routeName: 'teacher-sessions', label: 'Darslarim', icon: 'calendar' },
  { routeName: 'teacher-grading', label: 'Tekshirish', icon: 'clipboard' },
  { routeName: 'teacher-chat', label: 'Savollar', icon: 'message-circle' },
]

/**
 * Kurator menyusi ustoznikidan FARQ QILADI — eski `teacher.html` dagi
 * `{% if user.role == 'assistant' %}` shartlari bo'yicha:
 *   • "Kuratorlik" FAQAT kuratorda (ustozda yo'q);
 *   • ustozda "Tekshirish" va "Chatlar" bor edi — v2 da ular
 *     "Vazifalar" (baholash) va "Savollar" (DM) ga to'g'ri keladi.
 *
 * "Darslarim" — v2 qo'shimchasi: eski panelda darslar faqat guruh ichidagi
 * "Darslar" tabida edi. Menyudan OLIB TASHLANMADI, chunki u ishlaydigan
 * sahifa va uni yo'qotish funksiyani kamaytirardi; qo'shimcha band esa
 * mavjud bandlarning tartibi va nomini buzmaydi.
 */
const ASSISTANT_NAV: NavItem[] = [
  { routeName: 'teacher-home', label: 'Bosh sahifa', icon: 'graduation' },
  { routeName: 'teacher-groups', label: 'Guruhlarim', icon: 'users' },
  { routeName: 'teacher-sessions', label: 'Darslarim', icon: 'calendar' },
  { routeName: 'teacher-curator', label: 'Kuratorlik', icon: 'user-check' },
  { routeName: 'teacher-chat', label: 'Savollar', icon: 'message-circle' },
]

/*
  TARTIB eski `academic.html` menyusidan: Guruhlar -> Foydalanuvchilar ->
  Testlar -> To'lovlar -> Moliya -> Kurs quruvchi. "Jonli darslar" va
  "Uy vazifalari" — v2 qo'shimchalari, ular eski bandlarning KETMA-KETLIGINI
  buzmasligi uchun oxiriga qo'yilgan.

  "Kurs quruvchi" nomi ham eskisidan: v2 da "Kurs kontenti" deb o'zgartirilgan
  edi, lekin o'quv bo'limi xodimi menyuda o'sha eski so'zni qidiradi.
*/
const MANAGE_NAV: NavItem[] = [
  { routeName: 'manage-groups', label: 'Guruhlar', icon: 'grid' },
  { routeName: 'manage-users', label: 'Foydalanuvchilar', icon: 'users' },
  { routeName: 'manage-tests', label: 'Testlar', icon: 'award' },
  { routeName: 'manage-payments', label: 'To\u2018lovlar', icon: 'star' },
  { routeName: 'manage-finance', label: 'Moliya', icon: 'chart' },
  { routeName: 'manage-courses', label: 'Kurs quruvchi', icon: 'file-text' },
  { routeName: 'manage-sessions', label: 'Jonli darslar', icon: 'calendar' },
  { routeName: 'manage-assignments', label: 'Uy vazifalari', icon: 'clipboard' },
]

const NAV_BY_ROLE: Record<UserRoleName, NavItem[]> = {
  Student: STUDENT_NAV,
  Teacher: TEACHER_NAV,
  Assistant: ASSISTANT_NAV,
  Academic: MANAGE_NAV,
  Admin: MANAGE_NAV,
}

/**
 * Har rolning BOSH sahifasi. Ilgari hamma `StudentHomePage` ga tushardi —
 * "barcha akkauntlardan bir xil joyga kiryapti" muammosining sababi shu edi.
 */
const HOME_BY_ROLE: Record<UserRoleName, string> = {
  Student: 'student-home',
  Teacher: 'teacher-home',
  Assistant: 'teacher-home',
  Academic: 'manage-groups',
  Admin: 'manage-groups',
}

/** Rol noma'lum bo'lsa (backend yangi rol qo'shsa) — eng kam huquqli ko'rinish. */
const FALLBACK_ROUTE = 'student-home'

const EMPTY_NAV: NavItem[] = []

export function navItemsForRole(role: string | null): NavItem[] {
  if (role === null) return EMPTY_NAV
  return lookup(NAV_BY_ROLE, role, EMPTY_NAV)
}

export function homeRouteFor(role: string | null): string {
  if (role === null) return FALLBACK_ROUTE
  return lookup(HOME_BY_ROLE, role, FALLBACK_ROUTE)
}
