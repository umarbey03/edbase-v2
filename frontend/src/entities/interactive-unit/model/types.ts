/**
 * ══════════════════════════════════════════════════════════════════════════
 *  INTERAKTIV DARSLIK — server proyeksiyasining turlari (2026-10-29)
 * ══════════════════════════════════════════════════════════════════════════
 *
 * Server (`StudentUnitProjector`) `dars.json` dan OQ RO'YXAT bo'yicha
 * yig'ilgan obyektni yuboradi: javob kalitlari, ustoz izohlari, `tts` va
 * audio id'lari YO'Q. Audio — opaque belgi (`audio`), `ula` — opaque
 * belgilar (`chap`/`ong`). Shuning uchun bu turlarda `javob` maydoni umuman
 * yo'q: to'g'ri javob faqat `check` javobida, o'sha savol uchun keladi.
 *
 * ★ Blok va mashq — `tur` bo'yicha discriminated union. Kitobda yangi tur
 *   paydo bo'lsa, u `UnknownBlock` sifatida keladi va renderer uni sarlavha
 *   bilan ko'rsatadi (xato emas).
 */

export interface ArUz {
  ar: string | null
  uz: string | null
}

export interface UnitMeta {
  kursNomi: string | null
  daraja: string | null
  birlik: number | null
  birlikNomi: string | null
  nomUz: string | null
  nomAr: string | null
  shior: string | null
  kirish: string | null
  canDo: string[]
  grammatika: string[]
  vazifa: string | null
  tovush: string | null
}

export interface Persona {
  id: string | null
  ar: string | null
  uz: string | null
  rang: string | null
}

export interface VocabWord {
  id: string | null
  dars: string | null
  ar: string | null
  uz: string | null
  tur: string | null
  audio: string | null
}

// ------------------------------------------------------------------ mashqlar

interface ExerciseBase {
  id: string
  korsatma: string | null
  bosqich: number | null
}

export interface ChoiceQuestion {
  uz: string | null
  variantlar: string[]
  audio?: string | null
}

export interface MatchItem {
  id: string
  text: string | null
}

export interface ExTanla extends ExerciseBase {
  tur: 'tanla'
  savollar: ChoiceQuestion[]
}

export interface ExTf extends ExerciseBase {
  tur: 'tf'
  gaplar: { ar: string | null }[]
}

export interface ExUla extends ExerciseBase {
  tur: 'ula'
  chap: MatchItem[]
  ong: MatchItem[]
}

export interface ExOvlash extends ExerciseBase {
  tur: 'ovlash'
  sozlar: string[]
  soni: number
}

export interface ExJadval extends ExerciseBase {
  tur: 'jadval'
  ustunlar: string[]
  /** `null` — o'quvchi to'ldiradigan katak. */
  qatorlar: (string | null)[][]
}

export interface FillSentence {
  ar: string | null
  variantlar: string[]
}

export interface ExToldir extends ExerciseBase {
  tur: 'toldir'
  gaplar: FillSentence[]
}

export interface ExTartibla extends ExerciseBase {
  tur: 'tartibla'
  gaplar: { sozlar: string[] }[]
}

export interface ExUnknown extends ExerciseBase {
  tur: string
}

export type Exercise = ExTanla | ExTf | ExUla | ExOvlash | ExJadval | ExToldir | ExTartibla

// ------------------------------------------------------------------ bloklar

interface BlockBase {
  id: string
  vaqt: number | null
  sarlavhaUz: string | null
  sarlavhaAr: string | null
  korsatma: string | null
  mashqlar?: (Exercise | ExUnknown)[]
}

export interface BlockIsinish extends BlockBase {
  tur: 'isinish'
  kopru: ArUz[]
  lugatIds: string[]
}

export interface DataTableDto {
  sarlavha: string | null
  ustunlar: string[]
  qatorlar: (string | null)[][]
  audio: (string | null)[]
}

export interface BlockGrammatika extends BlockBase {
  tur: 'grammatika'
  kuzat: { ar: string | null; uz: string | null; rangli: string | null }[]
  savol: string | null
  qoida: string[]
  jadval: DataTableDto | null
  kontrast: { uz: string | null; ar: string | null; izoh: string | null } | null
}

export interface LetterDto {
  h: string | null
  nom: string | null
  tovush: string | null
  shakllar: string[]
  audio: string | null
  misollar: { ar: string | null; uz: string | null; audio: string | null }[]
}

export interface BlockHarf extends BlockBase {
  tur: 'harf'
  harflar: LetterDto[]
}

export interface BlockTovush extends BlockBase {
  tur: 'tovush'
  izoh: string | null
  juftlar: { sozlar: string[]; uz: string[]; audio: (string | null)[] }[]
  test: { audio: string | null; variantlar: string[] }[]
  testKey: string
  ehtiyot: { sarlavha: string | null; matn: string | null } | null
}

