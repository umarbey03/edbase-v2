/**
 * ============================================================================
 *  TELEFON RAQAMI — YAGONA FORMATLASH MANBASI
 * ============================================================================
 *
 * Loyiha egasi (2026-08-15): *"barcha joylarda telefon raqam formatlangan
 * holda bo'lsin — kiritish joylarida ham, displayda chiqarishda ham. Ya'ni
 * +998901234567 kabi emas, balki +998 90 123 45 67 kabi"*.
 *
 * ── CHEGARA QAYERDA ────────────────────────────────────────────────────────
 *
 * 🔴 FORMATLANGAN SATR HECH QACHON SERVERGA YUBORILMAYDI va hech qachon
 * SAQLANMAYDI. Baza va API'da raqam AVVALGIDEK `+998901234567` — bo'shliqsiz
 * E.164. Bo'shliq FAQAT ikki joyda paydo bo'ladi:
 *
 *     server  ──`formatPhone`──▶  ekran
 *     ekran   ──`formatPhoneInput`──▶  maydon ichidagi matn
 *     maydon  ──`normalizePhone`──▶  server
 *
 * ★ NEGA SHUNDAY, nega bo'shliq bilan saqlamaymiz: raqam TENGLIK ustida
 * ishlatiladi (`WHERE Phone = @p`, Telegram bog'lash, takrorlanish
 * tekshiruvi). Bir foydalanuvchi "+998 90..." deb, ikkinchisi "+99890..."
 * deb yozsa, ular BOSHQA-BOSHQA qiymat bo'lib qolardi va bitta odam ikki
 * hisob ochib olardi. Format esa KO'RINISH masalasi — u ma'lumot emas.
 *
 * ★ SHUNING UCHUN BU FAYL `shared/lib` DA: uni `entities` (`user`),
 * `features` (forma, kirish) va `widgets` (profil) — uchala qatlam ham
 * ishlatadi. Nusxa ko'chirilsa, biri "+998 90" deb, ikkinchisi "+998-90"
 * deb chizardi.
 *
 * ── O'ZBEK RAQAMINING TUZILISHI ────────────────────────────────────────────
 *
 *     +998   90    123   45   67
 *      │     │      │     │    │
 *      │     │      │     │    └─ oxirgi juftlik
 *      │     │      │     └────── o'rta juftlik
 *      │     │      └──────────── abonent boshi (3)
 *      │     └─────────────────── operator kodi (2)
 *      └───────────────────────── mamlakat kodi
 *
 * Ya'ni 998 dan keyin ANIQ 9 ta raqam, 2-3-2-2 bo'lib guruhlanadi.
 */

/** Mamlakat kodi — raqamsiz (`+` siz). */
const COUNTRY_CODE = '998'

/** 998 dan keyingi raqamlar soni. */
export const PHONE_NATIONAL_LENGTH = 9

/**
 * Guruhlar: `90 123 45 67`.
 *
 * ★ MASSIV, qotib qolgan `slice` lar EMAS: pastdagi ikkala funksiya ham shu
 * bitta ro'yxatdan o'qiydi, ya'ni guruhlash o'zgarsa (masalan operator kodi
 * uch xonali bo'lib qolsa) BITTA qator tahrirlanadi va ko'rsatish bilan
 * kiritish bir-biridan ajralib ketmaydi.
 */
const GROUPS = [2, 3, 2, 2] as const

/**
 * To'liq formatlangan satr uzunligi — maydondagi `maxlength` uchun.
 *
 * `+` (1) + `998` (3) + bo'shliq (1) + 9 ta raqam + guruhlar orasidagi 3 ta
 * bo'shliq = 17 (`+998 90 123 45 67`).
 *
 * ★ HISOBLANADI, qo'lda yozilmaydi: guruhlash o'zgarsa bu qiymat O'ZI
 * to'g'rilanadi. Qotib qolgan `17` esa `GROUPS` bilan jimgina ajralib
 * ketardi va maydon oxirgi raqamni "yutib" qo'yardi.
 */
