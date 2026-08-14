import { readonly, ref, type Ref } from 'vue'

/**
 * ============================================================================
 * R40 · «BU DARS BO'YICHA SAVOL» — DARS EKRANI BILAN CHAT EKRANI ORASIDAGI KO'PRIK
 * ============================================================================
 *
 * ★ MUAMMO NIMA EDI. `DirectMessage.moduleLessonId` server tomonda ALLAQACHON
 *   bor: u yuborishda tekshiriladi, dars nomiga aylantiriladi, DTO'da keladi
 *   va IKKI ekranda (`InboxThread`, `StudentChatPage`) ALLAQACHON chiziladi.
 *   Faqat bitta narsa yetishmasdi — uni HECH BIR UI to'ldirmasdi, ya'ni
 *   prod'dagi har bir nishon `null` edi. Bu fayl aynan o'sha bo'shliqni
 *   yopadi.
 *
 * ★ NEGA MARSHRUT SO'ROVI (`?lessonId=`) EMAS:
 *   Kontekstda dars NOMI ham kerak (chip'da ko'rsatiladi), nomni esa chat
 *   ekrani bilmaydi — u kurs daraxtini yuklamaydi. Nomni URL'ga solish
 *   demak uni foydalanuvchi tahrirlashi mumkin bo'lgan joyga qo'yish
 *   degani, ikkinchi so'rov bilan olib kelish esa faqat chip uchun butun
 *   kurs daraxtini tortish degani. Kontekst — YUBORILMAGAN QORALAMANING
 *   bir qismi, u sahifa yangilanganda yo'qolishi TABIIY (matn ham
 *   yo'qoladi).
 *
 * ★ NEGA `entities` QATLAMIDA, `features` DA EMAS:
 *   uni IKKI feature o'qiydi — dars varag'i (`features/student-course`) yozadi,
 *   chat sahifasi (`pages/student`) o'qiydi. FSD'da bir feature ikkinchisidan
 *   import qila olmaydi, shuning uchun umumiy bilim entity qatlamiga tushadi
 *   (`withDayLabels` bilan AYNI mulohaza).
 */

/** Savol yozilayotgan dars konteksti. */
export interface LessonQuestionContext {
  lessonId: number
  lessonName: string
}

const context = ref<LessonQuestionContext | null>(null)

/**
 * Dars varag'idagi "Savol berish" tugmasi shuni chaqiradi va chat tabiga
 * o'tadi. Kontekst chat ekranida chip bo'lib ko'rinadi va yuborilgan
 * xabarga `moduleLessonId` bo'lib biriktiriladi.
 */
export function askAboutLesson(lessonId: number, lessonName: string | null): void {
  context.value = { lessonId, lessonName: lessonName ?? 'Dars' }
}

/**
 * Kontekstni tozalaydi — xabar YUBORILGANDAN keyin yoki foydalanuvchi
 * chip'ni yopganda.
 *
 * ★ AVTOMATIK TOZALANMAYDI (masalan sahifadan chiqishda): o'quvchi chatga
 *   o'tib, ro'yxatdan suhbatdoshni tanlashi mumkin — o'sha oraliqda
 *   kontekst yo'qolsa, savol darssiz ketardi va nishon yana `null` bo'lardi.
 */
export function clearLessonQuestionContext(): void {
  context.value = null
}

/** Faqat O'QISH uchun havola — tasodifan boshqa joydan yozilmasin. */
export function useLessonQuestionContext(): Readonly<Ref<LessonQuestionContext | null>> {
  return readonly(context)
}
