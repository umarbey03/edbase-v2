/**
 * SPEC 5-bo'limidagi DTO'larning AYNAN nusxasi.
 * Maydon nomlari o'zgartirilmaydi (backend camelCase JSON qaytaradi).
 * C# `long` -> TS `number` (2^53 gacha xavfsiz).
 */

/** SPEC 2: `UserRole` enum nomlari (JSON'da satr sifatida keladi). */
export type UserRoleName = 'Student' | 'Teacher' | 'Assistant' | 'Academic' | 'Admin'

/** SPEC 2: `SessionType` */
export type SessionTypeName = 'Teacher' | 'Assistant'

/** SPEC 2: `SessionStatus` */
export type SessionStatusName = 'Scheduled' | 'Live' | 'Ended' | 'Cancelled'

/** RFC 7807 — global middleware qaytaradigan xato formati (SPEC 5). */
export interface ProblemDetails {
  type?: string
  title?: string
  status?: number
  detail?: string
  traceId?: string
  /** ASP.NET validatsiya xatolari: { "Email": ["..."] } */
  errors?: Record<string, string[]>
}

/**
 * `POST /api/v1/auth/phone/request-code` tanasi.
 *
 * ══════════════════════════════════════════════════════════════════════
 * ⚠️ `LoginRequest` (email + parol) OLIB TASHLANDI — 2026-08-13, loyiha
 *    egasining qarori. `POST /api/v1/auth/login` endpointi ham yo'q.
 *
 * Endi kirish IKKI BOSQICHLI: telefon raqami yuboriladi, kod esa o'sha
 * raqamga bog'langan TELEGRAM hisobiga keladi.
 * ══════════════════════════════════════════════════════════════════════
 */
export interface PhoneCodeRequest {
  /** Xom ko'rinish ham bo'ladi — normalizatsiya SERVERDA. */
  phone: string
}

/**
 * `request-code` javobi.
 *
 * 🔴 JAVOB HAR DOIM BIR XIL — raqam bazada bor yoki yo'qligidan qat'i
 * nazar. Interfeys HECH QACHON "bunday raqam topilmadi" deb ko'rsatmasin:
 * server bu ma'lumotni ataylab bermaydi (hisob sanashga qarshi), va uni
 * mijozda "o'ylab topish" himoyani bekor qilardi.
 */
export interface PhoneCodeResponse {
  /** Kod necha sekund yaroqli (taymer uchun). */
  expiresInSeconds: number
  /** Qayta yuborish tugmasi qachondan faollashadi. */
  resendAfterSeconds: number
}

/** `POST /api/v1/auth/phone/verify` tanasi. */
export interface PhoneVerifyRequest {
  phone: string
  code: string
}

/**
 * `POST /api/v1/auth/refresh` tanasi.
 * SPEC'da alohida DTO ko'rsatilmagan (5-bo'limda faqat javob turi bor) —
 * amalda yagona mantiqiy shakl shu.
 */
export interface RefreshRequest {
  refreshToken: string
}

/**
 * `GET /api/v1/auth/me` va `AuthResponse.user` — KIRGAN foydalanuvchining
 * O'ZI.
 *
 * 🔴 BU YAGONA "O'Z-O'ZIGA CHEKLANGAN" (self-scoped) shakl: serverda uni
 * to'ldiradigan yo'lda "kimning profili" degan parametr umuman yo'q, javob
 * tokendagi `sub` dan chiqadi. Shu sababli `phone` MAYDONI AYNAN SHU
 * TURDAN olinadi (suv belgisi — R8).
 */
export interface UserDto {
  id: number
  fullName: string
  email: string
  /**
   * 🔴 SUV BELGISI UCHUN YAGONA RUXSAT ETILGAN MANBA (R8).
   *
   * Guruh doirasidagi shakllardan (`GroupMemberDto`, davomat qatori,
   * qatnashuvchi DTO'si) OLINMASIN: ular ustozga ham ochiq va R27 aynan
   * o'sha yo'lni yopadi — suv belgisini o'shalardan yig'ish yopilgan
   * teshikni qayta ochardi.
   *
   * `null` — raqam kiritilmagan (bunday foydalanuvchilar BOR: ular
   * Telegram'ni ham ulay olmaydi).
   */
  phone: string | null
  role: UserRoleName
  /**
   * Profil rasmi oxirgi marta qachon almashtirilgani. `null` — rasm YO'Q,
   * interfeys ism harfini chizadi.
   *
   * ★ RASM MANZILI EMAS, VAQT TAMG'ASI: manzil har doim bir xil
   * (`/api/v1/profile/avatar/{id}`), shuning uchun u DTO'da takrorlanmaydi.
   * Klient manzilni `id` dan yasaydi va bu qiymatni `?v=` sifatida
   * qo'shadi — shusiz brauzer rasm almashtirilgandan keyin ham eskisini
   * ko'rsatib turardi.
   */
  avatarUpdatedAt: string | null
}

export interface AuthResponse {
  accessToken: string
  refreshToken: string
  user: UserDto
}

/**
 * ══════════════════════════════════════════════════════════════════════
 * ⚠️ FAQAT SINOV UCHUN — `GET /api/v1/auth/dev/quick-login` bitta qatori
 * ══════════════════════════════════════════════════════════════════════
 *
 * Bu shakl HAQIQIY kirish oqimiga UMUMAN aloqador emas. U faqat kirish
 * sahifasidagi "sinov paneli" tugmalarini chizish uchun.
 *
 * 🔴 RO'YXAT SERVERDAN KELADI, MIJOZDA YOZILMAYDI. Rollar, ismlar va
 * raqamlar frontendga QATTIQ YOZILSA, backend darvozasi yopilganda ham
 * tugmalar chizilib turardi — ya'ni interfeys mavjud bo'lmagan
 * xususiyatni va'da qilardi. Endi qoida oddiy: ro'yxat bo'sh yoki 404
 * bo'lsa — panel UMUMAN chizilmaydi.
 */
export interface DevQuickLoginAccount {
  /** Rolning MASHINA nomi — POST tanasiga aynan shu ketadi. */
  role: UserRoleName
  /** Tugmadagi o'zbekcha nom. */
  roleLabel: string
  fullName: string
  phone: string | null
}

/** ⚠️ FAQAT SINOV UCHUN. `GET /api/v1/auth/dev/quick-login` javobi. */
export interface DevQuickLoginList {
  /** Serverning O'ZI yozgan ogohlantirish — panelda AYNAN shu ko'rsatiladi. */
  warning: string
  /** Muhit nomi (`Development`, `Staging`…). */
  environment: string
  accounts: DevQuickLoginAccount[]
}

export interface LiveSessionDto {
  id: number
  groupId: number
  groupName: string
  title: string | null
  type: SessionTypeName
  status: SessionStatusName
  /** ISO-8601 (DateTimeOffset) */
  scheduledStart: string
  scheduledEnd: string
  actualStart: string | null
  endsAt: string | null
  isHost: boolean
  /** Shu darsni olib borishi kutilayotgan xodim (guruhning ustozi/kuratori). `null` — biriktirilmagan. */
  hostName: string | null
}

/**
 * `GET /api/v1/live-sessions/stats` qatori — "Darslarim" jadvali (R31).
 *
 * ★ `LiveSessionDto` DAN ALOHIDA TUR, chunki server ham ikkita alohida
 * shartnoma beradi: eski `GET /live-sessions` kengaytirilmadi (uni bosh
 * sahifa va `SessionBoard` allaqachon ishlatadi). Sabab backendda —
 * `LiveSessionDtos.cs`.
 *
 * ⚠️ `actualEnd` SHU YERDA BOR, `LiveSessionDto` da esa YO'Q — davomiylikni
 * mijozda hisoblab bo'lmasligining sababi aynan shu edi.
 */
export interface SessionStatsDto {
  id: number
  groupId: number
  groupName: string
  title: string | null
  type: SessionTypeName
  status: SessionStatusName
  /** ISO-8601 (DateTimeOffset) */
  scheduledStart: string
  scheduledEnd: string
  actualStart: string | null
  actualEnd: string | null
  /** Reja: `scheduledEnd − scheduledStart`. Doim bor. */
  plannedMinutes: number
  /** Haqiqiy: `actualEnd − actualStart`. `null` — dars o'tmagan/yakunlanmagan. */
  actualMinutes: number | null
  /** Guruhdagi HOZIRGI faol o'quvchilar soni (izoh: backend DTO). */
  studentCount: number
  /** `Present` + `Late` + `Partial`. Yozuvi yo'q o'quvchi sanalmaydi. */
  attendedCount: number
  isHost: boolean
  /**
   * Bu darsda o'quv bo'limining sifat tahlili bormi (R30).
   *
   * ★ "Tahlil" tugmasi FAQAT shu bayroq `true` bo'lganda chiziladi:
   * aks holda ustoz har qatorda tugma ko'rib, aksariyatida bo'sh oyna
   * ochardi. Serverda bu AYNI `SELECT` ichidagi korrelyatsion so'rov,
   * ya'ni qo'shimcha so'rov YO'Q.
   *
   * 🔴 Bu jadval o'quvchiga UMUMAN berilmaydi (server 403).
   */
  hasReview: boolean
  /** Xulosa yoki `null` — tahlil yo'q. */
  reviewStatus: SessionReviewVerdictName | null
}

/**
 * Dars sifati tahlilining xulosasi (R29 / R30).
 *
 * ★ UCHTA HOLAT ESKI ILOVADAN TIKLANDI: "Ko'rilmagan / Tasdiqlandi /
 * Muammo bor" (`RecordingCard.vue` izohidagi tarixiy yozuv). `NotReviewed`
 * — QORALAMA: xodim tahlilni yozdi, lekin xulosani hali chiqarmadi.
 * "Tahlil umuman yo'q" holati esa alohida — u `hasReview: false` bilan
 * ifodalanadi va nishonda AYNI "Ko'rilmagan" ni beradi.
 */
export type SessionReviewVerdictName = 'NotReviewed' | 'Approved' | 'HasIssue'

/**
 * `GET /api/v1/live-sessions/{id}/review` javobi.
 *
 * ⚠️ TAHLIL YO'Q BO'LSA SERVER `200` VA JSON `null` QAYTARADI — 404 emas.
 * "Hali yozilmagan" normal holat; 404 bo'lsa modal har ochilishida qizil
 * ogohlantirish ko'rsatardi.
 *
 * 🔴 O'QUVCHI BU MANZILGA UMUMAN KIRA OLMAYDI (`403`) — chegara serverda,
 * tugmani yashirishda emas.
 */
export interface SessionReviewDto {
  id: number
  sessionId: number
  verdict: SessionReviewVerdictName
  /** Ijobiy tomonlar. `null` — kiritilmagan (ixtiyoriy). */
  plus: string | null
  /** Kamchiliklar. `null` — kiritilmagan (ixtiyoriy). */
  minus: string | null
  /** Xulosa va yechimlar — YAKUNIY, MAJBURIY qism. */
  conclusion: string
  /** DARSNING jadval bo'yicha boshlanish vaqti (tahlil yozilgan vaqt EMAS — u `createdAt`/`updatedAt`da). */
  sessionScheduledStart: string
  groupName: string
  /** `null` — ustoz sarlavha kiritmagan (bunday holatda `groupName`ga tushiladi). */
  sessionTitle: string | null
  /** Darsni olib borishi kerak bo'lgan xodim. `null` — guruhga hali biriktirilmagan. */
  teacherName: string | null
  authorId: number
  /** Xulosani yozgan xodim — ustoz uchun "kim aytdi" savoliga javob. */
  authorName: string
  /** ★ QULAYLIK, RUXSAT EMAS: haqiqiy qoida serverda va u har yozishda qayta tekshiriladi. */
  canEdit: boolean
  createdAt: string
  updatedAt: string | null
  /** Mezon asosidagi ballar. Bo'sh massiv — hali ballanmagan yoki eski, ballashsiz tahlil. */
  scores: SessionReviewScoreDto[]
  totalScore: number
  totalMaxScore: number
  /** `null` — hali BIRORTA ham mezon bo'yicha ball qo'yilmagan (0% bilan ARALASHMASIN). */
  scorePercent: number | null
}

/**
 * `GET /api/v1/session-reviews/teachers-overview` — bitta xodim (ustoz/
 * kurator) bo'yicha tahlillar xulosasi ("Tahlillar" jadvali, boshqaruv
 * paneli, faqat Academic/Admin).
 */
export interface TeacherReviewOverviewDto {
  teacherId: number
  teacherName: string
  totalReviews: number
  approvedCount: number
  hasIssueCount: number
  notReviewedCount: number
  lastReviewAt: string
}

/** Bitta mezon bo'yicha qo'yilgan ball — yozish vaqtidagi nom/maksimal ball bilan (snapshot). */
export interface SessionReviewScoreDto {
  criterionId: number | null
  criterionName: string
  maxScore: number
  score: number
}

/** `PUT /api/v1/live-sessions/{id}/review` tanasi (UPSERT). */
export interface SaveSessionReviewRequest {
  verdict: SessionReviewVerdictName
  conclusion: string
  plus?: string | null
  minus?: string | null
  scores: SaveSessionReviewScoreRequest[]
}

/** Bitta mezon uchun yuborilgan ball. */
export interface SaveSessionReviewScoreRequest {
  criterionId: number
  score: number
}

/** Dars tahlili mezoni (Sozlamalar ro'yxati va tahlil formasi uchun). */
export interface AnalysisCriterionDto {
  id: number
  name: string
  maxScore: number
  sortOrder: number
}

/** `POST`/`PUT /api/v1/analysis-criteria` tanasi. */
export interface SaveAnalysisCriterionRequest {
  name: string
  maxScore: number
  sortOrder: number
}

/** Frontend LiveKit'ga AYNAN shu bilan ulanadi (SPEC 5). */
export interface LiveKitJoinDto {
  serverUrl: string
  token: string
  roomName: string
  isHost: boolean
  endsAt: string | null
}

export interface ChatMessageDto {
  id: number
  senderId: number
  senderName: string
  body: string
  sentAt: string
  /**
   * ★ REAL VAQTDAGI xabarning BARQAROR kaliti (REST tarixida bo'lmaydi).
   *
   * Nima uchun kerak: hub xabarni avval tarqatadi, keyin fon navbatida
   * bazaga yozadi — ya'ni tarqatilayotgan payt `id` HALI YO'Q va u yerda
   * 0 turadi. Takrorlarni `id` bo'yicha filtrlash shu sababli ishlamaydi
   * (batafsil: `entities/message/model/types.ts` -> `messageKey`).
   */
  clientId?: string | null
}

/* ==========================================================================
   Boshqaruv/o'quv API'lari (swagger: http://localhost:5080/swagger).
   Maydonlar swagger sxemasidan AYNAN olingan; nullable bo'lganlari `| null`.
   ========================================================================== */

/** ASP.NET `PagedResult<T>` — `total` umumiy soni, `totalPages` server hisoblaydi. */
export interface PagedResult<T> {
  items: T[] | null
  page: number
  pageSize: number
  total: number
  totalPages: number
}