export const PHONE_INPUT_MAXLENGTH =
  1 + COUNTRY_CODE.length + 1 + PHONE_NATIONAL_LENGTH + (GROUPS.length - 1)

/** Faqat raqamlarni qoldiradi (`+`, bo'shliq, qavs, defis — hammasi tushadi). */
export function phoneDigits(value: string): string {
  return value.replace(/\D+/gu, '')
}

/**
 * Raqamning MILLIY qismini ajratadi (998 siz, ko'pi bilan 9 ta raqam).
 *
 * Foydalanuvchi raqamni bir necha xil boshlashi mumkin va uchalasi ham
 * BIR XIL natija berishi kerak:
 *   • `+998901234567` (nusxa ko'chirgan) → `901234567`
 *   • `998901234567`  (`+` siz)          → `901234567`
 *   • `901234567`     (odatdagi yozuv)    → `901234567`
 *
 * ⚠️ FAQAT `startsWith('998')` GA TAYANIB BO'LMAYDI: `998` bilan
 * BOSHLANADIGAN MILLIY raqam ham bor — `99` operator kodi mavjud, ya'ni
 * `99 812 34 56` haqiqiy raqam va uning raqamlari `998123456`. Prefiks
 * bo'yicha ko'r-ko'rona kesilsa, undan `123456` qolib ketardi.
 *
 * Shuning uchun IKKI dalil tekshiriladi va ularning BIRI yetarli:
 *
 *   1. satr `+` bilan boshlanadi — ya'ni mamlakat kodi OSHKORA berilgan
 *      (`+998…`). Bu KIRITISH paytida hal qiluvchi: maydonda bizning
 *      o'z prefiksimiz turadi va har bosilgan tugmadan keyin qiymat
 *      qaytadan shu funksiyaga keladi;
 *   2. umumiy uzunlik 9 dan KATTA — `998901234567` (`+` siz nusxa).
 *
 * 🔴 BIRINCHI DALIL BO'LMASA MASKA BUZILADI (aynan shu xato topilgan
 * va tuzatilgan): maydonda `+998 90 12` turganda raqamlar `9989012`
 * bo'ladi — bu 9 tadan KICHIK, ya'ni faqat uzunlik qoidasi bilan `998`
 * kesilmasdi va natija `+998 99 890 12` bo'lardi. Har bosishda prefiks
 * raqamning ichiga qo'shilib borardi. Xuddi shu sabab maydonni
 * TOZALAB ham bo'lmasdi: `+998 ` → `998` → yana `+998 99 8`.
 *
 * `998123456` (roppa-rosa 9 xona, `+` siz) esa milliy raqam sifatida
 * o'qiladi — 99-operatorning raqami buzilmaydi.
 */
function nationalPart(value: string): string {
  const digits = phoneDigits(value)
  const hasExplicitCountryCode = value.trim().startsWith('+')

  const national =
    digits.startsWith(COUNTRY_CODE)
    && (hasExplicitCountryCode || digits.length > PHONE_NATIONAL_LENGTH)
      ? digits.slice(COUNTRY_CODE.length)
      : digits

  return national.slice(0, PHONE_NATIONAL_LENGTH)
}

/** Milliy raqamlarni `90 123 45 67` ko'rinishida guruhlaydi (chala bo'lsa ham). */
function groupNational(national: string): string {
  const parts: string[] = []
  let cursor = 0

  for (const size of GROUPS) {
    if (cursor >= national.length) break
    parts.push(national.slice(cursor, cursor + size))
    cursor += size
  }

  return parts.join(' ')
}

