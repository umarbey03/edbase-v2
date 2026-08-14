import type { LeaderboardRowDto } from '@/shared/types'

/**
 * Eski ilovadagi medal yorliqlari: 1–3 o'rin uchun medal, qolganida raqam.
 *
 * Emoji ATAYLAB (eski ilovada ham shunday): ikonka to'plamiga uchta yangi
 * shakl qo'shishdan arzon va o'quvchi ularni bir qarashda taniydi.
 */
export function rankBadge(rank: number): string {
  if (rank === 1) return '🥇'
  if (rank === 2) return '🥈'
  if (rank === 3) return '🥉'
  return String(rank)
}

/** Ball tafsilotidagi bitta mezon. */
export interface ScorePart {
  label: string
  /** `null` — shu oyda bu mezon bo'yicha ma'lumot yo'q. */
  percent: number | null
  /** `BaseBadge`/matn rangi uchun. */
  tone: 'success' | 'accent' | 'assistant' | 'warning'
}

/**
 * Yakuniy ball MAVJUD mezonlarning o'rtachasi (server hisoblaydi).
 * Tafsilot ko'rsatilishi SHART: eski ilovada o'quvchi qatorni bosib
 * "nega shu ball?" savoliga javob olardi — bu bo'lmasa reyting "qora
 * quti" bo'lib qoladi va ishonchni yo'qotadi.
 *
 * `null` mezon "0 ball" DEGANI EMAS — "hisobga olinmagan" degani, shuning
 * uchun ekranda ham nol emas, chiziqcha ko'rsatiladi.
 *
 * ★ R24 dan keyin mezon TO'RTTA. "Dars bahosi" oxirida turadi — mezonlar
 * tartibi eski ilovadagi ketma-ketlikni saqlaydi va yangi ustun mavjud
 * uchtasini surib qo'ymaydi.
 */
export function scoreParts(row: LeaderboardRowDto): ScorePart[] {
  return [
    { label: 'Davomat', percent: row.attendancePercent, tone: 'success' },
    { label: 'Vazifalar', percent: row.assignmentPercent, tone: 'accent' },
    { label: 'Testlar', percent: row.testPercent, tone: 'assistant' },
    { label: 'Dars bahosi', percent: row.lessonPercent, tone: 'warning' },
  ]
}
