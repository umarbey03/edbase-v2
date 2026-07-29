using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Zinnur.Infrastructure.Persistence;

/// <summary>
/// `dotnet ef` buyruqlari uchun DbContext yaratuvchi.
///
/// NIMA UCHUN KERAK: `dotnet ef migrations add` ilovani ISHGA TUSHIRMASDAN
/// DbContext'ni qurishi kerak. Odatda u `Program.cs` dagi host'ni topib
/// ishlatadi, lekin bizda WebApi ishga tushganda Postgres va Redis'ga ulanishga
/// urinadi — migratsiya yaratayotganda esa ular kerak emas va mavjud
/// bo'lmasligi ham mumkin.
///
/// Bu fabrika faqat dizayn vaqtida (design-time) ishlaydi, ishlab chiqarishda
/// hech qachon chaqirilmaydi. Ulanish satri haqiqiy bo'lishi shart emas —
/// EF faqat provayder turini (Npgsql) biladi va SQL generatsiya qiladi.
///
/// Ishlatish (lokal .NET o'rnatmasdan, Docker orqali):
///   docker run --rm -v "$PWD":/src -w /src mcr.microsoft.com/dotnet/sdk:9.0 \
///     bash -c "dotnet tool install -g dotnet-ef &amp;&amp; export PATH=\$PATH:/root/.dotnet/tools &amp;&amp; \
///       dotnet ef migrations add Initial \
///         -p src/Zinnur.Infrastructure -s src/Zinnur.WebApi \
///         -o Persistence/Migrations"
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    /// <summary>
    /// Dizayn vaqtidagi ulanish satri. Haqiqiy bazaga ULANMAYDI —
    /// EF undan faqat provayder sozlamalarini oladi.
    /// Kerak bo'lsa `ZINNUR_DESIGNTIME_CONNECTION` env bilan almashtiriladi
    /// (masalan mavjud bazadan `dotnet ef dbcontext scaffold` qilish uchun).
    /// </summary>
    private const string FallbackConnection =
        "Host=localhost;Port=5440;Database=zinnur;Username=zinnur;Password=zinnur";

    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var connection =
            Environment.GetEnvironmentVariable("ZINNUR_DESIGNTIME_CONNECTION")
            ?? FallbackConnection;

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(connection, npgsql =>
                // Migratsiyalar Infrastructure loyihasida saqlanadi (WebApi'da emas) —
                // shu tufayli baza sxemasi infratuzilma qatlamiga tegishli bo'lib qoladi.
                npgsql.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName))
            .Options;

        return new ApplicationDbContext(options);
    }
}
