import { http } from '@/shared/api'
import type {
  CreateEnrollmentApplicationRequest,
  EnrollmentApplicationDto,
  EnrollmentApplicationListParams,
  PagedResult,
  UpdateEnrollmentApplicationRequest,
} from '@/shared/types'

/**
 * `POST /api/v1/applications` — landing sahifadagi ariza formasi.
 *
 * ══════════════════════════════════════════════════════════════════════
 * 🔴 YAGONA ANONIM YOZISH ENDPOINTI — VA U HISOB YARATMAYDI.
 *
 * Ariza faqat "biz bilan bog'laning" so'rovi: u `Users` jadvaliga TEGMAYDI
 * va hech qanday kirish huquqi bermaydi. Hisobni hamon FAQAT o'quv bo'limi
 * ochadi (botning "akkaunt yaratmaydi" qoidasi bilan AYNI mulohaza).
 *
 * ★ JAVOB HAR DOIM BIR XIL: server bu raqam bazada bor-yo'qligini
 *   AYTMAYDI. Aks holda forma "bu raqam markazda o'qiydimi?" degan
 *   savolga javob beradigan ochiq qidiruv vositasiga aylanardi.
 * ══════════════════════════════════════════════════════════════════════
 *
 * `429` — kvota (raqam bo'yicha va IP bo'yicha).
 */
export function submitEnrollmentApplication(
  payload: CreateEnrollmentApplicationRequest,
): Promise<void> {
  return http.post<void>('/api/v1/applications', payload, { auth: false })
}

/**
 * `GET /api/v1/applications` — o'quv bo'limi va admin uchun ro'yxat.
 *
 * 🔴 FAQAT `Academic` va `Admin`: arizada telefon raqami bor, ya'ni u
 * R27 (kontakt ma'lumoti) doirasiga kiradi va ustozga ochilmaydi.
 */
export function fetchEnrollmentApplications(
  params: EnrollmentApplicationListParams,
  options?: { signal?: AbortSignal },
): Promise<PagedResult<EnrollmentApplicationDto>> {
  return http.get<PagedResult<EnrollmentApplicationDto>>('/api/v1/applications', {
    query: {
      status: params.status ?? undefined,
      search: params.search ?? undefined,
      page: params.page,
      pageSize: params.pageSize,
    },
    signal: options?.signal,
  })
}

/**
 * `PUT /api/v1/applications/{id}` — holatni va izohni yangilash.
 *
 * ★ ARIZA O'CHIRILMAYDI, HOLATI O'ZGARADI: "nechta ariza keldi, nechtasi
 * o'quvchiga aylandi" — bu markaz uchun asosiy o'lchov, o'chirilgan
 * qator esa uni jimgina buzardi.
 */
export function updateEnrollmentApplication(
  id: number,
  payload: UpdateEnrollmentApplicationRequest,
): Promise<EnrollmentApplicationDto> {
  return http.put<EnrollmentApplicationDto>(`/api/v1/applications/${id}`, payload)
}
