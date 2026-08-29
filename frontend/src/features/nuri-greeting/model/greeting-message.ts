import { formatTime, isSameDay } from '@/shared/lib/datetime'
import { formatSum } from '@/shared/lib/money'
import { truncate } from '@/shared/lib/text'
import type {
  AttendanceSummaryDto,
  LiveSessionDto,
  StudentAssignmentDto,
} from '@/shared/types'

/**
 * ════════════════════════════════════════════════════════════════════════
 * «NURI» NIMA DEYDI — HOLATNI TANLASH QOIDASI (2026-08-30)
 * ════════════════════════════════════════════════════════════════════════
 *
 * Bu fayl — salomlashuvning butun MAZMUNI. U ATAYLAB SOF FUNKSIYA:
 * so'rov ham, `ref` ham, `Date.now()` ham yo'q — hamma narsa kiruvchi
 * `GreetingInput` dan keladi.
 *
 * ★ NEGA SHUNDAY: "qaysi vaziyatda nima deymiz" — mahsulot qarori va u
 *   vaqt o'tib eng ko'p o'zgaradigan qism. Sof funksiya bo'lgani uchun
 *   uni o'qish, jadval sifatida ko'rib chiqish va sinash uchun na server,
 *   na brauzer kerak.
 *
 * ══════════════════════════════════════════════════════════════════════
 * 🔴 BITTA HOLAT — BITTA GAP. RO'YXAT TARTIBI = USTUVORLIK.
 *
 * O'quvchida bir vaqtda uchta sabab ham bo'lishi mumkin (o'tgan darsni
 * qoldirgan + vazifasi kechikkan + qarzi bor). Uchalasini bitta pufakka
 * yozish salomlashuvni HISOBOTGA aylantirardi — maskotning butun ma'nosi
 * esa aynan hisobot bo'lmaslikda. Shuning uchun pastdagi ro'yxat
 * yuqoridan pastga tekshiriladi va BIRINCHI mos kelgani g'olib bo'ladi.
 *
 * Tartib "eng shoshilinch — eng yuqorida" emas, "eng KO'P NARSANI HAL
 * QILADIGANI eng yuqorida" bo'yicha qurilgan:
 *
 *   1. tanishuv  — birinchi kirish. Bir marta bo'ladi va uni hech nima
 *                  bosib ketmasligi kerak.
 *   2. jonli     — dars AYNI HOZIR ketyapti. Vaqtga bog'liq yagona holat:
 *                  bir daqiqadan keyin bu gapning ma'nosi qolmaydi.
 *   3. qoldirgan — o'tgan darsga kelmagan. Markazning eng qimmat
 *                  ko'rsatkichi — to'kilish (`manage-attrition`,
 *                  `manage-absentees` panellari aynan shu uchun bor).
 *                  Qaytib kelmagan o'quvchi vazifa ham qilmaydi, pul ham
 *                  to'lamaydi — ya'ni bu gap qolgan hammasidan ustun.
 *   4. kechikkan — vazifa muddati o'tib ketyapti.
 *   5. bugun     — bugun darsi bor (hali boshlanmagan).
 *   6. vazifa    — topshirilmagan vazifa bor (muddati hali bor).
 *   7. qarz      — to'lov qarzi. ENG PASTDA ataylab: pul haqidagi gap
 *                  o'quv motivatsiyasini ko'taradigan gapni hech qachon
 *                  bosib ketmasligi kerak.
 *   8. seriya    — hech qanday muammo yo'q va maqtash uchun sabab bor.
 *   9. salom     — zaxira. Ma'lumot kelmasa ham ekran DOIM ishlaydi.
 * ══════════════════════════════════════════════════════════════════════
 *
 * ★ MUROJAAT "SEN" DA. O'quvchilar — bolalar va o'smirlar, maskot esa
 *   ularning "hamrohi"; "siz" bu ovozni rasmiy qilib, butun g'oyani
 *   buzardi. Loyiha egasi bergan namuna ham aynan shu ohangda edi
 *   («seni o'tgan darsda ko'rmadim…»).
 */

