/**
 * BILDIRISHNOMA bilan bog'liq TanStack Query kalitlari.
 *
 * ★ NEGA ALOHIDA FAYL: bu kalitlarni UCH joy ishlatadi — qo'ng'iroqcha
 * komponenti (o'qiydi), hub composable'i (bekor qiladi) va kelajakdagi
 * sahifalar. Kalit satr sifatida uch joyda qo'lda yozilsa, biri
 * o'zgarganda qolgani JIMGINA boshqa keshga qarab qolardi — TanStack
 * bunday xatoda hech qanday ogohlantirish bermaydi.
 */

/** Qo'ng'iroqcha ro'yxati (birinchi sahifa). */
export const NOTIFICATIONS_FEED_KEY = ['notifications', 'feed'] as const

/** Nishondagi raqam. */
export const NOTIFICATIONS_UNREAD_KEY = ['notifications', 'unread'] as const

/** Ikkalasini birdan bekor qilish uchun umumiy prefiks. */
export const NOTIFICATIONS_ROOT_KEY = ['notifications'] as const

/**
 * 🔴 O'QUVCHINING VAZIFALAR RO'YXATI — "page refresh kerak bo'lmasin"
 *    TALABINING AYNAN SHU YARMI.
 *
 * `StudentAssignmentsPage` shu kalit bilan o'qiydi va bugungacha uni
 * FAQAT o'quvchining O'ZI (javob topshirganda) bekor qilardi. Ustoz
 * baholaganda hech kim bekor qilmasdi — ya'ni o'quvchi bahoni ko'rish
 * uchun sahifani QO'LDA yangilashi kerak edi. Aynan shundan shikoyat
 * kelib chiqqan.
 *
 * ★ SATR SHU YERDA TAKRORLANDI, sahifadan import QILINMADI: `features`
 *   qatlami `pages` ga bog'lanmaydi (FSD qoidasi — bog'lanish faqat
 *   pastga qarab). Takror qiymat esa quyidagi izoh bilan ushlanadi.
 */
export const STUDENT_ASSIGNMENTS_KEY = ['assignments', 'mine'] as const
