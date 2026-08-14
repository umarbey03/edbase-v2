using Microsoft.Extensions.DependencyInjection;
using Zinnur.Application.Assignments.Services;
using Zinnur.Application.Auth.Services;
using Zinnur.Application.Common.Interfaces;
using Zinnur.Application.Common.Scope;
using Zinnur.Application.Courses.Services;
using Zinnur.Application.Gating.Services;
using Zinnur.Application.GroupChat.Services;
using Zinnur.Application.Groups.Services;
using Zinnur.Application.LiveSessions.Services;
using Zinnur.Application.Messaging.Services;
using Zinnur.Application.Payments.Services;
using Zinnur.Application.Progress.Services;
using Zinnur.Application.Recordings.Services;
using Zinnur.Application.Scheduling.Services;
using Zinnur.Application.Settings.Services;
using Zinnur.Application.StudentNotes.Services;
using Zinnur.Application.Tests.Services;
using Zinnur.Application.Users.Services;

namespace Zinnur.Application;

/// <summary>
/// Application qatlamini DI'ga ulaydi.
/// Har qatlam O'ZINI ro'yxatdan o'tkazadi — WebApi ichki tuzilmani bilmaydi.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IAuthService, AuthService>();

        // ---------------------------------------------------------------- TELEFON BILAN KIRISH
        //
        // 2026-08-13: email va parol bilan kirish olib tashlandi, o'rniga
        // telefon + Telegram orqali keladigan bir martalik kod.
        //
        // ★ SERVIS — SCOPED: kod so'rovi navbat yozuvini AYNI so'rovning
        //   `DbContext` kuzatuvchisiga qo'shadi va uni bitta
        //   `SaveChanges` bilan saqlaydi (commit-then-send). Singleton
        //   bo'lsa scoped kontekst ushlab qolinardi ("captive dependency")
        //   va ikkinchi so'rovda allaqachon yopilgan kontekstga urinilardi.
        //
        // ★ KOD SAQLOVCHISI — SINGLETON: uning ikkala bog'liqligi ham
        //   (`ICacheService`, `TimeProvider`) singleton va u hech qanday
        //   holat saqlamaydi. Scoped qilinsa har so'rovda bekorga obyekt
        //   yasalardi; captive-dependency xavfi esa yo'q, chunki qisqa
        //   umrli hech narsaga tegmaydi.
        services.AddSingleton<IPhoneLoginCodeStore, PhoneLoginCodeStore>();
        services.AddScoped<IPhoneLoginService, PhoneLoginService>();

        // Sessiya holati keshi: har so'rovda kirish tokenidagi `ver` shu yerdan
        // olinadigan JORIY versiya bilan solishtiriladi (`OnTokenValidated`).
        services.AddScoped<IAuthStateCache, AuthStateCache>();

        services.AddScoped<ILiveSessionService, LiveSessionService>();

        // DAVOMATNI QO'LDA TUZATISH — jonli oqim servisidan ATAYLAB alohida
        // (sabab `IAttendanceService` izohida). SCOPED: tuzatish va uning
        // audit izi AYNI `DbContext` kuzatuvchisida to'planib, BITTA
        // `SaveChanges` — ya'ni bitta tranzaksiya — bilan yoziladi.
        services.AddScoped<IAttendanceService, AttendanceService>();

        services.AddScoped<IUserService, UserService>();

        // ---------------------------------------------------------------- WAVE 1
        //
        // O'QUVCHI PROFILI (drawer uchun yagona agregat) va XODIM IZOHLARI.
        //
        // Profil servisi `IUserService` dan ATAYLAB ajratilgan: u faqat
        // O'QIYDI va uning ruxsat qoidasi boshqa savolga javob beradi
        // ("kim nimani KO'RADI", "kim kimni BOSHQARADI" emas) — batafsil
        // `IUserProfileService` izohida.
        //
        // Ikkalasi ham SCOPED: so'rov umriga bog'langan `DbContext` ga
        // tayanadi. Izoh servisi bunga qo'shimcha ravishda izoh qatorini
        // AYNI ChangeTracker'da to'playdi. Singleton bo'lsa scoped kontekst
        // ushlab qolinardi ("captive dependency") va ikkinchi so'rovda
        // allaqachon yopilgan kontekst bilan ishlashga urinilardi.
        services.AddScoped<IUserProfileService, UserProfileService>();
        services.AddScoped<IStudentNoteService, StudentNoteService>();

        services.AddScoped<IGroupService, GroupService>();

        // Jadval servisi guruh servisidan ALOHIDA: uni fon vazifasi
        // (muddati o'tgan darslarni yopish) ham chaqiradi.
        // `IScheduleTimeZoneProvider` — Infrastructure'da (konfiguratsiyadan o'qiladi).
        services.AddScoped<IScheduleService, ScheduleService>();

        // ---------------------------------------------------------------- FAZA 3
        //
        // GATING SCOPED bo'lishi SHART: u so'rov ichida hisoblangan daraxtni
        // xotirada eslab qoladi (`_snapshot`), ya'ni bitta HTTP so'rovida
        // daraxt ko'pi bilan bir marta quriladi. Singleton bo'lsa bu memo
        // FOYDALANUVCHILAR ORASIDA bo'lishilardi — bir o'quvchi boshqasining
        // progressini ko'rardi. Transient bo'lsa memo har chaqiruvda
        // yo'qolardi va kesh foydasi qolmasdi.
        services.AddScoped<IGatingService, GatingService>();

        services.AddScoped<IAssignmentService, AssignmentService>();
        services.AddScoped<ITestService, TestService>();

        // ---------------------------------------------------------------- FAZA 3.1
        //
        // KURS KONTENTI. SCOPED — u `IGatingService` ga bog'liq, u esa so'rov
        // ichida keshlanadigan snapshot saqlaydi. Singleton bo'lsa scoped
        // bog'liqlikni ushlab qolib "captive dependency" hosil bo'lardi:
        // birinchi so'rovning gating snapshot'i butun ilova umriga qotib
        // qolardi va HAMMA o'quvchiga o'sha bitta o'quvchining ochiq
        // darslari ko'rinardi.
        services.AddScoped<ICourseService, CourseService>();

        // ---------------------------------------------------------------- WAVE 1
        //
        // DARS MEDIASI (video qismlari / imtihon rasmlari) va UY VAZIFASI
        // SHARTINING biriktirmalari.
        //
        // SCOPED va `ICourseService` DAN ALOHIDA:
        //  • SCOPED, chunki `IGatingService` (so'rov ichida keshlanadigan
        //    snapshot) va `IPaymentBlockService` ga bog'liq. Singleton bo'lsa
        //    scoped bog'liqlik ushlab qolinib "captive dependency" hosil
        //    bo'lardi: BIRINCHI so'rovning gating snapshot'i butun ilova
        //    umriga qotib qolardi va hamma o'quvchiga o'sha bitta
        //    o'quvchining ochiq darslari ko'rinardi.
        //  • ALOHIDA interfeys, chunki media yo'li OQIM bilan ishlaydi
        //    (`IMediaStorage`). Uni kurs daraxti CRUD'iga qo'shish
        //    `ICourseService` ni omborga bog'lab qo'yardi — hozir u faqat
        //    bazani biladi va shu tufayli ombor sozlanmagan muhitda ham
        //    to'liq ishlaydi.
        services.AddScoped<ILessonAssetService, LessonAssetService>();
        services.AddScoped<IAssignmentAttachmentService, AssignmentAttachmentService>();

        // ---------------------------------------------------------------- FAZA 4.3
        //
        // MOLIYA. SCOPED — servis `DbContext` ning ChangeTracker'iga tayanadi:
        // bitta so'rovdagi barcha o'zgarish (oy yozuvi, jurnal, balans, audit)
        // AYNI kuzatuvchida to'planib, BITTA `SaveChanges` bilan yoziladi.
        // Singleton bo'lsa scoped `DbContext` ushlab qolinardi (captive
        // dependency) va ikkinchi so'rovda allaqachon yopilgan kontekst bilan
        // pul yozishga urinilardi.
        services.AddScoped<IPaymentService, PaymentService>();

        // Blok darvozasi ALOHIDA va KICHIK interfeys: uni moliyadan
        // TASHQARIDAGI servislar chaqiradi (jonli darsga kirish, kurs
        // kontenti) — ular butun moliya servisiga bog'lanib qolmasin.
        services.AddScoped<IPaymentBlockService, PaymentBlockService>();

        // MOLIYA YIG'MA HISOBOTI — faqat O'QISH, shuning uchun pul yozadigan
        // servisdan ALOHIDA tur. SCOPED: u ham so'rov umriga bog'langan
        // `DbContext` ga tayanadi (singleton bo'lsa yopilgan kontekst ushlab
        // qolinardi), va ruxsat tekshiruvi uchun `IPaymentService` ni
        // chaqiradi — moliyada ruxsat qoidasi YAGONA bo'lib qolsin.
        services.AddScoped<IPaymentSummaryService, PaymentSummaryService>();

        // ---------------------------------------------------------------- FAZA 5
        //
        // O'QUVCHI ILOVASI: reyting, davomat xulosasi va kurator yozishmasi.
        //
        // Hammasi SCOPED — barchasi so'rov umriga bog'langan `DbContext` ga
        // tayanadi. Singleton bo'lsa scoped kontekst ushlab qolinardi
        // ("captive dependency") va ikkinchi so'rovda allaqachon yopilgan
        // kontekst bilan ishlashga urinilardi.
        //
        // O'QUV MARKAZ QAMROVI — ko'p-markazli (multi-tenant) kelajak uchun
        // YAGONA chok. Bugun u hech narsani filtrlamaydi (bitta deployment =
        // bitta markaz), lekin "markaz bo'yicha" savoli KODDA NOMLANGAN
        // bo'lishi kerak: ertaga `LearningCenter` qo'shilganda o'zgarish
        // SHU ro'yxatdagi bitta qatorga (va `SingleCenterScope` ichiga)
        // tushadi, reyting servisiga emas. Batafsil `ILearningCenterScope`
        // izohida.
        //
        // SCOPED — u so'rov umriga bog'langan `DbContext` ga tayanadi.
        services.AddScoped<ILearningCenterScope, SingleCenterScope>();

        services.AddScoped<ILeaderboardService, LeaderboardService>();
        services.AddScoped<IAttendanceSummaryService, AttendanceSummaryService>();

        // "Kim kim bilan bog'langan" qoidasi ALOHIDA servisda: uni yozishma
        // ham, kelajakdagi kurator paneli ham ishlatadi. Eski tizimda shu
        // qoida bir necha joyda qo'lda takrorlangan va ba'zisida chala edi.
        services.AddScoped<ICuratorDirectory, CuratorDirectory>();
        services.AddScoped<IDirectMessageService, DirectMessageService>();

        // ---------------------------------------------------------------- FAZA 6
        //
        // GURUH CHATI — har guruhning doimiy chati (dars vaqtidan tashqarida
        // ham). SCOPED: so'rov umriga bog'langan `DbContext` ga tayanadi va
        // ruxsat qoidasi uchun `ICuratorDirectory` ni chaqiradi — "kim kim
        // bilan bog'langan" javobi butun loyihada YAGONA bo'lib qolsin.
        //
        // Singleton bo'lsa scoped kontekst ushlab qolinardi ("captive
        // dependency") va ikkinchi so'rovda allaqachon yopilgan kontekst
        // bilan xabar yozishga urinilardi.
        services.AddScoped<IGroupChatService, GroupChatService>();

        // ---------------------------------------------------------------- FAZA 5.3
        //
        // TIZIM SOZLAMALARI (super-admin paneli).
        //
        // IKKI XIZMAT, ATAYLAB AJRATILGAN:
        //  • `ISettingsResolver` — RUXSATSIZ o'qish yo'li. Uni moliya bloki
        //    har so'rovda chaqiradi va o'sha paytda joriy foydalanuvchi —
        //    oddiy o'quvchi. Agar o'qishga ham admin talab qilinsa, blok
        //    tekshiruvi umuman ishlamasdi.
        //  • `ISettingsService` — PANEL yo'li: rol bazadan qayta o'qiladi,
        //    faqat `Admin`, va har o'zgarish auditga tushadi.
        //
        // Ikkalasi ham SCOPED: `ISettingsResolver` port orqali `DbContext` ga
        // tayanadi, `ISettingsService` esa sozlama qatorini va uning audit
        // yozuvini AYNI ChangeTracker'da to'plab, BITTA `SaveChanges` bilan
        // saqlaydi. Singleton bo'lsa scoped kontekst ushlab qolinardi
        // ("captive dependency") va ikkinchi so'rovda allaqachon yopilgan
        // kontekst bilan yozishga urinilardi.
        services.AddScoped<ISettingsResolver, SettingsResolver>();
        services.AddScoped<ISettingsService, SettingsService>();

        // ---------------------------------------------------------------- FAZA 5.3
        //
        // DARS YOZUVI (LiveKit Egress -> obyekt ombori).
        //
        // Ikkalasi ham SCOPED: ular `DbContext` ga (port orqali) tayanadi va
        // o'zgarishlarni AYNI ChangeTracker'da to'playdi. Webhook uchun bu
        // AYNIQSA muhim — takror jurnali yozuvi va yozuv holatining
        // o'zgarishi BITTA `SaveChanges` bilan, ya'ni bitta tranzaksiyada
        // saqlanishi kerak (aks holda "takror deb belgilandi, lekin holat
        // o'zgarmadi" degan yo'qotish mumkin bo'lardi).
        //
        // Singleton bo'lsa scoped kontekst ushlab qolinardi ("captive
        // dependency") va ikkinchi so'rovda allaqachon yopilgan kontekst
        // bilan ishlashga urinilardi.
        services.AddScoped<IRecordingService, RecordingService>();
        services.AddScoped<IRecordingWebhookHandler, RecordingWebhookHandler>();

        // AVTOMATIK YOZUV NAVBATI (2026-08-13).
        //
        // 🔴 SCOPED BO'LISHI SHART — VA BU TASODIF EMAS. Navbat qatorini
        //    `LiveSessionService` NING AYNI `DbContext` iga qo'shadi va
        //    saqlashni O'ZI qilmaydi: ikkalasi bir scope'da bo'lgani uchun
        //    dars `Live` bo'lishi va navbat qatori BITTA tranzaksiyaga
        //    tushadi (izoh: `IAutoRecordingScheduler`).
        //
        // ★ NIMA UCHUN `IRecordingService` QAYTA ISHLATILMADI: u
        //   `ILiveSessionService` ga bog'liq, `LiveSessionService` esa
        //   navbatga bog'liq bo'ladi — ya'ni DI grafida HALQA hosil
        //   bo'lardi va konteyner ishga tushishda yiqilardi. Tor interfeys
        //   halqani TUZILISH bilan imkonsiz qiladi: navbat dars servisini
        //   umuman bilmaydi.
        services.AddScoped<IAutoRecordingScheduler, AutoRecordingScheduler>();

        // Vaqtni test qilish mumkin bo'lsin (DateTimeOffset.UtcNow qotib qolmasin)
        services.AddSingleton(TimeProvider.System);

        return services;
    }
}
