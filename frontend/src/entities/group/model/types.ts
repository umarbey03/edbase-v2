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

/**
 * `Du, Ch` — jadvaldagi "Kunlari" USTUNI.
 *
 * `groupScheduleSummary` kun va vaqtni birga beradi (kartochkada bitta qator
 * — joy tejaladi), jadvalda esa ular loyiha egasining ustunlar ro'yxati
 * bo'yicha ALOHIDA ustun. Shuning uchun kunlar qismi mustaqil yorliq
 * sifatida ham kerak.
 *
 * ★ Kun yo'q bo'lsa chiziqcha: bo'sh katak "yuklanmadi" degan taassurot
 * qoldirardi, `—` esa "belgilanmagan" degan aniq ma'noni beradi.
 */
export function groupWeekdaysLabel(group: GroupDto): string {
  const days = (group.weekdays ?? []).map(weekdayLabel)
  return days.length === 0 ? '—' : days.join(', ')
}

/**
 * "Biriktirilgan kurator" ustuni.
 *
 * 🔴 BACKENDDA "KURATOR" TUSHUNCHASI IKKITA (reja hujjatidagi q6 savoli):
 *  - `assistantName` — guruhga biriktirilgan ODAM (xodim);
 *  - `curatorGroupName` — shu guruhga bog'langan KURATOR GURUHI.
 *
 * Avval ODAM ko'rsatiladi: ustoz bu ustunga "kim bilan gaplashaman?" deb
 * qaraydi, guruh nomi bu savolga javob bermaydi. Odam biriktirilmagan
 * bo'lsa — guruh nomi zaxira sifatida chiqadi, u ham hech nimadan yaxshiroq.
 *
 * ★ UCHINCHI o'qilish ham bor: kurator GURUHINI olib boradigan xodim
 * (`CuratorGroup.TeacherId`). U `GroupDto` da UMUMAN yo'q va uni ko'rsatish
 * uchun backendga yangi maydon kerak bo'lardi — shuning uchun bu variant
 * ataylab tanlanmadi (bugungi ikki maydon bilan migratsiyasiz ishlanadi).
 */
export function groupCuratorLabel(group: GroupDto): string {
  return group.assistantName ?? group.curatorGroupName ?? '—'
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

/**
 * "Harf moduli · Harflar 2" — video darslar QAYSI qismdan boshlanishi.
 *
 * `null` bo'lsa guruh kursni boshidan boshlaydi va shu matn qaytadi:
 * bo'sh joy yoki chiziqcha "sozlanmagan" degan noaniqlikni qoldirardi,
 * holbuki `null` ANIQ ma'noga ega (boshidan).
 *
 * ★ Nomlar `GroupDto` ning O'ZIDA keladi (server ichki `SELECT` bilan
 * qo'shadi), ya'ni bu yorliq uchun kurs daraxtini yuklash kerak emas.
 */
export function videoStartLabel(group: GroupDto): string {
  if (group.videoStartLessonId === null) return 'Kurs boshidan'
  const lesson = group.videoStartLessonName ?? `Dars #${group.videoStartLessonId}`
  const module = group.videoStartModuleName
  return module === null ? lesson : `${module} · ${lesson}`
}
