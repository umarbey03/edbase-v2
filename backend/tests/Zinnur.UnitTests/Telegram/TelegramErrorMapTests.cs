using Zinnur.Application.Telegram;

namespace Zinnur.UnitTests.Telegram;

/// <summary>
/// Telegram xato kodlarining <c>Retry</c>/<c>Permanent</c> ga xaritalanishi.
///
/// ★ NIMA UCHUN MUHIM: noto'g'ri xaritalash ikki tomonga ham zarar beradi.
/// "Doimiy" xatoni qayta urinsak — bitta bloklangan foydalanuvchi tufayli
/// navbat 1.3 soat band bo'ladi. "Vaqtinchalik" xatoni yakuniy deb hisoblasak
/// — Telegram bir daqiqaga o'chib qo'yganda BUTUN kunlik eslatma yo'qoladi.
/// </summary>
public class TelegramErrorMapTests
{
    [Theory]
    [InlineData(200)]
    [InlineData(201)]
    public void FromStatus_WithSuccess_IsDelivered(int status)
    {
        TelegramErrorMap.FromStatus(status, description: null).Delivered.Should().BeTrue();
    }

    /// <summary>429 — biz juda tez yubordik, xato bizda emas.</summary>
    [Fact]
    public void FromStatus_WithTooManyRequests_IsRetryable()
    {
        var result = TelegramErrorMap.FromStatus(429, "Too Many Requests: retry after 30", retryAfter: 30);

        result.Delivered.Should().BeFalse();
        result.Retryable.Should().BeTrue();
        result.Reason.Should().Contain("retry_after=30s",
            "operator navbatdagi `LastError` dan sababni ko'rishi kerak");
    }

    [Fact]
    public void FromStatus_WithTooManyRequests_WithoutRetryAfter_IsRetryable()
    {
        var result = TelegramErrorMap.FromStatus(429, description: null);

        result.Retryable.Should().BeTrue();
    }

    [Theory]
    [InlineData(500)]
    [InlineData(502)]
    [InlineData(503)]
    [InlineData(504)]
    public void FromStatus_WithServerError_IsRetryable(int status)
    {
        var result = TelegramErrorMap.FromStatus(status, "Bad Gateway");

        result.Delivered.Should().BeFalse();
        result.Retryable.Should().BeTrue("Telegram tomonidagi uzilish o'tib ketadi");
    }

    /// <summary>
    /// 400 — "chat topilmadi" yoki "bot bloklangan". Qayta urinish HOLATNI
    /// O'ZGARTIRMAYDI, faqat navbatni band qiladi.
    /// </summary>
    [Theory]
    [InlineData(400, "Bad Request: chat not found")]
    [InlineData(403, "Forbidden: bot was blocked by the user")]
    [InlineData(404, "Not Found")]
    [InlineData(409, "Conflict")]
    public void FromStatus_WithClientError_IsPermanent(int status, string description)
    {
        var result = TelegramErrorMap.FromStatus(status, description);

        result.Delivered.Should().BeFalse();
        result.Retryable.Should().BeFalse();
        result.Reason.Should().Contain(description);
    }

    /// <summary>401 — bot tokeni noto'g'ri, ya'ni KONFIGURATSIYA xatosi.</summary>
    [Fact]
    public void FromStatus_WithUnauthorized_IsPermanent()
    {
        TelegramErrorMap.FromStatus(401, "Unauthorized").Retryable.Should().BeFalse();
    }

    /// <summary>Sabab `LastError` ustuniga (500 belgi) sig'ishi kerak.</summary>
    [Fact]
    public void FromStatus_WithVeryLongDescription_TrimsReason()
    {
        var result = TelegramErrorMap.FromStatus(400, new string('x', 5000));

        result.Reason!.Length.Should().BeLessThan(500);
    }

    [Fact]
    public void FromStatus_WithoutDescription_StillExplainsStatus()
    {
        var result = TelegramErrorMap.FromStatus(400, description: null);

        result.Reason.Should().Contain("400");
    }
}
