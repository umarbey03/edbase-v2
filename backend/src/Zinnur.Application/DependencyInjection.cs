using Microsoft.Extensions.DependencyInjection;
using Zinnur.Application.Assignments.Services;
using Zinnur.Application.Auth.Services;
using Zinnur.Application.Courses.Services;
using Zinnur.Application.Gating.Services;
using Zinnur.Application.Groups.Services;
using Zinnur.Application.LiveSessions.Services;
using Zinnur.Application.Scheduling.Services;
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
        services.AddScoped<ILiveSessionService, LiveSessionService>();
        services.AddScoped<IUserService, UserService>();
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

        // Vaqtni test qilish mumkin bo'lsin (DateTimeOffset.UtcNow qotib qolmasin)
        services.AddSingleton(TimeProvider.System);

        return services;
    }
}
