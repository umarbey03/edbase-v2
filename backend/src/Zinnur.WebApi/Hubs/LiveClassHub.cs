using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Zinnur.Application.Common.Interfaces;
using Zinnur.Application.Common.Models;
using Zinnur.Application.LiveSessions.Dtos;
using Zinnur.Application.LiveSessions.Services;
using Zinnur.Domain.Entities;

namespace Zinnur.WebApi.Hubs;

/// <summary>
/// Jonli dars uchun realtime kanal: chat, qo'l ko'tarish, ishtirokchilar ro'yxati.
///
/// ========================================================================
/// 200 FOYDALANUVCHI UCHUN LOYIHALASH QARORLARI (eng muhim qism)
/// ========================================================================
/// Bu hub'ning har bir qarori "200 kishi bir xonada" stsenariysiga qarab
/// olingan. Naif amalga oshirishda quyidagilar tizimni yiqitadi:
///
///  1) TO'LIQ RO'YXAT BROADCAST QILINMAYDI.
///     Naif yo'l: kimdir kirsa — 200 kishilik ro'yxatni 200 kishiga yuborish.
///     Bu 200 x 200 = 40 000 obyekt uzatish, va kirish-chiqish tez-tez bo'ladi.
///     Bu yerda faqat DELTA yuboriladi: kim kirdi/chiqdi + umumiy son.
///     To'liq ro'yxat FAQAT bir marta — <see cref="JoinSession"/> javobida.
///
///  2) PRESENCE REDIS'DA, jarayon xotirasida EMAS.
///     Ikkinchi API instance qo'shilsa, in-memory Dictionary'da har instance
///     o'z ro'yxatini ko'radi va o'quvchilar bir-birini ko'rmaydi.
///
///  3) CHAT BAZAGA FON NAVBATIDA yoziladi.
///     Naif yo'l: xabarni avval bazaga yozib, keyin broadcast qilish.
///     Bunda DB yozuv kechikishi (5-20 ms) butun chat tezligini belgilaydi.
///     Bu yerda: avval broadcast (tez), keyin fon xizmati paketlab yozadi.
///
///  4) RATE-LIMIT SERVERDA (Redis hisoblagichi).
///     Bitta foydalanuvchi sekundiga 50 xabar yuborsa, 200 kishilik xonada
///     bu 10 000 uzatish/sekund. Chegara: 10 sekundda 5 xabar
///     (<see cref="ChatRateMaxMessages"/> — nima uchun aynan shunday, izohi
///     o'sha maydonda).
///
///  5) RUXSAT TEKSHIRUVI JOIN PAYTIDA BIR MARTA.
///     Har xabarda bazaga borish 200 kishida sezilarli yuk. Ulanish
///     kontekstida (<see cref="Context.Items"/>) keshlanadi.
/// ========================================================================
/// </summary>
[Authorize]
public sealed class LiveClassHub(
    ILiveSessionService sessions,
    IPresenceService presence,
    ICacheService cache,
    IChatMessageWriter chatWriter,
    ILogger<LiveClassHub> logger) : Hub
{
    /// <summary>
    /// Chat tezlik chegarasi oynasi. Oyna ichida
    /// <see cref="ChatRateMaxMessages"/> tadan ortiq xabar qabul qilinmaydi.
    /// </summary>
    private static readonly TimeSpan ChatRateWindow = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Bitta oynadagi maksimal xabar soni.
    ///
    /// ★ NIMA UCHUN "1 xabar / 2 sekund" DAN VOZ KECHILDI:
    /// eski chegara qat'iy 2 sekundlik oyna edi va odam tabiiy yozadigan
    /// ketma-ket ikki qatorni ("Assalomu alaykum" + "savolim bor")
    /// IKKINCHISINI rad etardi. Jonli darsda bu "chat sekin ishlayapti"
    /// bo'lib his qilinardi.
    ///
    /// O'RTACHA tezlik O'ZGARMADI: 10 sekundda 5 ta = 2 sekundda 1 ta.
    /// Farqi — qisqa "portlash"ga yo'l qo'yiladi, ya'ni chatning tabiiy
    /// naqshi ishlaydi, flood himoyasi esa joyida qoladi (200 kishilik
    /// xonada har kishi uchun 0.5 xabar/sekund yuqori chegara).
    ///
    /// Shakl <c>GroupChatService</c> dagi bilan bir xil (u yerda 10/10 sek) —
    /// ikki chat ikki xil mantiqda ishlamasin.
    /// </summary>
    private const int ChatRateMaxMessages = 5;

    /// <summary>
    /// Chegaraga urilganda klientga ketadigan matn.
    ///
    /// ★ <see cref="HubException"/> matnini SignalR HAR DOIM klientga uzatadi
    /// (<c>EnableDetailedErrors</c> dan qat'i nazar) — ya'ni foydalanuvchi
    /// xabari rad etilganini KO'RADI, u jimgina yo'qolmaydi.
    /// </summary>
    private static readonly string ChatRateLimitMessage = string.Create(
        CultureInfo.InvariantCulture,
        $"Juda tez yozyapsiz — {ChatRateWindow.TotalSeconds:0} soniyada {ChatRateMaxMessages} ta xabar. Biroz kuting.");

    /// <summary>Klient bergan broadcast kalitining maksimal uzunligi.</summary>
    private const int MaxClientIdLength = 64;

    private const string SessionItemKey = "sessionId";

    // ---------------------------------------------------------------- yordamchi

    /// <summary>
    /// SignalR guruh nomi. <c>internal</c> — hub'dan TASHQARIDA ham kerak:
    /// <see cref="Services.LiveSessionNotifier"/> xuddi shu xonaga xabar
    /// yuboradi. Nom ikki joyda qo'lda yozilsa, biri o'zgarganda ikkinchisi
    /// bo'sh xonaga xabar yuborib turardi va buni hech kim sezmasdi.
    /// </summary>
    internal static string GroupName(long sessionId) =>
        $"session-{sessionId.ToString(CultureInfo.InvariantCulture)}";

    private long UserId =>
        long.Parse(
            Context.User?.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new HubException("Autentifikatsiya talab qilinadi."),
            CultureInfo.InvariantCulture);

    private string DisplayName =>
        Context.User?.FindFirstValue(ClaimTypes.Name) ?? "Noma'lum";

    private string RoleName =>
        Context.User?.FindFirstValue(ClaimTypes.Role) ?? "Student";

    /// <summary>Ulanish kontekstida saqlangan sessiya (ruxsat allaqachon tekshirilgan).</summary>
    private long? CurrentSessionId =>
        Context.Items.TryGetValue(SessionItemKey, out var v) && v is long id ? id : null;

    // ---------------------------------------------------------------- klient -> server

    /// <summary>
    /// Darsga qo'shiladi. Ruxsat SHU YERDA bir marta tekshiriladi.
    /// Javobda to'liq ishtirokchilar ro'yxati qaytadi (keyin faqat delta keladi).
    /// </summary>
    public async Task<JoinSessionResult> JoinSession(long sessionId)
    {
        var userId = UserId;

        // Ruxsat: a'zo yoki host emasmi — Application qatlami hal qiladi (DRY).
        //
        // ★ TARJIMA MAJBURIY (BUG TUZATISHI): bu qatorda ilgari
        // `HubErrors.TranslateAsync` YO'Q edi va izohda "klientga
        // HubException ketadi" deb YOZILGAN edi — lekin bu TO'G'RI EMAS edi.
        // `ForbiddenException`/`NotFoundException` SignalR'ga o'zgarishsiz
        // chiqib ketardi, u esa FAQAT `HubException` matnini uzatadi. Prod'da
        // (`EnableDetailedErrors=false`) o'quvchi "Bu darsga ruxsatingiz yo'q"
        // o'rniga "An unexpected error occurred invoking 'JoinSession'"
        // ko'rardi — ya'ni sababni bilmay qayta-qayta urinaverardi.
        // Batafsil: `HubErrors` sinfi izohi.
        var dto = await HubErrors.TranslateAsync(
            () => sessions.GetAsync(sessionId, userId, Context.ConnectionAborted));

        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(sessionId), Context.ConnectionAborted);
        Context.Items[SessionItemKey] = sessionId;

        var entry = new PresenceEntry(userId, DisplayName, RoleName, HandRaised: false, DateTimeOffset.UtcNow);
        await presence.AddAsync(sessionId, entry, Context.ConnectionAborted);

        // Davomat (host uchun yozilmaydi — servis o'zi hal qiladi).
        // Bu ham AYNI ruxsat tekshiruvidan o'tadi, ya'ni AYNI istisnolarni
        // tashlaydi — tarjimasiz qoldirilsa teshik yarim yopilgan bo'lardi.
        await HubErrors.TranslateAsync(
            () => sessions.RegisterJoinAsync(sessionId, userId, Context.ConnectionAborted));

        var list = await presence.ListAsync(sessionId, Context.ConnectionAborted);

        // DELTA broadcast — o'zidan boshqalarga
        await Clients.OthersInGroup(GroupName(sessionId)).SendAsync(
            "PresenceChanged",
            new PresenceDelta(userId, DisplayName, RoleName, Joined: true, list.Count),
            Context.ConnectionAborted);

        ApiLog.SessionJoined(logger, sessionId, userId, list.Count);

        return new JoinSessionResult(dto, list, list.Count);
    }

    public async Task LeaveSession(long sessionId)
    {
        await RemoveFromSessionAsync(sessionId, UserId);
    }

    /// <summary>
    /// Chat xabari. Server <c>SenderName</c> va <c>SentAt</c> ni O'ZI qo'yadi —
    /// klientga ishonilmaydi (soxta ism yoki vaqt yuborishning oldi olinadi).
    /// </summary>
    /// <param name="sessionId">Dars identifikatori.</param>
    /// <param name="body">Xabar matni (server tozalaydi va kesadi).</param>
    /// <param name="clientId">
    /// Klient yasagan barqaror kalit; broadcast'da o'zgarishsiz qaytariladi.
    /// Nima uchun kerakligi: <see cref="NormalizeClientId"/>.
    /// </param>
    public async Task SendMessage(long sessionId, string body, string? clientId)
    {
        var userId = UserId;

        if (CurrentSessionId != sessionId)
            throw new HubException("Avval darsga qo'shiling.");

        // --- Tezlik chegarasi: oynada N ta xabar (Redis atomar hisoblagichi) ---
        var key = $"chatrate:{sessionId.ToString(CultureInfo.InvariantCulture)}:{userId.ToString(CultureInfo.InvariantCulture)}";
        var hits = await cache.IncrementAsync(key, ChatRateWindow, Context.ConnectionAborted);
        if (hits > ChatRateMaxMessages)
            throw new HubException(ChatRateLimitMessage);

        // Domain qoidasi: bo'shlik kesiladi, 500 belgidan uzuni qirqiladi.
        //
        // ★ TARJIMA: bo'sh matnda `DomainException` ko'tariladi. Tarjimasiz
        // klient sababni ("Xabar bo'sh bo'lishi mumkin emas") emas, umumiy
        // xatoni ko'rardi — yuqoridagi ikki `HubException` bilan bir xil
        // yo'lda bo'lishi uchun bu ham o'giriladi.
        var text = HubErrors.Translate(() => ChatMessage.NormalizeBody(body));

        var broadcastKey = NormalizeClientId(clientId);

        var message = new ChatMessage
        {
            SessionId = sessionId,
            SenderId = userId,
            SenderName = DisplayName,
            Body = text,
            SentAt = DateTimeOffset.UtcNow,
        };

        // 1) AVVAL broadcast — foydalanuvchi kechikishni sezmaydi.
        //    `Id` hali 0 (baza raqamini fon xizmati beradi), shuning uchun
        //    klient uchun kalit — `ClientId`.
        await Clients.Group(GroupName(sessionId)).SendAsync(
            "ChatMessage",
            new ChatMessageDto(0, userId, DisplayName, text, message.SentAt, broadcastKey),
            Context.ConnectionAborted);

        // 2) KEYIN navbatga — fon xizmati paketlab bazaga yozadi
        await chatWriter.EnqueueAsync(message, Context.ConnectionAborted);
    }

    /// <summary>
    /// Broadcast kalitini tayyorlaydi.
    ///
    /// ★ BUG TUZATISHI — "chatda kechikish bor" shikoyatining ILDIZ SABABI.
    ///
    /// Ilgari broadcast'da <c>ChatMessageDto(0, ...)</c> ketardi, ya'ni HAR
    /// xabarning identifikatori BIR XIL (0) edi. Klient esa takrorlarni
    /// identifikator bo'yicha filtrlaydi (`useLiveHub.pushMessage`): birinchi
    /// xabardan keyin 0 "ko'rilgan" ro'yxatiga tushardi va o'sha darsdagi
    /// KEYINGI HAMMA xabar jimgina tashlanardi. Foydalanuvchi buni "xabar
    /// kech keladi" deb his qilardi — aslida xabar umuman kelmasdi va faqat
    /// sahifa yangilanganda (REST tarixi, haqiqiy Id bilan) paydo bo'lardi.
    /// Sim bo'ylab o'lchov 5-7 ms ko'rsatgani uchun nosozlik yuklama testida
    /// ham ko'rinmasdi — u ekranni emas, simni o'lchardi.
    ///
    /// Kalitni KLIENT yasashi bitta o'q bilan ikkinchi muammoni ham yechadi:
    /// yuboruvchi xabarini DARHOL ekranga chiqaradi (optimistik ko'rsatish)
    /// va o'z broadcast'i qaytganda uni AYNI kalit bo'yicha tanib, ikki marta
    /// ko'rsatmaydi.
    ///
    /// ★ KLIENTGA ISHONILMAYDI: shakli buzuq yoki juda uzun kalit rad etiladi
    /// va server o'zi yasaydi — kalit HAR broadcast'da BO'LISHI shart, aks
    /// holda yuqoridagi nosozlik qaytadi. Kalitning noyobligi ham klientga
    /// tashlab qo'yilmaydi: klient uni yuboruvchi identifikatori bilan
    /// birga kalitlaydi, ya'ni birov boshqaning kalitini "band qilib"
    /// uning xabarini bo'g'a olmaydi.
    /// </summary>
    private static string NormalizeClientId(string? raw)
    {
        if (string.IsNullOrEmpty(raw) || raw.Length > MaxClientIdLength)
            return Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);

        foreach (var ch in raw)
        {
            if (!char.IsAsciiLetterOrDigit(ch) && ch != '-')
                return Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        }

        return raw;
    }

    /// <summary>Qo'l ko'tarish / tushirish.</summary>
    public async Task RaiseHand(long sessionId, bool raised)
    {
        var userId = UserId;

        if (CurrentSessionId != sessionId)
            throw new HubException("Avval darsga qo'shiling.");

        await presence.SetHandRaisedAsync(sessionId, userId, raised, Context.ConnectionAborted);

        await Clients.Group(GroupName(sessionId)).SendAsync(
            "HandRaised",
            new HandRaisedEvent(userId, DisplayName, raised),
            Context.ConnectionAborted);
    }

    // ---------------------------------------------------------------- hayot sikli

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (CurrentSessionId is { } sessionId)
        {
            try
            {
                await RemoveFromSessionAsync(sessionId, UserId);
            }
            catch (Exception ex)
            {
                // Uzilish paytidagi xato boshqa foydalanuvchilarga ta'sir qilmasin
                ApiLog.DisconnectCleanupFailed(logger, ex, sessionId);
            }
        }

        await base.OnDisconnectedAsync(exception);
    }

    private async Task RemoveFromSessionAsync(long sessionId, long userId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(sessionId));
        await presence.RemoveAsync(sessionId, userId);
        await sessions.RegisterLeaveAsync(sessionId, userId);

        var count = await presence.CountAsync(sessionId);

        await Clients.OthersInGroup(GroupName(sessionId)).SendAsync(
            "PresenceChanged",
            new PresenceDelta(userId, DisplayName, RoleName, Joined: false, count));

        Context.Items.Remove(SessionItemKey);
    }
}

// ---------------------------------------------------------------- hub shartnomasi

/// <summary><c>JoinSession</c> javobi — to'liq ro'yxat FAQAT shu yerda beriladi.</summary>
public sealed record JoinSessionResult(
    LiveSessionDto Session,
    IReadOnlyList<PresenceEntry> Participants,
    int Count);

/// <summary>Ishtirokchi o'zgarishi — faqat DELTA (to'liq ro'yxat emas).</summary>
public sealed record PresenceDelta(
    long UserId, string DisplayName, string Role, bool Joined, int Count);

public sealed record HandRaisedEvent(long UserId, string DisplayName, bool Raised);

/// <summary>
/// <c>SessionEnded</c> — dars yakunlandi. Klient buni olib video va hub
/// ulanishini yopadi (`useLiveHub.handleSessionEnded`).
///
/// Hub ICHIDAN emas, <see cref="Services.LiveSessionNotifier"/> orqali
/// yuboriladi: darsni yakunlash hub metodi emas, use-case
/// (`LiveSessionService.EndAsync`) — u REST orqali ham, kelajakda fon xizmati
/// orqali ham chaqiriladi.
/// </summary>
public sealed record SessionEndedEvent(long SessionId);
