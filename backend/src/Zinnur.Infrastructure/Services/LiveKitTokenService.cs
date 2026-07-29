using System.Buffers;
using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
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
/// </summary>
public sealed class LiveKitTokenService : ILiveKitTokenService
{
    /// <summary>Default amal qilish muddati (SPEC 7): 6 soat.</summary>
    /// <remarks>
    /// Dars 80 daqiqa + uzaytirish 10 daqiqa. 6 soat qo'yilgani — bir kunlik
    /// jadvalda tokenni qayta so'ramaslik uchun; LiveKit tokeni faqat XONAGA
    /// KIRISH uchun, sessiya boshlangandan keyin uning muddati ta'sir qilmaydi.
    /// </remarks>
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromHours(6);

    /// <summary>{"alg":"HS256","typ":"JWT"} — o'zgarmas, bir marta hisoblanadi.</summary>
    private static readonly string EncodedHeader =
        Base64Url.EncodeToString(Encoding.UTF8.GetBytes("{\"alg\":\"HS256\",\"typ\":\"JWT\"}"));

    private readonly LiveKitOptions _options;
    private readonly byte[] _secret;

    public LiveKitTokenService(IOptions<LiveKitOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options.Value;
        _secret = Encoding.UTF8.GetBytes(_options.ApiSecret);
    }

    /// <inheritdoc />
    /// <remarks>
    /// ATAYLAB <c>PublicUrl</c> (ichki <c>Url</c> EMAS): bu qiymat
    /// <c>LiveKitJoinDto.ServerUrl</c> ichida BRAUZERGA ketadi. Prod'da sahifa
    /// HTTPS orqali beriladi va brauzer <c>ws://</c> ni "mixed content" deb
    /// bloklaydi — video ochilmaydi. Ichki <c>http://livekit:7880</c> esa
    /// konteyner tarmog'idan tashqarida umuman mavjud emas.
    /// </remarks>
    public string ServerUrl => _options.EffectivePublicUrl;

    /// <inheritdoc />
    public string CreateAccessToken(LiveKitTokenRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var now = DateTimeOffset.UtcNow;
        var expiresAt = now.Add(request.Ttl ?? DefaultTtl);

        var payload = new ArrayBufferWriter<byte>(initialCapacity: 512);

        using (var json = new Utf8JsonWriter(payload))
        {
            json.WriteStartObject();

            // `iss` — LiveKit serveri shu kalit nomi bo'yicha sirni topadi.
            json.WriteString("iss", _options.ApiKey);

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
        HMACSHA256.HashData(_secret, Encoding.UTF8.GetBytes(signingInput), signature);

        return string.Concat(signingInput, ".", Base64Url.EncodeToString(signature));
    }
}