export interface BlockYozish extends BlockBase {
  tur: 'yozish'
  pogonalar: { n: number | null; uz: string | null; hasNamuna: boolean }[]
}

export interface BlockYakun extends BlockBase {
  tur: 'yakun'
  cando: boolean
}

export interface BlockVazifa extends BlockBase {
  tur: 'vazifa'
  maqsad: string | null
  qolip: string[]
  baholash: { mezon: string | null; ball: number | null }[]
  rollar: { ism: string | null; belgi: string | null; qatorlar: string[] }[]
  natija: string | null
}

export interface BlockTabir extends BlockBase {
  tur: 'tabir'
  kartochkalar: {
    ibora: string | null
    uz: string | null
    misollar: string[]
    siz: string | null
    audio: string | null
  }[]
}

export interface BlockDialog extends BlockBase {
  tur: 'dialog'
  vaziyat: string | null
  bosqichlar: { n: number | null; uz: string | null }[]
  satrlar: { kim: string | null; ar: string | null; uz: string | null; audio: string | null }[]
  audioToliq: string | null
}

export interface BlockOqish extends BlockBase {
  tur: 'oqish'
  oldin: string | null
  matn: string[]
  yangi: ArUz[]
  audio: string | null
  ovoz: string | null
}

export interface UnknownBlock extends BlockBase {
  tur: string
}

export type KnownBlock =
  | BlockIsinish
  | BlockGrammatika
  | BlockHarf
  | BlockTovush
  | BlockYozish
  | BlockYakun
  | BlockVazifa
  | BlockTabir
  | BlockDialog
  | BlockOqish

export type UnitBlock = KnownBlock | UnknownBlock

export interface UnitPart {
  key: string
  nomUz: string | null
  nomAr: string | null
  qisqa: string | null
  bloklar: UnitBlock[]
}

// ------------------------------------------------------------------ daftar

interface WorkbookBase {
  id: string
  sessiya: number | null
  uz: string | null
}

export type WorkbookItem =
  | (WorkbookBase & { tur: 'ula'; chap: MatchItem[]; ong: MatchItem[] })
  | (WorkbookBase & { tur: 'toldir'; gaplar: FillSentence[] })
  | (WorkbookBase & { tur: 'diktant'; audio: (string | null)[] })
  | (WorkbookBase & { tur: 'tanla'; savollar: ChoiceQuestion[] })
  | (WorkbookBase & { tur: 'ochiq' | 'ovoz' })

// ------------------------------------------------------------------ progress

export interface FirstScore {
  right: number
  total: number
}

export interface UnitProgress {
  blocksDone: string[]
  items: Record<string, FirstScore>
  completedAt: string | null
  exerciseRight: number
  exerciseTotal: number
  test: { score: number; max: number; oralCount: number; submittedAt: string } | null
}

export interface StudentUnitDto {
  unitId: number
  version: number
  versionId: number
  book: string
  code: string
  lessonId: number | null
  meta: UnitMeta
  personajlar: Persona[]
  lugat: VocabWord[]
  darslar: UnitPart[]
  test: { nom: string | null; savollarSoni: number; ovozliSoni: number } | null
  daftar: WorkbookItem[]
  progress: UnitProgress
}

// ------------------------------------------------------------------ so'rov/javob

export interface CheckItemBody {
  index?: number
  choice?: number
  value?: boolean
  left?: string
  right?: string
  word?: number
  cells?: Record<string, string>
  words?: string[]
  text?: string
}

export interface CheckItemResult {
  correct: boolean
  /** Shu savolning to'g'ri javobi — faqat javobdan KEYIN. Shakli mashq turiga bog'liq. */
  reveal: {
    correctChoice?: number
    correctValue?: boolean
    tuzatish?: string | null
    javob?: string | null
    cells?: Record<string, { correct: boolean; javob: string }>
  }
  itemDone: boolean
  first: FirstScore | null
}

export interface LessonBookDto {
  access: 'granted' | 'upsell' | 'none' | 'staff'
  hasBook: boolean
  unit: {
    id: number
    book: string
    code: string
    titleUz: string
    titleAr: string | null
    progress: {
      blocksDone: number
      blocksTotal: number
      completed: boolean
      exerciseRight: number
      exerciseTotal: number
      testScore: number | null
      testMax: number | null
    }
  } | null
}

export interface AudioTicketDto {
  token: string
  expiresAt: string
}

export interface MiniTestQuestion {
  index: number
  kon: string | null
  uz: string | null
  mode: 'choice' | 'text' | 'oral'
  variantlar: string[] | null
  audio: string | null
}

export interface MiniTestDto {
  nom: string | null
  savollar: MiniTestQuestion[]
}

export interface MiniTestAnswer {
  index: number
  choice?: number | null
  text?: string | null
}

export interface MiniTestResult {
  score: number
  max: number
  oralCount: number
  counted: boolean
  questions: {
    index: number
    correct: boolean | null
    correctChoice: number | null
    javob: string | null
    oral: boolean
  }[]
}
