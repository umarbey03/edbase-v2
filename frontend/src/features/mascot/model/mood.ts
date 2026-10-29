import type { MascotMood } from './types'

/**
 * ════════════════════════════════════════════════════════════════════════
 * MASKOT KAYFIYATI — QOIDALAR JADVALI (2026-10-29)
 * ════════════════════════════════════════════════════════════════════════
 *
 * Kirish: o'quvchi holatining kichik "surati" (`MoodSignals`).
 * Chiqish: kayfiyat + qisqa sarlavha + bitta gap + bitta harakat.
 *
 * ★ SOF FUNKSIYA: so'rov, `ref`, `Date.now()` yo'q. Signal yig'ish
 *   (`signals.ts`) va qaror (shu fayl) ataylab ajratilgan — qoidani
 *   jadval sifatida o'qish va sinash uchun na server, na brauzer kerak.
 *
 * ★ `null` = "MA'LUMOT YO'Q", nol EMAS. Mustaqil o'quvchida (guruhsiz)
 *   davomat va jonli dars umuman yo'q; so'rov yiqilganda ham shunday.
 *   Har qoida `null` ni "bu signal gapirmaydi" deb o'qiydi — ya'ni
 *   ma'lumot yetishmasa maskot hech qachon xavotirga TUSHMAYDI.
 *
 * ══════════════════════════════════════════════════════════════════════
 * TARTIB (yuqoridan pastga, birinchi mos kelgani g'olib):
 *
 *   1. thinking   · live          — jonli dars AYNI HOZIR ketyapti.
 *   2. encouraging· new           — tarix yo'q (yangi o'quvchi).
 *   3. sleepy     · inactive      — ≥ 7 kun hech qanday faollik yo'q.
 *   4. YUTUQ va XAVOTIR TAROZISI:
 *        yutuqlar  = 1-3-o'rin · ketma-ket ≥ 4 dars · o'rtacha baho ≥ 90%
 *        xavotirlar= o'tgan darsni qoldirgan · kechikkan vazifa ·
 *                    davomat < 60% (kamida 5 dars)
 *      yutuq ≥ xavotir (va yutuq bor) → celebrate   ← TENGDA YAXSHI XABAR
 *      aks holda xavotir bor          → concerned
 *   5. thinking   · due-soon      — 24 soat ichida muddati tugaydigan ish.
 *   6. happy      · doing-well    — barqaror yaxshi (pastdagi shartlar).
 *   7. encouraging· pending/default.
 * ══════════════════════════════════════════════════════════════════════
 *
 * 🔴 HECH QACHON UYALTIRMAYDI: xavotir gaplari "sen qilmading" emas,
 *    "birga yopamiz" ohangida; raqamlar faqat yaxshi xabarda aytiladi.
 *    Murojaat "sen" da — salomlashuvdagi «Nuri» ovozi bilan bir xil
 *    (`nuri-greeting/model/greeting-message.ts` dagi izoh).
 */

/** Chegara qiymatlari — bitta joyda, sinovda ham shular ishlatiladi. */
export const MOOD_THRESHOLDS = {
  /** Shuncha kun faollik bo'lmasa — «sog'indim». */
  inactiveDays: 7,
  /** Ketma-ket dars — server bonusi (`PointRules`: 4 darsga +10). */
  streakBonus: 4,
  /** Reytingda shu o'ringacha — bayram. */
  topRank: 3,
  /** O'rtacha baho (%), bayram uchun; kamida `minGraded` ta baho. */
  topGradePercent: 90,
  minGraded: 2,
  /** Davomat shundan past bo'lsa xavotir; kamida `minLessonsForPercent` dars. */
  lowAttendancePercent: 60,
  minLessonsForPercent: 5,
  /** "Yaxshi ketyapti" chegaralari. */
  goodAttendancePercent: 80,
  goodStreak: 2,
  goodGradePercent: 75,
  /** Mustaqil o'quvchi: shuncha kun ichida faol bo'lsa — yaxshi. */
  recentActiveDays: 3,
} as const

export interface MoodSignals {
  /** Obuna bo'yicha mustaqil o'quvchi (guruh, jonli dars, davomat yo'q). */
  isSelfStudy: boolean
  /** Jonli dars ayni hozir ketyapti. */
  liveNow: boolean
  /** Biror narsa umuman qilinganmi (dars, topshiriq, ball). `false` — yangi. */
  hasHistory: boolean
  /** O'tilgan darslar soni. `null` — ma'lumot yo'q / mustaqil. */
  lessonsTotal: number | null
  /** Davomat foizi 0..100. */
  attendancePercent: number | null
  /** Ketma-ket qatnashish. `0` + `lessonsTotal > 0` = oxirgi darsni qoldirgan. */
  attendanceStreak: number | null
  /** Muddati o'tgan, hali topshirilmagan vazifalar. */
  overdueCount: number | null
  /** Topshirish mumkin, hali topshirilmagan (kechikkanlari ham ichida). */
  pendingCount: number | null
  /** Muddati 24 soat ichida tugaydigan (hali kechikmagan) vazifalar. */
  dueSoonCount: number | null
  /** Baholangan topshiriqlar soni va o'rtacha foizi. */
  gradedCount: number
  avgGradePercent: number | null
  /** Shu oydagi ball va guruhdagi o'rin. */
  monthPoints: number | null
  rank: number | null
  /** Oxirgi faollikdan beri o'tgan kunlar. `null` — noma'lum. */
  daysSinceActivity: number | null
}

