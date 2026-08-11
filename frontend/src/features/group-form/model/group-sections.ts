import { formatClock } from '@/shared/lib/datetime'
import type { DayOfWeekName, GroupDto, GroupTypeName, GroupWriteRequest } from '@/shared/types'

/**
 * GURUH FORMASINI UCH BO'LIMGA AJRATISH — sof mantiq (Vue'ga bog'liq emas).
 *
 * Talab (loyiha egasi): har bo'lim ALOHIDA tahrirlanadi va o'z "Saqlash"
 * tugmasiga ega. Taqsimot AYNAN topshiriqdagidek, o'zgartirilmaydi:
 *
 *   • `basic`    — nom · tur · ustoz · kurator · kurator guruhi ·
 *                  yozib olish · holat
 *   • `schedule` — boshlanish sanasi · dars kunlari · vaqt · dars
 *                  davomiyligi · kurs davomiyligi
 *   • `course`   — kurs · video darslar boshlanish darsi
 *
 * 🔴 NEGA BU FAYL BOR (eng muhim qism): `PUT /groups/{id}` TO'LIQ
 * ALMASHTIRISH. Bitta bo'lim maydonlarini yuborish qolgan HAMMA maydonni
 * `null` ga tushiradi — bu xato bir marta bo'lgan va guruhning kursi uzilib,
 * butun guruhda gating `NotInCourse` bo'lib qolgan. Shuning uchun payload
 * DOIM uchala bo'limdan yig'iladi (`buildPayload`), bo'limlar esa faqat
 * ma'lumotni qaysi karta EGALLAGANINI belgilaydi.
 *
 * ★ SHU SABABLI bo'lim saqlanganda qolgan ikkitasining qiymati SERVER
 * SNAPSHOT'idan (oxirgi `GET`/javob) olinadi, foydalanuvchining boshqa
 * kartadagi SAQLANMAGAN tahriridan EMAS: aks holda "Asosiy"ni saqlash
 * yo'l-yo'lakay "Jadval"dagi yarim yozilgan qiymatni ham jimgina saqlab,
 * jadvalni qayta generatsiya qilib yuborardi.
 */

export type GroupSectionKey = 'basic' | 'schedule' | 'course'

export interface BasicSectionForm {
  name: string
  type: GroupTypeName
  teacherId: number | null
  assistantId: number | null
  curatorGroupId: number | null
  recordEnabled: boolean
  isActive: boolean
}

export interface ScheduleSectionForm {
  /** `YYYY-MM-DD` — `<input type="date">` formati. */
  startDate: string
  /** `HH:mm` — `<input type="time">` formati (server `HH:mm:ss` kutadi). */
  startTime: string
  weekdays: DayOfWeekName[]
  durationMinutes: number
  courseMonths: number
}

export interface CourseSectionForm {
  courseId: number | null
  videoStartLessonId: number | null
}

export interface GroupSectionForms {
  basic: BasicSectionForm
  schedule: ScheduleSectionForm
  course: CourseSectionForm
}

export const GROUP_SECTION_TITLES: Record<GroupSectionKey, string> = {
  basic: 'Asosiy ma’lumotlar',
  schedule: 'Dars jadvali',
  course: 'Kurs',
}

/** Yangi guruh uchun standart qiymatlar (eski formadagilar bilan bir xil). */
const DEFAULT_START_TIME = '10:00'
const DEFAULT_DURATION_MINUTES = 80
const DEFAULT_COURSE_MONTHS = 8

/**
 * Bugun `YYYY-MM-DD` — MAHALLIY sana.
 *
 * 🔴 `toISOString().slice(0, 10)` ISHLATILMAYDI: u UTC'ga o'giradi va
 * Toshkentda (UTC+5) mahalliy yarim kechadan 05:00 gacha KECHAGI sanani
 * beradi. Guruh boshlanish sanasi bir kun orqaga surilib, jadval boshqa
 * kundan generatsiya qilinardi (`DAVOM_ETTIRISH.md` 14-tuzog'ining aynan
 * o'zi — u kalendar uchun yozilgan, lekin sabab bir xil).
 */
export function todayLocalDate(): string {
  const now = new Date()
  const month = `${now.getMonth() + 1}`.padStart(2, '0')
  const day = `${now.getDate()}`.padStart(2, '0')
  return `${now.getFullYear()}-${month}-${day}`
}

