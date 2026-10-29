/**
 * ══════════════════════════════════════════════════════════════════════════
 * DINIY ATAMALAR — YUBORISHDAN OLDIN YUMSHOQ OGOHLANTIRISH (2026-10-29)
 * ══════════════════════════════════════════════════════════════════════════
 *
 * Loyiha egasining qoidasi: platformada diniy atama UMUMAN ishlatilmaydi
 * (shartnomaning 1.7-bandi ham shuni aytadi).
 *
 * ★ BU MODERATSIYA EMAS — ataylab. Hech narsa o'chirilmaydi, xodimga
 *   xabar bormaydi, hech qayerga yozilmaydi. O'quvchi xabar yoki javob
 *   yubormoqchi bo'lganda matnida shunday so'z bo'lsa, unga "shu so'zni
 *   olib tashlang yoki almashtiring" deyiladi va yuborish to'xtaydi —
 *   so'zni o'zi tuzatadi. (Telegram guruhlaridagi kuzatuv tizimi —
 *   o'chirish, xodimga signal — bu yerga KO'CHIRILMADI: platformada
 *   bunga ehtiyoj yo'q, loyiha egasi shuni so'radi.)
 *
 * ★ RO'YXAT QISQA VA ANIQ: oddiy so'zlarga (ismlar, "roza" guli kabi)
 *   tegib ketmasligi uchun. Ism bo'lishi mumkin bo'lganlari faqat AYNAN
 *   (`exact`) tekshiriladi. Yolg'on signal o'quvchini bezovta qiladi va
 *   qoidaga ishonchni yo'qotadi — shuning uchun shubhali so'z qo'shilmaydi.
 */

/** Qo'shimchalari bilan (alloh → allohga, namozni …). Lotin, normallashgan. */
const PREFIX_STEMS = [
  'alloh', 'olloh', 'xudo', 'xudoy', 'quron', 'qoron', 'kuran',
  'namoz', 'paygambar', 'rasulul', 'masjid', 'jannat', 'dozax', 'oxirat',
  'hadis', 'zikr', 'tavba', 'istigfor', 'zakot', 'momin', 'kofir', 'shayton',
  'farishta', 'bismillah', 'bismilloh', 'inshaalloh', 'inshoalloh', 'mashaalloh',
  'mashoalloh', 'subhanalloh', 'alhamdulillah', 'astagfirulloh', 'sahoba',
  'shariat', 'oyat', 'duo',
] as const

/** Faqat AYNAN shu so'z — ism yoki boshqa ma'noga ham ega bo'lishi mumkin. */
const EXACT_WORDS = ['imom', 'haj', 'umra', 'iymon', 'sura', 'suralar'] as const

/** Arabcha (harakatlarsiz) — matn ichida qidiriladi. */
const ARABIC_PHRASES = [
  'الله', 'قرآن', 'القرآن', 'صلاة', 'الصلاة', 'مسجد', 'المسجد', 'الجنة',
  'النبي', 'سورة', 'آية', 'ان شاء', 'إن شاء', 'سبحان', 'بسم الله',
] as const

const CYRILLIC: Record<string, string> = {
  а: 'a', б: 'b', в: 'v', г: 'g', ғ: 'g', д: 'd', е: 'e', ё: 'yo', ж: 'j', з: 'z',
  и: 'i', й: 'y', к: 'k', қ: 'q', л: 'l', м: 'm', н: 'n', о: 'o', ў: 'o', п: 'p',
  р: 'r', с: 's', т: 't', у: 'u', ф: 'f', х: 'x', ҳ: 'h', ц: 'ts', ч: 'ch', ш: 'sh',
  ъ: '', ь: '', э: 'e', ю: 'yu', я: 'ya',
}

/** Lotin/kirill so'zni solishtirish shakliga keltirish: kichik harf, tutuq belgisiz. */
function normalizeWord(word: string): string {
  return [...word.toLowerCase()]
    .map((ch) => CYRILLIC[ch] ?? ch)
    .join('')
    .replace(/[ʻʼ'‘’`]/g, '')
}

const ARABIC_MARKS = /[ً-ٰٟـ]/g

/**
 * Matndagi diniy atamalar — foydalanuvchi YOZGAN shaklda (ogohlantirishda
 * aynan o'z so'zini ko'rsin). Takrorlar bir marta.
 */
export function findSensitiveWords(text: string): string[] {
  if (text.trim().length === 0) return []

  const found = new Set<string>()

  for (const raw of text.split(/[^\p{L}ʻʼ'‘’`]+/u)) {
    if (raw.length < 3) continue
    const word = normalizeWord(raw)
    if (
      EXACT_WORDS.some((w) => word === w)
      || PREFIX_STEMS.some((stem) => word.startsWith(stem))
    ) {
      found.add(raw)
    }
  }

  const arabic = text.replace(ARABIC_MARKS, '')
  for (const phrase of ARABIC_PHRASES) {
    if (arabic.includes(phrase)) found.add(phrase)
  }

  return [...found]
}
