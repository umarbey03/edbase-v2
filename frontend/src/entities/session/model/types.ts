import { lookup } from '@/shared/lib/lookup'
import type { LiveSessionDto, SessionStatusName, SessionTypeName } from '@/shared/types'

export type LiveSession = LiveSessionDto

const STATUS_LABELS: Record<SessionStatusName, string> = {
  Scheduled: 'Rejalashtirilgan',
  Live: 'Jonli efirda',
  Ended: 'Yakunlangan',
  Cancelled: 'Bekor qilingan',
}

export type StatusTone = 'neutral' | 'live' | 'warning' | 'danger'

const STATUS_TONES: Record<SessionStatusName, StatusTone> = {
  Scheduled: 'neutral',
  Live: 'live',
  Ended: 'neutral',
  Cancelled: 'danger',
}

const TYPE_LABELS: Record<SessionTypeName, string> = {
  Teacher: 'Ustoz darsi',
  Assistant: 'Kurator darsi',
}

export function sessionStatusLabel(status: string): string {
  return lookup(STATUS_LABELS, status, status)
}

export function sessionStatusTone(status: string): StatusTone {
  return lookup(STATUS_TONES, status, 'neutral')
}

export function sessionTypeLabel(type: string): string {
  return lookup(TYPE_LABELS, type, type)
}

export function sessionTitle(session: LiveSession): string {
  const title = session.title?.trim()
  return title !== undefined && title.length > 0 ? title : session.groupName
}

/** Darsga kirish mumkinmi: jonli, yoki boshlanishiga 15 daqiqadan kam qolgan. */
const EARLY_JOIN_WINDOW_MS = 15 * 60 * 1000

export function isJoinable(session: LiveSession, now: Date = new Date()): boolean {
  if (session.status === 'Live') return true
  if (session.status !== 'Scheduled') return false
  const start = new Date(session.scheduledStart).getTime()
  if (Number.isNaN(start)) return false
  return start - now.getTime() <= EARLY_JOIN_WINDOW_MS
}