export function basicFrom(group: GroupDto | null): BasicSectionForm {
  return {
    name: group?.name ?? '',
    type: group?.type ?? 'Group',
    teacherId: group?.teacherId ?? null,
    assistantId: group?.assistantId ?? null,
    curatorGroupId: group?.curatorGroupId ?? null,
    recordEnabled: group?.recordEnabled ?? false,
    isActive: group?.isActive ?? true,
  }
}

export function scheduleFrom(group: GroupDto | null): ScheduleSectionForm {
  return {
    startDate: group?.startDate ?? todayLocalDate(),
    startTime: group !== null ? formatClock(group.startTime) : DEFAULT_START_TIME,
    weekdays: [...(group?.weekdays ?? [])],
    durationMinutes: group?.durationMinutes ?? DEFAULT_DURATION_MINUTES,
    courseMonths: group?.courseMonths ?? DEFAULT_COURSE_MONTHS,
  }
}

export function courseFrom(group: GroupDto | null): CourseSectionForm {
  return {
    courseId: group?.courseId ?? null,
    videoStartLessonId: group?.videoStartLessonId ?? null,
  }
}

export function formsFrom(group: GroupDto | null): GroupSectionForms {
  return { basic: basicFrom(group), schedule: scheduleFrom(group), course: courseFrom(group) }
}

/**
 * TO'LIQ `PUT`/`POST` tanasi — uchala bo'limdan yig'iladi.
 *
 * Bitta ham maydon tushib qolmasligi uchun payload FAQAT shu funksiyada
 * quriladi (chaqiruv joyi bo'lim tanlaydi, maydonlarni emas).
 */
export function buildPayload(forms: GroupSectionForms): GroupWriteRequest {
  return {
    name: forms.basic.name.trim(),
    type: forms.basic.type,
    teacherId: forms.basic.teacherId,
    assistantId: forms.basic.assistantId,
    curatorGroupId: forms.basic.curatorGroupId,
    recordEnabled: forms.basic.recordEnabled,
    isActive: forms.basic.isActive,

    startDate: forms.schedule.startDate,
    // Backend `TimeOnly` kutadi, `<input type="time">` esa faqat `HH:mm` beradi.
    startTime: `${forms.schedule.startTime}:00`,
    weekdays: [...forms.schedule.weekdays],
    durationMinutes: forms.schedule.durationMinutes,
    courseMonths: forms.schedule.courseMonths,

    courseId: forms.course.courseId,
    /*
      Kurssiz guruhda dars Id'si yuborilsa server 400 beradi. UI kurs
      tanlanmaganda tanlagichni o'chiradi, lekin bu YAKUNIY to'siq emas
      (kurs "Biriktirilmagan"ga o'tkazilib, saqlanmagan holat qolishi
      mumkin) — shuning uchun qoida payload darajasida ham qo'yiladi.
    */
    videoStartLessonId: forms.course.courseId === null ? null : forms.course.videoStartLessonId,
  }
}

/**
 * Bo'limni saqlash uchun formalar to'plami: qolgan ikkisi SERVER holatidan,
 * shu bo'lim esa foydalanuvchi tahriridan.
 */
export function formsForSectionSave(
  server: GroupDto,
  section: GroupSectionKey,
  edited: GroupSectionForms,
): GroupSectionForms {
  const base = formsFrom(server)
  if (section === 'basic') return { ...base, basic: edited.basic }
  if (section === 'schedule') return { ...base, schedule: edited.schedule }
  return { ...base, course: edited.course }
}

/* ------------------------------------------------------------------ diff */

function sameWeekdays(a: readonly DayOfWeekName[], b: readonly DayOfWeekName[]): boolean {
  if (a.length !== b.length) return false
  const left = [...a].sort()
  const right = [...b].sort()
  return left.every((day, index) => day === right[index])
}

/**
 * O'ZGARGAN MAYDONLARNING O'ZBEKCHA NOMLARI — tasdiq oynasining `details`
 * ro'yxati uchun (reja B2: "o'zgargan maydonlar ro'yxati bilan").
 *
 * QIYMATLAR ATAYLAB KO'RSATILMAYDI, faqat maydon nomi: ustoz/kurator/kurs
 * uchun ismni chiqarish uchun uchta ro'yxatni bu yerga tortish kerak bo'lardi
 * va tasdiq matni ikki qatordan o'n qatorga o'sardi.
 */
