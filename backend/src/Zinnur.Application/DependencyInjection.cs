using Microsoft.Extensions.DependencyInjection;
using Zinnur.Application.Auth.Services;
using Zinnur.Application.LiveSessions.Services;
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

        // Vaqtni test qilish mumkin bo'lsin (DateTimeOffset.UtcNow qotib qolmasin)
        services.AddSingleton(TimeProvider.System);

        return services;
    }
}
