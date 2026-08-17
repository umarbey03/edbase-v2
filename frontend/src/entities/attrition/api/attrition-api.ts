import { http } from '@/shared/api'
import type {
  AttritionByGroupDto,
  AttritionByTeacherDto,
  AttritionListParams,
  AttritionRowDto,
  AttritionSummaryDto,
  GroupAttritionDetailDto,
  PagedResult,
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