/**
 * SERVERGA YUBORISH SHAKLI — maydondan BO'SHLIQLAR olib tashlanadi, VASSALOM.
 *
 * 🔴 API'ga yuboriladigan HAR bir raqam shu funksiyadan o'tadi:
 * `+998 90 123 45 67` → `+998901234567`.
 *
 * ══════════════════════════════════════════════════════════════════════
 *  NEGA BU YERDA NORMALIZATSIYA YO'Q (faqat bo'shliq tozalash)
 * ══════════════════════════════════════════════════════════════════════
 *
 * `LoginPage` da yozilgan qoida KUCHIDA QOLADI: *"telefon raqami serverga
 * XOM holda yuboriladi — mijozda hech qanday normalizatsiya QILINMAYDI"*.
 * Sabab o'sha: normalizatsiya qoidasi backendda BITTA joyda
 * (`User.NormalizePhone`) va u `PhoneNormalized` ustunini to'ldiradigan
 * AYNI metod. Mijozda ikkinchi nusxa paydo bo'lsa, ular asta bir-biridan
 * uzoqlashib "raqamim to'g'ri, lekin kod kelmayapti" turkumidagi
 * nosozlikni berardi.
 *
 * Shuning uchun bu funksiya raqamni O'ZGARTIRMAYDI — u FAQAT O'ZIMIZ
 * QO'SHGAN bo'shliqlarni olib tashlaydi. Mamlakat kodi qo'shilmaydi,
 * raqam kesilmaydi, `0` tashlanmaydi: bularning HAMMASI serverning ishi
 * va u buni allaqachon qiladi (`digits.Length` bo'yicha 9 / 13 / boshqa
 * shoxlari bilan).
 *
 * ★ CHET EL RAQAMI HAM BUZILMAYDI: `+7 916 555 12 34` → `+79165551234`.
 * Agar bu yerda "o'zbeklashtirish" bo'lganda, u `+998791655512` bo'lib
 * ketardi — ya'ni chet el raqami bilan ro'yxatdan o'tgan xodim tizimga
 * KIRA OLMASDI. Aynan shu xavf `LoginPage` izohida ogohlantirilgan.
 */
export function stripPhoneFormatting(value: string | null | undefined): string {
  return (value ?? '').replace(/\s+/gu, '')
}

/**
 * KO'RSATISH SHAKLI — `+998 90 123 45 67`.
 *
 * Bo'sh yoki `null` bo'lsa BO'SH SATR qaytadi: "Kiritilmagan" kabi o'rin
 * bosar matnni CHAQIRUVCHI tanlaydi (profil oynasida u kerak, jadval
 * katakchasida esa bo'sh joy to'g'riroq).
 *
 * ★ NOTANISH SHAKL O'ZGARTIRILMAYDI: agar qiymat o'zbek raqamiga
 * o'xshamasa (chet el raqami yoki bazadagi eski chala yozuv), u
 * SHUNDAYLIGICHA qaytadi. Uni majburan 2-3-2-2 ga bo'lish raqamni
 * O'QIB BO'LMAYDIGAN holga keltirardi — masalan `+7 916 555 12 34` ni
 * `+7 91 655 51 234` deb ko'rsatish odamni chalg'itadi.
 */
export function formatPhone(value: string | null | undefined): string {
  const raw = (value ?? '').trim()
  if (raw.length === 0) return ''

  const digits = phoneDigits(raw)

  // To'liq o'zbek raqami (998 + 9) yoki sof milliy qism (9) — ikkalasi ham
  // bir xil chiziladi.
  const isUzbek =
    (digits.startsWith(COUNTRY_CODE) && digits.length === COUNTRY_CODE.length + PHONE_NATIONAL_LENGTH)
    || digits.length === PHONE_NATIONAL_LENGTH

  if (!isUzbek) return raw

  return `+${COUNTRY_CODE} ${groupNational(nationalPart(digits))}`
}