/** `GroupType` enum. */
export type GroupTypeName = 'Group' | 'Individual' | 'Curator'

/** `DayOfWeek` — backend .NET nomlarini satr sifatida yuboradi. */
export type DayOfWeekName =
  | 'Sunday'
  | 'Monday'
  | 'Tuesday'
  | 'Wednesday'
  | 'Thursday'
  | 'Friday'
  | 'Saturday'

/** `MemberStatus` enum. */
export type MemberStatusName = 'Active' | 'Paused' | 'Stopped' | 'Moved'

/** `SubmissionStatus` enum. */
export type SubmissionStatusName = 'Submitted' | 'Graded'

/** `AttemptStatus` enum. Test hali boshlanmagan bo'lsa `null` keladi. */
export type AttemptStatusName = 'InProgress' | 'Submitted'

/** `TestKind` enum. */
export type TestKindName = 'Lesson' | 'Competition'

/**
 * `GroupStaffRole` (R33 + R40) — guruhning IKKI xodim o'rindig'idan qaysi
 * biri mas'ul. Server enum'i JSON'da SATR bo'lib keladi.
 *
 * ★ `UserRoleName` BILAN ARALASHTIRILMASIN: u foydalanuvchining ROLI
 * (`Student`, `Admin` ham bor), bu esa guruhdagi MAS'ULIYAT o'rni va
 * uning atigi uchta qiymati bor.
 */
export type GroupStaffRoleName = 'Both' | 'Teacher' | 'Assistant'

export interface GroupDto {
  id: number
  name: string | null
  type: GroupTypeName
  courseId: number | null
  courseName: string | null
  /* ===== R21b · GURUH KATEGORIYASI ===== */
  /**
   * O'quv YO'NALISHI ("ATF", "Grammatika", "CEFR", "IELTS").
   * `null` — yorliq qo'yilmagan (mavjud guruhlarning aksariyati shunday).
   *
   * ⚠️ `courseId` BILAN ARALASHTIRILMASIN: kurs — KONTENT (modul/dars/gating),
   * bu esa faqat YORLIQ. Server tomondagi to'liq chegara `GroupCategory`
   * sinfi izohida.
   */
  categoryId: number | null
  /** Kategoriya nomi. `categoryId` `null` bo'lsa bu ham `null` (server kafolati). */
  categoryName: string | null
  /* ===== /R21b ===== */
  /* ===== WAVE 2 · GURUH (wave2/groups) ===== */
  /**
   * Video darslar QAYSI kurs darsidan boshlanadi. `null` — guruh kursni
   * BOSHIDAN boshlaydi (eng ko'p uchraydigan holat).
   *
   * ★ Uchala maydon BIRGA `null` yoki BIRGA to'ldirilgan (server kafolati):
   * nomlar bazada ichki `SELECT` bilan olinadi, ya'ni ro'yxat uchun
   * qo'shimcha so'rov ketmaydi. Shuning uchun UI nomni ko'rsatish uchun
   * kurs daraxtini yuklashi SHART EMAS.
   */
  videoStartLessonId: number | null
  videoStartLessonName: string | null
  videoStartModuleName: string | null
  /* ===== /WAVE 2 · GURUH ===== */
  teacherId: number | null
  teacherName: string | null
  assistantId: number | null
  assistantName: string | null
  curatorGroupId: number | null
  curatorGroupName: string | null
  /** `YYYY-MM-DD` */
  startDate: string
  endDate: string
  courseMonths: number
  weekdays: DayOfWeekName[] | null
  /** `HH:mm:ss` */
  startTime: string
  durationMinutes: number
  isActive: boolean
  recordEnabled: boolean
  /**
   * Shu guruhning yozuvlari o'quvchilarga ko'rinadimi (R5).
   *
   * ⚠️ `recordEnabled` BILAN ARALASHTIRILMASIN: u "dars YOZIB OLINSINMI",
   * bu esa "yozilgan fayl o'quvchiga KO'RSATILSINMI". Ikkinchisi o'chiq
   * bo'lsa yozuv baribir olinadi va o'quv bo'limi uni ko'raveradi.
   */
  recordingsVisibleToStudents: boolean
  /* ===== R33 + R40 · KIM MAS'UL ===== */
  /**
   * R33 — bu guruhning topshirilgan ishlarini KIM tekshiradi.
   * Standart `'Both'` = bugungi xatti-harakat (ustoz ham, kurator ham).
   */
  assignmentGraderRole: GroupStaffRoleName
  /**
   * R40 — bu guruh o'quvchilarining savollariga KIM javob beradi.
   * Standart `'Assistant'` = bugungi xatti-harakat (faqat kurator).
   *
   * 🔴 `'Both'` bo'lsa o'quvchida IKKI suhbat bo'ladi (ustoz va kurator) —
   * `GET /messages/conversations` ikki qator qaytaradi.
   */
  questionResponderRole: GroupStaffRoleName
  /* ===== /R33 + R40 ===== */
  memberCount: number
  sessionCount: number
  createdAt: string
  updatedAt: string | null
}

/**
 * `GET /api/v1/groups/{id}/members` elementi.
 *
 * 🔴 KONTAKT ROLGA QARAB KESILADI (R27, `GroupService.ProjectMembers`):
 * so'rovchi USTOZ bo'lsa `email` va `phone` — `null`. Kurator, o'quv bo'limi
 * va admin uchun to'liq keladi (kuratorga raqam kerak: qo'ng'iroq — uning
 * asosiy amali).
 */
export interface GroupMemberDto {
  id: number
  studentId: number
  fullName: string | null
  /** `null` — so'rovchi ustoz (serverda kesilgan). Bazada ustun MAJBURIY. */
  email: string | null
  /** `null` — raqam kiritilmagan YOKI so'rovchi ustoz. Ikkisi farqlanmaydi. */
  phone: string | null
  status: MemberStatusName
  joinedAt: string
  pausedUntil: string | null
  sourceGroupId: number
  sourceGroupName: string | null
  /** `Stopped`/`Moved`ga o'tgan vaqt. `null` — hozir faol yoki pauzada. */
  leftAt: string | null
  /** Chiqarish/ko'chirishni bajargan xodim ismi. */
  leftByName: string | null
  /** `status === 'Moved'`da — qaysi guruhga. Boshqa holatda `null`. */
  movedToGroupId: number | null
  movedToGroupName: string | null
  /** Ko'chirish sababi (ko'chirishda majburiy yozilgan). */
  reason: string | null
}

/** `GET /api/v1/groups/{id}/schedule` elementi. */
export interface ScheduledSessionDto {
  id: number
  groupId: number
  title: string | null
  type: SessionTypeName
  status: SessionStatusName
  scheduledStart: string
  scheduledEnd: string
  actualStart: string | null
  actualEnd: string | null
  hostId: number | null
  hostName: string | null
  roomName: string | null
}

/**
 * `AnswerFormats` — .NET `[Flags]` enum. JSON'da `"Text, Image"` ko'rinishida
 * KELADI (bitta nom emas), shuning uchun turi `string`.
 */
export type AnswerFormatsValue = string

export interface AssignmentDto {
  id: number
  groupId: number | null
  groupName: string | null
  moduleLessonId: number | null
  moduleLessonName: string | null
  title: string | null
  description: string | null
  maxScore: number
  dueAt: string | null
  allowedFormats: AnswerFormatsValue
  /** ⚠️ WAVE 2 dan ESKIRGAN (deprecated) — o'rniga `attachments`. */
  imageKey: string | null
  /** WAVE 2 · vazifa SHARTIGA biriktirilgan fayllar (tartib bo'yicha). */
  attachments: AssignmentAttachmentDto[] | null
  createdById: number | null
  submissionCount: number
  gradedCount: number
  createdAt: string
  updatedAt: string | null
  /**
   * R33 — SHU vazifaning tekshiruvchisi. `null` — guruh sozlamasi ishlaydi
   * (`GroupDto.assignmentGraderRole`).
   *
   * ★ Faqat GURUH vazifasida to'ldiriladi: kurs vazifasi o'nlab guruhga
   * taalluqli va ularning har birida boshqa xodim ishlaydi, shuning uchun
   * server u yerda 409 beradi.
   */
  graderRole: GroupStaffRoleName | null
}

export interface SubmissionFileDto {
  id: number
  objectKey: string | null
  kind: string
  sizeBytes: number
  contentType: string | null
}

/**
 * R37 · USTOZ tekshirishda biriktirgan fayl.
 *
 * 🔴 `SubmissionFileDto` BILAN ARALASHTIRILMASIN: u o'quvchining javobi, bu
 * esa tekshiruvchining javobi. Ular boshqa-boshqa jadvaldan keladi va yuklab
 * olish manzillari ham boshqa (`/submissions/files/{id}` va
 * `/submissions/feedback-files/{id}`).
 */
export interface SubmissionFeedbackFileDto {
  id: number
  submissionId: number
  kind: AttachmentKindName
  contentType: string
  /** Ustoz bergan nom (tozalangan). */
  fileName: string | null
  sizeBytes: number
  createdById: number | null
  createdAt: string
}

/** O'quvchining O'Z topshirig'i (`assignments/mine` ichida). */
export interface StudentSubmissionDto {
  id: number
  status: SubmissionStatusName
  text: string | null
  score: number | null
  scorePercent: number | null
  feedback: string | null
  submittedAt: string
  attemptNumber: number
  allowResubmit: boolean
  resubmitNote: string | null
  isLate: boolean
  files: SubmissionFileDto[] | null
  /** R37 · ustoz tekshirishda biriktirgan fayllar. */
  feedbackFiles: SubmissionFeedbackFileDto[] | null
}

/**
 * `GET /api/v1/assignments/mine`.
 * `lessonUnlocked` — dars hali ochilmagan bo'lsa vazifa KO'RINADI, lekin
 * topshirib bo'lmaydi; sabab foydalanuvchiga aytilishi shart.
 */
export interface StudentAssignmentDto {
  id: number
  groupId: number | null
  groupName: string | null
  moduleLessonId: number | null
  moduleLessonName: string | null
  title: string | null
  description: string | null
  maxScore: number
  dueAt: string | null
  allowedFormats: AnswerFormatsValue
  /** ⚠️ WAVE 2 dan ESKIRGAN (deprecated) — o'rniga `attachments`. */
  imageKey: string | null
  /** WAVE 2 · shart biriktirmalari. Qulflangan darsning vazifasida BO'SH. */
  attachments: AssignmentAttachmentDto[] | null
  isOverdue: boolean
  lessonUnlocked: boolean
  canSubmit: boolean
  mySubmission: StudentSubmissionDto | null
}

/** `GET /api/v1/assignments/{id}/submissions` — ustoz uchun to'liq ko'rinish. */
export interface SubmissionDto {
  id: number
  assignmentId: number
  studentId: number
  studentName: string | null
  text: string | null
  status: SubmissionStatusName
  score: number | null
  scorePercent: number | null
  feedback: string | null
  gradedById: number | null
  gradedAt: string | null
  submittedAt: string
  attemptNumber: number
  allowResubmit: boolean
  resubmitNote: string | null
  isLate: boolean
  files: SubmissionFileDto[] | null
  /** R37 · ustoz tekshirishda biriktirgan fayllar. */
  feedbackFiles: SubmissionFeedbackFileDto[] | null
}

/* ==========================================================================
   O'QUV BO'LIMI UMUMIY KO'RINISHI (2026-08-15) — `GET /assignments/overview/*`.
   ========================================================================== */

/** Guruh (yoki "Kurs vazifalari" — `groupId: null`) bo'yicha uy vazifalari xulosasi. */
export interface AssignmentGroupOverviewDto {
  groupId: number | null
  groupName: string
  groupType: GroupTypeName | null
  teacherId: number | null
  teacherName: string | null
  assignmentCount: number
  submissionCount: number
  gradedCount: number
  ungradedCount: number
  lastSubmittedAt: string | null
}

/**
 * Bitta javob — guruh, ustoz va tekshiruvchi konteksti bilan (`overview/submissions`).
 *
 * `graderLabel` — "kim tekshirishi kerak" ko'rsatish matni. `null` — kurs
 * vazifasi (hamma guruhga taalluqli, bitta aniq tekshiruvchi yo'q).
 */
export interface SubmissionOverviewDto {
  submissionId: number
  assignmentId: number
  assignmentTitle: string | null
  groupId: number | null
  groupName: string | null
  groupType: GroupTypeName | null
  teacherId: number | null
  teacherName: string | null
  studentId: number
  studentName: string | null
  status: SubmissionStatusName
  score: number | null
  maxScore: number
  scorePercent: number | null
  submittedAt: string
  isLate: boolean
  attemptNumber: number
  gradedAt: string | null
  gradedById: number | null
  gradedByName: string | null
  graderLabel: string | null
}

export interface GradeSubmissionRequest {
  score: number
  feedback?: string | null
}

/**
 * `POST /api/v1/assignments` tanasi.
 *
 * NISHON — `groupId` YOKI `moduleLessonId`, ikkalasi ham emas va hech biri
 * ham emas (server `Assignment.Validate()` da 409 beradi). Shuning uchun
 * ikkalasi ham majburiy-nullable: "umuman yubormaslik" degan uchinchi holat
 * bo'lmaydi.
 */
export interface CreateAssignmentRequest {
  title: string
  /** Guruh vazifasi — ustoz/kurator FAQAT o'z guruhiga bera oladi. */
  groupId: number | null
  /** Kurs darsi vazifasi — faqat o'quv bo'limi/admin (aks holda 403). */
  moduleLessonId: number | null
  description: string | null
  maxScore: number
  /** ISO-8601. `null` — muddatsiz. */
  dueAt: string | null
  allowedFormats: AnswerFormatsValue
  imageKey: string | null
  /** R33 — tekshiruvchi. `null` = guruh sozlamasi (eng ko'p uchraydigan holat). */
  graderRole: GroupStaffRoleName | null
}

/**
 * `PUT /api/v1/assignments/{id}` tanasi.
 *
 * ★ TO'LIQ ALMASHTIRISH. C# `UpdateAssignmentRequest` ning ixtiyoriy
 * maydonlari `= null` standart qiymatga ega va servis ularni
 * TO'G'RIDAN-TO'G'RI yozadi — yuborilmagan maydon JIMGINA `null` bo'ladi
 * (tavsif, muddat, rasm kaliti yo'qoladi).
 *
 * Shu sababli bu yerda birorta maydon `?` bilan BELGILANMAGAN, garchi
 * serverda ular ixtiyoriy bo'lsa ham: shunda "maydonni yuborishni unutish"
 * kompilyatsiya xatosiga aylanadi, ya'ni xato ishlatishda emas,
 * `npm run typecheck` da ushlanadi.
 *
 * NISHON (guruh/dars) bu tanada YO'Q — server uni ataylab o'zgartirmaydi
 * (mavjud javoblar begona vazifaga tegib qolardi).
 */
