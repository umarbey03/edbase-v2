import { http } from '@/shared/api'
import type {
  AttritionByGroupDto,
  AttritionByTeacherDto,
  AttritionListParams,
  AttritionReasonDto,
  AttritionReasonsDto,
  AttritionReturnedDto,
  AttritionRowDto,
  AttritionStudentSummaryDto,
  AttritionSummaryDto,
  GroupAttritionDetailDto,
  PagedResult,
  SaveAttritionReasonRequest,
} from '@/shared/types'

const BASE = '/api/v1/attrition'

/**
 * TO'KILISHLAR HISOBOTI (2026-08-17) — manba `GroupMembershipEvent`
 * jurnali (o'chmaydigan). FAQAT O'QIYDI: hodisalarni guruh a'zoligini
 * o'zgartiruvchi amallar yozadi.
 */

/** Filtrni so'rov parametrlariga aylantiradi (maydonlar OSHKOR — `fetchUsers` naqshi). */
function toQuery(params: AttritionListParams): Record<string, string | number | boolean | undefined> {
  return {
    search: params.search,
    kind: params.kind,
    groupId: params.groupId,
    teacherId: params.teacherId,
    from: params.from,
    to: params.to,
    trial: params.trial,
    sort: params.sort,
    desc: params.desc,
    page: params.page,
    pageSize: params.pageSize,
  }
}

export function fetchAttrition(
  params: AttritionListParams = {},
  options?: { signal?: AbortSignal },
): Promise<PagedResult<AttritionRowDto>> {
  return http.get<PagedResult<AttritionRowDto>>(BASE, {
    query: toQuery(params),
    signal: options?.signal,
  })
}

/**
 * Yig'ma — AYNI filtr, lekin sahifalashsiz (butun to'plamni sanaydi).
 * `page`/`pageSize`/`sort` ataylab tashlanadi.
 */
export function fetchAttritionSummary(
  params: AttritionListParams = {},
  options?: { signal?: AbortSignal },
): Promise<AttritionSummaryDto> {
  const { page: _page, pageSize: _pageSize, sort: _sort, desc: _desc, ...rest } = params

  return http.get<AttritionSummaryDto>(`${BASE}/summary`, {
    query: toQuery(rest),
    signal: options?.signal,
  })
}

export function fetchAttritionByTeacher(
  params: AttritionListParams = {},
  options?: { signal?: AbortSignal },
): Promise<AttritionByTeacherDto[]> {
  const { page: _page, pageSize: _pageSize, sort: _sort, desc: _desc, ...rest } = params

  return http.get<AttritionByTeacherDto[]>(`${BASE}/by-teacher`, {
    query: toQuery(rest),
    signal: options?.signal,
  })
}

export function fetchAttritionByGroup(
  params: AttritionListParams = {},
  options?: { signal?: AbortSignal },
): Promise<AttritionByGroupDto[]> {
  const { page: _page, pageSize: _pageSize, sort: _sort, desc: _desc, ...rest } = params

  return http.get<AttritionByGroupDto[]>(`${BASE}/by-group`, {
    query: toQuery(rest),
    signal: options?.signal,
  })
}

export function fetchAttritionGroupDetail(
  groupId: number,
  params: AttritionListParams = {},
  options?: { signal?: AbortSignal },
): Promise<GroupAttritionDetailDto> {
  const { page: _page, pageSize: _pageSize, sort: _sort, desc: _desc, ...rest } = params

  return http.get<GroupAttritionDetailDto>(`${BASE}/group/${groupId}`, {
    query: toQuery(rest),
    signal: options?.signal,
  })
}

/* ══════════════════════════════════════════════════════════ o'quvchi kesimi

   O'quv bo'limi so'rovi (2026-08-18): "nechta o'quvchini yo'qotdik va
   nechtasini qaytara oldik". Yuqoridagilar HODISALARNI sanaydi — bular
   O'QUVCHILARNI.                                                        */

export function fetchAttritionStudents(
  params: AttritionListParams = {},
  options?: { signal?: AbortSignal },
): Promise<AttritionStudentSummaryDto> {
  const { page: _page, pageSize: _pageSize, sort: _sort, desc: _desc, ...rest } = params

  return http.get<AttritionStudentSummaryDto>(`${BASE}/students`, {
    query: toQuery(rest),
    signal: options?.signal,
  })
}

/** To'kilib, keyin qayta faol bo'lganlar. */
export function fetchAttritionReturned(
  params: AttritionListParams = {},
  options?: { signal?: AbortSignal },
): Promise<AttritionReturnedDto[]> {
  const { page: _page, pageSize: _pageSize, sort: _sort, desc: _desc, ...rest } = params

  return http.get<AttritionReturnedDto[]>(`${BASE}/returned`, {
    query: toQuery(rest),
    signal: options?.signal,
  })
}

/** Sabablar foizda. */
export function fetchAttritionReasons(
  params: AttritionListParams = {},
  options?: { signal?: AbortSignal },
): Promise<AttritionReasonsDto> {
  const { page: _page, pageSize: _pageSize, sort: _sort, desc: _desc, ...rest } = params

  return http.get<AttritionReasonsDto>(`${BASE}/reasons`, {
    query: toQuery(rest),
    signal: options?.signal,
  })
}

/* ══════════════════════════════════════════════ sabablar katalogi (sozlamalar) */

const REASONS = '/api/v1/attrition-reasons'

/** @param activeOnly Chiqarish/muzlatish oynasi uchun `true`. */
export function fetchAttritionReasonCatalogue(
  activeOnly = false,
  options?: { signal?: AbortSignal },
): Promise<AttritionReasonDto[]> {
  return http.get<AttritionReasonDto[]>(REASONS, {
    query: { activeOnly },
    signal: options?.signal,
  })
}

export function createAttritionReason(
  body: SaveAttritionReasonRequest,
): Promise<AttritionReasonDto> {
  return http.post<AttritionReasonDto>(REASONS, body)
}

export function updateAttritionReason(
  id: number,
  body: SaveAttritionReasonRequest,
): Promise<AttritionReasonDto> {
  return http.put<AttritionReasonDto>(`${REASONS}/${id}`, body)
}

/** Ishlatilgan sabab o'chirilmaydi — ARXIVLANADI (server hal qiladi). */
export function deleteAttritionReason(id: number): Promise<void> {
  return http.delete<void>(`${REASONS}/${id}`)
}