/** Karta tugmasi — marshrut emas, NIYAT (marshrutni UI tanlaydi). */
export type MoodAction = 'join-live' | 'assignments' | 'learn' | 'rating'

export type MoodReason =
  | 'live'
  | 'new'
  | 'inactive'
  | 'top-rank'
  | 'streak-bonus'
  | 'top-grades'
  | 'missed-lesson'
  | 'overdue'
  | 'low-attendance'
  | 'due-soon'
  | 'doing-well'
  | 'pending'
  | 'default'

export interface MoodResult {
  mood: MascotMood
  reason: MoodReason
  title: string
  message: string
  action: MoodAction | null
}

type Win = 'top-rank' | 'streak-bonus' | 'top-grades'
type Worry = 'missed-lesson' | 'overdue' | 'low-attendance'

const T = MOOD_THRESHOLDS

/** Yutuqlar — kuchlisi birinchi. */
export function collectWins(s: MoodSignals): Win[] {
  const wins: Win[] = []
  if (s.rank !== null && s.rank <= T.topRank && (s.monthPoints ?? 0) > 0) wins.push('top-rank')
  if (s.attendanceStreak !== null && s.attendanceStreak >= T.streakBonus) wins.push('streak-bonus')
  if (s.avgGradePercent !== null && s.gradedCount >= T.minGraded && s.avgGradePercent >= T.topGradePercent) {
    wins.push('top-grades')
  }
  return wins
}

/**
 * Xavotirlar — muhimi birinchi. Qoldirilgan dars kechikkan vazifadan
 * USTUN: qaytib kelmagan o'quvchi vazifa ham qilmaydi (salomlashuvdagi
 * tartib bilan bir xil mulohaza).
 */
export function collectWorries(s: MoodSignals): Worry[] {
  const worries: Worry[] = []
  if (!s.isSelfStudy && (s.lessonsTotal ?? 0) > 0 && s.attendanceStreak === 0) worries.push('missed-lesson')
  if ((s.overdueCount ?? 0) > 0) worries.push('overdue')
  if (
    s.lessonsTotal !== null
    && s.attendancePercent !== null
    && s.lessonsTotal >= T.minLessonsForPercent
    && s.attendancePercent < T.lowAttendancePercent
  ) {
    worries.push('low-attendance')
  }
  return worries
}

function isDoingWell(s: MoodSignals): boolean {
  if (s.attendanceStreak !== null && s.attendanceStreak >= T.goodStreak) return true
  if (
    s.attendancePercent !== null
    && (s.lessonsTotal ?? 0) >= 3
    && s.attendancePercent >= T.goodAttendancePercent
  ) {
    return true
  }
  if (s.avgGradePercent !== null && s.gradedCount >= 1 && s.avgGradePercent >= T.goodGradePercent) return true
  return s.isSelfStudy && s.daysSinceActivity !== null && s.daysSinceActivity <= T.recentActiveDays
}

/** Mustaqil o'quvchining asosiy yo'li — darslar; guruhniki — vazifalar. */
function defaultAction(s: MoodSignals): MoodAction | null {
  if ((s.pendingCount ?? 0) > 0) return 'assignments'
  return s.isSelfStudy ? 'learn' : null
}

function winText(win: Win, s: MoodSignals): { title: string, message: string } {
  switch (win) {
    case 'top-rank':
      return s.rank === 1
        ? { title: 'Guruhda 1-o‘rin!', message: 'Bu oy eng ko‘p ball sen tomonda. Zo‘r ketyapsan!' }
        : { title: `Guruhda ${s.rank}-o‘rin!`, message: 'Kuchli uchlikdasan. Yana bir oz — va cho‘qqi seniki.' }
    case 'streak-bonus':
      return {
        title: `Ketma-ket ${s.attendanceStreak} dars!`,
        message: 'Bitta ham darsni qoldirmading — bonus ball ham sening. Barakalla!',
      }
    case 'top-grades':
      return {
        title: 'A’lo baholar!',
        message: `Vazifalaringdan o‘rtacha ${Math.round(s.avgGradePercent ?? 0)}%. Ustozing ham xursand.`,
      }
  }
}

/** Bayram gapiga qo'shiladigan yumshoq eslatma (tarozi tengligida). */
const WORRY_NUDGE: Record<Worry, string> = {
  'overdue': ' Kechikkan vazifani ham yopsak — mukammal bo‘ladi.',
  'missed-lesson': ' Keyingi darsda albatta ko‘rishamiz!',
  'low-attendance': ' Darslarga ham birga kelib tursak — yanada zo‘r.',
}