export interface UpdateAssignmentRequest {
  title: string
  description: string | null
  maxScore: number
  dueAt: string | null
  allowedFormats: AnswerFormatsValue
  imageKey: string | null
  /** R33 — tekshiruvchi. `null` = guruh sozlamasi (eng ko'p uchraydigan holat). */
  graderRole: GroupStaffRoleName | null
}

/** `POST /api/v1/submissions/{id}/reopen` tanasi. `note` O'QUVCHIGA ko'rinadi. */
export interface ReopenSubmissionRequest {
  note?: string | null
}

/** `GET /api/v1/tests/available` — o'quvchining testlari. */
export interface AvailableTestDto {
  id: number
  title: string | null
  description: string | null
  kind: TestKindName
  moduleLessonId: number | null
  moduleLessonName: string | null
  timeLimitMinutes: number | null
  dueAt: string | null
  questionCount: number
  maxScore: number
  myStatus: AttemptStatusName | null
  myScore: number | null
  canStart: boolean
}

/* ==========================================================================
   TESTLAR — XODIM KO'RINISHI (o'quv bo'limi/admin).

   ★ BACKEND ATAYLAB IKKI XIL TUR QAYTARADI va biz shu ajratishni frontendda
   ham SAQLAYMIZ:
     • `TestAuthoringDto`  (`GET /tests/{id}`)      — to'g'ri javoblar BILAN;
     • `TakeTestDto`       (`GET /tests/{id}/take`) — `isCorrect` maydoni YO'Q.

   Bitta umumiy tur yozib "o'quvchida shu maydonni ko'rsatmaymiz" deyish
   xavfli: bir joyda unutilsa javoblar jimgina oshkor bo'lardi va buni hech
   kim sezmasdi. Ikki turda bunday xato TypeScript darajasida imkonsiz.
   ========================================================================== */

/** `GET /api/v1/tests` qatori. `maxScore` — savollar balining yig'indisi. */
export interface TestDto {
  id: number
  title: string | null
  description: string | null
  kind: TestKindName
  moduleLessonId: number | null
  moduleLessonName: string | null
  timeLimitMinutes: number | null
  dueAt: string | null
  isPublished: boolean
  createdById: number | null
  questionCount: number
  maxScore: number
  /** Topshirilgan urinishlar soni. Noldan katta bo'lsa test QULFLANADI. */
  attemptCount: number
  createdAt: string
  updatedAt: string | null
}

export interface AuthoringOptionDto {
  id: number
  body: string | null
  position: number
  isCorrect: boolean
}

export interface AuthoringQuestionDto {
  id: number
  body: string | null
  imageKey: string | null
  position: number
  points: number
  /** Bir nechta to'g'ri variant belgilanganmi (server hisoblaydi). */
  isMultipleChoice: boolean
  options: AuthoringOptionDto[] | null
}

/** `GET /api/v1/tests/{id}` — TO'G'RI JAVOBLAR bilan, faqat xodim uchun. */
export interface TestAuthoringDto {
  test: TestDto
  questions: AuthoringQuestionDto[] | null
}

/* ==========================================================================
   TESTLAR — O'QUVCHI KO'RINISHI.
   ========================================================================== */

/** ★ Variant — o'quvchi ko'rinishi. `isCorrect` BU TURDA YO'Q (ataylab). */
export interface TakeOptionDto {
  id: number
  body: string | null
  position: number
}

export interface TakeQuestionDto {
  id: number
  body: string | null
  imageKey: string | null
  position: number
  points: number
  /**
   * Savolda bir nechta to'g'ri javob bor — interfeys CHECKBOX ko'rsatadi
   * (radio emas). Bu QAYSI variant to'g'ri ekanini oshkor qilmaydi.
   */
  multipleAnswers: boolean
  options: TakeOptionDto[] | null
}

/** `GET /api/v1/tests/{id}/take` — yechish varaqasi. */
export interface TakeTestDto {
  id: number
  title: string | null
  description: string | null
  timeLimitMinutes: number | null
  dueAt: string | null
  attemptId: number
  startedAt: string
  /**
   * ★ Urinish qachon tugaydi — SERVER hisobi (vaqt chegarasi va `dueAt` dan
   * ERTAROG'I, tolerantlik qo'shilgan). `null` — chegarasiz.
   *
   * Klient taymeri AYNAN shu qiymatga tayanadi, lekin qaror baribir
   * serverniki: `TestService.EnsureWithinTimeLimitAsync` muddati o'tgan
   * urinishni 0 ball bilan yopadi va 409 qaytaradi.
   */
  deadline: string | null
  maxScore: number
  questions: TakeQuestionDto[] | null
}

/** `POST /api/v1/tests/{id}/start` javobi. Amal IDEMPOTENT. */
export interface StartAttemptDto {
  attemptId: number
  testId: number
  startedAt: string
  deadline: string | null
  timeLimitMinutes: number | null
}

/** `POST /tests/{id}/submit` va `GET /tests/{id}/my-result` javobi. */
export interface MyResultDto {
  testId: number
  title: string | null
  attemptId: number
  status: AttemptStatusName
  score: number | null
  maxScore: number | null
  percent: number | null
  startedAt: string
  submittedAt: string | null
  /** Vaqt tugagani uchun majburan yopilganmi (0 ball). */
  closedByTimeout: boolean
}

/**
 * `GET /api/v1/tests/{id}/results` qatori — BITTA URINISH = BITTA QATOR.
 *
 * `groupNames` ataylab SATR (ro'yxat emas): ikki guruhdagi o'quvchi bitta
 * qatorda, guruhlari vergul bilan ko'rsatiladi.
 */
export interface TestResultRowDto {
  attemptId: number
  studentId: number
  studentName: string | null
  groupNames: string | null
  score: number | null
  maxScore: number | null
  percent: number | null
  submittedAt: string | null
  closedByTimeout: boolean
}

/* ==========================================================================
   TESTLAR — SO'ROV TANALARI.
   ========================================================================== */

/**
 * `POST /api/v1/tests`.
 *
 * `kind` JSON'da SATR: `"Lesson"` (kurs darsiga bog'lanadi, sur'at nazoratiga
 * kiradi) yoki `"Competition"` (kursdan mustaqil). Domain qoidasi qat'iy:
 * dars testida `moduleLessonId` SHART, musobaqada esa BO'LMASLIGI shart —
 * aks holda 409.
 */
export interface CreateTestRequest {
  title: string
  kind: TestKindName
  moduleLessonId: number | null
  description: string | null
  timeLimitMinutes: number | null
  dueAt: string | null
}

/**
 * `PUT /api/v1/tests/{id}` — ★ TO'LIQ ALMASHTIRISH.
 *
 * C# `UpdateTestRequest` ning ixtiyoriy maydonlari `= null` standart qiymatga
 * ega va `TestService.UpdateAsync` ularni TO'G'RIDAN-TO'G'RI yozadi
 * (`test.TimeLimitMinutes = request.TimeLimitMinutes`). Ya'ni yuborilmagan
 * maydon JIMGINA `null` bo'ladi: tavsif, vaqt chegarasi va muddat yo'qoladi.
 *
 * Shuning uchun bu yerda birorta maydon `?` bilan belgilanmagan — "yuborishni
 * unutish" `npm run typecheck` da ushlanadi, ishlatishda emas.
 *
 * TUR va DARS bu tanada YO'Q: server ularni ataylab o'zgartirmaydi (musobaqa
 * testini dars testiga aylantirish gating'ni va mavjud natijalar ma'nosini
 * buzardi).
 */
export interface UpdateTestRequest {
  title: string
  description: string | null
  timeLimitMinutes: number | null
  dueAt: string | null
}

export interface SaveOptionRequest {
  body: string
  isCorrect: boolean
  /** `null` bo'lsa server ro'yxatdagi tartibni qo'llaydi. */
  position: number | null
}

/**
 * Savol yozish tanasi (`POST .../questions` va `PUT .../questions/{id}`).
 *
 * ★ TAHRIRLASHDA VARIANTLAR BUTUNLAY ALMASHTIRILADI: server eskilarini
 * o'chirib, shu ro'yxatdan yangisini yozadi. Demak forma mavjud variantlarni
 * yuklab, HAMMASINI qaytarib yuborishi shart.
 *
 * Domain talabi: kamida 2 variant, kamida 1 tasi to'g'ri, ball noldan katta.
 */
export interface SaveQuestionRequest {
  body: string
  options: SaveOptionRequest[]
  points: number
  position: number | null
  imageKey: string | null
}

/** Bitta savolga tanlangan variant(lar). Bo'sh ro'yxat — javobsiz (0 ball). */
export interface QuestionAnswerRequest {
  questionId: number
  optionIds: number[]
}

/**
 * `POST /api/v1/tests/{id}/submit`.
 *
 * Baholash SERVERDA: klient hisoblagan ballga hech qachon ishonilmaydi.
 * Begona variant ID'lari serverda filtrlanadi.
 */
export interface SubmitTestRequest {
  answers: QuestionAnswerRequest[]
}

/**
 * `GET /api/v1/users` elementi. `role` backendda `string` (enum nomi).
 *
 * 🔴 `GET /users/{id}/profile` ICHIDA kelganda kontakt ROLGA qarab kesiladi
 * (R27): so'rovchi USTOZ bo'lsa `email`, `phone`, `telegramId` va
 * `telegramUsername` — hammasi `null`. `/api/v1/users` ro'yxati esa faqat
 * o'quv bo'limi/adminga ochiq, ya'ni u yerda kesish yo'q.
 */
export interface UserDetailsDto {
  id: number
  fullName: string | null
  /** `null` — so'rovchi ustoz (kesilgan). Bazada ustun MAJBURIY. */
  email: string | null
  phone: string | null
  telegramId: number | null
  /**
   * WAVE 2 (`wave2/users`): Telegram `from.username`, `@` BELGISIZ.
   *
   * 🔴 IDENTIFIKATOR SIFATIDA ISHLATILMAYDI — bo'shatilgan nom boshqa odamga
   * o'tadi (shu sababli backendda unikal indeks ATAYLAB yo'q). Faqat
   * `t.me/<username>` havolasi uchun; shaxs `telegramId` bo'yicha aniqlanadi.
   */
  telegramUsername: string | null
  role: string | null
  isActive: boolean
  createdAt: string
  updatedAt: string | null
}

export interface CreateUserRequest {
  fullName: string
  email: string
  role: UserRoleName
  /**
   * 🔴 XODIM ROLLARI UCHUN MAJBURIY (`Student` dan tashqari hammasi) —
   * server 400 qaytaradi.
   *
   * Sabab: 2026-08-13 dan kirish faqat telefon orqali. Telefonsiz xodim
   * CRM'da normal ko'rinadi, lekin hech qachon kira olmaydi.
   */
  phone?: string | null

  // ⚠️ `password` maydoni OLIB TASHLANDI (2026-08-13): parol bilan kirish
  //    yo'q, server uni umuman qabul qilmaydi.
  isActive: boolean
}

export interface CreateUserResponse {
  user: UserDetailsDto

  // ⚠️ `temporaryPassword` OLIB TASHLANDI (2026-08-13). Yangi
  //    foydalanuvchi botga raqamini ulab, kod bilan kiradi — uzatiladigan
  //    "boshlang'ich parol" degan narsa yo'q.
}

export interface UpdateUserRequest {
  fullName: string
  email: string
  phone?: string | null
  role: UserRoleName
}

/**
 * `POST /groups` va `PUT /groups/{id}` uchun YAGONA shakl.
 *
 * ★ NEGA BITTA TUR, backendda ikkitasi bo'lsa ham (`CreateGroupRequest` va
 * `UpdateGroupRequest`): ikkala record MAYDON-MAYDON AYNAN bir xil
 * (`GroupDtos.cs`, 2026-08-11 holati). Ikki nusxa tur yozilsa yangi maydon
 * bittasiga qo'shilib ikkinchisida unutilardi — bu esa `PUT` to'liq
 * almashtirish semantikasida maydonni jimgina `null` ga tushirardi.
 * Shakllar ajralib ketsa AYNI SHU joyda ikkiga bo'linadi.
 */
export interface GroupWriteRequest {
  name: string
  /** `YYYY-MM-DD` */
  startDate: string
  weekdays: DayOfWeekName[]
  /** `HH:mm:ss` */
  startTime: string
  type: GroupTypeName
  durationMinutes: number
  courseMonths: number
  courseId?: number | null
  /* ===== WAVE 2 · GURUH (wave2/groups) ===== */
  /**
   * Video darslar boshlanish nuqtasi (kurs darsining Id'si).
   *
   * 🔴 `PUT` = TO'LIQ ALMASHTIRISH: yuborilmasa yoki `null` yuborilsa guruh
   * kursni boshidan boshlaydigan holatga QAYTADI. Tahrirlash formasi joriy
   * qiymatni yuklab, qaytarib yuborishi shart.
   *
   * 🔴 Kurs almashtirilganda BU MAYDON YUBORILMAYDI (yoki yangi kursning
   * darsi yuboriladi): eski kursning darsi 400 bilan rad etiladi
   * (`problem.errors.videoStartLessonId`). Kurssiz guruhda dars yuborilsa
   * ham 400.
   */
  videoStartLessonId?: number | null
  /* ===== /WAVE 2 · GURUH ===== */
  /* ===== R21b · GURUH KATEGORIYASI ===== */
  /**
   * O'quv yo'nalishi (kategoriya Id'si).
   *
   * 🔴 HAR DOIM YUBORILSIN: bu PUT = TO'LIQ ALMASHTIRISH. Yuborilmasa server
   * `null` yozadi va guruh yorlig'ini JIMGINA yo'qotadi — aynan shu tuzoq
   * loyihada bir marta ishlagan (kurs uzilib butun guruhda gating
   * `NotInCourse` bo'lgan). Formada u `buildPayload` orqali uchala
   * bo'limdan yig'iladi, ya'ni bitta bo'limni saqlash boshqasini
   * o'chirmaydi (`features/group-form/model/group-sections.ts`).
   */
  categoryId?: number | null
  /* ===== /R21b ===== */
  teacherId?: number | null
  assistantId?: number | null
  curatorGroupId?: number | null
  recordEnabled: boolean
  /**
   * R5. 🔴 HAR DOIM YUBORILSIN: bu PUT semantikasi — yuborilmagan maydon
   * server tomonda standart qiymatga tushadi. Server standarti `true`
   * (ya'ni tushib qolgan maydon yozuvlarni YOPMAYDI), lekin formadagi
   * joriy qiymat baribir uzatilishi kerak, aks holda tahrirlash har
   * safar kalitni `true` ga qaytarardi.
   */
  recordingsVisibleToStudents: boolean
  /**
   * R33 — tekshiruvchi. 🔴 HAR DOIM YUBORILSIN (PUT semantikasi).
   * Server standarti `'Both'` = bugungi xatti-harakat.
   */
  assignmentGraderRole: GroupStaffRoleName
  /**
   * R40 — savollarga javob beruvchi. 🔴 HAR DOIM YUBORILSIN.
   *
   * Server standarti `'Assistant'` = bugungi xatti-harakat. Standart
   * `'Both'` bo'lganda maydonni yubormagan klient guruh savollarini
   * JIMGINA ustozga ham ochib yuborardi.
   */
  questionResponderRole: GroupStaffRoleName
  isActive: boolean
}

