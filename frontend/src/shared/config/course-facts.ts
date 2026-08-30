/*
  ══════════════════════════════════════════════════════════════════════════
  KURS RAQAMLARI — YAGONA MANBA
  ══════════════════════════════════════════════════════════════════════════

  Bu yerda faqat OMMAGA ochiq, o'zgarmas kurs parametrlari turadi: kurs
  davomiyligi, haftalik dars kunlari, guruh hajmi va to'lov shartlari.

  ★ NIMA UCHUN ALOHIDA MODUL (2026-08-30):
    Ilgari bu qiymatlar FAQAT `pages/landing/model/content.ts` da edi va
    bu to'g'ri edi — ularni bitta sahifa ishlatardi. Endi ikkita sahifa
    ishlatadi: landing va KIRISH sahifasining brend paneli.

    Bir sahifa ikkinchisidan import qila olmaydi (FSD'da `pages` qatlami
    o'zaro bog'lanmaydi), ya'ni tanlov ikkita edi: qiymatni IKKI JOYDA
    takrorlash yoki umumiy qatlamga chiqarish. Takrorlangan raqam
    ertami-kechmi ajralib ketadi — va aynan narx bo'yicha ajralsa, ikki
    sahifa ikki xil summa ko'rsatardi.

  ┌────────────────────────────────────────────────────────────────────┐
  │ 🔴 BU YERGA "NECHTA O'QUVCHI BOR" KABI HISOBLAGICH YOZILMAYDI      │
  └────────────────────────────────────────────────────────────────────┘
  Faqat SHARTNOMAVIY qiymatlar: ular markaz qarori bilan o'zgaradi va
  o'zgarganda shu fayl ham qo'lda tahrirlanadi.

  O'quvchi/guruh SONI esa har kuni o'zgaradi. Uni bu yerga qo'lda yozish
  degani — bir hafta ichida yolg'on raqam ko'rsatish degani. Haqiqiy son
  kerak bo'lsa, backendda ANONIM (autentifikatsiyasiz) endpoint kerak;
  hozir `/api/v1/*` ning hammasi kirishni talab qiladi.

  ⚠️ QIYMAT O'ZGARSA: shu faylni tahrirlang. Landing matnining QOLGAN
     qismi (gaplar, izohlar, savol-javob) hamon
     `pages/landing/model/content.ts` da — u yerda ham shu raqamlar
     GAP ICHIDA takrorlanadi, chunki matnni bo'laklarga bo'lish uni
     o'qib bo'lmas holga keltirardi. Narx yoki davomiylik o'zgarsa,
     ikkala faylni ham ko'rib chiqing.
*/

export const COURSE_FACTS = {
  /** Asosiy kurs (ATF) davomiyligi. */
  courseDuration: '8 oy',

  /** Haftasiga dars kunlari: 2 kun asosiy ustoz + 3 kun support teacher. */
  weeklyLessonDays: '5 kun',

  /** Guruhdagi o'quvchilar soni. */
  groupSize: '18–20',

  /** Bitta to'lovga kiradigan asosiy dars soni. */
  lessonsPerPayment: '8 ta dars',

  /** Bitta to'lov summasi (`lessonsPerPayment` uchun), birliksiz. */
  price: '540 000',

  /** Bitta asosiy darsning narxi, birliksiz. */
  pricePerLesson: '67 500',

  /** Asosiy ustoz darsining davomiyligi. */
  lessonDuration: '1 soat 20 daqiqa',
} as const