/** Tanlangan holat kaliti — ekranda ko'rinmaydi, sozlash va tahlil uchun. */
export type GreetingKey =
  | 'tanishuv'
  | 'jonli'
  | 'qoldirgan'
  | 'kechikkan'
  | 'bugun'
  | 'vazifa'
  | 'qarz'
  | 'seriya'
  | 'salom'

export interface GreetingMessage {
  key: GreetingKey
  /** Pufak ichidagi matn — harfma-harf yoziladi. */
  text: string
}

export interface GreetingInput {
  /** `User.fullName` — birinchi so'zi murojaat sifatida ishlatiladi. */
  fullName: string
  /**
   * Shu qurilmada shu o'quvchi ILGARI salomlashganmi
   * (`hasGreetedBefore`).
   */
  greetedBefore: boolean
  now: Date
  /** Hozir ketayotgan dars. Bo'lmasa — `null`. */
  liveSession: LiveSessionDto | null
  /** Eng yaqin kelayotgan dars. Bo'lmasa — `null`. */
  nextSession: LiveSessionDto | null
  /** Davomat xulosasi. So'rov muvaffaqiyatsiz bo'lsa — `null`. */
  attendance: AttendanceSummaryDto | null
  /** O'quvchining vazifalari. So'rov muvaffaqiyatsiz bo'lsa — `null`. */
  assignments: StudentAssignmentDto[] | null
  /** To'lov qarzi. `null` — ma'lumot yo'q yoki o'quvchi to'lovdan istisno. */
  debt: number | null
}

/**
 * Murojaat uchun BIRINCHI so'z.
 *
 * ★ EVRISTIKA, QOIDA EMAS — va bu ochiq tan olinadi. Bazada yagona
 *   "To'liq ism" maydoni bor (`UserFormDialog`), ya'ni "ism qayerda
 *   turadi" degan shartnoma YO'Q. O'zbekistonda bunday maydonga odatda
 *   "Ism Familiya" yoziladi, shuning uchun birinchi so'z olinadi.
 *
 * ★ XATO BO'LSA HAM ZARARSIZ: eng yomon holatda maskot familiya bilan
 *   murojaat qiladi. Shuning uchun bu yerda "aqlli" tahlil (familiya
 *   qo'shimchalarini topish va h.k.) ATAYLAB yozilmadi — u ba'zi
 *   ismlarda to'g'ri, ba'zilarida kulgili natija berardi.
 *
 * ★ UZUNLIK CHEGARASI: maydon erkin matn va u yerda bir so'zli uzun
 *   yozuv bo'lishi mumkin — pufakni cho'zib yubormasin.
 */
function firstNameOf(fullName: string): string {
  const word = fullName.trim().split(/\s+/)[0] ?? ''
  return truncate(word, 20)
}

/**
 * «Najmiddin, seni …» yoki ism bo'lmasa «Seni …».
 *
 * ★ GAPLAR KICHIK HARF BILAN yoziladi va bosh harf SHU YERDA qo'yiladi:
 * aks holda har gapning ikki nusxasini (ismli va ismsiz) saqlash kerak
 * bo'lardi. Ism yo'q holat kamdan-kam, lekin REAL — `fullName` bo'sh
 * bo'lishi mumkin.
 */
function addressed(firstName: string, sentence: string): string {
  if (firstName.length === 0) {
    return sentence.charAt(0).toUpperCase() + sentence.slice(1)
  }
  return `${firstName}, ${sentence}`
}

/** Dars BUGUNMI (mahalliy kun bo'yicha). */
function startsToday(session: LiveSessionDto, now: Date): boolean {
  const start = new Date(session.scheduledStart)
  if (Number.isNaN(start.getTime())) return false
  return isSameDay(start, now)
}

