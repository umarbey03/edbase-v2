import { http } from '@/shared/api'
import type {
  CreateUserRequest,
  CreateUserResponse,
  PagedResult,
  UpdateUserRequest,
  UserDetailsDto,
  UserRoleName,
} from '@/shared/types'

const BASE = '/api/v1/users'

/**
 * Qidiruv uchun MINIMAL belgi soni — server shartnomasi
 * (`UserService.MinSearchLength = 3`; telefon raqamda ham shu chegara).
 * Guruh va kursda bu chegara 2, foydalanuvchilarda 3 — ataylab boshqacha,
 * shuning uchun umumiy doimiy YO'Q: har entity o'z shartnomasini biladi.
 */
export const USER_SEARCH_MIN = 3

export interface UserListParams {
  search?: string
  role?: UserRoleName
  isActive?: boolean
  page?: number
  pageSize?: number
}

/** `GET /api/v1/users` — faqat Academic/Admin (boshqa rollarda 403). */
export function fetchUsers(
  params: UserListParams = {},
  options?: { signal?: AbortSignal },
): Promise<PagedResult<UserDetailsDto>> {
  return http.get<PagedResult<UserDetailsDto>>(BASE, {
    query: {
      Search: params.search,
      Role: params.role,
      IsActive: params.isActive,
      Page: params.page,
      PageSize: params.pageSize,
    },
    signal: options?.signal,
  })
}

/**
 * `POST /api/v1/users`.
 * Parol berilmasa server vaqtinchalik parol qaytaradi — uni foydalanuvchiga
 * BIR MARTA ko'rsatish kerak (qayta olib bo'lmaydi).
 */
export function createUser(body: CreateUserRequest): Promise<CreateUserResponse> {
  return http.post<CreateUserResponse>(BASE, body)
}

export function updateUser(id: number, body: UpdateUserRequest): Promise<UserDetailsDto> {
  return http.put<UserDetailsDto>(`${BASE}/${id}`, body)
}

export function activateUser(id: number): Promise<void> {
  return http.post<void>(`${BASE}/${id}/activate`)
}

export function deactivateUser(id: number): Promise<void> {
  return http.post<void>(`${BASE}/${id}/deactivate`)
}