/**
 * KIRITISH PAYTIDAGI SHAKL — har bosilgan tugmadan keyin qayta hisoblanadi.
 *
 * `formatPhone` dan FARQI: bu funksiya CHALA raqamni ham formatlaydi
 * (`+998 90 12`), chunki foydalanuvchi hali yozayotgan bo'ladi.
 * `formatPhone` esa faqat TUGALLANGAN raqamni bezaydi.
 *
 * ★ MAYDON BUTUNLAY BO'SHATILISHI MUMKIN: raqamlar tugasa BO'SH SATR
 * qaytadi. Agar bu yerda doim `+998 ` qaytarilsa, foydalanuvchi maydonni
 * tozalay olmasdi (Backspace har safar prefiksga urilib turardi) va
 * placeholder hech qachon ko'rinmasdi.
 *
 * 🔴 CHET EL RAQAMIGA UMUMAN TEGILMAYDI. Agar satr `+` bilan boshlansa
 * va raqamlari `998` dan boshlanmasa (`+7…`, `+1…`), qiymat
 * SHUNDAYLIGICHA qaytariladi.
 *
 * Bu shart bo'lmasa maska `+7 916…` ni `+998 79 165…` ga aylantirib,
 * chet el raqami bilan ro'yxatdan o'tgan xodimni tizimga KIRITMAY
 * qo'yardi — `LoginPage` izohida aynan shu xavfdan ogohlantirilgan
 * ("qat'iy shakl qoidasi ... xodimni to'sib qo'yardi").
 *
 * ⚠️ SHUNING UCHUN CHET EL RAQAMI `+` BILAN BOSHLANISHI SHART. `+` siz
 * yozilgan raqam O'ZBEK raqami deb qabul qilinadi — bu ilovada shunday
 * bo'lishi tabiiy va ikkinchi tomondan, `+` siz raqamni chet elniki deb
 * "taxmin qilish" har bir o'zbek raqamini ham shubha ostiga qo'yardi.
 */
export function formatPhoneInput(value: string): string {
  const digits = phoneDigits(value)
  const hasPlus = value.trimStart().startsWith('+')

  // Raqamsiz holat: `+` yozib qo'yilgan bo'lsa saqlanadi (foydalanuvchi
  // chet el kodini terishni boshlagan), aks holda maydon bo'shaydi.
  if (digits.length === 0) return hasPlus ? value : ''

  // Chet el raqami — tegilmaydi (yuqoridagi izoh).
  if (hasPlus && !digits.startsWith(COUNTRY_CODE)) return value

  const national = nationalPart(value)

  // `+998` terilib bo'lgan, lekin milliy raqam hali yo'q. Bu yerda BO'SH
  // satr qaytarilsa, foydalanuvchi mamlakat kodini yozib tugatishi bilan
  // maydon o'zidan-o'zi tozalanardi.
  if (national.length === 0) return `+${COUNTRY_CODE}`

  return `+${COUNTRY_CODE} ${groupNational(national)}`
}

/**
 * Formatlangan matnda N-raqamdan KEYINGI kursor o'rnini topadi.
 *
 * Maskaning eng ko'p uchraydigan xatosi shu yerda: matn qayta yozilgandan
 * keyin kursor OXIRIGA tashlanadi va o'rtadagi xatoni tuzatmoqchi bo'lgan
 * odam har tuzatishdan keyin qo'lda orqaga qaytishga majbur bo'ladi.
 * Shuning uchun kursor BELGI o'rni bo'yicha emas, RAQAM soni bo'yicha
 * saqlanadi.
 *
 * @param digitIndex FORMATLANGAN satrdagi raqam tartibi (1 dan boshlab),
 * ya'ni mamlakat kodining raqamlari ham SANALADI.
 */
function caretAfterDigit(formatted: string, digitIndex: number): number {
  if (digitIndex <= 0) return 0

  let seen = 0
  for (let i = 0; i < formatted.length; i += 1) {
    if (/\d/u.test(formatted[i] ?? '')) {
      seen += 1
      if (seen === digitIndex) return i + 1
    }
  }

  return formatted.length
}

