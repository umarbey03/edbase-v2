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

/*
  ★ "Chatlar" — eski `teacher.html` menyusidagi band (657-qator). U yerda
  AYNAN "Tekshirish" dan KEYIN turgan, shu nisbiy o'rni saqlandi. Mavjud
  bandlarning nomi ham, o'zaro tartibi ham TEGILMAGAN.
*/
const TEACHER_NAV: NavItem[] = [
  { routeName: 'teacher-home', label: 'Bosh sahifa', icon: 'graduation' },
  { routeName: 'teacher-groups', label: 'Guruhlarim', icon: 'users' },
  { routeName: 'teacher-sessions', label: 'Darslarim', icon: 'calendar' },
  { routeName: 'teacher-grading', label: 'Tekshirish', icon: 'clipboard' },
  { routeName: 'teacher-group-chats', label: 'Chatlar', icon: 'chat' },
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
 *
 * ★ "CHATLAR" KURATORGA HAM BERILDI — eski ilovadan ATAYLAB farq.
 * Eskisida bu band `{% if user.role != 'assistant' %}` shartida edi, ya'ni
 * kurator guruh chatiga faqat guruh sahifasidagi "Chat" tabi orqali kirardi.
 * v2 da esa KURATOR OQIMI (`Curator` kanali) — modelning teng huquqli yarmi:
 * o'quvchi kuratorga alohida yozadi va u xabarlarni ustoz KO'RMAYDI. Ya'ni
 * kuratorda "barcha chatlarim bitta joyda" ekrani ustozdagidan kam kerak
 * emas. Server ham unga aynan shu kanal qatorlarini beradi.
 */
const ASSISTANT_NAV: NavItem[] = [
  { routeName: 'teacher-home', label: 'Bosh sahifa', icon: 'graduation' },
  { routeName: 'teacher-groups', label: 'Guruhlarim', icon: 'users' },
  { routeName: 'teacher-sessions', label: 'Darslarim', icon: 'calendar' },
  { routeName: 'teacher-curator', label: 'Kuratorlik', icon: 'user-check' },
  { routeName: 'teacher-group-chats', label: 'Chatlar', icon: 'chat' },
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

/*
  ADMIN menyusi = o'quv bo'limi menyusi + BITTA band.

  ★ `MANAGE_NAV` NUSXA QILINMAYDI, ustiga qo'shiladi: eski `academic.html`
  dan ko'chirilgan sakkiztaning TARTIBI ham, NOMI ham shu bilan o'z-o'zidan
  saqlanadi. Ro'yxatni qo'lda qayta yozsak, kelajakda `MANAGE_NAV` ga
  qo'shilgan band adminda paydo bo'lmay qolardi.

  ★ "Tizim sozlamalari" AYNAN OXIRIDA: u kundalik ish emas (yiliga bir necha
  marta ochiladi), va boshiga qo'yilsa o'quv bo'limi xodimi bilan bitta
  kompyuterda ishlaydigan admin uchun menyu "siljib ketgan"dek ko'rinardi.

  ★ FAQAT ADMIN: `/api/v1/settings/*` serverda `[Authorize(Roles = "Admin")]`.
  O'quv bo'limiga ko'rsatilsa, bosgan zahoti 403 olardi.
*/
const ADMIN_NAV: NavItem[] = [
  ...MANAGE_NAV,
  { routeName: 'manage-settings', label: 'Tizim sozlamalari', icon: 'sliders' },
]

const NAV_BY_ROLE: Record<UserRoleName, NavItem[]> = {
  Student: STUDENT_NAV,
  Teacher: TEACHER_NAV,
  Assistant: ASSISTANT_NAV,
  Academic: MANAGE_NAV,
  Admin: ADMIN_NAV,
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
