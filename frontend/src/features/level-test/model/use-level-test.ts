import { computed, ref, shallowRef } from 'vue'

import type { BlockId, BlockScore, Question, ResultKey } from './questions'
import {
  BLOCK_ORDER,
  BLOCKS,
  FAST_ANSWER_MS,
  FAST_ANSWER_RATE,
  GATES,
  KEY_QUESTIONS,
  QUESTIONS,
  RESULTS,
} from './questions'

/*
  ══════════════════════════════════════════════════════════════════════════
  DARAJA TESTINING HOLAT MASHINASI
  ══════════════════════════════════════════════════════════════════════════

  ★ NEGA KOMPONENTDAN AJRATILGAN: bu yerda test MANTIQI (navbat, darvoza,
    ball, tezlik tekshiruvi) turadi, `LevelTestModal.vue` da esa faqat
    chizish. Mantiq alohida bo'lgani uchun uni komponentni ishga
    tushirmasdan ham o'qish va tekshirish mumkin.

  ★ NEGA STORE (Pinia) EMAS: test holati BITTA modal ichida yashaydi va
    u yopilganda kerak emas. Store bo'lsa, holat sahifa bo'ylab qolib
    ketardi va ikkinchi marta ochilganda tozalash haqida alohida
    o'ylash kerak bo'lardi.

  ⚠️ `Math.random()` VA `Date.now()` BOR — ya'ni bu modul faqat
     BRAUZERDA, test ochilgandan keyin ishlaydi. Prerender paytida
     (`entry-ssg.ts`) modal yopiq bo'lgani uchun `start()` chaqirilmaydi
     va SSR bilan brauzer chizmasi bir xil qoladi.
*/

/** Modal qaysi ekranni ko'rsatib turibdi. */
export type LevelTestScreen = 'quiz' | 'warning' | 'result'

/** «Bilmayman» varianti — asl indekslar bilan to'qnashmaydigan qiymat. */
export const DONT_KNOW = -1

export interface DisplayOption {
  /** Ekranda ko'rsatiladigan matn. */
  readonly label: string
  /** Arabcha variantmi — boshqa shrift va `rtl` yo'nalishi kerak. */
  readonly isArabic: boolean
  /** `options` ichidagi ASL indeks — javob shu bilan saqlanadi. */
  readonly value: number
  /** `A`, `B`, `C`… — ekrandagi tartib bo'yicha. */
  readonly key: string
}

export interface BlockBreakdown {
  readonly id: BlockId
  readonly name: string
  readonly reached: boolean
  readonly score: number
  readonly total: number
  readonly percent: number
  /** Rang darajasi: yaxshi / o'rta / past / o'tilmadi. */
  readonly tone: 'good' | 'mid' | 'low' | 'skip'
}

export interface ReviewItem {
  readonly number: number
  readonly question: string
  readonly arabic?: string
  readonly isCorrect: boolean
  readonly given: string
  readonly expected: string
  readonly explanation: string
}

const OPTION_KEYS = 'ABCDE'

function optionLabel(option: { text?: string, arabic?: string }): string {
  return option.arabic ?? option.text ?? ''
}

