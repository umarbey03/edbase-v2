using Microsoft.EntityFrameworkCore;
using Zinnur.Domain.Entities;

namespace Zinnur.Application.Common.Interfaces;

/// <summary>
/// Baza uchun PORT. Application qatlami faqat shu interfeysni biladi —
/// haqiqiy <c>DbContext</c> Infrastructure'da (Dependency Inversion).
/// Shu tufayli use-case'lar bazasiz, InMemory yoki mock bilan test qilinadi.
/// </summary>
public interface IApplicationDbContext
{
    DbSet<User> Users { get; }

    /// <summary>
    /// Xodimlarning o'quvchi haqidagi ICHKI izohlari. O'quvchining O'ZI
    /// ularni ko'rmaydi — sabab <see cref="StudentNote"/> sinfi izohida.
    /// </summary>
    DbSet<StudentNote> StudentNotes { get; }

    /// <summary>
    /// Telegram bog'lanishini uzish izi. Faqat QO'SHILADI va O'QILADI:
    /// yozuv yaratilgandan keyin yangilanmaydi va o'chirilmaydi.
    /// </summary>
    DbSet<TelegramUnlinkAudit> TelegramUnlinkAudits { get; }

    DbSet<Course> Courses { get; }
    DbSet<CourseModule> Modules { get; }
    DbSet<ModuleLesson> ModuleLessons { get; }
    DbSet<Group> Groups { get; }
    DbSet<GroupMember> GroupMembers { get; }
    DbSet<LiveSession> LiveSessions { get; }
    DbSet<Attendance> Attendances { get; }

    /// <summary>
    /// Davomatni QO'LDA tuzatish izi (kim, qachon, nimadan-nimaga).
    /// Faqat QO'SHILADI va O'QILADI — yozuv yaratilgandan keyin hech
    /// qachon yangilanmaydi va o'chirilmaydi.
    /// </summary>
    DbSet<AttendanceAudit> AttendanceAudits { get; }

    /// <summary>
    /// O'quvchining BITTA DARS uchun bahosi (R24).
    ///
    /// ★ <see cref="Submissions"/> BILAN ARALASHTIRILMAYDI: u o'quvchi
    /// TOPSHIRGAN ish, bu esa ustozning dars uchun qo'ygan bahosi —
    /// topshirilgan ish umuman bo'lmasligi mumkin. Farqi batafsil
    /// <see cref="LessonGrade"/> sinfi izohida.
    /// </summary>
    DbSet<LessonGrade> LessonGrades { get; }

    /// <summary>
    /// Dars bahosini o'zgartirish izi (kim, qachon, nimadan-nimaga).
    /// <see cref="AttendanceAudits"/> kabi faqat QO'SHILADI va O'QILADI.
    /// </summary>
    DbSet<LessonGradeAudit> LessonGradeAudits { get; }

    DbSet<ChatMessage> ChatMessages { get; }

    /// <summary>
    /// Kurator ↔ o'quvchi shaxsiy yozishmasi. <see cref="ChatMessages"/>
    /// (jonli dars xonasi oqimi) bilan ARALASHTIRILMAYDI — farqi
    /// <see cref="DirectMessage"/> sinfi izohida batafsil.
    /// </summary>
    DbSet<DirectMessage> DirectMessages { get; }

    /// <summary>
    /// Shaxsiy yozishmaga biriktirilgan fayl (2026-08-17) —
    /// <see cref="GroupChatAttachments"/> bilan AYNI naqsh, sabab
    /// <see cref="DirectMessageAttachment"/> izohida.
    /// </summary>
    DbSet<DirectMessageAttachment> DirectMessageAttachments { get; }

    /// <summary>
    /// Guruhning DOIMIY chati — dars vaqtidan TASHQARIDA ham ishlaydi.
    /// Yuqoridagi ikkalasi bilan ARALASHTIRILMAYDI: uchalasining farqi
    /// <see cref="GroupChatMessage"/> sinfi izohida batafsil.
    /// </summary>
    DbSet<GroupChatMessage> GroupChatMessages { get; }

    /// <summary>
    /// Guruh chatidagi "qayergacha o'qidim" belgilari — o'qilmaganlar
    /// sanog'ining asosi. Har foydalanuvchi + oqim uchun BITTA qator;
    /// nima uchun har xabarda bayroq emasligi <see cref="GroupChatRead"/> da.
    /// </summary>
    DbSet<GroupChatRead> GroupChatReads { get; }

    /// <summary>
    /// Guruh chati xabariga biriktirilgan fayllar (R16b) — rasm/ovoz/hujjat.
    ///
    /// ★ Xabar bilan BITTA tranzaksiyada yaratiladi, ya'ni "egasiz
    /// biriktirma" holati yo'q; ombordagi obyektni o'chirish esa
    /// <see cref="Zinnur.Application.Jobs.ChatRetentionJob"/> ning ishi
    /// (sabab <see cref="GroupChatAttachment"/> izohida).
    /// </summary>
    DbSet<GroupChatAttachment> GroupChatAttachments { get; }

    // ---------------------------------------------------------------- FAZA 3: o'quv jarayoni

