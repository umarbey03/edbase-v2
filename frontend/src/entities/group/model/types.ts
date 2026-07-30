import { formatClock } from '@/shared/lib/datetime'
import { lookup } from '@/shared/lib/lookup'
import type { DayOfWeekName, GroupDto, GroupTypeName, MemberStatusName } from '@/shared/types'

/** `BaseBadge` `tone` prop'i bilan mos qism to'plam. */
export type GroupTone = 'accent' | 'assistant' | 'neutral' | 'success' | 'warning' | 'danger'

const GROUP_TYPE_LABELS: Record<GroupTypeName, string> = {
  Group: 'Guruh',
  Individual: 'Individual',
  Curator: 'Kurator guruhi',
}

const GROUP_TYPE_TONES: Record<GroupTypeName, GroupTone> = {
  Group: 'accent',
  Individual: 'neutral',
  Curator: 'assistant',
}

const MEMBER_STATUS_LABELS: Record<MemberStatusName, string> = {
  Active: 'Faol',
  Paused: 'To‘xtatilgan',
  Stopped: 'Chiqarilgan',
  Moved: 'Ko‘chirilgan',
}

const MEMBER_STATUS_TONES: Record<MemberStatusName, GroupTone> = {
  Active: 'success',
  Paused: 'warning',
  Stopped: 'danger',
  Moved: 'neutral',
}

/** `.NET DayOfWeek` nomlari -> qisqa o'zbekcha (jadvalda joy tejaladi). */
const WEEKDAY_SHORT: Record<DayOfWeekName, string> = {
  Monday: 'Du',
  Tuesday: 'Se',
  Wednesday: 'Ch',
  Thursday: 'Pa',
  Friday: 'Ju',
  Saturday: 'Sh',
  Sunday: 'Ya',
}

export function groupTypeLabel(value: string): string {
  return lookup(GROUP_TYPE_LABELS, value, value)
}

export function groupTypeTone(value: string): GroupTone {
  return lookup(GROUP_TYPE_TONES, value, 'neutral')
}

export function memberStatusLabel(value: string): string {
  return lookup(MEMBER_STATUS_LABELS, value, value)
}

export function memberStatusTone(value: string): GroupTone {
  return lookup(MEMBER_STATUS_TONES, value, 'neutral')
}

export function weekdayLabel(value: string): string {
  return lookup(WEEKDAY_SHORT, value, value.slice(0, 2))
}

/** `Du, Ch · 10:00` — guruh kartochkasidagi bir qatorlik jadval xulosasi. */
export function groupScheduleSummary(group: GroupDto): string {
  const days = (group.weekdays ?? []).map(weekdayLabel).join(', ')
  const time = formatClock(group.startTime)
  if (days.length === 0) return time
  return `${days} · ${time}`
}

export function groupDisplayName(group: GroupDto): string {
  return group.name ?? `Guruh #${group.id}`
}