export interface CreateGroupResponse {
  group: GroupDto
  sessionsCreated: number
}

export interface ScheduleChangeSummary {
  scheduleTouched: boolean
  regenerated: boolean
  created: number
  deleted: number
  preserved: number
  hostsUpdated: number
  titlesUpdated: number
  reason: string | null
}

export interface UpdateGroupResponse {
  group: GroupDto
  schedule: ScheduleChangeSummary
}

export interface AddMemberRequest {
  studentId: number
}

/** `pausedUntil` — `YYYY-MM-DD`. `null` bo'lsa muddatsiz pauza (qo'lda tiklanadi). */
export interface PauseMemberRequest {
  pausedUntil?: string | null
}

export interface MoveMemberRequest {
  targetGroupId: number
  /** MAJBURIY — bo'sh yuborilsa server 409 qaytaradi. */
  reason: string
}

/**
 * Ko'chirish natijasi — IKKI tomon ham qaytadi: eski guruhdagi yozuv
 * (`Moved`) va yangi guruhdagi yozuv (`Active`). Server buni ataylab shunday
 * beradi, chunki UI ikkala guruh ro'yxatini ham yangilashi kerak.
 */
export interface MoveMemberResponse {
  left: GroupMemberDto
  arrived: GroupMemberDto
}

/* ==========================================================================
   R21b · GURUH KATEGORIYALARI (o'quv yo'nalishlari lug'ati)
   ========================================================================== */

/**
 * `GET /api/v1/group-categories` elementi.
 *
 * ★ SAHIFALANMAYDI — server ATAYLAB `PagedResult` emas, oddiy massiv
 * qaytaradi: bu lug'at tanlagichlarni to'ldiradi va u YAXLIT kerak
 * (sahifalangan bo'lsa 26-band jimgina tushib qolardi).
 */
export interface GroupCategoryDto {
  id: number
  name: string | null
  position: number
  isActive: boolean
  /**
   * Shu kategoriyaga biriktirilgan guruhlar soni.
   *
   * 🔴 O'CHIRISHDAN OLDIN KO'RSATILISHI SHART: server bunday kategoriyani
   * o'chirtirmaydi (409), chunki bazadagi FK `SET NULL` bo'lib, o'chirish
   * o'nlab guruhning yorlig'ini JIMGINA yo'q qilardi.
   */
  groupCount: number
  createdAt: string
  updatedAt: string | null
}

/**
 * `POST /group-categories` va `PUT /group-categories/{id}` uchun YAGONA shakl.
 *
 * ★ Backendda ikkita record bo'lsa ham (`Create...` / `Update...`) ular
 * maydon-maydon AYNAN bir xil — `GroupWriteRequest` bilan AYNI mulohaza.
 */
export interface GroupCategoryWriteRequest {
  name: string
  isActive: boolean
}

/** `GET /api/v1/groups/{id}/curator-candidates` elementi. */
export interface CuratorCandidateDto {
  id: number
  name: string | null
  assistantId: number | null
  assistantName: string | null
  courseId: number | null
  courseName: string | null
  weekdays: DayOfWeekName[] | null
  /** `HH:mm:ss` */
  startTime: string
  /** Shu kurator guruhiga allaqachon nechta guruh bog'langan. */
  linkedGroupCount: number
}

/* ==========================================================================
   Kurs kontenti (kurs -> modul -> dars).
   ========================================================================== */

/**
 * `LessonLockReason` enum — dars nima uchun yopiq.
 *
 * ⚠️ `BeforeGroupStart` — WAVE 1 da qo'shilgan to'rtinchi qiymat
 * (`Group.VideoStartLessonId`): dars guruh boshlagan qismdan OLDINDA va
 * o'quvchining o'quv rejasiga UMUMAN kirmaydi. Tur uchta qiymatda qolib
 * ketgan edi, ya'ni server bergan sabab UI'da umumiy "Yopiq" bo'lib
 * ko'rinardi.
 */
export type LessonLockReasonName =
  | 'PreviousIncomplete'
  | 'TeacherPace'
  | 'NotInCourse'
  | 'BeforeGroupStart'

/**
 * `GET /api/v1/courses` qatori — daraxtsiz, yengil.
 * `groupCount` kursni o'chirishdan oldin nechta guruh ta'sirlanishini ko'rsatadi.
 */
export interface CourseDto {
  id: number
  name: string | null
  description: string | null
  isActive: boolean
  position: number
  moduleCount: number
  lessonCount: number
  groupCount: number
  createdAt: string
  updatedAt: string | null
}

/**
 * Modul ichidagi dars.
 *
 * `description` QULFLANGAN darsda `null` keladi (sarlavha ko'rinadi, mazmun
 * yo'q). Xodim uchun `unlocked` DOIM `true` — gating faqat o'quvchiga tegishli.
 */
export interface CourseLessonDto {
  id: number
  moduleId: number
  name: string | null
  description: string | null
  position: number
  durationMin: number | null
  /** WAVE 2 · dars turi: `Normal` — video, `Exam` — rasm. */
  kind: LessonKindName
  /**
   * WAVE 2 · dars mediasi TARTIB bo'yicha (video qismlari yoki imtihon
   * rasmlari). 🔴 QULFLANGAN darsda BO'SH massiv — `description` bilan ayni
   * qoida (sarlavha ko'rinadi, mazmun yo'q), lekin `kind` baribir keladi.
   */
  assets: LessonAssetDto[] | null
  unlocked: boolean
  lockReason: LessonLockReasonName | null
  /**
   * WAVE 2 · dars TUGATILGANMI (video ko'rilgan + vazifa topshirilgan + test
   * yechilgan — mavjud bo'lganlari uchun).
   *
   * 🔴 BU MAYDON SHU TIPDA YO'Q EDI, server esa uni WAVE 2 DAN BERI YUBORADI
   * (`CourseDtos.cs` · `CourseLessonDto.Completed`, `CourseService.MapLesson`
   * uni gating daraxtidagi `LessonGateDto.Completed` dan oladi). Tip uni
   * o'tkazib yuborgani uchun frontendda uchta izohda "server bermaydi" deb
   * yozilgan va progress "ochilgan darslar" bo'yicha hisoblangan edi —
   * o'quvchi ochib qo'ygan, lekin tugatmagan darsi ham "bajarilgan" bo'lib
   * ko'rinardi. Uchala izoh ham tuzatildi (2026-08-13, R9).
   *
   * ★ `unlocked` BILAN ADASHTIRMANG — bular ikki BOSHQA savol: dars ochiq,
   *   lekin tugatilmagan bo'lishi mumkin.
   *
   * ★ QULFLANGAN DARSDA DOIM `false` — server ataylab shunday qiladi
   *   (`MapLesson` izohi): vazifasi va testi yo'q kursda xom `Completed`
   *   qiymati butun daraxtni, hali ochilmaganini ham, "tugatilgan"
   *   ko'rsatardi.
   *
   * ★ XODIM UCHUN DOIM `false` — "tugatilgan" o'quvchi progressi, xodimda
   *   esa progress yozuvi yo'q.
   */
  completed: boolean
  hasAssignment: boolean
  hasTest: boolean
}

export interface CourseModuleDto {
  id: number
  courseId: number
  name: string | null
  position: number
  lessons: CourseLessonDto[] | null
}

/**
 * `GET /api/v1/courses/{id}` — kurs daraxti.
 *
 * Modul va darslar tartibi AYNAN gating hisoblagan ketma-ketlik bilan bir xil
 * keladi (backend kafolati) — ro'yxatdagi tartibni frontend qayta saralamaydi.
 */
export interface CourseTreeDto {
  id: number
  name: string | null
  description: string | null
  isActive: boolean
  position: number
  modules: CourseModuleDto[] | null
  createdAt: string
  updatedAt: string | null
}

/**
 * Kurs yozish shakli. `position` ATAYLAB yo'q — tartib faqat
 * `reorder` amali bilan o'zgaradi (server shunday talab qiladi).
 */
export interface CourseWriteRequest {
  name: string
  description?: string | null
  isActive: boolean
}

export interface ModuleWriteRequest {
  name: string
}

export interface LessonWriteRequest {
  name: string
  description?: string | null
  durationMin?: number | null
  /**
   * WAVE 2 · dars turi.
   *
   * 🔴 ATAYLAB `?` YO'Q, garchi serverda standart qiymati (`Normal`) bo'lsa
   * ham: `PUT` — TO'LIQ ALMASHTIRISH (`DAVOM_ETTIRISH.md` 6-bo'lim, 1-tuzoq),
   * ya'ni maydonni yubormaslik imtihon darsini jimgina `Normal` ga
   * qaytarardi. Majburiy qilinganda bu xato `npm run typecheck` da ushlanadi.
   */
  kind: LessonKindName
}

/**
 * Tartib so'rovi. ★ TO'LIQ ro'yxat kutiladi — server yetishmagan yoki
 * begona Id'da 400 qaytaradi (yarim tartib yozilmaydi).
 */
export interface ReorderRequest {
  orderedIds: number[]
}

/** Reorder javobi — har elementning yangi tartib raqami. */
export interface PositionDto {
  id: number
  position: number
}

/* ==========================================================================
   Reyting (leaderboard) — eski ilovaning "Reyting" tabi.
   ========================================================================== */

/**
 * Reyting jadvalining bitta qatori.
 *
 * `rank` TAKRORLANISHI MUMKIN: bir xil ballda ikki o'quvchi bir xil o'rin
 * oladi (1, 2, 2, 4). Shuning uchun ro'yxat `rows` TARTIBIDA chiziladi,
 * `rank` esa faqat YORLIQ sifatida ko'rsatiladi (server izohi).
 */
export interface LeaderboardRowDto {
  studentId: number
  studentName: string | null
  rank: number
  /** Yakuniy ball 0..100 — MAVJUD mezonlar o'rtachasi (`null` mezon kirmaydi). */
  total: number
  /** `null` — shu oyda o'tilgan dars yo'q. */
  attendancePercent: number | null
  /** `null` — shu oyda baholangan vazifa yo'q. */
  assignmentPercent: number | null
  /** `null` — shu oyda topshirilgan test yo'q. */
  testPercent: number | null
  isMe: boolean
  /**
   * R24 · DARS bahosi foizi. `null` — shu oyda dars bahosi yo'q.
   *
   * ★ `assignmentPercent` BILAN ARALASHTIRILMAYDI: u topshirilgan
   * ISHNING bahosi, bu esa DARSNING bahosi.
   *
   * Maydon `isMe` DAN KEYIN — server DTO'sidagi tartibning aynan
   * nusxasi (u yerda ham oxirgi, sabab: yozuv Redis'da JSON bo'lib
   * saqlanadi va o'rtaga qo'shilgan maydon eski keshni surib yuborardi).
   */
  lessonPercent: number | null
}

export interface GroupLeaderboardDto {
  groupId: number
  groupName: string | null
  /** `YYYY-MM` */
  period: string
  studentCount: number
  /** So'rovchi o'quvchining qatori. Xodim so'rasa `null`. */
  me: LeaderboardRowDto | null
  rows: LeaderboardRowDto[] | null
}

/**
 * Reyting QAMROVI (server enum'i, `JsonStringEnumConverter` bilan matn).
 *
 * 🔴 `Center` — "TIZIMDAGI HAMMA" DEGANI EMAS, bitta O'QUV MARKAZ.
 *    Mahsulot bir necha markazga sotiladi va serverdagi
 *    `ILearningCenterScope` aynan shu chegarani ushlab turadi.
 */
export type LeaderboardScopeName = 'Group' | 'Center'

/**
 * Butun o'quv markaz bo'yicha jadval.
 *
 * ★ `rows` TO'LIQ EMAS: server eng yaxshi `topCount` ta qatorni yuboradi.
 *   `studentCount` esa TO'LIQ son — ya'ni `rows.length < studentCount`
 *   bo'lishi NORMAL va bu "ma'lumot yetishmayapti" degani emas.
 */
export interface CenterLeaderboardDto {
  /** `YYYY-MM` */
  period: string
  /** Markazdagi reytingga kirgan o'quvchilar TO'LIQ soni. */
  studentCount: number
  /** Jadvalda ko'pi bilan shuncha qator keladi (serverdagi chegara). */
  topCount: number
  /**
   * So'rovchining qatori — `rows` ICHIDA BO'LMASLIGI MUMKIN (u yuqori
   * yuzlikka kirmasa). O'rin HAR DOIM to'liq ro'yxatdan olingan.
   * Xodim so'rasa `null`.
   */
  me: LeaderboardRowDto | null
  rows: LeaderboardRowDto[] | null
}

/** "Mening o'rnim" — jadvalsiz yengil ko'rinish. `groupId` `null` — faol guruh yo'q. */
export interface MyRankDto {
  /**
   * Javob qaysi qamrov bo'yicha. `Center` da `groupId` HAR DOIM `null` —
   * bu "guruh topilmadi" bilan aralashmasin uchun diskriminator kerak.
   */
  scope: LeaderboardScopeName
  groupId: number | null
  groupName: string | null
  period: string
  studentCount: number
  me: LeaderboardRowDto | null
}

/* ==========================================================================
   Kurator chati (DM) — eski ilovaning "Chat" tabi.
   ========================================================================== */

/** Suhbatlar ro'yxatidagi qator (Telegram uslubidagi ro'yxat). */
export interface ConversationDto {
  /** Suhbatdosh Id'si — thread endpointlariga SHU yuboriladi. */
  peerId: number
  peerName: string | null
  peerRole: string
  /** Kurator ro'yxatida ko'rinadi; o'quvchida `null`. */
  groupName: string | null
  lastMessageId: number | null
  lastMessagePreview: string | null
  lastMessageAt: string | null
  /** Oxirgi xabarni O'ZIM yozdimmi. Xabar yo'q bo'lsa `null`. */
  lastMessageMine: boolean | null
  unreadCount: number
}

export interface DirectMessageDto {
  id: number
  senderId: number
  senderName: string | null
  mine: boolean
  body: string
  /** Savol qaysi kurs darsidan yozilgan. `null` — umumiy. */
  moduleLessonId: number | null
  moduleLessonName: string | null
  sentAt: string
  /** Suhbatdosh MENING xabarimni o'qidimi ("ikki belgi"). */
  readByPeer: boolean
}

/**
 * Xabarlar sahifasi — KEYSET (kursorli) sahifalash.
 *
 * `items` ESKIDAN YANGIGA tartibda keladi va ekranga shundayligicha
 * chiziladi. Ofsetli sahifalash ATAYLAB ishlatilmagan: chat oqimi o'sib
 * turadi va yangi xabar kelganda oyna siljib, ko'rilgan xabarlar qayta
 * chiqardi (server izohi).
 */
export interface MessagePageDto {
  peerId: number
  peerName: string | null
  items: DirectMessageDto[] | null
  hasMore: boolean
  /** Keyingi sahifa uchun `?beforeId=`. `hasMore=false` bo'lsa `null`. */
  nextBeforeId: number | null
  unreadCount: number
}

