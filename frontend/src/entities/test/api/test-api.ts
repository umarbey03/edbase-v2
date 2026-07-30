import { http } from '@/shared/api'
import type { DownloadedFile } from '@/shared/api'
import type {
  AuthoringQuestionDto,
  AvailableTestDto,
  CreateTestRequest,
  MyResultDto,
  PagedResult,
  SaveQuestionRequest,
  StartAttemptDto,
  SubmitTestRequest,
  TakeTestDto,
  TestAuthoringDto,
  TestDto,
  TestKindName,
  TestResultRowDto,
  UpdateTestRequest,
} from '@/shared/types'

const BASE = '/api/v1/tests'

/* ==========================================================================
   O'QUVCHI OQIMI
   ========================================================================== */

/**
 * `GET /api/v1/tests/available` — o'quvchining testlari.
 *
 * DIQQAT: backendda `/tests/mine` YO'Q (swagger'da tekshirildi), o'quvchi
 * uchun yagona ro'yxat shu. `/tests` esa faqat Academic/Admin uchun (403).
 *
 * Ro'yxatda TOPSHIRILGAN testlar ham qoladi (`canStart: false`) — o'quvchi
 * natijasini ko'rishi kerak.
 */
export function fetchAvailableTests(options?: {
  signal?: AbortSignal
}): Promise<AvailableTestDto[]> {
  return http.get<AvailableTestDto[]>(`${BASE}/available`, { signal: options?.signal })
}

/**
 * `POST /api/v1/tests/{id}/start` — urinishni boshlaydi.
 *
 * ★ IDEMPOTENT: qayta chaqirilsa AYNI urinish qaytadi va TAYMER NOLDAN
 * BOSHLANMAYDI (`StartedAt` o'zgarmaydi). Shuning uchun sahifa yangilanganda
 * yoki ikkinchi tab ochilganda uni qo'rqmasdan qayta chaqirsa bo'ladi.
 *
 * Xatolar: 409 — test e'lon qilinmagan, muddati o'tgan yoki allaqachon
 * topshirilgan; 403 — dars qulflangan yoki profil faol emas.
 */
export function startTest(testId: number): Promise<StartAttemptDto> {
  return http.post<StartAttemptDto>(`${BASE}/${testId}/start`)
}

/**
 * `GET /api/v1/tests/{id}/take` — ★ yechish varaqasi.
 *
 * Javobda `isCorrect` MAYDONI UMUMAN YO'Q (`TakeOptionDto`), ya'ni to'g'ri
 * javoblarni klientdan "yashirish" kerak emas — ular kelmaydi ham.
 *
 * Urinish boshlanmagan bo'lsa 409 ("Avval testni boshlang") qaytadi, shuning
 * uchun `startTest` dan KEYIN chaqiriladi.
 */
export function fetchTestForTaking(
  testId: number,
  options?: { signal?: AbortSignal },
): Promise<TakeTestDto> {
  return http.get<TakeTestDto>(`${BASE}/${testId}/take`, { signal: options?.signal })
}

/**
 * `POST /api/v1/tests/{id}/submit` — javoblarni topshiradi.
 *
 * Baholash SERVERDA. Ikkinchi topshirish 409 beradi (unikal indeks va `xmin`
 * qulfi) — UI takroriy bosishni `isPending` bilan to'sadi, lekin 409 kelsa
 * ham tushunarli xabar ko'rsatilishi shart.
 *
 * Vaqt tugagan bo'lsa server urinishni 0 ball bilan YOPADI va 409 qaytaradi
 * ("Test uchun ajratilgan vaqt tugagan — urinish yopildi").
 */
export function submitTest(testId: number, body: SubmitTestRequest): Promise<MyResultDto> {
  return http.post<MyResultDto>(`${BASE}/${testId}/submit`, body)
}

/** `GET /api/v1/tests/{id}/my-result`. Urinish bo'lmasa 404. */
export function fetchMyTestResult(
  testId: number,
  options?: { signal?: AbortSignal },
): Promise<MyResultDto> {
  return http.get<MyResultDto>(`${BASE}/${testId}/my-result`, { signal: options?.signal })
}

/* ==========================================================================
   XODIM OQIMI (Academic/Admin — boshqa rol 403 oladi)
   ========================================================================== */

export interface TestListParams {
  kind?: TestKindName
  isPublished?: boolean
  moduleLessonId?: number
  page?: number
  pageSize?: number
}

/**
 * `GET /api/v1/tests`.
 *
 * NOM BO'YICHA QIDIRUV YO'Q — server faqat `Kind`, `IsPublished` va
 * `ModuleLessonId` filtrlarini biladi (`TestListQuery`). Shuning uchun bu
 * ro'yxatda `COURSE_SEARCH_MIN` kabi minimal uzunlik qoidasi kerak emas;
 * u faqat kurs/dars tanlagichida (test formasi) qo'llanadi.
 */