export function changedFieldLabels(
  section: GroupSectionKey,
  edited: GroupSectionForms,
  server: GroupDto,
): string[] {
  const base = formsFrom(server)
  const labels: string[] = []

  if (section === 'basic') {
    const next = edited.basic
    const prev = base.basic
    if (next.name.trim() !== prev.name.trim()) labels.push('Guruh nomi')
    if (next.type !== prev.type) labels.push('Guruh turi')
    if (next.teacherId !== prev.teacherId) labels.push('Ustoz')
    if (next.assistantId !== prev.assistantId) labels.push('Kurator')
    if (next.curatorGroupId !== prev.curatorGroupId) labels.push('Kurator guruhi')
    if (next.recordEnabled !== prev.recordEnabled) labels.push('Darslarni yozib olish')
    if (next.isActive !== prev.isActive) labels.push('Guruh statusi')
    return labels
  }

  if (section === 'schedule') {
    const next = edited.schedule
    const prev = base.schedule
    if (next.startDate !== prev.startDate) labels.push('Boshlanish sanasi')
    if (!sameWeekdays(next.weekdays, prev.weekdays)) labels.push('Dars kunlari')
    if (next.startTime !== prev.startTime) labels.push('Boshlanish vaqti')
    if (next.durationMinutes !== prev.durationMinutes) labels.push('Dars davomiyligi')
    if (next.courseMonths !== prev.courseMonths) labels.push('Kurs davomiyligi')
    return labels
  }

  const next = edited.course
  const prev = base.course
  if (next.courseId !== prev.courseId) labels.push('Kurs')
  if (next.videoStartLessonId !== prev.videoStartLessonId) {
    labels.push('Video darslar boshlanish nuqtasi')
  }
  return labels
}

export function sectionIsDirty(
  section: GroupSectionKey,
  edited: GroupSectionForms,
  server: GroupDto,
): boolean {
  return changedFieldLabels(section, edited, server).length > 0
}

/**
 * 🔴 JADVAL QAYTA GENERATSIYA QILINADIMI — backend qoidasining AYNAN nusxasi
 * (`Group.ScheduleRuleDiffersFrom`): sana · vaqt · davomiylik · oy · kunlar
 * VA **guruh turi**.
 *
 * ⚠️ E'TIBOR: `type` "Asosiy ma'lumotlar" bo'limida turadi (loyiha egasining
 * taqsimoti), lekin jadval qoidasining QISMI. Ya'ni "Asosiy"ni saqlash ham
 * jadvalni qayta tuzishi mumkin — shuning uchun ogohlantirish faqat "Dars
 * jadvali" kartasiga bog'lanmaydi, SHU funksiyaga bog'lanadi. Aks holda
 * turni almashtirgan xodim 69 ta darsning jimgina qayta yaratilganini
 * ko'rmasdi.
 *
 * Bu nusxa ehtiyotkorlik tomonga xato qiladi: server "tegilmadi" desa
 * ogohlantirish ortiqcha ko'rsatilgan bo'ladi (zarari yo'q), teskarisi
 * bo'lsa foydalanuvchi kutilmagan o'zgarish ko'rardi.
 */
export function scheduleRuleChanged(edited: GroupSectionForms, server: GroupDto): boolean {
  const base = formsFrom(server)
  return (
    edited.schedule.startDate !== base.schedule.startDate ||
    edited.schedule.startTime !== base.schedule.startTime ||
    edited.schedule.durationMinutes !== base.schedule.durationMinutes ||
    edited.schedule.courseMonths !== base.schedule.courseMonths ||
    edited.basic.type !== base.basic.type ||
    !sameWeekdays(edited.schedule.weekdays, base.schedule.weekdays)
  )
}

/** Saqlashni to'sadigan xatolar (bo'lim bo'yicha). `null` — hammasi joyida. */
export function sectionValidationError(
  section: GroupSectionKey,
  forms: GroupSectionForms,
): string | null {
  if (section === 'basic') {
    return forms.basic.name.trim().length === 0 ? 'Guruh nomini kiriting.' : null
  }
  if (section === 'schedule') {
    if (forms.schedule.startDate.length === 0) return 'Boshlanish sanasini tanlang.'
    if (forms.schedule.weekdays.length === 0) return 'Kamida bitta dars kunini tanlang.'
    return null
  }
  return null
}