    DbSet<Assignment> Assignments { get; }
    DbSet<Submission> Submissions { get; }
    DbSet<SubmissionFile> SubmissionFiles { get; }

    /// <summary>
    /// USTOZ tekshirishda biriktirgan fayllar (R37).
    ///
    /// 🔴 <see cref="SubmissionFiles"/> BILAN ARALASHTIRILMAYDI: u
    /// o'quvchining javobi, bu esa tekshiruvchining javobi. Nima uchun
    /// bitta jadvalda EMASligi <see cref="SubmissionFeedbackFile"/> izohida.
    /// </summary>
    DbSet<SubmissionFeedbackFile> SubmissionFeedbackFiles { get; }
    DbSet<Test> Tests { get; }
    DbSet<TestQuestion> TestQuestions { get; }
    DbSet<TestOption> TestOptions { get; }
    DbSet<TestAttempt> TestAttempts { get; }
    DbSet<TestAnswer> TestAnswers { get; }
    DbSet<LessonProgress> LessonProgress { get; }

    // ---------------------------------------------------------------- WAVE 1: dars mediasi

    /// <summary>
    /// Dars mediasi: odatiy darsda VIDEO qismlari, imtihon darsida RASMLAR.
    /// Ikkalasi BITTA jadvalda — sabab <see cref="LessonAsset"/> izohida.
    /// </summary>
    DbSet<LessonAsset> LessonAssets { get; }

    /// <summary>
    /// Uy vazifasi SHARTIGA biriktirilgan fayllar (rasm/audio/hujjat).
    /// O'quvchining JAVOB fayllari bu yerda EMAS — <see cref="SubmissionFiles"/>.
    /// </summary>
    DbSet<AssignmentAttachment> AssignmentAttachments { get; }

    // ---------------------------------------------------------------- FAZA 4: moliya

    DbSet<Payment> Payments { get; }
    DbSet<Tariff> Tariffs { get; }
    DbSet<StudentDiscount> StudentDiscounts { get; }
    DbSet<StudentAccount> StudentAccounts { get; }
    DbSet<PaymentTransaction> PaymentTransactions { get; }
    DbSet<PaymentAudit> PaymentAudits { get; }

    /// <summary>
    /// Bosqichma-bosqich hisoblash (2026-08-16) — bir dars, bir o'quvchi
    /// uchun hisoblangan ulush. Idempotentlik qulfi + hisobot manbai
    /// (izoh: <see cref="LessonCharge"/>).
    /// </summary>
    DbSet<LessonCharge> LessonCharges { get; }

    /// <summary>Ustoz/kurator oylik stavkasi — narx TARIXI (izoh: <see cref="TeacherRate"/>).</summary>
    DbSet<TeacherRate> TeacherRates { get; }

    /// <summary>Ustoz/kurator haqi SNAPSHOT'i — izoh: <see cref="SessionPayout"/>.</summary>
    DbSet<SessionPayout> SessionPayouts { get; }

    /// <summary>Oylik davri tasdiqlash/to'lov holati — izoh: <see cref="PayrollApproval"/>.</summary>
    DbSet<PayrollApproval> PayrollApprovals { get; }

    /// <summary>Qo'lda bonus/ushlab qolish — izoh: <see cref="PayrollAdjustment"/>.</summary>
    DbSet<PayrollAdjustment> PayrollAdjustments { get; }

    // ---------------------------------------------------------------- FAZA 5.3: dars yozuvi

    /// <summary>
    /// Dars yozuvi urinishlari (LiveKit Egress → obyekt ombori).
    ///
    /// ★ NIMA UCHUN BU YERDA OCHILADI, <see cref="AttendanceAudits"/> kabi:
    /// yozuv — BIZNES ma'lumoti. O'quvchi uni ro'yxatda ko'radi, xodim esa
    /// "nega bu darsning yozuvi yo'q?" degan savolga javobni AYNAN shu
    /// jadvaldan oladi.
    ///
    /// ⚠️ Takroriy webhook hodisalarining jurnali BU YERDA YO'Q: u biznes
    /// ma'lumoti emas, YETKAZIB BERISH mexanizmi va Infrastructure ichida
    /// qoladi (<c>TelegramUpdates</c> bilan AYNI sabab). Use-case unga
    /// <c>ILiveKitWebhookLog</c> porti orqali tegadi.
    /// </summary>
    DbSet<SessionRecording> SessionRecordings { get; }

    /* ===== R35/R36 · ILOVA ICHIDAGI BILDIRISHNOMA =====

       ★ NEGA ALOHIDA BLOK: bu faylga bir necha tarmoq AYNI vaqtda qo'shadi.
       Mavjud bo'limlar orasiga qistirilgan qator merge paytida to'qnashuv
       beradi, uzluksiz blok esa bermaydi. */

    /// <summary>
    /// Foydalanuvchining qo'ng'iroqcha ro'yxati (o'qilgan/o'qilmagan).
    ///
    /// 🔴 <c>MessageOutbox</c> BILAN ARALASHTIRILMAYDI VA U QAYTA
    /// ISHLATILMAYDI: navbat jadvali ATAYLAB shu interfeysda YO'Q (u
    /// yetkazib berish mexanizmi) va uning matni Telegram HTML — Vue
    /// ro'yxati uchun noto'g'ri shakl. To'liq sabab
    /// <see cref="Notification"/> sinfi izohida.
    /// </summary>
    DbSet<Notification> Notifications { get; }