export interface SendDirectMessageRequest {
  body: string
  /** Ixtiyoriy kontekst — savol qaysi dars sahifasidan yozilgan. */
  moduleLessonId?: number | null
}

/** `markedCount` — nechta xabar belgilandi (idempotent: takrorda 0). */
export interface MarkReadResultDto {
  markedCount: number
  unreadCount: number
}

/* ==========================================================================
   R40 · DARS SAVOLLARI NAVBATI (`GET /api/v1/messages/lesson-questions`)
   ==========================================================================

   ★ YANGI XABAR TURI EMAS: bu AYNI shaxsiy yozishmaning filtrlangan
   ko'rinishi (`moduleLessonId` to'ldirilgan xabarlar). Har qator `peerId`
   beradi va u MAVJUD suhbat endpointlariga olib boradi — ikkinchi chat
   tizimi qurilmagan.
   ========================================================================== */

export interface LessonQuestionDto {
  messageId: number
  /** O'quvchi Id'si — suhbatni ochish uchun `peerId` sifatida ishlatiladi. */
  peerId: number
  peerName: string | null
  groupName: string | null
  moduleLessonId: number
  moduleLessonName: string | null
  body: string
  sentAt: string
  /** Shu savoldan KEYIN xodim javob yozganmi. Navbat tartibi shunga tayanadi. */
  answered: boolean
  read: boolean
}

/* ==========================================================================
   Davomat xulosasi — bosh sahifadagi doira.
   ========================================================================== */

/** Bitta chelak: o'tilgan darslar, qatnashgan/qoldirgan va foiz. */
export interface AttendanceBucketDto {
  /** O'TILGAN (yakunlangan) darslar soni. */
  total: number
  /** Qatnashgan (kelgan, kechikkan, qisman). */
  attended: number
  missed: number
  /** 0..100. Dars o'tilmagan bo'lsa 0. */
  percent: number
}

/**
 * O'quvchining davomat xulosasi.
 *
 * `teacher` va `assistant` ATAYLAB ajratilgan: reyting davomat foizini FAQAT
 * ustoz darslaridan oladi, bosh sahifadagi doira esa `overall` ni ko'rsatadi
 * (eski ilovadagidek).
 */
export interface AttendanceSummaryDto {
  groupIds: number[] | null
  from: string | null
  to: string | null
  overall: AttendanceBucketDto
  teacher: AttendanceBucketDto
  assistant: AttendanceBucketDto
  /** Ketma-ket qatnashish seriyasi (birinchi qoldirilgan darsda uziladi). */
  streak: number
}

/* ==========================================================================
   MOLIYA — to'lovlar, tariflar, chegirmalar, bloklash.

   Manba: `backend/src/Zinnur.Application/Payments/Dtos/PaymentDtos.cs`
   (maydon nomlari va tartibi AYNAN o'sha yerdan; enum'lar `Program.cs` dagi
   `JsonStringEnumConverter` tufayli JSON'da SATR bo'lib keladi).

   ★ PUL — serverda `decimal`, JSON'da esa oddiy son: `450000.00` -> `450000`.
   TypeScript'da `number` (IEEE-754) bo'lgani uchun ULARNI QO'SHISH XAVFLI
   (`0.1 + 0.2`). Shu sababli:
     • ko'rsatiladigan har bir summa SERVERDAN kelgan holicha chiziladi;
     • mijozda qo'shish kerak bo'lganda faqat `shared/lib/money.ts` dagi
       `sumMoney` ishlatiladi (u tiyinda, butun sonda qo'shadi).
   ========================================================================== */

/** `PaymentStatus`. ★ `Partial` ham QARZ — qolgan qismi bo'yicha. */
export type PaymentStatusName = 'Due' | 'Partial' | 'Paid' | 'Waived'

/**
 * `PaymentMethod` — ATAYLAB IKKITA (backend qarori, 2026-07-30).
 * Click/Payme YO'Q: eski ilovadagi erkin satr kassa hisobotini buzardi.
 */
export type PaymentMethodName = 'Cash' | 'Card'

/** `PaymentTransactionKind` — moliya jurnalidagi yozuv turi. */
export type PaymentTransactionKindName = 'Payment' | 'Refund' | 'Waiver' | 'BalanceUse'

/** `DiscountKind` — foizda yoki qat'iy summada. */
export type DiscountKindName = 'Percent' | 'Amount'

/** `PaymentBlockScope` — ierarxik: keyingisi oldingisini o'z ichiga oladi. */
export type PaymentBlockScopeName = 'None' | 'Video' | 'Live' | 'Platform'

/** Bitta oylik to'lov yozuvi (o'quvchi × guruh × oy). */
export interface PaymentDto {
  id: number
  studentId: number
  studentName: string
  groupId: number
  groupName: string
  /** Hisob oyi, `YYYY-MM`. */
  period: string
  /** Tarif summasi — chegirmagacha. */
  baseAmount: number
  discountAmount: number
  /** To'lanishi kerak bo'lgan yakuniy summa. */
  amount: number
  paidAmount: number
  /** Qolgan qarz (`amount − paidAmount`). Serverda hisoblanadi. */
  outstanding: number
  status: PaymentStatusName
  paidAt: string | null
  method: PaymentMethodName | null
  note: string | null
  createdAt: string
  updatedAt: string | null
}

/** `POST /payments/periods/open` tanasi. `null` — joriy oy / barcha guruhlar. */
export interface OpenPeriodRequest {
  period?: string | null
  groupId?: number | null
}

/** Oy ochish natijasi — IDEMPOTENT amalning hisoboti. */
export interface OpenPeriodResult {
  period: string
  created: number
  /** Allaqachon ochiq bo'lgani uchun o'tkazib yuborilgan a'zoliklar. XATO EMAS. */
  alreadyOpen: number
  /** Tarif topilmagani uchun ochilmaganlar; sabablari `warnings` da. */
  skippedNoTariff: number
  /** Ochilgandan keyin balansdan avtomatik yopilgan summa. */
  balanceApplied: number
  monthsClosedFromBalance: number
  payments: PaymentDto[] | null
  warnings: string[] | null
}

/** `POST /payments` tanasi — pul qabul qilishning YAGONA yo'li. */
export interface RecordPaymentRequest {
  studentId: number
  amount: number
  method: PaymentMethodName
  /** `null` — pul o'quvchining BARCHA guruhlari bo'yicha eng eskidan taqsimlanadi. */
  groupId?: number | null
  note?: string | null
}

/** Kvitansiya — to'lov natijasining to'liq tasviri. */
export interface PaymentReceiptDto {
  transactionId: number
  /** `ZN-2026-07-000123`. */
  receiptNo: string
  studentId: number
  studentName: string
  amount: number
  /** Qarzlarga haqiqatan tushgan summa. */
  applied: number
  /** Qarzdan ortib, balansga o'tgan summa. */
  toBalance: number
  monthsClosed: number
  monthsPartial: number
  balance: number
  /** To'lovdan KEYINGI umumiy qarz. */
  debtAfter: number
  method: PaymentMethodName
  affectedMonths: PaymentDto[] | null
  createdAt: string
}

/** Kechirim sababi — auditda saqlanadi. */
export interface WaiveRequest {
  reason?: string | null
}

export interface ReversePaymentRequest {
  studentId: number
  amount: number
  reason?: string | null
}

/** Qaytarish natijasi. ★ `unreturned > 0` — XATO EMAS, xodimga aytiladigan FAKT. */
export interface ReversalDto {
  studentId: number
  requested: number
  returned: number
  fromBalance: number
  fromPayments: number
  unreturned: number
  balance: number
  debtAfter: number
  affectedMonths: PaymentDto[] | null
}

/** Moliya jurnali qatori — pul harakatining O'ZGARMAS yozuvi. */
export interface PaymentTransactionDto {
  id: number
  studentId: number
  groupId: number | null
  groupName: string | null
  kind: PaymentTransactionKindName
  amount: number
  receiptNo: string | null
  method: PaymentMethodName | null
  note: string | null
  actorId: number | null
  actorName: string | null
  createdAt: string
}

/** O'quvchining moliya hisobi: qarz, balans, oylar tarixi va oxirgi jurnal. */
export interface StudentAccountDto {
  studentId: number
  fullName: string
  /** Ochiq oylar bo'yicha jami qarz. */
  debt: number
  /** Ortiqcha to'langan va hali sarflanmagan pul. */
  balance: number
  exempt: boolean
  openMonths: number
  paid: number
  months: PaymentDto[] | null
  recentTransactions: PaymentTransactionDto[] | null
}

/** Bloklash darvozasining natijasi. */
export interface PaymentBlockDto {
  studentId: number
  blocked: boolean
  debt: number
  threshold: number
  configuredScope: PaymentBlockScopeName
  requestedScope: PaymentBlockScopeName
  exempt: boolean
  /** Global "qattiq rejim". `false` — qarz ko'rsatiladi, lekin hech kim bloklanmaydi. */
  enforced: boolean
  reason: string | null
}

export interface SetExemptRequest {
  exempt: boolean
  reason?: string | null
}

/** ★ `enforce` — FAQAT O'QISH uchun: u muhit xossasi (`Payments:EnforceBlock`). */
export interface FinanceSettingsDto {
  blockThreshold: number
  blockScope: PaymentBlockScopeName
  enforce: boolean
}

/** `PUT /payments/settings` tanasi. `enforce` bu yerda YO'Q — u o'zgartirilmaydi. */
export interface UpdateFinanceSettingsRequest {
  blockThreshold: number
  blockScope: PaymentBlockScopeName
}

export interface TariffDto {
  id: number
  name: string
  amount: number
  lessonsCount: number
  courseId: number | null
  courseName: string | null
  groupId: number | null
  groupName: string | null
  /** `DateOnly` — `YYYY-MM-DD`. */
  activeFrom: string
  isActive: boolean
  /** Aniqlik darajasi: qanchalik yuqori bo'lsa, tarif shunchalik "aniq". */
  specificity: number
  createdAt: string
  updatedAt: string | null
}

export interface CreateTariffRequest {
  name: string
  amount: number
  activeFrom: string
  lessonsCount: number
  courseId?: number | null
  groupId?: number | null
  isActive: boolean
}

/**
 * ★ `PUT /payments/tariffs/{id}` — TO'LIQ ALMASHTIRISH.
 *
 * Yuborilmagan maydon standart qiymat bilan YOZILADI (`courseId` yuborilmasa
 * tarif jimgina "barcha kurslar" ga aylanadi). Shuning uchun bu turda
 * ixtiyoriy maydon YO'Q: forma mavjud qiymatlarni yuklab, HAMMASINI qaytaradi.
 */
export interface UpdateTariffRequest {
  name: string
  amount: number
  activeFrom: string
  lessonsCount: number
  isActive: boolean
  courseId: number | null
  groupId: number | null
}

export interface StudentDiscountDto {
  id: number
  studentId: number
  studentName: string
  groupId: number | null
  groupName: string | null
  kind: DiscountKindName
  value: number
  validFrom: string
  validTo: string | null
  isActive: boolean
  reason: string | null
  specificity: number
  createdAt: string
  updatedAt: string | null
}

export interface CreateDiscountRequest {
  kind: DiscountKindName
  value: number
  validFrom: string
  validTo?: string | null
  groupId?: number | null
  reason?: string | null
  isActive: boolean
}

/** ★ `PUT` — TO'LIQ ALMASHTIRISH (izoh: `UpdateTariffRequest`). */
export interface UpdateDiscountRequest {
  kind: DiscountKindName
  value: number
  validFrom: string
  isActive: boolean
  validTo: string | null
  groupId: number | null
  reason: string | null
}

/* ==========================================================================
   MOLIYA DASHBOARD'I — `GET /payments/summary` (+ `/summary/export`).

   ★ UCH XIL MA'NO bitta javobda. Ularni chalkashtirish hisobotni YOLG'ON
   qiladi, shuning uchun tur darajasida ham ajratib izohlangan:

     • DAVR (jurnal) — `from..to` KUNLARI orasida KASSAGA tushgan pul:
       `collected`, `refunded`, `netCollected`, `balanceUsed`, `waived`,
       `payingStudents`, `paymentCount`.
     • HISOB (accrual) — `fromPeriod..toPeriod` OYLARIGA yozilgan summalar:
       `billed`, `discounts`, `periodCollected`, `collectionRate`, `groups[]`.
     • HOLAT — BUGUNGI kesim, davr filtriga UMUMAN bog'liq emas:
       `outstanding`, `studentBalance`, `debtorStudents`, `aging[]`.

   Kassir davrni o'zgartirganda qarz o'zgarmasligi BUG EMAS — shu sababli
   ekranda ham bu farq yozib qo'yilgan (`FinanceDashboard.vue`).

   ★ Pul maydonlarining BIRORTASI `null` EMAS — bo'sh bazada ham `0` keladi.
   Shuning uchun bu yerda `number | null` yo'q va UI'da "NaN" chiqmaydi.
   ========================================================================== */

/** Qarz yoshi guruhlari — server DOIM shu TO'RTTASINI shu tartibda yuboradi. */
export type PaymentAgingBucketName = '0-30' | '31-60' | '61-90' | '90+'

export interface PaymentSummaryKpiDto {
  /* --- DAVR: moliya jurnali (`from..to` kunlari) --- */
  collected: number
  refunded: number
  /** `collected − refunded`. */
  netCollected: number
  /** Oldindan to'langan puldan yopilgan summa (yangi pul EMAS). */
  balanceUsed: number
  waived: number

  /* --- HISOB: `fromPeriod..toPeriod` oylari --- */
  billed: number
  discounts: number
  /** Shu OYLARGA tegishli to'langan summa (pul boshqa oyda kelgan bo'lishi mumkin). */
  periodCollected: number
  /** 0..100 oralig'idagi FOIZ (ulush emas). */
  collectionRate: number

  /* --- HOLAT: bugungi kesim, davr filtriga bog'liq EMAS --- */
  /** `sum(aging[].amount)` bilan AYNAN teng (server kafolati). */
  outstanding: number
  /** O'quvchilarning oldindan to'langan qoldig'i. */
  studentBalance: number

  /* --- sanoqlar --- */
  payingStudents: number
  debtorStudents: number
  paymentCount: number
}

/** Bitta qarz yoshi guruhi. `maxDays: null` — yuqori chegara yo'q (`90+`). */
export interface PaymentAgingBucketDto {
  bucket: PaymentAgingBucketName
  minDays: number
  maxDays: number | null
  amount: number
  students: number
  months: number
}

/** "Oxirgi 12 oy" dinamikasining bitta oyi. */
export interface PaymentMonthSummaryDto {
  /** Hisob oyi, `YYYY-MM`. */
  period: string
  billed: number
  /** ★ SHU hisob oyiga tegishli to'langan summa — kunlik kassa raqami EMAS. */
  collected: number
  outstanding: number
  waived: number
  discounts: number
  collectionRate: number
  records: number
}

