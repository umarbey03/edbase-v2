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
  Testlar -> Dars yozuvlari -> Kurs quruvchi. "Jonli darslar" va "Uy
  vazifalari" — v2 qo'shimchalari, ular eski bandlarning KETMA-KETLIGINI
  buzmasligi uchun oxiriga qo'yilgan.

  ★ "TO'LOVLAR" VA "MOLIYA" BU YERDA YO'Q (loyiha egasi, 2026-08-15):
  *"to'lovlar va moliya qismi o'quv bo'limi uchun kerak emas, u qismi admin
  panelda bo'lsa yetadi"*. Ikkalasi endi FAQAT `ADMIN_NAV`da — "Tizim
  sozlamalari" bilan AYNI mulohaza (moliyaviy ma'lumot — o'quv jarayoni
  emas, markaz boshqaruvi).

  ★ "Dars yozuvlari" — eski `academic.html` menyusining 481-qatoridagi band
  (`showPage('recordings')`, `#i-video` ikonkasi). U yerda AYNAN "Testlar" dan
  KEYIN turgan, shu nisbiy o'rni saqlandi. Mavjud bandlarning nomi ham,
  o'zaro tartibi ham TEGILMAGAN.

  "Kurs quruvchi" nomi ham eskisidan: v2 da "Kurs kontenti" deb o'zgartirilgan
  edi, lekin o'quv bo'limi xodimi menyuda o'sha eski so'zni qidiradi.
*/
const MANAGE_NAV: NavItem[] = [
  { routeName: 'manage-groups', label: 'Guruhlar', icon: 'grid' },
  { routeName: 'manage-users', label: 'Foydalanuvchilar', icon: 'users' },
  { routeName: 'manage-tests', label: 'Testlar', icon: 'award' },
  /*
    Ikonka `camera` — bu AYNAN eski sprite'dagi `#i-video` shakli (chapda
    to'rtburchak kadr, o'ngda uchburchak). `AppIcon` da u allaqachon shu nom
    bilan mavjud, shuning uchun bir xil chiziqni ikkinchi nom ostida
    takrorlash o'rniga mavjudi ishlatildi.
  */
  { routeName: 'manage-recordings', label: 'Dars yozuvlari', icon: 'camera' },
  /*
    "Ustozlar holati" — v2 qo'shimchasi (2026-08-17): kunlik "darsga o'ta
    olasizmi?" tasdiqlash + o'rinbosar tizimi paneli. "Uy vazifalari" dan
    OLDIN qo'yildi: ikkalasi ham kundalik nazorat oqimi, lekin bu band
    ertalabki eng birinchi tekshiriladigan narsa bo'lgani uchun yuqoriroq.
  */
  { routeName: 'manage-teacher-availability', label: 'Ustozlar holati', icon: 'user-check' },
  /*
    "To'kilishlar" — v2 qo'shimchasi (2026-08-17): o'quvchilarning guruhdan
    ketishi/muzlatilishi/ko'chirilishi hisoboti. "Ustozlar holati" dan
    KEYIN: ikkalasi ham kundalik nazorat, lekin to'kilish haftalik/oylik
    ko'rib chiqiladigan ko'rsatkich, ertalabki tekshiruv emas.
  */
  { routeName: 'manage-attrition', label: 'To‘kilishlar', icon: 'chart' },
  /*
    "Jarimalar" — v2 qo'shimchasi (2026-08-18): ustoz/kurator intizomi
    (kech boshlangan va o'tilmagan darslar). To'kilishlardan KEYIN:
    ikkalasi ham nazorat hisoboti, lekin jarima oylikka ta'sir qiladi
    va kamroq ochiladi.
  */
  { routeName: 'manage-penalties', label: 'Jarimalar', icon: 'wallet' },
  { routeName: 'manage-courses', label: 'Kurs quruvchi', icon: 'file-text' },
  { routeName: 'manage-sessions', label: 'Jonli darslar', icon: 'calendar' },
  { routeName: 'manage-assignments', label: 'Uy vazifalari', icon: 'clipboard' },
  /*
    "Xabarlar" — v2 qo'shimchasi (2026-08-16): guruhlarga Telegram/platforma
    chati orqali xabar yuborish paneli. Eski ilovada bunday bo'lim yo'q edi,
    shuning uchun "qayerga qo'yish kerak" degan meros qarori yo'q — mavjud
    "Uy vazifalari" dan KEYIN, "Sozlamalar" dan OLDIN qo'yildi: ikkalasi ham
    kundalik ISH oqimi (vazifa berish, xabar yuborish), sozlamalar esa
    kamdan-kam ochiladigan TAYYORGARLIK bo'limi.
  */
  { routeName: 'manage-broadcasts', label: 'Xabarlar', icon: 'send' },
  /*
    O'quv jarayoni sozlamalari (dars tahlili mezonlari, guruh
    yo'nalishlari). Nom va ikonka endi shunchaki "Sozlamalar" / `sliders`
    (loyiha egasi, 2026-08-15) — Admin'ning "Tizim sozlamalari" bandi bilan
    AYNAN bir xil ikonka, ATAYLAB: ikkalasi ham "sozlamalar" tushunchasi,
    va Academic roli ularni hech qachon BIRGA ko'rmaydi (`MANAGE_NAV` da
    faqat shu band bor, `manage-settings` esa `ADMIN_ONLY`). Admin roli
    ikkalasini bitta menyuda ko'radi (`ADMIN_NAV = [...MANAGE_NAV, ...,
    manage-settings]`) — bu qabul qilingan holat, ikonka to'qnashuvi emas.
  */
  { routeName: 'manage-academic-settings', label: 'Sozlamalar', icon: 'sliders' },
]

/*
  ADMIN menyusi = o'quv bo'limi menyusi + moliyaviy bandlar + sozlamalar.

  ★ `MANAGE_NAV` NUSXA QILINMAYDI, ustiga qo'shiladi: eski `academic.html`
  dan ko'chirilgan bandlarning TARTIBI ham, NOMI ham shu bilan o'z-o'zidan
  saqlanadi. Ro'yxatni qo'lda qayta yozsak, kelajakda `MANAGE_NAV` ga
  qo'shilgan band adminda paydo bo'lmay qolardi.

  ★ "To'lovlar"/"Moliya" VA "Tizim sozlamalari" AYNAN OXIRIDA: ular
  kundalik o'quv jarayoni emas, va boshiga qo'yilsa o'quv bo'limi xodimi
  bilan bitta kompyuterda ishlaydigan admin uchun menyu "siljib ketgan"dek
  ko'rinardi.

  ★ FAQAT ADMIN: `/api/v1/settings/*`, `/api/v1/payments/*`, `/api/v1/finance/*`
  serverda `[Authorize(Roles = "Admin")]`. O'quv bo'limiga ko'rsatilsa,
  bosgan zahoti 403 olardi.
*/
const ADMIN_NAV: NavItem[] = [
  ...MANAGE_NAV,
  { routeName: 'manage-payments', label: 'To‘lovlar', icon: 'star' },
  { routeName: 'manage-finance', label: 'Moliya', icon: 'chart' },
  { routeName: 'manage-payroll', label: 'Oylik hisoblash', icon: 'wallet' },
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
