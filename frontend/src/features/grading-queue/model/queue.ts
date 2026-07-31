/**
 * Tekshirish navbatining sof (holatsiz) qoidalari.
 *
 * Ular ALOHIDA faylda, chunki hammasi eski ilovadan AYNAN ko'chirilgan
 * xulq-atvor: ustozlar shu tartibni yodlab olishgan va uni "yaxshilash"
 * ularning ish tezligini pasaytiradi. Manba:
 * `Zinnur-platform/app/templates/teacher.html` (qatorlar 1289–1391).
 */

/**
 * Tezkor baho tugmalari — eng yuqori balldan pastga qarab BESHTA qiymat.
 *
 * Eski ilovadagi hisob (teacher.html:1382–1384) o'zgarishsiz ko'chirilgan:
 * 5 ballik tizimda tugmalar `5 4 3 2 1` bo'ladi va klaviaturadagi `1`–`5`
 * AYNAN shu qiymatlarni qo'yadi.
 *
 * NEGA 10 dan oshmaydi: yorliq bitta raqamli tugma (`1`–`9`), ya'ni 10 dan
 * katta bahoni tugma bilan qo'yib bo'lmaydi. Maksimal ball kattaroq bo'lgan
 * vazifada (masalan 100) tugmalar baribir ko'rsatiladi, lekin asosiy usul —
 * yonidagi "boshqa ball" maydoni.
 */
export function quickGradeOptions(maxScore: number): number[] {
  const top = Math.min(Math.round(maxScore), 10)
  const options: number[] = []
  for (let value = top; value >= Math.max(1, top - 4); value -= 1) options.push(value)
  return options
}

/**
 * Tayyor izohlar — eski ilovaning `QUICK_FB` massivi, matnlari bilan birga.
 *
 * Ustoz kuniga yuzlab ish tekshiradi va izohlarning aksari takrorlanadi;
 * bitta bosish bilan qo'yilgani uchun izoh YOZILADI, bo'sh qolmaydi.
 */
export const QUICK_FEEDBACK: readonly string[] = [
  'Zo‘r! Shunday davom eting',
  'Talaffuzga e’tibor bering',
  'Yozuvni chiroyliroq yozing',
  'Qayta yuboring',
  'Yaxshi, lekin sekinroq o‘qing',
]