/** Guruh kesimi. Server QARZI KATTASIDAN tartiblab yuboradi. */
export interface PaymentGroupSummaryDto {
  groupId: number
  groupName: string
  billed: number
  collected: number
  outstanding: number
  waived: number
  collectionRate: number
  students: number
}

/** To'lov usuli kesimi. `method: null` — eski yozuvda usul ko'rsatilmagan. */
export interface PaymentMethodSummaryDto {
  method: PaymentMethodName | null
  /** Serverning tayyor yorlig'i: `Naqd` / `Karta` / `Ko'rsatilmagan`. */
  methodName: string
  amount: number
  count: number
  /** Umumiy summadagi ulush, 0..100. */
  share: number
}

export interface PaymentSummaryDto {
  /** So'ralgan oraliq, MAHALLIY sana (`YYYY-MM-DD`), IKKALASI HAM kiradi. */
  from: string
  to: string
  /** Hisob (accrual) oylari, `YYYY-MM`. */
  fromPeriod: string
  toPeriod: string
  /** HOLAT raqamlari qaysi paytga tegishli ekani. */
  asOf: string
  kpi: PaymentSummaryKpiDto
  /** DOIM 4 ta element. */
  aging: PaymentAgingBucketDto[]
  /** DOIM 12 ta element, ESKIDAN YANGIGA. */
  months: PaymentMonthSummaryDto[]
  groups: PaymentGroupSummaryDto[]
  methods: PaymentMethodSummaryDto[]
}

/* ==========================================================================
   TIZIM SOZLAMALARI (`/api/v1/settings`) — FAQAT `Admin`.

   Turlar SWAGGER MATNIDAN EMAS, jonli javobdan yozilgan
   (`GET /api/v1/settings`, `GET|PUT /settings/{key}`, `POST .../reset`):
   matnli shartnoma bilan haqiqiy JSON bir-biriga mos kelmasligi mumkin, bu
   esa `strictTemplates` ostida sahifa bo'sh chizilishiga olib borardi.
   ========================================================================== */

/**
 * `SettingKind` — maydon QANDAY chiziladi.
 *
 * ★ Bu FAQAT ko'rinish haqida: qiymatning O'ZI serverda ham, so'rovda ham
 * DOIM SATR (`"true"`, `"540000"`). `Toggle` uchun `true` mantiqiy qiymat
 * yuborsak, server uni `string` kutib 400 qaytarardi.
 */
export type SettingKindName = 'Text' | 'Number' | 'Money' | 'Toggle' | 'Choice' | 'Secret'

/**
 * Qiymat AYNI PAYTDA qayerdan kelayotgani.
 *
 *  • `Default`     — kodagi standart (baza va muhitda yozuv yo'q);
 *  • `Environment` — muhit o'zgaruvchisi/`appsettings` ustun keldi;
 *  • `Database`    — paneldan yozilgan qiymat.
 *
 * ★ "Standartga qaytarish" FAQAT `Database` da ma'noga ega: u bazadagi
 * yozuvni o'chiradi, natijada qiymat `Environment` yoki `Default` ga
 * TUSHADI (jonli tekshiruvda `reset` dan keyin `origin` `Environment` bo'ldi,
 * ya'ni "reset" == "standart qiymatni yozish" EMAS, "ustki qatlamni olib
 * tashlash"). Boshqa manbalarda server 400 bilan rad etadi.
 */
/**
 * Qiymat AMALDA qayerdan kelgani.
 *
 * 🔴 `EnvironmentOverride` (2026-08-13) — SHOSHILINCH ("break-glass")
 * rejim: bazadagi qiymat BOR, lekin muhit o'zgaruvchisi uni ustidan
 * yozgan. Faqat `telegram.bot_token` va `telegram.webhook_secret` da
 * uchraydi — email va parol bilan kirish olib tashlangach, ular buzilsa
 * tizim o'zini o'zi qulflab qo'yardi (tokenni tuzatadigan panel ham
 * kirish ortida qolardi).
 *
 * ★ `Environment` DAN AJRATILGAN: u — "baza hali to'ldirilmagan" (normal
 * holat), bu esa "bazadagi qiymat ATAYLAB chetlab o'tilyapti" (avariya).
 * Panel ikkalasini bir xil ko'rsatsa, operator tizim shoshilinch rejimda
 * turganini bilmay, o'zgaruvchini olib tashlashni unutardi.
 */
export type SettingOriginName =
  | 'Default'
  | 'Environment'
  | 'Database'
  | 'EnvironmentOverride'

/** Qo'shimcha shakl tekshiruvi (serverda bajariladi, UI faqat ishorasini beradi). */
export type SettingFormatName = 'None' | 'Url' | 'TimeZone'

/** `SettingGroup` — sozlamalar bo'limi. */
export type SettingGroupKey =
  | 'General'
  | 'Finance'
  | 'Telegram'
  | 'LiveKit'
  | 'Storage'
  | 'Security'

/**
 * Chegaralar SERVER manbasi.
 *
 * ★ UI ularni KO'CHIRMAYDI: min/maks va tanlov ro'yxati shu yerdan olinadi.
 * Kodda takrorlansa, server chegarasi o'zgarganda forma eskisiga qarab
 * ishlayverar va foydalanuvchi tushunarsiz 400 olardi.
 */
export interface SettingConstraintsDto {
  /** `kind: 'Choice'` da to'ldiriladi; qolganlarida bo'sh massiv (null EMAS). */
  choices: string[]
  minimum: number | null
  maximum: number | null
  maxLength: number
  format: SettingFormatName
}

/**
 * Bitta sozlama.
 *
 * ★★ SIR (`isSecret: true`): server `value` VA `defaultValue` ni DOIM `null`
 * qilib yuboradi — faqat `maskedValue` (`••••••••cret`) va `isSet` keladi.
 * Ya'ni mijozda sirning o'zi UMUMAN bo'lmaydi va "ko'rsatish" tugmasi
 * texnik jihatdan ham imkonsiz: ko'rsatadigan narsa yo'q.
 */
export interface SettingDto {
  key: string
  group: SettingGroupKey
  groupName: string
  name: string
  description: string
  kind: SettingKindName
  isSecret: boolean
  isEditable: boolean
  /**
   * `isEditable: false` bo'lganda NEGA tahrirlanmasligi — foydalanuvchi uchun
   * yozilgan matn (masalan JWT siri nega faqat muhitda qolishi). UI uni
   * YASHIRMAYDI: aks holda o'chirilgan maydon sababsiz "buzuq" ko'rinardi.
   */
  readOnlyReason: string | null
  origin: SettingOriginName
  /** Qiymat umuman o'rnatilganmi (sirlarda `value` yo'q, faqat shu bilinadi). */
  isSet: boolean
  value: string | null
  maskedValue: string | null
  defaultValue: string | null
  constraints: SettingConstraintsDto
  /** ISO-8601. Faqat `origin: 'Database'` da to'ladi. */
  updatedAt: string | null
  updatedById: number | null
}

export interface SettingGroupDto {
  group: SettingGroupKey
  /** Serverning tayyor o'zbekcha nomi ("Moliya", "Ombor (fayllar)"). */
  name: string
  description: string
  items: SettingDto[]
}

/** `GET /api/v1/settings` javobi. */
export interface SettingsPageDto {
  groups: SettingGroupDto[]
}

/** `PUT /api/v1/settings/{key}` tanasi — qiymat DOIM satr. */
export interface UpdateSettingRequest {
  value: string
}

/* ==========================================================================
   GURUH CHATI (`/api/v1/group-chat`) — har guruhning DOIMIY suhbati.

   ★ IKKI KANAL — bu modulning eng muhim qoidasi (eski `chat_messages.channel`
   ustunining v2 dagi ko'rinishi). O'quvchi ustozga va kuratorga ALOHIDA
   yozadi: ustoz kurator oqimini KO'RMAYDI va aksincha. Server izolyatsiyani
   qat'iy ta'minlaydi — ruxsat etilmagan kanal so'ralsa jimgina almashtirmaydi,
   403 qaytaradi (jonli tekshirildi, matni: "Bu kanalga ruxsatingiz yo'q:
   ustoz o'quvchining kuratorga atalgan savollarini ko'rmaydi (va aksincha).").

   Shuning uchun UI hech qachon kanalni "taxmin qilmaydi": qaysi kanallar
   ochiqligini FAQAT server aytadi (`availableChannels`).
   ========================================================================== */

/** Serverda enum, simda SATR. Eski ilovadagi `"teacher"` / `"assistant"`. */
export type GroupChatChannelName = 'Teacher' | 'Curator'

/**
 * Bitta guruh chati xabari.
 *
 * ★ `mine` MAYDONI YO'Q — ataylab. Obyekt SignalR xonasidagi hammaga BITTA
 * nusxada yuboriladi, ya'ni server uni har bir qabul qiluvchi uchun alohida
 * bo'yay olmaydi. "Bu mening xabarimmi" savoliga klient `senderId` ni joriy
 * foydalanuvchi id'siga solishtirib javob beradi.
 */
export interface GroupChatMessageDto {
  id: number
  groupId: number
  channel: GroupChatChannelName
  senderId: number
  senderName: string
  senderRole: UserRoleName
  /**
   * ⚠️ R16b DAN KEYIN BO'SH SATR BO'LISHI MUMKIN — izohsiz surat
   * (Telegram'dagi kabi). `null` HECH QACHON kelmaydi: server ustunni
   * NOT NULL saqlaydi va bo'sh matnni faqat BIRIKTIRMASI BOR xabarda
   * ruxsat etadi.
   */
  body: string
  /** ISO-8601. */
  sentAt: string
  /**
   * R16b · biriktirilgan fayllar. Biriktirmasiz xabarda BO'SH massiv.
   *
   * ⚠️ `| null` — hub orqali kelgan eski shakldan himoya: realtime payload
   * `useGroupChatHub` da maydonma-maydon tekshiriladi va noma'lum shakl
   * rad etiladi, lekin tur darajasida ham "bo'lmasligi mumkin" deb
   * belgilanadi (loyihadagi barcha massiv maydonlaridagi AYNI kelishuv).
   */
  attachments: GroupChatAttachmentDto[] | null
}

/**
 * R16b · chat xabariga biriktirilgan BITTA fayl.
 *
 * 🔴 `objectKey` YO'Q va bo'lmaydi: baytlar
 * `GET /api/v1/group-chat/attachments/{id}` orqali, oqimni O'QISH ruxsatidan
 * qaytadan o'tib olinadi.
 */
export interface GroupChatAttachmentDto {
  id: number
  kind: AttachmentKindName
  contentType: string
  /** Ko'rsatiladigan nom (tozalangan). Hujjat uchun MUHIM. */
  fileName: string | null
  sizeBytes: number
  durationSec: number | null
}

/** `GET /groups/{id}/messages` javobi. */
export interface GroupChatPageDto {
  groupId: number
  groupName: string
  /** Server AYNAN qaysi kanalni berdi (so'ralmagan bo'lsa — birinchi ruxsatlisi). */
  channel: GroupChatChannelName
  /** UI tab'lari uchun: shu foydalanuvchiga shu guruhda ochiq kanallar. */
  availableChannels: GroupChatChannelName[]
  /** ★ ESKIDAN YANGIGA tartiblangan (chatda shundayligicha chiziladi). */
  items: GroupChatMessageDto[] | null
  hasMore: boolean
  /** Yuqoriga scroll qilganda keyingi sahifa uchun `?beforeId=`. */
  nextBeforeId: number | null
  unreadCount: number
}

/**
 * `GET /threads` elementi — "Chatlar" ro'yxatining bitta qatori.
 *
 * ★ Element (guruh, KANAL) juftligiga to'g'ri keladi, guruhga emas: o'quvchi
 * ikki kanalga ham yozadigan bo'lsa, bitta guruh ro'yxatda IKKI QATOR bo'lib
 * ko'rinadi (jonli tekshirildi). Eski o'quvchi ilovasi ham aynan shunday
 * chizardi (`student.html` — har guruh uchun "Ustoz chati" va "Kurator chati").
 */
export interface GroupChatThreadDto {
  groupId: number
  groupName: string
  channel: GroupChatChannelName
  lastMessageId: number | null
  lastMessagePreview: string | null
  lastMessageSenderName: string | null
  lastMessageAt: string | null
  unreadCount: number
  /* ===== R38 · CHAT FILTRI UCHUN QO'SHIMCHA USTUNLAR ===== */
  /**
   * GURUH turi.
   *
   * ⚠️ `channel` BILAN ARALASHTIRILMASIN — u SUHBATDOSHNI bildiradi
   * ("Ustoz chati" / "Kurator chati") va guruh turiga umuman bog'liq emas.
   *
   * ⚠️ HECH QACHON `'Curator'` BO'LMAYDI: kurator TURIDAGI guruhning
   * alohida chati yo'q va u bu ro'yxatga umuman tushmaydi (server qoidasi,
   * to'rt joyda). Shuning uchun filtr tanlagichida ham faqat `Group` va
   * `Individual` bo'ladi.
   */
  groupType: GroupTypeName
  /** O'quv yo'nalishi (R21b). `null` — yorliqsiz guruh. */
  categoryId: number | null
  /** Kategoriya nomi. `categoryId` `null` bo'lsa bu ham `null`. */
  categoryName: string | null
  /* ===== /R38 ===== */
}

/** Hub'dagi `JoinThread` javobi. ★ OBYEKT, massiv EMAS (jonli tekshirildi). */
export interface GroupChatAccessDto {
  groupId: number
  groupName: string
  channel: GroupChatChannelName
  availableChannels: GroupChatChannelName[]
}

/** `POST /groups/{id}/read` javobi. */
export interface GroupChatReadResultDto {
  groupId: number
  channel: GroupChatChannelName
  lastReadMessageId: number | null
  unreadCount: number
  /** `false` — o'qilgan chegara allaqachon shu yerda edi (takroriy so'rov). */
  changed: boolean
}

/** `POST /groups/{id}/messages` tanasi. `channel` berilmasa server o'zi tanlaydi. */
export interface SendGroupChatMessageRequest {
  channel?: GroupChatChannelName
  body: string
}

/** `POST /groups/{id}/read` tanasi. `upToMessageId` berilmasa — oxirigacha. */
export interface MarkGroupChatReadRequest {
  channel?: GroupChatChannelName
  upToMessageId?: number
}

/* ==========================================================================
   DARS YOZUVLARI (`Recordings` tegi).

   ★ SHAKL JONLI API'DAN OLINGAN (swagger + `curl`), matnli shartnomadan EMAS.
   Tekshirilgan chaqiruvlar: `GET /api/v1/recordings?from&to`,
   `GET /api/v1/live-sessions/{id}/recordings`, `GET /api/v1/recordings/{id}/link`.
   ========================================================================== */

