using System.Buffers;
using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Zinnur.Application.Common.Interfaces;
using Zinnur.Application.Common.Models;
using Zinnur.Infrastructure.Options;

namespace Zinnur.Infrastructure.Services;

/// <summary>
/// LiveKit uchun HS256 JWT yasaydi (SPEC 7-bo'lim).
///
/// ======================================================================
/// NIMA UCHUN TOKEN QO'LDA YIG'ILADI (kutubxonasiz)
/// ======================================================================
/// LiveKit grant'lari `video` claim'i ichida JSON OBYEKT bo'lishi va
/// kalitlari AYNAN camelCase bo'lishi shart:
///     roomJoin, room, canPublish, canSubscribe, canPublishData, roomAdmin
///
/// `JwtSecurityTokenHandler` bilan yozilganda `Claim` qiymati DOIM satr
/// hisoblanadi va JSON `"video":"{\"roomJoin\":true}"` ko'rinishida —
/// ya'ni obyekt emas, SATR bo'lib chiqadi. LiveKit bunday tokenni
/// XATO BERMASDAN rad etadi: klient shunchaki "ulanmadi" holatida qoladi
/// va logda hech narsa yozilmaydi. Bu xatoni topish uchun soatlab vaqt ketadi.
///
/// Shuning uchun payload `Utf8JsonWriter` bilan bayt darajasida yoziladi —
/// natija 100% bashorat qilinadi va kutubxona versiyasi o'zgarsa ham
/// buzilmaydi. Snake_case yoki PascalCase'ga aylantiruvchi hech qanday
/// serializer siyosati oraliqda yo'q.
///
/// Chiqadigan payload (aniq shakl):
/// {
///   "iss":"&lt;ApiKey&gt;", "sub":"&lt;Identity&gt;", "name":"&lt;DisplayName&gt;",
///   "nbf":1730000000, "exp":1730021600,
///   "video":{"roomJoin":true,"room":"&lt;RoomName&gt;","canPublish":true,
///            "canSubscribe":true,"canPublishData":true,"roomAdmin":false}
/// }
///
/// ══════════════════════════════════════════════════════════════════════════
/// ★★ KALIT VA SIR HAR TOKENDA QAYTA O'QILADI
/// (<see cref="IRuntimeOptions{TOptions}"/>).
///
/// Ilgari sir konstruktorda bir marta baytga o'girilib, SINGLETON xizmatga
/// qotib qolardi. LiveKit kalitlari esa AYLANTIRILADIGAN (rotate) ma'lumot:
/// server tomonidagi `LIVEKIT_KEYS` almashtirilgach, biz ham darhol yangi
/// juftlik bilan imzolashimiz kerak. Aks holda hamma jonli dars bir zumda
/// uzilardi va sababi hech qayerda ko'rinmasdi (LiveKit yaroqsiz tokenni
/// XATO BERMASDAN rad etadi).
///
/// ⚠️ Kesim bitta chaqiruv ichida BIR MARTA olinadi: `iss` claim'i
/// (<c>ApiKey</c>) va imzo kaliti (<c>ApiSecret</c>) AYNI juftlikdan
/// chiqishi SHART — aralashib ketsa token yaroqsiz bo'lardi.
///
/// ★ MANZILLAR (<c>Url</c>, <c>PublicUrl</c>) bazadan boshqarilmaydi —
/// sabab <c>RuntimeLiveKitOptions</c> izohida.
/// ══════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class LiveKitTokenService : ILiveKitTokenService
{
    /// <summary>Zaxira muddat — chaqiruvchi <c>Ttl</c> bermasa.</summary>
    /// <remarks>
    /// ⚠️ 2026-08-22 DA 6 SOATDAN 2 SOATGA TUSHIRILDI.
    ///
    /// Eski izoh "LiveKit tokeni faqat XONAGA KIRISH uchun, sessiya
    /// boshlangandan keyin uning muddati ta'sir qilmaydi" deb yozgan edi va
    /// bu TO'G'RI — lekin xulosasi noto'g'ri edi. Aynan "faqat kirish uchun"
    /// bo'lgani sababli muddat MUHIM: guruhdan chiqarilgan yoki qarzi uchun
    /// bloklangan o'quvchi eski tokeni bilan XONAGA QAYTA KIRA olardi va bu
    /// bizning API'ni butunlay chetlab o'tardi (`DEPLOY_UBUNTU.md` Ilova A,
    /// 4-risk).
    ///
    /// ★ HAQIQIY MUDDAT ENDI SHU YERDA HISOBLANMAYDI: yagona chaqiruvchi —
    ///   <c>LiveSessionService.CreateJoinTokenAsync</c> — uni darsning
    ///   tugash vaqtidan kelib chiqib beradi (<c>JoinTokenTtl</c>). Bu
    ///   qiymat faqat kimdir <c>Ttl</c> ni unutgan holat uchun qoladi va
    ///   ataylab qisqartirildi: eng uzun dars ham 2 soatdan oshmaydi
    ///   (80 daqiqa + 10 daqiqa uzaytirish + zaxira).
    /// </remarks>
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromHours(2);

    /// <summary>{"alg":"HS256","typ":"JWT"} — o'zgarmas, bir marta hisoblanadi.</summary>
    private static readonly string EncodedHeader =
        Base64Url.EncodeToString(Encoding.UTF8.GetBytes("{\"alg\":\"HS256\",\"typ\":\"JWT\"}"));

    private readonly IRuntimeOptions<LiveKitOptions> _options;

    public LiveKitTokenService(IRuntimeOptions<LiveKitOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options;
    }

    /// <inheritdoc />
    /// <remarks>
    /// ATAYLAB <c>PublicUrl</c> (ichki <c>Url</c> EMAS): bu qiymat
    /// <c>LiveKitJoinDto.ServerUrl</c> ichida BRAUZERGA ketadi. Prod'da sahifa
    /// HTTPS orqali beriladi va brauzer <c>ws://</c> ni "mixed content" deb
    /// bloklaydi — video ochilmaydi. Ichki <c>http://livekit:7880</c> esa
    /// konteyner tarmog'idan tashqarida umuman mavjud emas.
    /// </remarks>
    public string ServerUrl => _options.Current.EffectivePublicUrl;

    /// <inheritdoc />
    public string CreateAccessToken(LiveKitTokenRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Kesim BIR MARTA — `iss` va imzo kaliti AYNI juftlikdan (izoh yuqorida).
        var settings = _options.Current;

        var now = DateTimeOffset.UtcNow;
        var expiresAt = now.Add(request.Ttl ?? DefaultTtl);

        var payload = new ArrayBufferWriter<byte>(initialCapacity: 512);

        using (var json = new Utf8JsonWriter(payload))
        {
            json.WriteStartObject();

            // `iss` — LiveKit serveri shu kalit nomi bo'yicha sirni topadi.
            json.WriteString("iss", settings.ApiKey);

            // `sub` — ishtirokchi identity'si. Xonada TAKRORLANMAS bo'lishi shart:
            // bir xil identity bilan ikkinchi ulanish birinchisini xonadan
            // chiqarib yuboradi (LiveKit qoidasi). Bizda bu userId.
            json.WriteString("sub", request.Identity);

            // `name` — xonada ko'rinadigan ism.
            json.WriteString("name", request.DisplayName);

            json.WriteNumber("nbf", now.ToUnixTimeSeconds());
            json.WriteNumber("exp", expiresAt.ToUnixTimeSeconds());

            // ---- `video` grant'i: KALITLAR camelCase, aks holda LiveKit JIM rad etadi ----
            json.WriteStartObject("video");
            json.WriteBoolean("roomJoin", true);
            json.WriteString("room", request.RoomName);
            json.WriteBoolean("canPublish", request.CanPublish);
            json.WriteBoolean("canSubscribe", true);
            json.WriteBoolean("canPublishData", true);      // chat/signal ma'lumotlari uchun
            json.WriteBoolean("roomAdmin", request.IsHost); // host xonani boshqara oladi
            json.WriteEndObject();

            json.WriteEndObject();
        }

        var signingInput = string.Concat(EncodedHeader, ".", Base64Url.EncodeToString(payload.WrittenSpan));

        Span<byte> signature = stackalloc byte[HMACSHA256.HashSizeInBytes];

        HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(settings.ApiSecret),
            Encoding.UTF8.GetBytes(signingInput),
            signature);

        return string.Concat(signingInput, ".", Base64Url.EncodeToString(signature));
    }
}