export function useLevelTest() {
  /**
   * Ayni paytdagi savollar navbati.
   *
   * ★ `shallowRef`: massiv ELEMENTLARI o'zgarmaydi (savollar — o'zgarmas
   * ma'lumot), faqat massivning o'zi almashadi. Chuqur kuzatuv har bir
   * savol obyektiga proksi o'rnatardi va bekorga ishlardi.
   */
  const queue = shallowRef<Question[]>([])
  const index = ref(0)

  /** Savolning `QUESTIONS` ichidagi indeksi -> tanlangan ASL variant. */
  const answers = ref(new Map<number, number>())

  /** Savol indeksi -> javob berishga ketgan vaqt (ms). */
  const times = new Map<number, number>()

  /** Savol indeksi -> variantlarning aralashtirilgan tartibi. */
  const shuffled = shallowRef(new Map<number, number[]>())

  const finishedKey = ref<ResultKey | null>(null)
  const screen = ref<LevelTestScreen>('quiz')

  /** Javob tanlangandan keyingi qisqa pauza — takroriy bosishni to'sadi. */
  const isLocked = ref(false)

  /** Ishonchsiz natija ekranida ko'rsatiladigan o'rtacha vaqt (soniya). */
  const averageSeconds = ref('0,0')

  let questionStartedAt = 0
  let advanceTimer: ReturnType<typeof setTimeout> | null = null

  // ────────────────────────────────────────────────── yordamchilar ──

  function questionsOf(block: BlockId): Question[] {
    return QUESTIONS.filter(q => q.block === block)
  }

  function indexOf(question: Question): number {
    return QUESTIONS.indexOf(question)
  }

  /** Faqat NAVBATDAGI savollar bo'yicha ball — o'tilmagan blok 0 emas, yo'q. */
  function computeScore(): BlockScore {
    const result: Record<BlockId, number> = {
      harf: 0,
      talaffuz: 0,
      soz: 0,
      sarf: 0,
      nahv: 0,
    }

    for (const question of queue.value) {
      if (answers.value.get(indexOf(question)) === question.correct) {
        result[question.block]++
      }
    }

    return result
  }

  function keysOk(): boolean {
    return KEY_QUESTIONS.every(
      qi => answers.value.get(qi) === QUESTIONS[qi]?.correct,
    )
  }

  function answeredIn(block: BlockId): number {
    return queue.value.filter(
      q => q.block === block && answers.value.has(indexOf(q)),
    ).length
  }

  /*
    Navbatni blok oxirigacha qisqartirish.

    🔴 NEGA KERAK: odam orqaga qaytib javobini o'zgartirsa, undan
       KEYINGI bloklar eski javob asosida qo'shilgan bo'ladi. Ular
       o'chirilmasa, darvoza allaqachon ochilgan yo'lni qayta
       hisoblardi va natija javobga mos kelmasdi.
  */
  function truncateAfter(block: BlockId): void {
    let last = -1

    for (let i = 0; i < queue.value.length; i++) {
      if (queue.value[i]?.block === block) last = i
    }

    queue.value = queue.value.slice(0, last + 1)
  }

  /** Fisher–Yates: variantlar tartibi har seansda yangidan. */
  function shuffleAll(): void {
    const map = new Map<number, number[]>()

    QUESTIONS.forEach((question, qi) => {
      const order = question.options.map((_, i) => i)

      for (let i = order.length - 1; i > 0; i--) {
        const j = Math.floor(Math.random() * (i + 1))
        ;[order[i], order[j]] = [order[j] as number, order[i] as number]
      }

      map.set(qi, order)
    })

    shuffled.value = map
  }

  // ─────────────────────────────────────────────── ko'rinadigan holat ──

  const current = computed<Question | null>(() => queue.value[index.value] ?? null)

  /**
   * Bosqich yorlig'i.
   *
   * ★ NEGA «6 / 16» EMAS: adaptiv testda navbat uzayib boradi, ya'ni
   * maxraj o'zgaruvchan bo'lardi va odam "test cho'zilyaptimi?" deb
   * o'ylardi. Bosqich raqami esa hech qachon o'zgarmaydi.
   */
  const blockLabel = computed(() => {
    const question = current.value
    if (question === null) return ''

    const position = BLOCK_ORDER.indexOf(question.block) + 1

    return `${position}-bosqich · ${BLOCKS[question.block].name}`
  })

  /** Savolning blok ichidagi tartibi (0 dan). */
  const positionInBlock = computed(() => {
    const question = current.value
    if (question === null) return 0

    let position = 0

    for (let i = 0; i < index.value; i++) {
      if (queue.value[i]?.block === question.block) position++
    }

    return position
  })

  const countLabel = computed(() => {
    const question = current.value
    if (question === null) return ''

    return `Savol ${positionInBlock.value + 1} / ${BLOCKS[question.block].total}`
  })

  const progressPercent = computed(() => {
    const question = current.value
    if (question === null) return 0

    const blockIndex = BLOCK_ORDER.indexOf(question.block)
    const withinBlock = (positionInBlock.value + 1) / BLOCKS[question.block].total

    return ((blockIndex + withinBlock) / BLOCK_ORDER.length) * 100
  })

  /** Ekrandagi variantlar — aralashtirilgan tartibda, oxirida «Bilmayman». */
  const options = computed<DisplayOption[]>(() => {
    const question = current.value
    if (question === null) return []

    const qi = indexOf(question)
    const order = shuffled.value.get(qi) ?? question.options.map((_, i) => i)

    const list: DisplayOption[] = order.map((original, slot) => {
      const option = question.options[original]

      return {
        label: optionLabel(option ?? {}),
        isArabic: option?.arabic !== undefined,
        value: original,
        key: OPTION_KEYS[slot] ?? '',
      }
    })

    list.push({
      label: 'Bilmayman',
      isArabic: false,
      value: DONT_KNOW,
      key: OPTION_KEYS[order.length] ?? '',
    })

    return list
  })

  const pickedValue = computed(() => {
    const question = current.value
    if (question === null) return undefined

    return answers.value.get(indexOf(question))
  })

  const canGoBack = computed(() => index.value > 0)

  const result = computed(() =>
    finishedKey.value === null ? null : RESULTS[finishedKey.value],
  )

  /** Bloklar bo'yicha natija — natija ekranidagi ustunchalar. */
  const breakdown = computed<BlockBreakdown[]>(() => {
    const score = computeScore()

    return BLOCK_ORDER.map((id) => {
      const reached = queue.value.some(q => q.block === id)
      const total = BLOCKS[id].total
      const got = score[id]
      const percent = reached ? Math.round((got / total) * 100) : 0

      const tone: BlockBreakdown['tone'] = !reached
        ? 'skip'
        : percent >= 75
          ? 'good'
          : percent >= 40
            ? 'mid'
            : 'low'

      return { id, name: BLOCKS[id].name, reached, score: got, total, percent, tone }
    })
  })

  /** «Javoblaringiz tahlili» ro'yxati. */
  const review = computed<ReviewItem[]>(() =>
    queue.value.map((question, n) => {
      const qi = indexOf(question)
      const given = answers.value.get(qi)
      const isCorrect = given === question.correct

      return {
        number: n + 1,
        question: question.question,
        arabic: question.arabic,
        isCorrect,
        given:
          given === DONT_KNOW || given === undefined
            ? 'Bilmayman'
            : optionLabel(question.options[given] ?? {}),
        expected: optionLabel(question.options[question.correct] ?? {}),
        explanation: question.explanation,
      }
    }),
  )

  /**
   * Ariza izohiga tushadigan satr.
   *
   * ★ NEGA MATN: ariza `note` maydoni erkin matn. Menejer uni
   * o'qiydi — ya'ni tuzilma emas, o'qiladigan jumla kerak.
   */
  const summaryLine = computed(() => {
    const level = result.value
    if (level === null) return ''

    const score = computeScore()
    const details = BLOCK_ORDER
      .filter(id => queue.value.some(q => q.block === id))
      .map(id => `${BLOCKS[id].name} ${score[id]}/${BLOCKS[id].total}`)
      .join(', ')

    return (
      `Daraja testi: ${level.level} — ${level.name}. `
      + `Tavsiya: ${level.recommendation}. (${details})`
    )
  })

  // ──────────────────────────────────────────────────── harakatlar ──

  function start(): void {
    if (advanceTimer !== null) clearTimeout(advanceTimer)

    queue.value = questionsOf('harf')
    index.value = 0
    answers.value = new Map()
    times.clear()
    finishedKey.value = null
    screen.value = 'quiz'
    isLocked.value = false
    shuffleAll()
    questionStartedAt = Date.now()
  }

  /*
    Javoblar o'qib ulgurilmaydigan tezlikda berilganmi?

    Kamida 4 ta javob kerak: uchtasi bilan "tez" ulushi juda qo'pol
    chiqadi va halol, lekin tez o'qiydigan odamni bekorga to'sardi.
  */
  function isSuspiciouslyFast(): { average: number } | null {
    const measured = queue.value
      .map(q => times.get(indexOf(q)))
      .filter((value): value is number => typeof value === 'number')

    if (measured.length < 4) return null

    const fast = measured.filter(value => value < FAST_ANSWER_MS).length
    const average = measured.reduce((a, b) => a + b, 0) / measured.length

    return fast / measured.length >= FAST_ANSWER_RATE ? { average } : null
  }

  function finish(key: ResultKey): void {
    finishedKey.value = key

    const suspicious = isSuspiciouslyFast()

    if (suspicious !== null) {
      averageSeconds.value = (suspicious.average / 1000).toFixed(1).replace('.', ',')
      screen.value = 'warning'
      return
    }

    screen.value = 'result'
  }

  function showResultAnyway(): void {
    if (finishedKey.value !== null) screen.value = 'result'
  }

  function advance(): void {
    const question = current.value
    if (question === null) return

    const next = queue.value[index.value + 1]
    const isLastOfBlock
      = index.value === queue.value.length - 1
        || (next !== undefined && next.block !== question.block)

    if (isLastOfBlock) {
      // Himoya: blok to'liq javoblanmagan bo'lsa darvoza ochilmaydi.
      if (answeredIn(question.block) < BLOCKS[question.block].total) {
        if (index.value < queue.value.length - 1) {
          index.value++
          questionStartedAt = Date.now()
        }
        return
      }

      truncateAfter(question.block)

      const gate = GATES[question.block]

      if (gate !== undefined) {
        const key = gate(computeScore(), keysOk())
        if (key !== null) {
          finish(key)
          return
        }
      }

      const nextBlock = BLOCK_ORDER[BLOCK_ORDER.indexOf(question.block) + 1]

      if (nextBlock === undefined) {
        finish('E')
        return
      }

      queue.value = [...queue.value, ...questionsOf(nextBlock)]
    }

    index.value++
    questionStartedAt = Date.now()
  }

  function choose(value: number): void {
    const question = current.value
    if (question === null || isLocked.value) return

    const qi = indexOf(question)

    answers.value.set(qi, value)
    // Map o'zgarishi reaktiv emas — yangi nusxa bilan almashtiramiz.
    answers.value = new Map(answers.value)
    times.set(qi, Date.now() - questionStartedAt)

    // Tanlov ko'rinib tursin, keyin keyingi savolga o'tamiz.
    isLocked.value = true
    advanceTimer = setTimeout(() => {
      isLocked.value = false
      advance()
    }, 340)
  }

  function back(): void {
    if (index.value === 0) return

    index.value--
    questionStartedAt = Date.now()
  }

  function stop(): void {
    if (advanceTimer !== null) clearTimeout(advanceTimer)
    isLocked.value = false
  }

  /**
   * Klaviatura bilan javob berish: 1–5 yoki A–E (EKRANDAGI tartib bo'yicha).
   *
   * @returns tugma qayta ishlandimi — chaqiruvchi `preventDefault` qilishi uchun.
   */
  function chooseBySlot(slot: number): boolean {
    const option = options.value[slot]
    if (option === undefined) return false

    choose(option.value)
    return true
  }

  return {
    // holat
    screen,
    current,
    options,
    pickedValue,
    isLocked,
    canGoBack,
    blockLabel,
    countLabel,
    progressPercent,
    averageSeconds,
    result,
    breakdown,
    review,
    summaryLine,
    // harakatlar
    start,
    stop,
    choose,
    chooseBySlot,
    back,
    showResultAnyway,
  }
}