export function fetchTests(
  params: TestListParams = {},
  options?: { signal?: AbortSignal },
): Promise<PagedResult<TestDto>> {
  return http.get<PagedResult<TestDto>>(BASE, {
    // Swagger'dagi query nomlari BOSH HARF bilan — `courses`/`groups` bilan bir xil.
    query: {
      Kind: params.kind,
      IsPublished: params.isPublished,
      ModuleLessonId: params.moduleLessonId,
      Page: params.page,
      PageSize: params.pageSize,
    },
    signal: options?.signal,
  })
}

/** `GET /api/v1/tests/{id}` — tahrirlash ko'rinishi, TO'G'RI JAVOBLAR bilan. */
export function fetchTestForAuthoring(
  id: number,
  options?: { signal?: AbortSignal },
): Promise<TestAuthoringDto> {
  return http.get<TestAuthoringDto>(`${BASE}/${id}`, { signal: options?.signal })
}

/**
 * `POST /api/v1/tests`.
 *
 * 409: Domain qoidasi (`Test.Validate`) — sarlavha bo'sh, vaqt chegarasi
 * musbat emas, dars testida dars ko'rsatilmagan yoki musobaqa testi darsga
 * bog'langan. 404: ko'rsatilgan dars topilmadi.
 */
export function createTest(body: CreateTestRequest): Promise<TestDto> {
  return http.post<TestDto>(BASE, body)
}

/**
 * `PUT /api/v1/tests/{id}` — ★ TO'LIQ ALMASHTIRISH.
 *
 * Yuborilmagan maydon serverda `null` bo'lib yoziladi (`UpdateTestRequest`
 * izohiga qarang), shuning uchun chaqiruvchi mavjud qiymatlarni yuklab,
 * HAMMASINI qaytarib yuborishi shart. Tur va dars bu tanada yo'q — server
 * ularni ataylab o'zgartirmaydi.
 */
export function updateTest(id: number, body: UpdateTestRequest): Promise<TestDto> {
  return http.put<TestDto>(`${BASE}/${id}`, body)
}

/** `DELETE /api/v1/tests/{id}`. Urinish boshlangan bo'lsa 409 (natijalar yo'qolmasin). */
export function deleteTest(id: number): Promise<void> {
  return http.delete<void>(`${BASE}/${id}`)
}

/**
 * `POST /api/v1/tests/{id}/questions` — savol + variantlar.
 * Domain: kamida 2 variant, kamida 1 to'g'ri, ball noldan katta (aks holda 409).
 */
export function addTestQuestion(
  testId: number,
  body: SaveQuestionRequest,
): Promise<AuthoringQuestionDto> {
  return http.post<AuthoringQuestionDto>(`${BASE}/${testId}/questions`, body)
}

/**
 * `PUT /api/v1/tests/{id}/questions/{questionId}` — ★ VARIANTLAR BUTUNLAY
 * ALMASHTIRILADI: server eskilarini o'chirib, yuborilgan ro'yxatni yozadi.
 * Ya'ni forma mavjud variantlarni yuklab, hammasini qaytarishi shart.
 */
export function updateTestQuestion(
  testId: number,
  questionId: number,
  body: SaveQuestionRequest,
): Promise<AuthoringQuestionDto> {
  return http.put<AuthoringQuestionDto>(`${BASE}/${testId}/questions/${questionId}`, body)
}

export function deleteTestQuestion(testId: number, questionId: number): Promise<void> {
  return http.delete<void>(`${BASE}/${testId}/questions/${questionId}`)
}

/**
 * `POST /api/v1/tests/{id}/publish`.
 *
 * 409: bo'sh test yoki nuqsonli savol (`Test.Publish()` har bir savolni
 * qayta tekshiradi) — sabab `detail` da to'liq keladi.
 */
export function publishTest(id: number): Promise<TestDto> {
  return http.post<TestDto>(`${BASE}/${id}/publish`)
}

/** `POST /api/v1/tests/{id}/unpublish`. Server qo'shimcha shart qo'ymaydi. */
export function unpublishTest(id: number): Promise<TestDto> {
  return http.post<TestDto>(`${BASE}/${id}/unpublish`)
}

/** `GET /api/v1/tests/{id}/results` — bitta urinish = bitta qator. */
export function fetchTestResults(
  id: number,
  options?: { signal?: AbortSignal },
): Promise<TestResultRowDto[]> {
  return http.get<TestResultRowDto[]>(`${BASE}/${id}/results`, { signal: options?.signal })
}

/**
 * `GET /api/v1/tests/{id}/results/export` — CSV (Excel uchun BOM bilan).
 *
 * Javob JSON emas, shuning uchun `http.download` ishlatiladi: u `Authorization`
 * sarlavhasini qo'yadi (oddiy havola qo'ymasdi) va fayl nomini
 * `Content-Disposition` dan oladi.
 */
export function downloadTestResultsCsv(id: number): Promise<DownloadedFile> {
  return http.download(`${BASE}/${id}/results/export`, `test-${id}-natijalar.csv`)
}