/**
 * Yozuv holati. Serverdagi `RecordingStatus` enum'i JSON'da SATR bo'lib keladi.
 *
 * ★ QIYMATLAR JONLI TEKSHIRILGAN: bazadagi `Status` ustuni 0..4 ni oladi va
 * DTO mos ravishda `Requested`/`Starting`/`Active`/`Completed`/`Failed`
 * qaytaradi. Noma'lum qiymat kelsa (backend yangi holat qo'shsa) DTO shunchaki
 * raqamni satr sifatida beradi — shuning uchun `lookup()` bilan o'qiladi va
 * hech qachon `undefined` label chiqmaydi.
 */
export type RecordingStatusName = 'Requested' | 'Starting' | 'Active' | 'Completed' | 'Failed'

/** `RecordingDto` — swagger sxemasi bilan bir xil, nullable maydonlar `| null`. */
export interface RecordingDto {
  id: number
  sessionId: number
  /** Noma'lum holat ham kelishi mumkin, shuning uchun `string` (yuqoridagi izoh). */
  status: string | null
  /** FAQAT `Completed` da `true` — jonli tekshirilgan. Ko'rish tugmasi shunga bog'liq. */
  isPlayable: boolean
  startedAt: string | null
  endedAt: string | null
  durationSeconds: number | null
  sizeBytes: number | null
  /** Yozuvni boshlashga necha marta urinilgan (xato bo'lsa qayta urinadi). */
  attempts: number
  /** Oxirgi xato matni (masalan egress rad etgani). `null` — xato yo'q. */
  error: string | null
  createdAt: string
  /**
   * SHU yozuvning ko'rinish kaliti (R5) — XODIM interfeysidagi tugma
   * holati.
   *
   * ⚠️ "O'quvchi buni ko'radi" DEGANI EMAS: amaldagi ko'rinish uchta
   * kalitning ko'paytmasi (global sozlama × guruh × shu bayroq).
   * O'quvchiga kelgan ro'yxatda u har doim `true` bo'ladi — ko'rinmaydigan
   * yozuv ro'yxatga umuman tushmaydi.
   */
  isVisibleToStudents: boolean
  /**
   * Bu DARSDA o'quv bo'limining sifat tahlili bormi (R29).
   *
   * 🔴 O'QUVCHIGA HAR DOIM `false`: tahlil undan yopiq va uning BORLIGI
   * haqidagi ishora ham berilmaydi (server tomonda kesiladi).
   */
  hasReview: boolean
  /** Tahlil xulosasi yoki `null` — tahlil yo'q. */
  reviewStatus: SessionReviewVerdictName | null
}

/**
 * `GET /api/v1/recordings/section` javobi (R5).
 *
 * ★ NEGA ALOHIDA ENDPOINT: o'quvchining "O'quv" ekranida yozuvlar bo'limiga
 * KIRISH KARTOCHKASI turadi. Bo'lim yopilganda kartochka qolsa, o'quvchi
 * uni bosib abadiy bo'sh sahifaga tushardi. Ro'yxatning O'ZI bu savolga
 * javob bera olmaydi: bo'sh ro'yxat "yopilgan" ni ham, "hali yozuv yo'q" ni
 * ham bildiradi va bu ikki holat foydalanuvchi uchun butunlay boshqacha.
 */
export interface RecordingSectionDto {
  visible: boolean
}

/** `GET /api/v1/recordings` qaytaradigan qator: yozuv + qaysi dars/guruh. */
export interface RecordingListItemDto {
  recording: RecordingDto
  groupId: number
  groupName: string | null
  title: string | null
  /** `DateOnly` — `YYYY-MM-DD` (vaqt zonasiz). */
  localDate: string
  scheduledStart: string
}

/**
 * `GET /api/v1/recordings/{id}/link` javobi.
 *
 * ★ BU PRESIGNED (imzolangan) S3 MANZIL, API orqali oqim EMAS — jonli
 * tekshirilgan: javobda `X-Amz-Signature` va `X-Amz-Expires=900` bor.
 * Ya'ni manzil `Authorization` sarlavhasini TALAB QILMAYDI va uni to'g'ridan
 * to'g'ri `<video src>` ga berish mumkin; lekin u MUDDATLI — `expiresAt`
 * o'tgach 403 bo'lib qoladi va qaytadan so'rash kerak.
 */
export interface RecordingLinkDto {
  url: string | null
  expiresAt: string
}

/**
 * `GET /api/v1/live-sessions/{id}/recording-status` javobi.
 *
 * ══════════════════════════════════════════════════════════════════════
 * 🔴 ROZILIK INDIKATORI UCHUN — VA U O'QUVCHIGA HAM OChIQ
 * ══════════════════════════════════════════════════════════════════════
 *
 * 2026-08-13 dan dars yozuvi AVTOMATIK boshlanadi (guruhning
 * `recordEnabled` kaliti bo'yicha). Shu bilan birga jonli xonadagi HAR
 * BIR ishtirokchi — o'quvchi ham — yozib olinayotganini ko'rishi SHART.
 * Bu endpoint aynan shu uchun qo'shildi va u YAGONA yozuv endpointi ki,
 * unda rol darvozasi yo'q.
 *
 * ★ NIMA UCHUN `GET .../recordings` DAN FOYDALANILMAYDI: o'sha ro'yxat
 * o'quvchiga FAQAT `Completed` yozuvlarni beradi (server tomonda
 * filtrlanadi), ya'ni KETAYOTGAN yozuv unga umuman ko'rinmaydi —
 * indikator hech qachon yonmasdi.
 */
export interface RecordingLiveStatusDto {
  /**
   * Yakunlanmagan yozuv bormi (`Requested`, `Starting` yoki `Active`).
   *
   * ⚠️ `Active` DAN KENGROQ va bu ATAYLAB: "yozilmayapti" deb yolg'on
   * aytish roziligni buzardi, "yozilmoqda" deb ortiqcha ogohlantirish
   * esa zararsiz. Shubha "ha" foydasiga hal qilinadi (backenddagi
   * `IRecordingService.GetLiveStatusAsync` izohi).
   */
  isRecording: boolean
  /**
   * Yozuv HAQIQATAN boshlangan payt. `null` — hali navbatda (bu holatda
   * ham `isRecording` `true` bo'ladi). Faqat izoh matni uchun.
   */
  startedAt: string | null
}

/* ===== WAVE 2 · FOYDALANUVCHI (wave2/users) =====

   O'quvchi profili drawer'i (`GET /users/{id}/profile`), Telegram
   bog'lanishini uzish va ichki izohlar CRUD'i.

   ★ SHAKL BACKEND RECORD'LARIDAN AYNAN ko'chirilgan
   (`Application/Users/Dtos/UserProfileDtos.cs`,
   `Application/StudentNotes/Dtos/StudentNoteDtos.cs`,
   `Application/Users/Dtos/UserDtos.cs`). C# `long` -> `number`,
   `DateTimeOffset` -> ISO satr, `DateOnly` -> `YYYY-MM-DD`, enum -> SATR.

   🔴 NULL'LARNING MA'NOSI RUXSATGA BOG'LIQ va serverda KESILADI:
     • `finance === null`   -> so'rovchi USTOZ/KURATOR (moliya javobda YO'Q);
     • `notes === null`     -> so'rovchi o'quvchining O'ZI (ichki eslatma);
     • `finance.transactions === null` -> yana o'quvchining o'zi.
   Ya'ni bu maydonlarni frontendda "yashirish" emas, YO'QLIGINI hurmat qilish
   kerak — bo'lim UMUMAN render qilinmaydi.
   ========================================================================== */

/** Profil drawer'ining butun mazmuni — BITTA so'rovda (7 ta emas). */
export interface UserProfileDto {
  /** ★ Ro'yxatdagi bilan AYNI tur — ikkinchi "profil foydalanuvchisi" shakli YO'Q. */
  user: UserDetailsDto
  telegram: ProfileTelegramDto
  groups: ProfileGroupDto[]
  /** 🔴 `null` — ustoz/kurator so'ragan (bo'lim render QILINMAYDI). */
  finance: ProfileFinanceDto | null
  study: ProfileStudyDto
  /** 🔴 `null` — o'quvchining o'zi so'ragan (bo'lim render QILINMAYDI). */
  notes: StudentNoteDto[] | null
}

/** Telegram ulanish holati + OXIRGI uzishning izi. */
export interface ProfileTelegramDto {
  /**
   * Bazada bog'lanish BORMI. ★ Ustoz uchun ham HAQIQIY qiymat keladi:
   * bu HOLAT ("o'quvchi kira oladimi"), kontakt emas.
   */
  linked: boolean
  /**
   * 🔴 Ustozga DOIM `null` (R27). `linked === true && telegramId === null`
   * degani "bog'langan, ammo sizga ko'rsatilmaydi" — "bog'lanmagan" EMAS.
   */
  telegramId: number | null
  /**
   * `@` BELGISIZ (`UserDetailsDto.telegramUsername` dagi ogohlantirish o'sha).
   * 🔴 Ustozga DOIM `null`: `t.me/<username>` — to'g'ridan-to'g'ri bog'lanish
   * kanali, ya'ni KONTAKT.
   */
  username: string | null
  linkedAt: string | null
  /**
   * Oxirgi uzish izi. Uchalasi ham `Student` rolida DOIM `null`: "sizni
   * Aziz Karimov uzgan" degan matn ichki ish tartibini oshkor qilardi.
   * Bog'lanish HOZIR mavjud bo'lsa ham to'lishi mumkin ("uzilgan, keyin
   * qaytadan bog'langan" tarixi).
   */
  unlinkedAt: string | null
  unlinkedByName: string | null
  unlinkReason: string | null
}

/** O'quvchining bitta guruhdagi a'zoligi (hamma holat bilan). */
export interface ProfileGroupDto {
  groupId: number
  groupName: string
  teacherName: string | null
  status: MemberStatusName
  joinedAt: string
  /**
   * ⚠️ TAXMINIY: a'zolik qatorining `updatedAt` qiymati va faqat
   * `Stopped`/`Moved` holatida keladi. "Qachon chiqdi" ustuni modelda YO'Q,
   * `updatedAt` esa pauza/tiklashda ham yangilanadi — shuning uchun UI'da
   * "chiqqan sana" deb DA'VO QILINMAYDI, "oxirgi o'zgarish" deb yoziladi.
   */
  leftAt: string | null
  /**
   * ⚠️ HOZIR DOIM `null` — `GroupMember` ko'chirish havolasini SAQLAMAYDI
   * (`MovedToGroupId` ustuni yo'q, alohida vazifada qo'shiladi). Shu sababli
   * "→ qayerga" chipi FAQAT `movedToGroupId !== null` shartida chiziladi.
   */
  movedToGroupId: number | null
  movedToGroupName: string | null
  /** `YYYY-MM-DD`, faqat `Paused` holatida. */
  pausedUntil: string | null
}

/** O'quvchining moliya kesimi. */
export interface ProfileFinanceDto {
  /** Ortiqcha to'langan va hali sarflanmagan pul. */
  balance: number
  totalPaid: number
  /**
   * Ochiq oylarning QOLGAN qismi (`amount − paidAmount`) yig'indisi.
   * Formula moliya moduli bilan AYNI: qisman to'langan oy to'liq qarz
   * deb sanalmaydi, kechirilgan oy esa umuman qarz emas.
   */
  totalDue: number
  /** AMALDAGI bloklash qamrovi (sozlamadagi emas) — `None` = bloklanmagan. */
  blockScope: PaymentBlockScopeName
  periods: ProfilePeriodDto[]
  /** 🔴 `null` — o'quvchining o'zi so'ragan. Aks holda OXIRGI 50 ta. */
  transactions: PaymentTransactionDto[] | null
  /** 50 tadan ko'p yozuv bormi — "Hammasini ko'rish" tugmasi shunga bog'liq. */
  hasMoreTransactions: boolean
}

/** Bitta hisob oyi (o'quvchi × guruh × oy). */
export interface ProfilePeriodDto {
  /** Hisob oyi, `YYYY-MM`. */
  month: string
  groupId: number
  groupName: string
  amount: number
  paidAmount: number
  outstanding: number
  status: PaymentStatusName
  /**
   * SHU oyda SHU guruhda O'TKAZILGAN darslar soni.
   * To'lov modeli OYLIK — "qaysi dars uchun" kesimi modelda yo'q; xodim
   * "540 000 so'm / 8 dars" deb tushuntira olishi uchun shu son beriladi.
   */
  sessionCount: number
}

/** O'quv natijalari: uy vazifalari, testlar, davomat. */
export interface ProfileStudyDto {
  assignments: ProfileAssignmentDto[]
  /** 50 tadan ko'p javob bormi (to'liq ro'yxat uchun alohida endpoint kerak). */
  hasMoreAssignments: boolean
  tests: ProfileTestDto[]
  hasMoreTests: boolean
  attendance: ProfileAttendanceDto
}

/** Uy vazifasiga topshirilgan javob va bahosi. */
export interface ProfileAssignmentDto {
  submissionId: number
  assignmentId: number
  title: string
  /** Guruh vazifasi bo'lsa guruh nomi, KURS vazifasida `null`. */
  groupName: string | null
  /** Kurs vazifasi bo'lsa dars nomi, aks holda `null`. */
  lessonName: string | null
  score: number | null
  maxScore: number
  status: SubmissionStatusName
  submittedAt: string
  isLate: boolean
  /** 🔴 Faqat SON: havola ham, `objectKey` ham ATAYLAB yo'q (16-tuzoq). */
  fileCount: number
}

/** Test urinishi natijasi. */
export interface ProfileTestDto {
  attemptId: number
  testId: number
  title: string
  kind: TestKindName
  /**
   * Olingan BALL (to'g'ri javoblar soni EMAS): har savolning o'z `points` i
   * bor, shuning uchun "N/M to'g'ri" deb yozish MUMKIN EMAS.
   */
  score: number | null
  maxScore: number | null
  /** Foiz (0..100), bir xona aniqlikda. */
  scorePercent: number | null
  closedByTimeout: boolean
  /** Tugatilmagan urinishda `null`. */
  finishedAt: string | null
}

/**
 * Davomat: maxraj — FAOL guruhlardagi YAKUNLANGAN darslar, "kelgan" esa
 * `Absent` dan boshqa har qanday holat (kechikkan ham kelgan hisoblanadi).
 * Formula platformadagi bilan AYNI — ikkinchisi yozilsa profil va o'quvchi
 * ilovasi turli foiz ko'rsatardi.
 */
export interface ProfileAttendanceDto {
  total: number
  present: number
  missed: number
  percent: number
}

/**
 * Xodimning o'quvchi haqidagi ICHKI izohi.
 *
 * 🔴 O'QUVCHIGA HECH QACHON KO'RSATILMAYDI: `Student` roli izohlar
 * endpointidan 403 oladi va agregatda `notes` bloki `null` bo'ladi.
 */
export interface StudentNoteDto {
  id: number
  studentId: number
  body: string
  authorId: number
  authorName: string
  groupId: number | null
  groupName: string | null
  createdAt: string
  updatedAt: string | null
  /**
   * So'rovchi shu izohni tahrirlay/o'chira oladimi.
   * ★ FAQAT KO'RINISH uchun — server har `PUT`/`DELETE` da qaytadan tekshiradi.
   */
  canEdit: boolean
}