    /* ===== /R35/R36 ===== */

    /* ===== R29/R30 · DARS SIFATI TAHLILI =====

       Yuqoridagi blok qoidasi bilan AYNI sabab: bu faylga bir necha tarmoq
       bir vaqtda qo'shmoqda, shuning uchun yangi qator mavjud bo'limlar
       ORASIGA qistirilmaydi. */

    /// <summary>
    /// O'quv bo'limining DARS SIFATI bo'yicha xulosasi (R29 / R30).
    ///
    /// 🔴 DARSGA bog'langan, YOZUVGA emas — sabab <see cref="SessionReview"/>
    /// sinfi izohida (qisqasi: bitta darsning bir nechta yozuvi bo'lishi
    /// mumkin, yozuvi umuman bo'lmasligi ham mumkin, tahlil esa ikkalasidan
    /// ham omon qolishi kerak).
    ///
    /// ⚠️ O'QUVCHIDAN TO'LIQ YOPIQ — bu jadval hech qachon o'quvchi
    /// ko'radigan javobga proyeksiya qilinmasin (<see cref="StudentNotes"/>
    /// bilan AYNI qoida va AYNI sabab).
    /// </summary>
    DbSet<SessionReview> SessionReviews { get; }

    /// <summary>Bitta <see cref="SessionReview"/> ichidagi mezon ballari.</summary>
    DbSet<SessionReviewScore> SessionReviewScores { get; }

    /// <summary>Dars tahlili mezonlari katalogi (o'quv bo'limi sozlaydi).</summary>
    DbSet<AnalysisCriterion> AnalysisCriteria { get; }

    /* ===== /R29/R30 ===== */

    /* ===== R21b · GURUH KATEGORIYASI =====

       Yuqoridagi bloklar qoidasi bilan AYNI sabab (parallel tarmoqlar). */

    /// <summary>
    /// Guruhlarning o'quv YO'NALISHI lug'ati ("ATF", "Grammatika", "CEFR",
    /// "IELTS") — R21b.
    ///
    /// ⚠️ <see cref="Courses"/> BILAN ARALASHTIRILMAYDI: kursda MODUL va
    /// DARSLAR bor (gating shu daraxt bo'yicha hisoblanadi), bu esa faqat
    /// YORLIQ — o'chirilsa birorta dars ham yo'qolmaydi. Ular takrorlanib
    /// qolishi mumkinligi haqidagi ochiq savol <see cref="GroupCategory"/>
    /// sinfi izohida.
    /// </summary>
    DbSet<GroupCategory> GroupCategories { get; }

    /* ===== /R21b ===== */

    /* ===== 2026-08-16: "Xabarlar" paneli ===== */

    /// <summary>Guruhlarga yuboriladigan tayyor xabar shablonlari (Sozlamalar panelidan boshqariladi).</summary>
    DbSet<MessageTemplate> MessageTemplates { get; }

    /// <summary>Guruhlarga yuborilgan xabarlar TARIXI (har yuborish — bitta qator).</summary>
    DbSet<GroupBroadcast> GroupBroadcasts { get; }

    /* ===== /2026-08-16 ===== */

    /* ===== 2026-08-16: To'lov (dars-asosida) + bayram kalendari ===== */

    /// <summary>
    /// Umumiy bayram kalendari — o'quv/admin bo'limi e'lon qilgan sanalar.
    /// Har sana BARCHA guruhlarning o'sha kundagi darsini bekor qiladi
    /// (<c>HolidayService.CreateAsync</c>).
    /// </summary>
    DbSet<Holiday> Holidays { get; }

    /* ===== /2026-08-16 (to'lov) ===== */

    /* ===== 2026-08-17: ustoz kunlik tasdiqlash + o'rinbosar ===== */

    DbSet<TeacherDailyCheckin> TeacherDailyCheckins { get; }

    DbSet<TeacherCheckinAffectedSession> TeacherCheckinAffectedSessions { get; }

    DbSet<SessionCoverageRequest> SessionCoverageRequests { get; }

    DbSet<SubstituteOffer> SubstituteOffers { get; }

    /// <summary>
    /// A'zolik hodisalari jurnali (to'kilish/muzlatish/ko'chirish tarixi).
    /// FAQAT QO'SHILADI — sabab <see cref="GroupMembershipEvent"/> izohida.
    /// </summary>
    DbSet<GroupMembershipEvent> GroupMembershipEvents { get; }

    /// <summary>Ustoz/kurator jarimalari (2026-08-18) — oylikka FAQAT tasdiqlangach tushadi.</summary>
    DbSet<Penalty> Penalties { get; }

    /// <summary>Jarima tariflari katalogi — sozlamalardan boshqariladi.</summary>
    DbSet<PenaltyCategory> PenaltyCategories { get; }

    /* ===== /2026-08-17 ===== */

    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
