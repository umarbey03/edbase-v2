using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Zinnur.WebApi.Hubs;

/// <summary>
/// ========================================================================
/// BILDIRISHNOMA KANALI — <c>/hubs/notifications</c>
/// ========================================================================
///
/// ══════════════════════════════════════════════════════════════════════
/// 🔴 BU REPOZITORIYDAGI BIRINCHI FOYDALANUVCHI DARAJASIDAGI HUB.
///
/// <see cref="LiveClassHub"/> va <see cref="GroupChatHub"/> — ikkalasi ham
/// <c>Clients.Group</c> ishlatadi va shu sababli ikkalasida ham
/// «obuna bo'lish» metodi bor (<c>JoinSession</c>, <c>JoinThread</c>):
/// ularning qamrovi DARS yoki OQIM, ya'ni klient qaysi xonaga kirishini
/// aytishi kerak.
///
/// Bu yerda qamrov — ODAM. Ya'ni klient serverga hech nima aytmaydi:
/// <c>Clients.User(id)</c> ulanishni TOKENDAN aniqlaydi.
/// ══════════════════════════════════════════════════════════════════════
///
/// ── NIMA UCHUN HUB'DA BITTA HAM METOD YO'Q ─────────────────────────────
///
/// Bu bo'sh sinf EMAS — u ikkita ish bajaradi:
///
///  1) <c>MapHub&lt;NotificationHub&gt;</c> uchun MANZIL beradi va
///     <c>[Authorize]</c> bilan ulanishni tokenga bog'laydi. Autentifikatsiya
///     uchun QO'SHIMCHA KOD KERAK EMAS: <c>Program.cs</c> dagi
///     <c>OnMessageReceived</c> allaqachon <c>/hubs</c> bilan boshlanadigan
///     HAR yo'l uchun query'dagi tokenni qabul qiladi (aynan shu sabab
///     <see cref="GroupChatHub"/> qo'shilganda ham auth kodi yozilmagan).
///
///  2) <see cref="IHubContext{THub}"/> uchun TUR beradi —
///     <see cref="NotificationNotifier"/> aynan shu turdagi kontekstni
///     so'raydi.
///
/// «O'qildi» belgilash hub'da ATAYLAB YO'Q: u HOLATNI O'ZGARTIRADI va
/// REST'da allaqachon bor (<c>POST /api/v1/notifications/read</c>). Ikki
/// yo'l bo'lsa ruxsat va idempotentlik qoidasi ikki joyda yozilardi —
/// <see cref="GroupChatHub"/> da aynan shu sabab tarix va tezlik chegarasi
/// hub'ga ko'chirilmagan.
///
/// ── ULANISH KIMGA TEGISHLI EKANI QANDAY ANIQLANADI ─────────────────────
///
/// SignalR ning standart <c>DefaultUserIdProvider</c> si ulanish
/// egasini <c>ClaimTypes.NameIdentifier</c> claim'idan oladi. Bizning
/// tokenda u <c>sub</c> sifatida yoziladi (<c>JwtTokenService</c>) va
/// <c>JwtBearer</c> ning kirish xaritasi (inbound claim map) uni
/// <c>ClaimTypes.NameIdentifier</c> ga o'giradi — <c>Program.cs</c> dagi
/// <c>TokenValidationParameters</c> izohida yozilganidek.
///
/// ★ BU TAXMIN EMAS, ISHLAB TURGAN FAKT: <see cref="GroupChatHub"/> va
///   <see cref="LiveClassHub"/> foydalanuvchini AYNAN shu claim'dan
///   o'qiydi va ularning jonli hub testlari (`GroupChatRealtimeTests`,
///   `LiveChatBroadcastTests`) o'tadi. Ya'ni claim ulanish ichida
///   to'ldirilgani allaqachon isbotlangan; `Clients.User(...)` esa AYNI
///   claim'ni o'qiydi.
///
/// ★ SHUNGA QARAMAY ALOHIDA TEST BOR (`NotificationRealtimeTests`):
///   yuqoridagi zanjir uchta mustaqil sozlamaga tayanadi (claim nomi,
///   xaritalash, standart provayder) va ulardan birortasi o'zgarsa
///   bildirishnoma JIMGINA hech kimga bormay qolardi — hub ulanadi,
///   xato chiqmaydi, faqat xabar yo'q. Bu turdagi nosozlikni faqat
///   uchdan-uchgacha test tutadi.
/// </summary>
[Authorize]
public sealed class NotificationHub : Hub
{
    // ★ ATAYLAB BO'SH. Sabab yuqoridagi izohda — bu transport nuqtasi,
    //   xatti-harakat emas. Metod qo'shishdan oldin o'sha izohni o'qing:
    //   holat o'zgartiradigan amal REST'da qoladi.
}