/** `POST /users/{id}/notes` tanasi. Bo'sh yoki 2000+ matn -> 409. */
export interface CreateStudentNoteRequest {
  body: string
  /**
   * Ixtiyoriy kontekst: "qaysi guruhdagi xatti-harakati haqida".
   * Begona guruh -> 400 (`problem.errors.groupId[0]`).
   */
  groupId?: number | null
}

/** `PUT /users/{id}/notes/{noteId}` tanasi — faqat MATN o'zgaradi. */
export interface UpdateStudentNoteRequest {
  body: string
}

/** `POST /users/{id}/telegram/unlink` tanasi — butunlay ixtiyoriy. */
export interface TelegramUnlinkRequest {
  /** Audit iziga yoziladi (maks 500 belgi; server ortig'ini qirqadi). */
  reason?: string | null
}

/**
 * Uzishdan keyingi holat. Ikkala maydon ham DOIM `null` — shakl profil
 * javobidagi `telegram` bloki bilan bir xil bo'lsin.
 */
export interface TelegramUnlinkResponse {
  telegramId: number | null
  telegramUsername: string | null
}

/* ===== /WAVE 2 · FOYDALANUVCHI ===== */

/* ===== WAVE 2 · KURS/DARS (wave2/course) ===== */

/**
 * `LessonKind` enum — dars turi (JSON'da SATR).
 *
 * 🔴 DOMAIN INVARIANTI: `Normal` darsda faqat `Video`, `Exam` darsda faqat
 * `Image` asset bo'ladi. Turni almashtirishda mos kelmagan fayl BOR bo'lsa
 * server **409** qaytaradi va jimgina O'CHIRMAYDI (bir soatlik video shunday
 * yo'qolmasligi kerak) — UI 409 matnini ko'rsatib, avval fayllarni o'chirishga
 * yo'naltiradi.
 */
export type LessonKindName = 'Normal' | 'Exam'

/** `LessonAssetKind` enum — dars mediasining turi (JSON'da SATR). */
export type LessonAssetKindName = 'Video' | 'Image'

/**
 * Darsga biriktirilgan bitta media: video QISMI yoki imtihon rasmi.
 *
 * 🔴 `objectKey` BU YERDA YO'Q va qo'shilmaydi (`DAVOM_ETTIRISH.md`
 * 6-bo'lim, 16-tuzoq) — ombor kaliti ichki joylashuv ma'lumoti. Fayl DOIM
 * `GET /api/v1/lessons/assets/{assetId}` orqali, har so'rovda tekshiriladigan
 * ruxsat bilan o'qiladi.
 */
export interface LessonAssetDto {
  id: number
  lessonId: number
  kind: LessonAssetKindName
  /** 0 dan boshlanadigan zich tartib — `reorder` shu qiymatni qayta yozadi. */
  position: number
  /** Ko'rinadigan nom ("1-qism", "Nazariya"). `null` — UI tartibdan nom yasaydi. */
  title: string | null
  contentType: string
  sizeBytes: number
  /**
   * ⚠️ Davomiylik KLIENTDAN keladi (serverda media dekoder yo'q) — FAQAT
   * ko'rsatish uchun, unga hech qanday qaror bog'lanmaydi (13-bo'lim, 47-tuzoq).
   */
  durationSec: number | null
  width: number | null
  height: number | null
  createdAt: string
}

/**
 * `GET /api/v1/lessons/assets/{assetId}/ticket` javobi — `<video src>` ga
 * qo'yiladigan qisqa muddatli chipta (`?ticket=` so'rov parametri).
 * `Authorization` sarlavhasi bilan EMAS: brauzerning `<video>` elementi uni
 * yubora olmaydi.
 */
export interface MediaAccessTicketDto {
  token: string
  expiresAt: string
}

/**
 * `AttachmentKind` enum — biriktirilgan faylning turi.
 *
 * ★ BITTA TUR, UCH ISHLATUVCHI: vazifa sharti, chat biriktirmasi (R16b) va
 * ustozning tekshiruv fayli (R37). Server uchalasida ham AYNI `AttachmentKind`
 * enum'ini yuboradi — uchta bir xil union e'lon qilish ulardan biri
 * yangilanmay qolishiga olib kelardi.
 */
export type AttachmentKindName = 'Image' | 'Audio' | 'Document'

/**
 * @deprecated Yangi kodda `AttachmentKindName` ishlatilsin — nomi turkumni
 * vazifaga bog'lab qo'yadi, holbuki u umumiy. Alias mavjud importlar
 * buzilmasin uchun saqlangan.
 */
export type AssignmentAttachmentKindName = AttachmentKindName

/**
 * Vazifa SHARTIGA biriktirilgan bitta fayl.
 *
 * 🔴 `objectKey` YO'Q. Fayl `GET /api/v1/assignments/attachments/{id}` orqali
 * o'qiladi.
 *
 * ⚠️ `kind` MAZMUNDAN aniqlanadi, kengaytmadan emas: `ftyp` konteyneri
 * (mp4/m4a) shu yo'lda AUDIO deb qabul qilinadi (13-bo'lim, 46-tuzoq).
 */
export interface AssignmentAttachmentDto {
  id: number
  assignmentId: number
  kind: AssignmentAttachmentKindName
  position: number
  contentType: string
  sizeBytes: number
  /** ⚠️ Klientdan keladi — faqat ko'rsatish uchun. */
  durationSec: number | null
  createdAt: string
}

/**
 * `POST /api/v1/lessons/{lessonId}/assets` ning `multipart` maydonlari
 * (`file` dan tashqari hammasi ixtiyoriy).
 *
 * ⚠️ `kind` YUBORILMAYDI — u DARS TURIDAN kelib chiqadi. Klientdan qabul
 * qilinsa invariantni buzadigan yozuv yasash mumkin bo'lardi.
 */
export interface LessonAssetUploadFields {
  /** Video qismining nomi ("1-qism"). */
  title?: string | null
  durationSec?: number | null
  width?: number | null
  height?: number | null
}

/* ===== /WAVE 2 · KURS/DARS ===== */

/* ===== R35/R36 · BILDIRISHNOMA ==========================================

   ★ NEGA ALOHIDA BLOK OXIRDA (yuqoridagi WAVE 2 bloklari bilan AYNI
   sabab): bu faylga bir necha tarmoq ayni vaqtda qo'shadi va alifbo
   bo'yicha aralashtirilgan qatorlar merge paytida to'qnashuv beradi.  */

/** `NotificationKind` enum — hodisa turi (ikonka va o'tish yo'li shundan). */
export type NotificationKindName = 'SubmissionGraded'

/**
 * Qo'ng'iroqchadagi bitta qator.
 *
 * 🔴 `body` — SOF MATN, HTML EMAS. Uni `v-html` bilan chizish TAQIQLANADI:
 * ichida ustozning izohi bor, ya'ni foydalanuvchi yozgan matn. Telegram
 * yo'lidagi xabar ekranlangan HTML, LEKIN u umuman boshqa jadvalda
 * (`MessageOutbox`) va bu yerga hech qachon tushmaydi.
 */
export interface NotificationDto {
  id: number
  kind: NotificationKindName
  title: string
  body: string
  /**
   * Bosilganda qayerga o'tish. Ma'nosi `kind` ga bog'liq:
   * `SubmissionGraded` uchun — javob (`submission`) Id'si.
   */
  entityId: number | null
  read: boolean
  createdAt: string
}

/**
 * Bildirishnomalar sahifasi — KURSORLI sahifalash (`MessagePageDto` bilan
 * AYNI shakl, ataylab).
 */
export interface NotificationPageDto {
  /** YANGIDAN ESKIGA tartibda. */
  items: NotificationDto[]
  hasMore: boolean
  /** Keyingi sahifa uchun `?beforeId=`. `hasMore=false` bo'lsa `null`. */
  nextBeforeId: number | null
  /** ★ UMUMIY o'qilmaganlar soni — sahifadagi emas. */
  unreadCount: number
}

export interface NotificationUnreadDto {
  unreadCount: number
}

/** `markedCount` — takroriy so'rovda `0` (idempotent). */
export interface NotificationReadResultDto {
  markedCount: number
  unreadCount: number
}

/** `ids` berilmasa — BARCHA o'qilmaganlar. */
export interface MarkNotificationsReadRequest {
  ids?: number[]
}

/** `deletedCount` — allaqachon o'chgan Id qayta yuborilsa `0` (idempotent). */
export interface NotificationDeleteResultDto {
  deletedCount: number
  /**
   * ★ Amaldan KEYINGI o'qilmaganlar soni. O'CHIRISH UNI HAM KAMAYTIRADI:
   * o'qilmagan qator o'chsa u hech qachon o'qilmaydi, nishonda qolsa
   * foydalanuvchi ochib bo'lmaydigan raqamni ko'rib turardi.
   */
  unreadCount: number
}

/**
 * 🔴 `MarkNotificationsReadRequest` DAN FARQI: bu yerda `ids` MAJBURIY va
 * BO'SH BO'LMASLIGI kerak — bo'sh ro'yxat "hammasini o'chir" DEGANI EMAS,
 * server 400 qaytaradi. Sabab: noto'g'ri "hammasini o'qildi" bir bosishda
 * qaytariladi, noto'g'ri "hammasini o'chir" esa qaytarilmaydi.
 */
export interface DeleteNotificationsRequest {
  /** 1..50 ta. */
  ids: number[]
}

/* ===== /R35/R36 · BILDIRISHNOMA ===== */

/* ===== 2026-08-15 · O'Z PROFILINI TAHRIRLASH =============================

   ★ NEGA ALOHIDA BLOK OXIRDA: yuqoridagi bloklar bilan AYNI sabab — bu
   faylga bir necha tarmoq ayni vaqtda qo'shadi.                        */

/** `PUT /api/v1/profile` — faqat ism (rol/email XODIM ishi). */
export interface UpdateProfileRequest {
  fullName: string
}

/** `POST /api/v1/profile/phone` — almashtirishning 1-bosqichi. */
export interface ChangePhoneRequest {
  /** Xom ko'rinish ham bo'ladi — normalizatsiya SERVERDA. */
  phone: string
}

/** `POST /api/v1/profile/phone/confirm` — Telegramga kelgan kod. */
export interface ConfirmPhoneRequest {
  code: string
}

/**
 * Kutayotgan telefon almashtirish holati.
 *
 * ★ EKRAN `codeSent` GA QARAB IKKIGA BO'LINADI: `false` — "botga
 * raqamni ulashing" ko'rsatmasi, `true` — kod kiritish maydoni.
 */
export interface PhoneChangeStatusDto {
  /** Kutayotgan YANGI raqam (E.164, formatlanmagan). */
  phone: string
  codeSent: boolean
  /** Bot `@username` i — sozlanmagan bo'lsa `null` (havolasiz matn). */
  botUsername: string | null
  expiresInSeconds: number
}

/** `POST /api/v1/profile/avatar` javobi. */
export interface AvatarUploadedDto {
  /** Kesh buzish uchun vaqt tamg'asi (`?v=`). */
  avatarUpdatedAt: string
}

/* ===== /O'Z PROFILINI TAHRIRLASH ===== */

/* ============================================================================
   "XABARLAR" PANELI (2026-08-16) — guruhlarga shablon/qo'lda xabar yuborish.
   ============================================================================ */

export interface MessageTemplateDto {
  id: number
  name: string
  body: string
  isActive: boolean
  createdAt: string
  updatedAt: string | null
}

export interface CreateMessageTemplateRequest {
  name: string
  body: string
  isActive: boolean
}

export interface UpdateMessageTemplateRequest {
  name: string
  body: string
  isActive: boolean
}

/** `POST /api/v1/broadcasts` tanasi. */
export interface SendGroupBroadcastRequest {
  groupIds: number[]
  body: string
  templateId: number | null
  sendToTelegram: boolean
  sendToPlatformChat: boolean
}

/** Yuborilgan xabar tarixi qatori. */
export interface GroupBroadcastDto {
  id: number
  authorId: number
  authorName: string
  templateId: number | null
  templateName: string | null
  body: string
  targetGroupNames: string
  targetGroupCount: number
  sentToTelegram: boolean
  sentToPlatformChat: boolean
  telegramRecipientCount: number
  createdAt: string
}

/* ===== /"XABARLAR" PANELI ===== */

/* ============================================================================
   BAYRAM KALENDARI (2026-08-16) — umumiy sanalar, guruh jadvalini avtomatik
   suradi va o'sha kunlar uchun to'lov yechilmaydi.
   ============================================================================ */

export interface HolidayDto {
  id: number
  date: string
  label: string
  createdById: number
  createdByName: string | null
  createdAt: string
}

export interface CreateHolidayRequest {
  date: string
  label: string
}

/** `POST /api/v1/holidays` javobi — bayram + ta'sirlangan guruh/dars soni. */
export interface HolidayImpactDto {
  holiday: HolidayDto
  affectedGroupCount: number
  cancelledSessionCount: number
}

/* ===== /BAYRAM KALENDARI ===== */

/* ============================================================================
   OYLIK HISOBLASH (2026-08-16) — ustoz/kurator haqi, FAQAT Admin ko'radi.
   ============================================================================ */

export interface TeacherRateDto {
  id: number
  userId: number | null
  userName: string | null
  role: UserRoleName
  perSessionRate: number
  perStudentBonusRate: number
  /** `DateOnly` — `YYYY-MM-DD`. */
  activeFrom: string
  isActive: boolean
  specificity: number
  createdAt: string
  updatedAt: string | null
}

export interface CreateTeacherRateRequest {
  role: UserRoleName
  perSessionRate: number
  perStudentBonusRate: number
  activeFrom: string
  userId?: number | null
  isActive: boolean
}

/** ★ `PUT` — TO'LIQ ALMASHTIRISH (izoh: `UpdateTariffRequest` bilan AYNI naqsh). */
export interface UpdateTeacherRateRequest {
  role: UserRoleName
  perSessionRate: number
  perStudentBonusRate: number
  activeFrom: string
  isActive: boolean
  userId: number | null
}

export interface PayrollSummaryRowDto {
  userId: number
  fullName: string
  role: UserRoleName
  sessionCount: number
  totalStudentsAttended: number
  baseAmount: number
  bonusAmount: number
  total: number
  /** Stavka topilmagan darslar soni — 0 bo'lmasa hisobot TO'LIQ EMAS. */
  sessionsWithoutRate: number
}

export interface PayrollSummaryDto {
  period: string
  rows: PayrollSummaryRowDto[]
  grandTotal: number
}

export interface PayrollSessionRowDto {
  sessionId: number
  groupId: number
  groupName: string
  scheduledStart: string
  attendedStudents: number
  sessionRate: number
  bonusAmount: number
  total: number
  rateMissing: boolean
}

export interface PayrollDetailDto {
  userId: number
  fullName: string
  role: UserRoleName
  period: string
  sessions: PayrollSessionRowDto[]
  grandTotal: number
}

/* ===== /OYLIK HISOBLASH ===== */
