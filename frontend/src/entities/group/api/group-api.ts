import { http } from '@/shared/api'
import type {
  AddMemberRequest,
  CreateGroupResponse,
  CuratorCandidateDto,
  GroupDto,
  GroupMemberDto,
  GroupTypeName,
  GroupWriteRequest,
  MoveMemberRequest,
  MoveMemberResponse,
  PagedResult,
  PauseMemberRequest,
  RemoveMemberRequest,
  ScheduleChangeSummary,
  ScheduledSessionDto,
  UpdateGroupResponse,
} from '@/shared/types'

const BASE = '/api/v1/groups'

/**
 * Qidiruv uchun MINIMAL belgi soni — server shartnomasi
 * (`GroupService.MinSearchLength = 2`). Qisqasi 400 bilan qaytadi, shuning
 * uchun klient yubormaydi.
 */
export const GROUP_SEARCH_MIN = 2

export interface GroupListParams {
  search?: string
  type?: GroupTypeName
  isActive?: boolean
  /**
   * R21b · o'quv yo'nalishi bo'yicha filtr.
   *
   * ★ FILTR SERVERDA: ro'yxat sahifalangan (`PAGE_SIZE = 20`), ya'ni
   * mijozdagi filtr FAQAT joriy sahifani ko'rardi va "topilmadi" deb
   * yolg'on aytardi — aynan shu sabab bilan qidiruv ham serverga
   * ko'chirilgan edi.
   */
  categoryId?: number
  page?: number
  pageSize?: number
}

/**
 * `GET /api/v1/groups`.
 *
 * DIQQAT: ro'yxatni FILTRLASH backendda bo'ladi — ustoz/kurator faqat o'z
 * guruhlarini oladi, o'quv bo'limi esa hammasini. Frontend qo'shimcha
 * filtr QO'YMAYDI (aks holda ruxsat mantig'i ikki joyda takrorlanardi).
 */
export function fetchGroups(
  params: GroupListParams = {},
  options?: { signal?: AbortSignal },
): Promise<PagedResult<GroupDto>> {
  return http.get<PagedResult<GroupDto>>(BASE, {
    // Swagger'dagi query nomlari BOSH HARF bilan (`Search`, `Page`) — mos yozamiz.
    query: {
      Search: params.search,
      Type: params.type,
      IsActive: params.isActive,
      CategoryId: params.categoryId,
      Page: params.page,
      PageSize: params.pageSize,
    },
    signal: options?.signal,
  })
}

export function fetchGroup(id: number, options?: { signal?: AbortSignal }): Promise<GroupDto> {
  return http.get<GroupDto>(`${BASE}/${id}`, { signal: options?.signal })
}

export function fetchGroupMembers(
  id: number,
  options?: { signal?: AbortSignal },
): Promise<GroupMemberDto[]> {
  return http.get<GroupMemberDto[]>(`${BASE}/${id}/members`, { signal: options?.signal })
}

/** `GET /api/v1/groups/{id}/schedule` — guruhning butun dars jadvali. */
export function fetchGroupSchedule(
  id: number,
  options?: { signal?: AbortSignal },
): Promise<ScheduledSessionDto[]> {
  return http.get<ScheduledSessionDto[]>(`${BASE}/${id}/schedule`, { signal: options?.signal })
}

/**
 * `GET /api/v1/groups/{id}/curator-candidates` — shu guruhga bog'lash mumkin
 * bo'lgan kurator guruhlari.
 *
 * Server nomzodlarni O'ZI filtrlaydi (faol, o'zi boshqa kuratorga bog'lanmagan,
 * o'zi emas). Kurator guruhining O'ZI uchun ro'yxat BO'SH keladi — bu xato emas,
 * Domain qoidasi: kurator guruhi boshqa kuratorga bog'lanmaydi.
 */
export function fetchCuratorCandidates(
  id: number,
  options?: { signal?: AbortSignal },
): Promise<CuratorCandidateDto[]> {
  return http.get<CuratorCandidateDto[]>(`${BASE}/${id}/curator-candidates`, {
    signal: options?.signal,
  })
}

