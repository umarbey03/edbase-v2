/**
 * MOSLIKNI BELGILASH (2026-08-19).
 *
 * ★ NEGA KERAK: qidiruv "doniyor" ni telefon raqamidan ham, ismdan ham
 * topadi. Natija qatorida NIMA sababdan mos kelgani ko'rinmasa, xodim
 * ro'yxatni qaytadan ko'z bilan solishtirishga majbur bo'lardi —
 * ayniqsa bir familiyali bir necha o'quvchi chiqqanda.
 *
 * ★ `v-html` EMAS, BO'LAKLAR: matn serverdan keladi (ism, guruh nomi),
 * ya'ni undagi `<` belgisi bilan HTML ni buzish yoki skript qistirish
 * mumkin edi. Bu yerda faqat MA'LUMOT qaytariladi, chizishni esa Vue
 * o'zining ekranlashi bilan bajaradi.
 *
 * ★ SO'ZMA-SO'Z: "ali val" deb yozilganda "Alisher Valiyev" ning IKKALA
 * qismi ham belgilanadi. Butun satr bo'yicha qidirilsa, ikki so'zli
 * so'rov hech qachon mos kelmasdi va belgilash umuman ishlamasdi.
 */

export interface TextPart {
  text: string
  /** Shu bo'lak qidiruv so'ziga mos kelganmi. */
  hit: boolean
}

/**
 * Bo'sh natija uchun umumiy massiv — har chaqiruvda yangisi
 * yaratilmasin (ro'yxat har harfda qayta chiziladi).
 */
const EMPTY: TextPart[] = []

function tokensOf(query: string): string[] {
  return [...new Set(query.toLocaleLowerCase().split(/\s+/).filter((part) => part.length > 0))]
    // Uzunroq so'z oldin tekshiriladi: "ali" va "alisher" ikkalasi ham
    // bo'lsa, qisqasi mos kelib uzunini yarmida kesib qo'yardi.
    .sort((a, b) => b.length - a.length)
}

export function highlightParts(text: string, query: string): TextPart[] {
  if (text.length === 0) return EMPTY

  const tokens = tokensOf(query)
  if (tokens.length === 0) return [{ text, hit: false }]

  const lowered = text.toLocaleLowerCase()
  const parts: TextPart[] = []
  let plain = ''

  for (let i = 0; i < text.length; ) {
    const token = tokens.find((candidate) => lowered.startsWith(candidate, i))

    if (token === undefined) {
      plain += text[i]
      i += 1
      continue
    }

    if (plain.length > 0) {
      parts.push({ text: plain, hit: false })
      plain = ''
    }

    parts.push({ text: text.slice(i, i + token.length), hit: true })
    i += token.length
  }

  if (plain.length > 0) parts.push({ text: plain, hit: false })

  return parts
}
