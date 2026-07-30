import { sessionTypeShortLabel } from '@/entities/session'
import type { ScheduledSessionDto } from '@/shared/types'

/**
 * Guruh darslari kalendari — eski `renderCalendar()`.
 *
 * ★ HAFTA DUSHANBADAN boshlanadi (eski `lead = (first.getDay()+6)%7` va
 * `DOW = ["Du","Se","Ch","Pa","Ju","Sh","Ya"]`). Bu o'quvchi ilovasidagi
 * kalendardan FARQ QILADI — u yakshanbadan boshlanadi
 * (`WEEKDAY_HEADERS_UZ`). Ikkalasi ham eski ilovadan aynan ko'chirilgan,
 * shuning uchun "birlashtirish" qilinmadi: ustoz va o'quvchi ikki xil
 * setkani ko'rib o'rgangan.
 */
export const TEACHER_WEEKDAYS: readonly string[] = ['Du', 'Se', 'Ch', 'Pa', 'Ju', 'Sh', 'Ya']

function dayKey(date: Date): string {
  const month = String(date.getMonth() + 1).padStart(2, '0')
  const day = String(date.getDate()).padStart(2, '0')
  return `${date.getFullYear()}-${month}-${day}`
}

export interface CalendarDay {
  key: string
  dayNumber: number
  /** `false` — oldingi/keyingi oyning "quyrug'i" (xiraroq chiziladi). */
  inMonth: boolean
  isToday: boolean
  sessions: ScheduledSessionDto[]
}

/**
 * Oy setkasi: oldingi oy quyrug'i + oy kunlari. Keyingi oy quyrug'i eski
 * ilovada ham CHIZILMAGAN — oxirgi qator to'liq bo'lmasligi mumkin.
 */
export function buildMonthGrid(
  anchor: Date,
  sessions: readonly ScheduledSessionDto[],
  now: Date,
): CalendarDay[] {
  const year = anchor.getFullYear()
  const month = anchor.getMonth()

  const byDay = new Map<string, ScheduledSessionDto[]>()
  for (const session of sessions) {
    const key = dayKey(new Date(session.scheduledStart))
    const list = byDay.get(key)
    if (list === undefined) byDay.set(key, [session])
    else list.push(session)
  }
  for (const list of byDay.values()) {
    list.sort(
      (a, b) => new Date(a.scheduledStart).getTime() - new Date(b.scheduledStart).getTime(),
    )
  }

  const todayKey = dayKey(now)
  const cells: CalendarDay[] = []

  // Dushanba = 0 (JS'da yakshanba = 0, shuning uchun +6 va mod 7).
  const lead = (new Date(year, month, 1).getDay() + 6) % 7
  const prevMonthDays = new Date(year, month, 0).getDate()

  for (let i = 0; i < lead; i += 1) {
    const dayNumber = prevMonthDays - lead + i + 1
    cells.push({
      key: `out-${dayNumber}`,
      dayNumber,
      inMonth: false,
      isToday: false,
      sessions: [],
    })
  }

  const daysInMonth = new Date(year, month + 1, 0).getDate()
  for (let day = 1; day <= daysInMonth; day += 1) {
    const key = dayKey(new Date(year, month, day))
    cells.push({
      key,
      dayNumber: day,
      inMonth: true,
      isToday: key === todayKey,
      sessions: byDay.get(key) ?? [],
    })
  }

  return cells
}

/** Kalendar katagidagi hodisa rangi (eski `.cal-ev` sinflari). */
export type CalendarEventTone = 'live' | 'held' | 'missed' | 'teacher' | 'assistant'

export interface CalendarEvent {
  label: string
  tone: CalendarEventTone
}

/** Eski `renderCalendar()` ichidagi yorliq/rang tanlash mantiqining nusxasi. */
export function calendarEvent(session: ScheduledSessionDto, now: Date): CalendarEvent {
  if (session.status === 'Live') return { label: 'Jonli', tone: 'live' }
  if (session.status === 'Ended') return { label: '✓ O‘tilgan', tone: 'held' }
  if (session.status === 'Cancelled') return { label: 'Bekor', tone: 'missed' }

  const end = new Date(session.scheduledEnd).getTime()
  if (!Number.isNaN(end) && end < now.getTime()) {
    return { label: '✗ O‘tilmagan', tone: 'missed' }
  }

  return {
    label: sessionTypeShortLabel(session.type),
    tone: session.type === 'Teacher' ? 'teacher' : 'assistant',
  }
}

/** Eski `updateMeta()`: "O'tilgan darslar — Ustoz: 12 | Kurator: 4". */
export function heldSummary(sessions: readonly ScheduledSessionDto[]): string {
  const teacher = sessions.filter((s) => s.type === 'Teacher' && s.status === 'Ended').length
  const assistant = sessions.filter((s) => s.type === 'Assistant' && s.status === 'Ended').length
  return `O‘tilgan darslar — Ustoz: ${teacher} | Kurator: ${assistant}`
}