/**
 * Kursorgacha bo'lgan MILLIY raqamlar soni.
 *
 * 🔴 BU FUNKSIYA NIMA UCHUN ALOHIDA (topilgan va tuzatilgan xato):
 * kursor o'rnini raqam soni bilan saqlashda IKKI xil "raqam tartibi"
 * bor va ular ADASHTIRILGAN edi —
 *
 *   • foydalanuvchi yozgan satrda (`9`) mamlakat kodi HALI YO'Q;
 *   • formatlangan satrda (`+998 9`) esa u BOR.
 *
 * Birinchi bosishda "kursorgacha 1 ta raqam" deb hisoblanardi va kursor
 * formatlangan satrning BIRINCHI raqamidan keyin, ya'ni `+9|98 9` ga
 * qo'yilardi. Keyingi raqam esa o'sha yerga yozilardi — natijada
 * `901234567` yozganda maydonda `+90123456798 9` paydo bo'lardi.
 *
 * Shuning uchun bu yerda kursorgacha bo'lgan qism AYNI `nationalPart`
 * qoidasi bilan o'lchanadi va mamlakat kodi tashlanadi; chaqiruvchi esa
 * uni formatlangan satr koordinatasiga qaytaradi.
 */
function nationalDigitsBefore(text: string): number {
  const digits = phoneDigits(text)
  const hasPrefix = text.trimStart().startsWith('+') && digits.startsWith(COUNTRY_CODE)

  return hasPrefix ? digits.length - COUNTRY_CODE.length : digits.length
}

/**
 * Maydonga maskani QO'LLAYDI va kursorni joyida saqlaydi.
 *
 * Ishlatilishi (`v-model` EMAS — sabab quyida):
 *
 * ```vue
 * <input
 *   :value="form.phone"
 *   inputmode="tel"
 *   @input="form.phone = maskPhoneField($event.target as HTMLInputElement)"
 * >
 * ```
 *
 * ★ NEGA `v-model` EMAS: `v-model` avval modelni yangilaydi, keyin DOM'ni.
 * Agar maska modelga yozilsa, Vue maydonni qayta chizadi va kursor
 * oxiriga sakraydi — aynan yuqorida tasvirlangan xato. `:value` +
 * `@input` bilan esa DOM qiymati SHU YERDA to'g'rilanadi va Vue keyin
 * AYNI qiymatni ko'rib, maydonga umuman tegmaydi.
 *
 * ★ MAMLAKAT KODINI O'CHIRIB BO'LMAYDI degan qoida YO'Q: `+998` prefiksi
 * "qulflangan" bo'lsa, chet el raqamini kiritish imkonsiz bo'lardi va
 * maydon foydalanuvchi bilan "kurashardi". Prefiks shunchaki birinchi
 * raqam yozilishi bilan O'ZI paydo bo'ladi.
 *
 * @returns Modelga yoziladigan formatlangan qiymat.
 */
export function maskPhoneField(field: HTMLInputElement): string {
  const caret = field.selectionStart ?? field.value.length
  const nationalBefore = nationalDigitsBefore(field.value.slice(0, caret))

  const formatted = formatPhoneInput(field.value)

  // Matn o'zgarmagan bo'lsa kursorga TEGILMAYDI — chet el raqami aynan
  // shu tarmoqdan o'tadi (`formatPhoneInput` uni qaytaruvchi qilib
  // beradi) va uning kursori butunlay brauzer ixtiyorida qoladi.
  if (field.value === formatted) return formatted

  field.value = formatted

  // MILLIY tartibni FORMATLANGAN tartibga o'tkazamiz: prefiks
  // qo'yilgan bo'lsa uning 3 ta raqami ham oldinda turadi.
  const prefixDigits = formatted.startsWith(`+${COUNTRY_CODE}`) ? COUNTRY_CODE.length : 0
  const next = caretAfterDigit(formatted, prefixDigits + nationalBefore)

  field.setSelectionRange(next, next)

  return formatted
}

/** To'liq (12 raqamli) o'zbek raqamimi — forma tugmasini yoqish uchun. */
export function isCompletePhone(value: string | null | undefined): boolean {
  return nationalPart(value ?? '').length === PHONE_NATIONAL_LENGTH
}