/**
 * Hali BOSHLANMAGAN vazifalar.
 *
 * ★ `canSubmit` SERVER QARORI va u qayta hisoblanmaydi (`assignmentState`
 *   dagi bilan AYNI mulohaza): qulflangan dars, yopilgan vazifa va
 *   to'lov to'sig'i — hammasi o'sha bayroqda jamlangan. Mijozda
 *   `lessonUnlocked` va `isOverdue` ni birlashtirib "o'z qoidamiz" ni
 *   yasash ikki manba yaratardi.
 *
 * ★ QAYTA TOPSHIRISH (`allowResubmit`) BU YERGA KIRMAYDI: o'quvchi ishni
 *   allaqachon topshirgan va unga ustozning izohi kelgan. «Vazifang
 *   kutyapti» degan gap bunday holatda YOLG'ON bo'lardi — kutayotgan
 *   narsa vazifa emas, o'quvchining tuzatishi.
 */
function untouched(items: StudentAssignmentDto[]): StudentAssignmentDto[] {
  return items.filter((item) => item.canSubmit && item.mySubmission === null)
}

export function pickGreeting(input: GreetingInput): GreetingMessage {
  const name = firstNameOf(input.fullName)

  /* ─────────────────────────────────────────────── 1. TANISHUV ───────── */
  /*
    IKKI SHART BIRGA: qurilmada belgi yo'q VA hali bitta ham dars
    o'tilmagan. Yolg'iz birinchisi yetmaydi — brauzer xotirasini tozalagan
    yoki telefonini almashtirgan eski o'quvchi ham "yangi" bo'lib
    ko'rinardi va unga tanishtiruv matni chiqardi.

    Davomat ma'lumoti kelmagan bo'lsa (`null`) tanishuv KO'RSATILMAYDI:
    noaniqlikda "sen yangisan" deyishdan ko'ra oddiy salom aytish
    xavfsizroq.
  */
  const noLessonsYet = input.attendance !== null && input.attendance.overall.total === 0
  if (!input.greetedBefore && noLessonsYet) {
    return {
      key: 'tanishuv',
      text: addressed(
        name,
        'tanishib qo‘yaylik — men Nuri. Darsing, vazifang va natijalaring '
        + 'shu yerda. Yo‘lda birga bo‘lamiz!',
      ),
    }
  }

  /* ────────────────────────────────────────────────── 2. JONLI ───────── */
  if (input.liveSession !== null) {
    return {
      key: 'jonli',
      text: addressed(name, 'darsimiz allaqachon boshlandi! Kutyapmiz, tez qo‘shil.'),
    }
  }

  /* ────────────────────────────────────────────── 3. QOLDIRGAN ───────── */
  /*
    ★ "O'TGAN DARSGA KELMAGAN" NI QAYERDAN BILAMIZ: `AttendanceSummaryDto`
      da darslar RO'YXATI yo'q, lekin `streak` bor — u ketma-ket
      qatnashish seriyasi va BIRINCHI qoldirilgan darsda uziladi. Ya'ni
      "dars o'tilgan, lekin seriya nol" degani AYNAN "eng oxirgi darsga
      kelmagan" degani. Alohida so'rov ham, alohida endpoint ham kerak
      emas.

    ★ MATN AYBLAMAYDI. «Kelmading» emas, «ko'rmadim» — bu loyiha egasi
      bergan namunadagi ohang va u ataylab: aybdor his qilgan o'quvchi
      qaytib kelmaydi, sog'ingan maskot esa qaytishga sabab beradi.
  */
  /*
    Bugungi dars IKKI holatda kerak bo'ladi (3 va 5), shuning uchun bir
    marta hisoblanadi: shart ikki joyda takrorlansa, biri o'zgarganda
    ikkinchisi jimgina orqada qolardi.
  */
  const todaySession = input.nextSession !== null && startsToday(input.nextSession, input.now)
    ? input.nextSession
    : null

  const attendance = input.attendance
  if (attendance !== null && attendance.overall.total > 0 && attendance.streak === 0) {
    return {
      key: 'qoldirgan',
      // Bugun darsi bo'lsa — taklif ANIQ vaqt bilan aytiladi, mavhum emas.
      text: todaySession !== null
        ? addressed(
          name,
          `seni o‘tgan darsda ko‘rmadim… Bugun soat ${formatTime(todaySession.scheduledStart)} da `
          + 'darsimiz bor — birga o‘qiymizmi?',
        )
        : addressed(name, 'seni o‘tgan darsda ko‘rmadim… Qaytding, demak davom etamiz!'),
    }
  }

  /* ───────────────────────────────────── 4-6. VAZIFA VA JADVAL ───────── */
  const pending = input.assignments !== null ? untouched(input.assignments) : []
  const overdue = pending.filter((item) => item.isOverdue)

  if (overdue.length > 0) {
    return {
      key: 'kechikkan',
      text: addressed(
        name,
        overdue.length === 1
          ? 'bitta vazifang muddatidan kechikdi. Zarari yo‘q — bugun yopamiz.'
          : `${overdue.length} ta vazifang muddatidan kechikdi. Bittadan boshlaymiz.`,
      ),
    }
  }

  if (todaySession !== null) {
    return {
      key: 'bugun',
      text: addressed(
        name,
        `bugun soat ${formatTime(todaySession.scheduledStart)} da darsimiz bor. Tayyormisan?`,
      ),
    }
  }

  if (pending.length > 0) {
    return {
      key: 'vazifa',
      text: addressed(
        name,
        pending.length === 1
          ? 'bitta vazifang seni kutyapti. Bugun yopib qo‘yamizmi?'
          : `${pending.length} ta vazifang seni kutyapti. Bugun bittasini yopamizmi?`,
      ),
    }
  }

  /* ─────────────────────────────────────────────────── 7. QARZ ───────── */
  /*
    ★ SUMMA AYTILADI, "qarzing bor" bilan cheklanilmaydi: o'quvchi buni
      uyda ota-onasiga yetkazadi va aniq raqamsiz gap foydasiz bo'lardi.
      Raqamning O'ZI o'quvchiga allaqachon ochiq (profil moliya bo'limi),
      ya'ni bu yerda YANGI ma'lumot oshkor bo'lmayapti.

    ★ AYBLASH YO'Q va TO'SIQ HAM YO'Q: bu shunchaki eslatma. Haqiqiy
      to'siqni (bloklash) server qo'yadi va o'z matnini o'zi beradi
      (`StudentRecordingsPage` dagi `detail`).
  */
  if (input.debt !== null && input.debt > 0) {
    return {
      key: 'qarz',
      text: addressed(
        name,
        `to‘lov bo‘yicha ${formatSum(input.debt)} qarz turibdi — uyda eslatib qo‘ysang bo‘ldi.`,
      ),
    }
  }

  /* ───────────────────────────────────────────────── 8. SERIYA ───────── */
  /*
    ★ CHEGARA 3: ikki dars — tasodif, uch dars — odat. Har qatnashuvni
      maqtash maqtovni arzonlashtirardi.
  */
  if (attendance !== null && attendance.streak >= 3) {
    return {
      key: 'seriya',
      text: addressed(
        name,
        `ketma-ket ${attendance.streak} darsda qatnashding — zo‘r ketyapsan! `
        + 'Shu ruhda davom etamiz.',
      ),
    }
  }

  /* ────────────────────────────────────────────────── 9. SALOM ───────── */
  /*
    ZAXIRA. Bu yerga IKKI yo'l bilan kelinadi: (a) hammasi joyida,
    (b) so'rovlarning hammasi uzilgan (internet yo'q). Ikkalasida ham
    ekran ISHLAYDI va foydalanuvchi hech qanday xato ko'rmaydi —
    salomlashuv ma'lumotga bog'liq bo'lmasligi kerak.
  */
  return {
    key: 'salom',
    text: addressed(name, 'yana ko‘rishganimizdan xursandman. Bugun nima o‘rganamiz?'),
  }
}
