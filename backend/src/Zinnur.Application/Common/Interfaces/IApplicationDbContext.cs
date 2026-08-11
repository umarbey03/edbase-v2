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

    DbSet<ChatMessage> ChatMessages { get; }

    /// <summary>
    /// Kurator ↔ o'quvchi shaxsiy yozishmasi. <see cref="ChatMessages"/>
    /// (jonli dars xonasi oqimi) bilan ARALASHTIRILMAYDI — farqi
    /// <see cref="DirectMessage"/> sinfi izohida batafsil.
    /// </summary>
    DbSet<DirectMessage> DirectMessages { get; }

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

    // ---------------------------------------------------------------- FAZA 3: o'quv jarayoni

    DbSet<Assignment> Assignments { get; }
    DbSet<Submission> Submissions { get; }
    DbSet<SubmissionFile> SubmissionFiles { get; }
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

    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