function worryText(worry: Worry, s: MoodSignals): { title: string, message: string } {
  switch (worry) {
    case 'missed-lesson':
      return {
        title: 'Seni o‘tgan darsda ko‘rmadim',
        message: 'Hech gap emas — o‘tgan mavzuni ko‘rib, keyingi darsga birga tayyorlanamiz.',
      }
    case 'overdue':
      return {
        title: (s.overdueCount ?? 0) === 1 ? 'Bitta vazifa kutib qoldi' : `${s.overdueCount} ta vazifa kutib qoldi`,
        message: 'Zarari yo‘q — bittadan boshlaymiz. Bugun bittasini yopamizmi?',
      }
    case 'low-attendance':
      return {
        title: 'Darslarda seni sog‘inyapmiz',
        message: 'Har bir dars — oldinga bir qadam. Keyingisiga birga boramizmi?',
      }
  }
}

export function decideMood(s: MoodSignals): MoodResult {
  /* 1. Jonli dars — vaqtga bog'liq yagona holat, hammasidan ustun. */
  if (s.liveNow && !s.isSelfStudy) {
    return {
      mood: 'thinking',
      reason: 'live',
      title: 'Dars ketyapti!',
      message: 'Hamma joyida — faqat sen yetishmayapsan. Tez qo‘shil!',
      action: 'join-live',
    }
  }

  const worries = collectWorries(s)

  /* 2. Yangi o'quvchi. Kechikkan vazifasi bo'lsa — endi "yangi" emas. */
  if (!s.hasHistory && !worries.includes('overdue')) {
    return {
      mood: 'encouraging',
      reason: 'new',
      title: 'Yo‘lni birga boshlaymiz!',
      message: s.isSelfStudy
        ? 'Birinchi darsni ochib ko‘r — men har qadamda yoningdaman.'
        : 'Birinchi darsdan keyin bu yerda yutuqlaring paydo bo‘ladi.',
      action: defaultAction(s) ?? (s.isSelfStudy ? 'learn' : null),
    }
  }

  /* 3. Uzoq tanaffus — ayblov emas, sog'inch. */
  if (s.daysSinceActivity !== null && s.daysSinceActivity >= T.inactiveDays) {
    return {
      mood: 'sleepy',
      reason: 'inactive',
      title: 'Seni sog‘indim!',
      message: 'Bir necha kundan beri ko‘rishmadik. Kichik bir qadamdan qayta boshlaymizmi?',
      action: (s.overdueCount ?? 0) > 0 || (s.pendingCount ?? 0) > 0 ? 'assignments' : 'learn',
    }
  }

  /* 4. Tarozi: tengda yaxshi xabar g'olib. */
  const wins = collectWins(s)
  const [firstWin] = wins
  if (firstWin !== undefined && wins.length >= worries.length) {
    const text = winText(firstWin, s)
    const [firstWorry] = worries
    return {
      mood: 'celebrate',
      reason: firstWin,
      title: text.title,
      message: firstWorry === undefined ? text.message : text.message + WORRY_NUDGE[firstWorry],
      action: worries.includes('overdue') ? 'assignments' : 'rating',
    }
  }

  const [firstWorry] = worries
  if (firstWorry !== undefined) {
    const text = worryText(firstWorry, s)
    return {
      mood: 'concerned',
      reason: firstWorry,
      title: text.title,
      message: text.message,
      action: firstWorry === 'missed-lesson' && (s.overdueCount ?? 0) === 0 ? 'learn' : 'assignments',
    }
  }

  /* 5. Muddati yaqin ish. */
  if ((s.dueSoonCount ?? 0) > 0) {
    return {
      mood: 'thinking',
      reason: 'due-soon',
      title: 'Muddat yaqinlashyapti',
      message: 'Bir vazifaning muddati ertagacha. Hozir boshlasak — bemalol ulguramiz.',
      action: 'assignments',
    }
  }

  /* 6. Barqaror yaxshi. */
  if (isDoingWell(s)) {
    const pending = s.pendingCount ?? 0
    return {
      mood: 'happy',
      reason: 'doing-well',
      title: 'Zo‘r ketyapsan!',
      message: pending > 0
        ? 'Shu ruhda davom etamiz. Navbatdagi vazifa ham seni kutyapti.'
        : 'Shu ruhda davom etamiz — har kungi kichik qadam katta natija beradi.',
      action: defaultAction(s) ?? 'rating',
    }
  }

  /* 7. Neytral. */
  if ((s.pendingCount ?? 0) > 0) {
    return {
      mood: 'encouraging',
      reason: 'pending',
      title: 'Vazifang kutyapti',
      message: 'Bugun bittasini yopib qo‘yamizmi? Boshlash — eng qiyini.',
      action: 'assignments',
    }
  }

  return {
    mood: 'encouraging',
    reason: 'default',
    title: 'Bugun nima o‘rganamiz?',
    message: 'Har kuni ozgina — va natija o‘zi keladi. Men yoningdaman.',
    action: s.isSelfStudy ? 'learn' : 'rating',
  }
}
