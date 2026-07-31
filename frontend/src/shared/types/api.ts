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

/** `POST /api/v1/auth/login` tanasi */
export interface LoginRequest {
  email: string
  password: string
}

/**
 * `POST /api/v1/auth/refresh` tanasi.
 * SPEC'da alohida DTO ko'rsatilmagan (5-bo'limda faqat javob turi bor) —
 * amalda yagona mantiqiy shakl shu.
 */
export interface RefreshRequest {
  refreshToken: string
}

export interface UserDto {
  id: number
  fullName: string
  email: string
  role: UserRoleName
}

export interface AuthResponse {
  accessToken: string
  refreshToken: string
  user: UserDto
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

export interface GroupDto {
  id: number
  name: string | null
  type: GroupTypeName
  courseId: number | null
  courseName: string | null
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
  memberCount: number
  sessionCount: number
  createdAt: string
  updatedAt: string | null
}

export interface GroupMemberDto {
  id: number
  studentId: number
  fullName: string | null
  email: string | null
  phone: string | null
  status: MemberStatusName
  joinedAt: string
  pausedUntil: string | null
  sourceGroupId: number
  sourceGroupName: string | null
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
  imageKey: string | null
  createdById: number | null
  submissionCount: number
  gradedCount: number
  createdAt: string
  updatedAt: string | null
}

export interface SubmissionFileDto {
  id: number
  objectKey: string | null
  kind: string
  sizeBytes: number
  contentType: string | null
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
  imageKey: string | null
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

/** `GET /api/v1/users` elementi. `role` backendda `string` (enum nomi). */
export interface UserDetailsDto {
  id: number
  fullName: string | null
  email: string | null
  phone: string | null
  telegramId: number | null
  role: string | null
  isActive: boolean
  createdAt: string
  updatedAt: string | null
}

export interface CreateUserRequest {
  fullName: string
  email: string
  role: UserRoleName
  phone?: string | null
  /** Bo'sh bo'lsa server vaqtinchalik parol o'ylab topadi. */
  password?: string | null
  isActive: boolean
}

export interface CreateUserResponse {
  user: UserDetailsDto
  /** Faqat parol server tomonidan generatsiya qilinganda to'ladi. */
  temporaryPassword: string | null
}

export interface UpdateUserRequest {
  fullName: string
  email: string
  phone?: string | null
  role: UserRoleName
}

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
  teacherId?: number | null
  assistantId?: number | null
  curatorGroupId?: number | null
  recordEnabled: boolean
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

/** `LessonLockReason` enum — dars nima uchun yopiq. */
export type LessonLockReasonName = 'PreviousIncomplete' | 'TeacherPace' | 'NotInCourse'

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
  unlocked: boolean
  lockReason: LessonLockReasonName | null
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
  /** Yakuniy ball 0..100 — uch mezon o'rtachasi. */
  total: number
  /** `null` — shu oyda o'tilgan dars yo'q. */
  attendancePercent: number | null
  /** `null` — shu oyda baholangan vazifa yo'q. */
  assignmentPercent: number | null
  /** `null` — shu oyda topshirilgan test yo'q. */
  testPercent: number | null
  isMe: boolean
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

/** "Mening o'rnim" — jadvalsiz yengil ko'rinish. `groupId` `null` — faol guruh yo'q. */
export interface MyRankDto {
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
export type SettingOriginName = 'Default' | 'Environment' | 'Database'

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
  body: string
  /** ISO-8601. */
  sentAt: string
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
