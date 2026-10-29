using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Zinnur.Application.Interactive.Content;
using Zinnur.Infrastructure.Options;

namespace Zinnur.Infrastructure.Services;

/// <summary>
/// Interaktiv darslik imzosi (2026-10-29).
///
/// ★ KALIT JWT SIRIDAN HOSIL QILINADI (yangi sir sozlanmaydi), lekin to'g'ridan
///   to'g'ri ishlatilmaydi: <c>HMAC(sir, "zinnur:interactive:v1")</c>. Shu tufayli
///   darslik belgisi hech qachon JWT yoki media chiptasi imzosi bilan bir xil
///   bo'lmaydi, ular bir-birining o'rnida ishlamaydi.
/// ⚠️ JWT siri almashtirilsa audio belgilari va chiptalar o'zgaradi — klient
///   birlikni qayta yuklaganda yangilarini oladi, ma'lumot yo'qolmaydi.
/// </summary>
public sealed class HmacInteractiveSigner(IOptions<JwtOptions> options) : IInteractiveSigner
{
    private readonly byte[] _key = HMACSHA256.HashData(
        Encoding.UTF8.GetBytes((options ?? throw new ArgumentNullException(nameof(options))).Value.Secret),
        Encoding.UTF8.GetBytes("zinnur:interactive:v1"));

    public byte[] Sign(string purpose, string message) =>
        HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(purpose + "\n" + message));
}