export function createGroup(body: GroupWriteRequest): Promise<CreateGroupResponse> {
  return http.post<CreateGroupResponse>(BASE, body)
}

export function updateGroup(id: number, body: GroupWriteRequest): Promise<UpdateGroupResponse> {
  return http.put<UpdateGroupResponse>(`${BASE}/${id}`, body)
}

/* --------------------------------------------------------------- a'zolik */

/**
 * `POST /api/v1/groups/{id}/members` — guruhga o'quvchi qo'shish.
 *
 * 409: o'quvchi allaqachon a'zo, yoki guruh/o'quvchi holati mos emas.
 * Sabab `ProblemDetails.detail` da keladi.
 */
export function addMember(groupId: number, body: AddMemberRequest): Promise<GroupMemberDto> {
  return http.post<GroupMemberDto>(`${BASE}/${groupId}/members`, body)
}

/**
 * Pauza. `pausedUntil` bo'lmasa — MUDDATSIZ pauza (qo'lda tiklanadi).
 * Muddatli pauzada o'quvchi ko'rsatilgan sanagacha darslarga kirmaydi.
 */
export function pauseMember(
  groupId: number,
  studentId: number,
  body: PauseMemberRequest,
): Promise<GroupMemberDto> {
  return http.post<GroupMemberDto>(`${BASE}/${groupId}/members/${studentId}/pause`, body)
}

export function resumeMember(groupId: number, studentId: number): Promise<GroupMemberDto> {
  return http.post<GroupMemberDto>(`${BASE}/${groupId}/members/${studentId}/resume`)
}

/**
 * YUMSHOQ chiqarish: yozuv o'chirilmaydi, holati `Stopped` bo'ladi —
 * davomat va to'lov tarixi a'zolikka ishora qilib turadi (server izohi).
 * Shuning uchun UI'da ham "o'chirish" emas, "chiqarish" deb ataladi.
 */
export function removeMember(
  groupId: number,
  studentId: number,
  body: RemoveMemberRequest,
): Promise<GroupMemberDto> {
  // ★ `POST`, `DELETE` EMAS (2026-08-17): chiqarish endi MAJBURIY sabab
  //   talab qiladi va u so'rov TANASIDA ketadi — `DELETE` bilan tana
  //   yuborish ko'p klient/proksida ishonchsiz (server izohi ham shu).
  return http.post<GroupMemberDto>(`${BASE}/${groupId}/members/${studentId}/remove`, body)
}

/** Boshqa guruhga ko'chirish — serverda ATOMIK (bitta tranzaksiya). */
export function moveMember(
  groupId: number,
  studentId: number,
  body: MoveMemberRequest,
): Promise<MoveMemberResponse> {
  return http.post<MoveMemberResponse>(`${BASE}/${groupId}/members/${studentId}/move`, body)
}

/* ------------------------------------------------------- guruh hayot sikli */

export function archiveGroup(id: number): Promise<GroupDto> {
  return http.post<GroupDto>(`${BASE}/${id}/archive`)
}

export function restoreGroup(id: number): Promise<GroupDto> {
  return http.post<GroupDto>(`${BASE}/${id}/restore`)
}

/**
 * Jadvalni ATAYLAB qayta tuzadi. Faqat KELAJAKDAGI rejalashtirilgan darslar
 * almashtiriladi; o'tgan, jonli, yakunlangan va bekor qilingan darslar
 * saqlanadi (server kafolati). Nechta dars yaratilgani/o'chirilgani javobda
 * keladi va foydalanuvchiga ko'rsatiladi — aks holda o'nlab darsning jimgina
 * almashishi kutilmagan bo'lardi.
 */
export function regenerateSchedule(id: number): Promise<ScheduleChangeSummary> {
  return http.post<ScheduleChangeSummary>(`${BASE}/${id}/schedule/regenerate`)
}
