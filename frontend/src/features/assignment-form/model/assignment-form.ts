import { parseAnswerFormats, serializeAnswerFormats } from '@/entities/assignment'
import type { AnswerFormatName } from '@/entities/assignment'
import { fromDateTimeLocalInput, toDateTimeLocalInput } from '@/shared/lib/datetime'
import type { AssignmentDto, CreateAssignmentRequest, UpdateAssignmentRequest } from '@/shared/types'

/**
 * ========================================================================
 * UY VAZIFASI FORMASINING YAGONA MANTIG'I
 * ========================================================================
 *
 * ★ NIMA UCHUN ALOHIDA MODUL: forma endi IKKI joyda ochiladi —
 *   1) `AssignmentFormDialog` (vazifalar sahifasi, ustozning baholash
 *      sahifasi) — nishon tanlash bilan;
 *   2) `LessonAssignmentSection` (dars drawer'ining 4-bo'limi) — nishon
 *      ALLAQACHON ma'lum (shu dars).
 *
 * Ikki nusxa saqlansa, validatsiya qoidasi (masalan "kamida bitta javob
 * formati") bir joyda tuzatilib, ikkinchisida eski holida qolardi. Shuning
 * uchun HOLAT, TEKSHIRUV va SO'ROV TANASI shu yerda — komponentlar faqat
 * ko'rinish.
 *
 * ── 🔴 `PUT` = TO'LIQ ALMASHTIRISH ────────────────────────────────────
 *
 * `UpdateAssignmentRequest` ning ixtiyoriy maydonlari serverda `= null`
 * standart qiymatga ega va servis ularni TO'G'RIDAN-TO'G'RI yozadi. Ya'ni
 * yuborilmagan maydon JIMGINA o'chadi (`DAVOM_ETTIRISH.md` 6-bo'lim,
 * 1-tuzoq). Shu sababli `buildUpdateRequest` HAMMA maydonni qaytaradi,
 * jumladan UI'da tahrirlanmaydigan `imageKey` ni ham.
 */

/** Server: `Assignment.MaxTitleLength`. */
export const MAX_ASSIGNMENT_TITLE = 200
/** Server: `Assignment.MaxDescriptionLength`. */
export const MAX_ASSIGNMENT_DESCRIPTION = 4000

/** Standart javob formatlari — eng ko'p ishlatiladigan juftlik. */
const DEFAULT_FORMATS: readonly AnswerFormatName[] = ['Text', 'Image']

export interface AssignmentFormState {
  title: string
  description: string
  /** Satr sifatida: "4,5" ham qabul qilinadi (o'zbek klaviaturasi). */
  maxScoreText: string
  /** `<input type="datetime-local">` qiymati (mahalliy vaqt). */
  dueLocal: string
  formats: AnswerFormatName[]
  /**
   * ⚠️ ESKIRGAN maydon. UI'da TAHRIRLANMAYDI (o'rniga `attachments`), lekin
   * `PUT` da QAYTARILISHI SHART — aks holda eski vazifalarning shart rasmi
   * birinchi tahrirlashdayoq yo'qolardi.
   */
  imageKey: string | null
}

export function createAssignmentFormState(assignment: AssignmentDto | null): AssignmentFormState {
  return {
    title: assignment?.title ?? '',
    description: assignment?.description ?? '',
    maxScoreText: assignment !== null ? String(assignment.maxScore) : '5',
    dueLocal: toDateTimeLocalInput(assignment?.dueAt ?? null),
    formats:
      assignment !== null ? parseAnswerFormats(assignment.allowedFormats) : [...DEFAULT_FORMATS],
    imageKey: assignment?.imageKey ?? null,
  }
}

export interface AssignmentFormErrors {
  title: string | null
  description: string | null
  maxScore: string | null
  formats: string | null
}

/** Vergul bilan yozilgan ball ("4,5") ham to'g'ri hisoblanadi. */
export function parseMaxScore(raw: string): number {
  return Number(raw.replace(',', '.'))
}

