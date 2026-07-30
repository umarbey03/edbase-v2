import { http } from '@/shared/api'
import type {
  CourseDto,
  CourseLessonDto,
  CourseModuleDto,
  CourseTreeDto,
  CourseWriteRequest,
  LessonWriteRequest,
  ModuleWriteRequest,
  PagedResult,
  PositionDto,
} from '@/shared/types'

const BASE = '/api/v1/courses'

/**
 * Qidiruv uchun MINIMAL belgi soni — server shartnomasi
 * (`CourseService.MinSearchLength = 2`). Qisqa satr yuborilsa server 400 beradi,
 * ya'ni jadval o'rniga xato ekrani chiqadi. Shuning uchun klient qisqa satrni
 * UMUMAN yubormaydi va foydalanuvchiga nima kutilayotganini aytadi.
 */
export const COURSE_SEARCH_MIN = 2

export interface CourseListParams {
  search?: string
  isActive?: boolean
  page?: number
  pageSize?: number
}

/**
 * `GET /api/v1/courses`.
 *
 * RO'YXAT DARAXTSIZ: modul va darslar faqat bitta kurs so'ralganda
 * (`fetchCourseTree`) keladi. Ro'yxatda ular ham bo'lsa javob 50 ta kursda
 * megabaytlarga o'sardi — backend ataylab shunday ajratgan.
 */
export function fetchCourses(
  params: CourseListParams = {},
  options?: { signal?: AbortSignal },
): Promise<PagedResult<CourseDto>> {
  return http.get<PagedResult<CourseDto>>(BASE, {
    // Swagger'dagi query nomlari BOSH HARF bilan — `groups` bilan bir xil.
    query: {
      Search: params.search,
      IsActive: params.isActive,
      Page: params.page,
      PageSize: params.pageSize,
    },
    signal: options?.signal,
  })
}

/** `GET /api/v1/courses/{id}` — modul va darslar bilan to'liq daraxt. */
export function fetchCourseTree(
  id: number,
  options?: { signal?: AbortSignal },
): Promise<CourseTreeDto> {
  return http.get<CourseTreeDto>(`${BASE}/${id}`, { signal: options?.signal })
}

export function createCourse(body: CourseWriteRequest): Promise<CourseDto> {
  return http.post<CourseDto>(BASE, body)
}

export function updateCourse(id: number, body: CourseWriteRequest): Promise<CourseDto> {
  return http.put<CourseDto>(`${BASE}/${id}`, body)
}

/**
 * `DELETE /api/v1/courses/{id}`.
 *
 * 409 QAYTADI agar kursga guruh biriktirilgan bo'lsa yoki darslariga
 * o'quvchi javobi/test urinishi bog'langan bo'lsa. Sabab `ProblemDetails.detail`
 * da to'liq yozilgan — foydalanuvchiga AYNAN o'sha matn ko'rsatiladi
 * (o'z so'zimiz bilan qayta yozsak, sabab yo'qoladi).
 */
export function deleteCourse(id: number): Promise<void> {
  return http.delete<void>(`${BASE}/${id}`)
}

/** Kurslar tartibi. ★ BARCHA kurs Id'lari yuborilishi shart. */
export function reorderCourses(orderedIds: number[]): Promise<PositionDto[]> {
  return http.post<PositionDto[]>(`${BASE}/reorder`, { orderedIds })
}

/* ------------------------------------------------------------------ modul */

export function createModule(courseId: number, body: ModuleWriteRequest): Promise<CourseModuleDto> {
  return http.post<CourseModuleDto>(`${BASE}/${courseId}/modules`, body)
}

export function updateModule(
  courseId: number,
  moduleId: number,
  body: ModuleWriteRequest,
): Promise<CourseModuleDto> {
  return http.put<CourseModuleDto>(`${BASE}/${courseId}/modules/${moduleId}`, body)
}

/** Modulni ichidagi darslar bilan o'chiradi. O'quvchi ishi bog'langan bo'lsa — 409. */
export function deleteModule(courseId: number, moduleId: number): Promise<void> {
  return http.delete<void>(`${BASE}/${courseId}/modules/${moduleId}`)
}

/** Kurs ichidagi modullar tartibi. ★ To'liq ro'yxat. */
export function reorderModules(courseId: number, orderedIds: number[]): Promise<PositionDto[]> {
  return http.post<PositionDto[]>(`${BASE}/${courseId}/modules/reorder`, { orderedIds })
}

/* ------------------------------------------------------------------- dars */

export function createLesson(
  courseId: number,
  moduleId: number,
  body: LessonWriteRequest,
): Promise<CourseLessonDto> {
  return http.post<CourseLessonDto>(`${BASE}/${courseId}/modules/${moduleId}/lessons`, body)
}

export function updateLesson(
  courseId: number,
  moduleId: number,
  lessonId: number,
  body: LessonWriteRequest,
): Promise<CourseLessonDto> {
  return http.put<CourseLessonDto>(
    `${BASE}/${courseId}/modules/${moduleId}/lessons/${lessonId}`,
    body,
  )
}

export function deleteLesson(
  courseId: number,
  moduleId: number,
  lessonId: number,
): Promise<void> {
  return http.delete<void>(`${BASE}/${courseId}/modules/${moduleId}/lessons/${lessonId}`)
}

/**
 * Modul ichidagi darslar tartibi. ★ To'liq ro'yxat.
 *
 * DIQQAT: bu amal GATING ketma-ketligini o'zgartiradi — darslar tartibi
 * "oldingi dars tugatilganmi" qoidasining asosi. Shuning uchun UI'da
 * ogohlantirish ko'rsatiladi.
 */
export function reorderLessons(
  courseId: number,
  moduleId: number,
  orderedIds: number[],
): Promise<PositionDto[]> {
  return http.post<PositionDto[]>(
    `${BASE}/${courseId}/modules/${moduleId}/lessons/reorder`,
    { orderedIds },
  )
}
