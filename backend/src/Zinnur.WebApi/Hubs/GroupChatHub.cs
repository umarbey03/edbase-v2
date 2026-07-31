using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Zinnur.Application.GroupChat.Dtos;
using Zinnur.Application.GroupChat.Services;
using Zinnur.Domain.Enums;

namespace Zinnur.WebApi.Hubs;

/// <summary>
/// ========================================================================
/// GURUH CHATI — REALTIME KANAL
/// ========================================================================
///
/// ── NIMA UCHUN ALOHIDA HUB (va <see cref="LiveClassHub"/> KENGAYTIRILMADI)
///
/// Qaror: ALOHIDA hub. Sabablari — muhimidan boshlab:
///
///  1) ULANISH HOLATI BOSHQA. `LiveClassHub` ulanishi BITTA darsga
///     bog'langan (`Context.Items["sessionId"]`), va uning uzilish yo'li
///     DAVOMAT yozadi (`RegisterLeaveAsync`) hamda presence'ni tozalaydi.
///     Guruh chatida esa foydalanuvchi BIR NECHTA oqimga bir vaqtda obuna
///     bo'ladi ("Chatlar" ekranida hammasi ochiq turadi) va hech qanday
///     davomat yo'q. Ikkalasini bitta hub'ga qo'shsam, guruh chatiga
///     ulangan-uzilgan har odam davomat yozuvchi kodga kirardi — ya'ni
///     JONLI SINOVDAN O'TGAN va pul/baho bilan bog'liq oqimga tegilardi.
///
///  2) ULANISH UMRI BOSHQA. Dars hub'i 80 daqiqa yashaydi, guruh chati
///     ilova ochiq turgan butun vaqt. Bitta hub'da `KeepAlive` va
///     `ClientTimeout` sozlamalari ikkala stsenariyga ham noto'g'ri
///     bo'lardi.
///
///  3) NOSOZLIKNING TARQALISHI. Chatdagi xato dars xonasini yiqitmasin.
///
/// ── KAMCHILIK VA U QANDAY QOPLANDI ─────────────────────────────────────
///
/// Alohida hub'ning narxi — takrorlanish. U QAERDA TAKRORLANMAGANI:
///
///   * RUXSAT — `IGroupChatService.ResolveAccessAsync` da, REST bilan
///     BITTA kod. Hub o'z tekshiruvini yozmaydi.
///   * TEZLIK CHEGARASI — `GroupChatService.SendAsync` ichida (Redis),
///     ya'ni REST va hub BITTA budjetni bo'lishadi. Ikki joyda bo'lsa
///     foydalanuvchi ikki yo'ldan yozib chegarani ikkilantirardi.
///   * SAQLASH — o'sha use-case'da.
///   * AUTENTIFIKATSIYA — `Program.cs` dagi `OnMessageReceived` allaqachon
///     `/hubs` bilan boshlanadigan HAR yo'l uchun query'dagi tokenni
///     qabul qiladi; yangi kod kerak bo'lmadi.
///
/// Ya'ni bu yerda faqat TRANSPORT bor: claim'dan Id olish (~10 qator,
/// ataylab takrorlandi — `LiveClassHub` ga tegmaslik uchun) va SignalR
/// guruhiga qo'shish.
///
/// ── OBUNA QAMROVI ──────────────────────────────────────────────────────
///
/// SignalR guruhi = `(GroupId, Channel)` juftligi, GURUH EMAS. Bu KANAL
/// IZOLYATSIYASINING transport tomonidagi ta'minoti: ustoz `Curator`
/// oqimining SignalR guruhiga umuman qo'shilmaydi, ya'ni xabar unga
/// jismonan yetib bormaydi — ruxsat tekshiruvi bir kun buzilib qolsa ham.
/// </summary>
[Authorize]
public sealed class GroupChatHub(
    IGroupChatService chat,
    ILogger<GroupChatHub> logger) : Hub
{
    /// <summary>Ulanish davomida obuna bo'lingan oqimlar.</summary>
    private const string ThreadsItemKey = "groupChatThreads";

    // ---------------------------------------------------------------- yordamchi

    /// <summary>
    /// SignalR guruh nomi. <c>internal</c> — hub'dan TASHQARIDA ham kerak:
    /// <see cref="GroupChatNotifier"/> xuddi shu xonaga xabar yuboradi.
    /// Nom ikki joyda qo'lda yozilsa, biri o'zgarganda ikkinchisi bo'sh
    /// xonaga xabar yuborib turardi va buni hech kim sezmasdi (aynan shu
    /// sabab <see cref="LiveClassHub.GroupName"/> da ham yozilgan).
    /// </summary>
    internal static string ThreadName(long groupId, GroupChatChannel channel) =>
        string.Create(CultureInfo.InvariantCulture, $"gchat-{groupId}-{(int)channel}");

    private long UserId =>
        long.Parse(
            Context.User?.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new HubException("Autentifikatsiya talab qilinadi."),
            CultureInfo.InvariantCulture);

    private HashSet<string> Threads
    {
        get
        {
            if (Context.Items.TryGetValue(ThreadsItemKey, out var value)
                && value is HashSet<string> existing)
            {
                return existing;
            }

            var created = new HashSet<string>(StringComparer.Ordinal);
            Context.Items[ThreadsItemKey] = created;
            return created;
        }
    }

    // ---------------------------------------------------------------- klient -> server

    /// <summary>
    /// Oqimga obuna bo'ladi. Ruxsat SHU YERDA — use-case orqali — tekshiriladi.
    ///
    /// Javobda tarix QAYTMAYDI (dars hub'idagi <c>JoinSession</c> dan farqi):
    /// tarix REST orqali sahifalab olinadi va u kursorli. Hub javobiga
    /// qo'shilsa, sahifalash mantiqi ikki joyda bo'lardi.
    /// </summary>
    /// <returns>Aniqlangan oqim (kanal berilmasa server o'zi tanlaydi).</returns>
    public async Task<GroupChatAccessDto> JoinThread(long groupId, GroupChatChannel? channel)
    {
        var userId = UserId;

        // Ruxsat va kanal — Application qatlami hal qiladi (DRY).
        var access = await HubErrors.TranslateAsync(() => chat.ResolveAccessAsync(
            userId, groupId, channel, Context.ConnectionAborted));

        var thread = ThreadName(access.GroupId, access.Channel);

        await Groups.AddToGroupAsync(Context.ConnectionId, thread, Context.ConnectionAborted);
        Threads.Add(thread);

        GroupChatLog.ThreadJoined(logger, access.GroupId, access.Channel.ToString(), userId);

        return access;
    }

    /// <summary>Oqim obunasini bekor qiladi (foydalanuvchi boshqa chatga o'tdi).</summary>
    public async Task LeaveThread(long groupId, GroupChatChannel channel)
    {
        var thread = ThreadName(groupId, channel);

        await Groups.RemoveFromGroupAsync(Context.ConnectionId, thread, Context.ConnectionAborted);
        Threads.Remove(thread);
    }

    /// <summary>
    /// Xabar yuboradi.
    ///
    /// ★ HUB XABARNI O'ZI TARQATMAYDI. Use-case uni bazaga yozib, keyin
    /// <see cref="IGroupChatNotifier"/> orqali tarqatadi (commit-then-send).
    /// Bu yerda qo'shimcha broadcast qilinsa, xabar IKKI marta ketardi —
    /// va bittasi Id'siz bo'lardi.
    ///
    /// ★ Tezlik chegarasi ham use-case ichida: REST bilan bitta budjet.
    /// </summary>
    public async Task<GroupChatMessageDto> SendMessage(
        long groupId, GroupChatChannel? channel, string body) =>
        await HubErrors.TranslateAsync(() => chat.SendAsync(
            UserId,
            groupId,
            new SendGroupChatMessageRequest(channel, body),
            Context.ConnectionAborted));

    // ---------------------------------------------------------------- xatolarni tarjima qilish
    //
    // ★ TARJIMA <see cref="HubErrors"/> DA — nima uchun kerakligi va nima
    // uchun hub ichidagi `private` metod EMASligi o'sha sinf izohida.
    // Qisqasi: SignalR FAQAT `HubException` matnini klientga uzatadi, va
    // AYNI qoida `LiveClassHub` da ham kerak bo'ldi.
    //
    // ★ ISHLAYOTGANINI `HubErrorTranslationTests` isbotlaydi: u hub
    // metodlarini HAQIQIY use-case bilan chaqirib, klientga `HubException`
    // (turi bo'yicha, matn tarkibi bo'yicha emas) yetishini tekshiradi.
    // Yangi ommaviy metod qo'shilsa, o'sha sinfdagi ro'yxat testi qizaradi.

    // ---------------------------------------------------------------- hayot sikli

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        // ★ HECH QANDAY BAZA YOZUVI YO'Q — `LiveClassHub` dan asosiy farq.
        // SignalR uzilgan ulanishni guruhlardan o'zi chiqaradi, shuning
        // uchun qo'lda tozalash ham shart emas; `Context.Items` ulanish
        // bilan birga yo'qoladi.
        await base.OnDisconnectedAsync(exception);
    }
}

// ---------------------------------------------------------------- hub shartnomasi
//
// Klient tinglaydigan hodisa BITTA: `GroupChatMessage` — tanasi
// `GroupChatMessageDto` (REST javobidagi bilan AYNAN bir xil tur).
//
// ★ NIMA UCHUN ALOHIDA "event" record'i YO'Q (`SessionEndedEvent` dan
// farqi): agar hodisa uchun boshqa shakl yaratilsa, frontend bitta xabarni
// ikki xil ko'rinishda tahlil qilishga majbur bo'lardi — biri REST
// sahifasidan, ikkinchisi realtime'dan — va ular vaqt o'tib ajralib
// ketardi.