export function validateAssignmentForm(state: AssignmentFormState): AssignmentFormErrors {
  const title = state.title.trim()
  const score = parseMaxScore(state.maxScoreText)

  return {
    title:
      title.length === 0
        ? 'Sarlavha kiritilishi kerak.'
        : title.length > MAX_ASSIGNMENT_TITLE
          ? `Sarlavha ${MAX_ASSIGNMENT_TITLE} belgidan oshmasin.`
          : null,
    description:
      state.description.trim().length > MAX_ASSIGNMENT_DESCRIPTION
        ? `Tavsif ${MAX_ASSIGNMENT_DESCRIPTION} belgidan oshmasin.`
        : null,
    maxScore:
      state.maxScoreText.trim().length === 0
        ? 'Maksimal ball kiritilishi kerak.'
        : !Number.isFinite(score)
          ? 'Ball raqam bo‘lishi kerak.'
          : score <= 0
            ? 'Ball noldan katta bo‘lishi kerak.'
            : null,
    /*
      🔴 KAMIDA BITTA FORMAT. Serverda bu qoida `AnswerFormats.None` ni rad
      etadi va endi ANIQ 400 + `problem.errors.allowedFormats` beradi
      (ilgari `DomainException` orqali 409 bo'lib chiqardi — 43-tuzoq).
      Klientda ham to'siladi: "yuborib ko'r, keyin ko'rasan" oqimi yaxshi
      forma emas.
    */
    formats: state.formats.length === 0 ? 'Kamida bitta javob formati tanlanishi kerak.' : null,
  }
}

export function isAssignmentFormValid(errors: AssignmentFormErrors): boolean {
  return (
    errors.title === null
    && errors.description === null
    && errors.maxScore === null
    && errors.formats === null
  )
}

/** ★ HAMMA maydon qaytariladi (yuqoridagi `PUT` izohi). */
export function buildUpdateRequest(state: AssignmentFormState): UpdateAssignmentRequest {
  const description = state.description.trim()
  return {
    title: state.title.trim(),
    // Bo'sh matn `null` sifatida ketadi — bazada bo'sh satr saqlanmasin.
    description: description.length > 0 ? description : null,
    maxScore: parseMaxScore(state.maxScoreText),
    dueAt: fromDateTimeLocalInput(state.dueLocal),
    allowedFormats: serializeAnswerFormats(state.formats),
    imageKey: state.imageKey,
  }
}

/** Nishon: "YOKI guruh, YOKI kurs darsi" — ikkinchisi DOIM `null`. */
export interface AssignmentTargetIds {
  groupId: number | null
  moduleLessonId: number | null
}

export function buildCreateRequest(
  state: AssignmentFormState,
  target: AssignmentTargetIds,
): CreateAssignmentRequest {
  return {
    ...buildUpdateRequest(state),
    groupId: target.groupId,
    moduleLessonId: target.moduleLessonId,
  }
}

/**
 * O'ZGARGAN MAYDONLAR ro'yxati — tasdiq oynasining `details` i uchun.
 *
 * Talab (B2 jadvali): *"ma'lumotni almashtiruvchi saqlash → HAR DOIM,
 * `primary` — o'zgargan maydonlar ro'yxati bilan"*. Ro'yxat bo'sh bo'lsa
 * chaqiruvchi tasdiq so'ramaydi ham: hech narsa o'zgarmagan bo'lsa oyna
 * ko'rsatish foydalanuvchini "nima o'zgardi?" degan savol bilan qoldirardi.
 */
export function changedAssignmentFields(
  assignment: AssignmentDto | null,
  state: AssignmentFormState,
): string[] {
  if (assignment === null) return []
  const next = buildUpdateRequest(state)
  const changes: string[] = []

  if ((assignment.title ?? '') !== next.title) changes.push('Sarlavha')
  if ((assignment.description ?? '') !== (next.description ?? '')) changes.push('Shart matni')
  if (assignment.maxScore !== next.maxScore) changes.push('Maksimal ball')
  if ((assignment.dueAt ?? '') !== (next.dueAt ?? '')) changes.push('Topshirish muddati')
  /*
    Formatlar TARTIBGA bog'liq bo'lmagan holda solishtiriladi:
    `serializeAnswerFormats` doim bir xil tartibda yozadi (`ANSWER_FORMAT_OPTIONS`),
    lekin serverdan kelgan satr boshqa tartibda bo'lishi mumkin — o'shanda
    "o'zgardi" deb yolg'on ko'rsatilardi.
  */
  const before = serializeAnswerFormats(parseAnswerFormats(assignment.allowedFormats))
  if (before !== next.allowedFormats) changes.push('Javob formatlari')

  return changes
}
