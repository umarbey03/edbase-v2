using Zinnur.Application.Notifications;

namespace Zinnur.UnitTests.Notifications;

/// <summary>
/// Qayta urinish jadvali (eksponensial backoff).
///
/// NIMA UCHUN ALOHIDA SINALADI: bu qoida sof funksiya, lekin uning xatosi
/// juda qimmat. Kechikish juda qisqa bo'lsa — Telegram uzilganda navbat
/// sekundiga minglab urinish bilan o'zimizni chegaraga urardi; juda uzun
/// bo'lsa — "15 daqiqada dars boshlanadi" eslatmasi dars tugagach kelardi.
/// Chegara holati (urinishlar tugashi) esa "zaharli xabar" navbatni abadiy
/// band qilib qo'yishining oldini oladi.
/// </summary>
public class OutboxRetryPolicyTests
{
    /// <summary>★ Birinchi yiqilishdan keyin qisqa tanaffus — 1 daqiqa.</summary>
    [Fact]
    public void NextDelay_AfterFirstFailure_IsOneMinute() =>
        OutboxRetryPolicy.NextDelay(1).Should().Be(TimeSpan.FromMinutes(1));

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 5)]
    [InlineData(3, 15)]
    [InlineData(4, 60)]
    public void NextDelay_GrowsExponentially(int failedAttempts, int expectedMinutes) =>
        OutboxRetryPolicy.NextDelay(failedAttempts)
            .Should().Be(TimeSpan.FromMinutes(expectedMinutes));

    /// <summary>Har keyingi kutish oldingisidan UZUN bo'lishi shart.</summary>
    [Fact]
    public void NextDelay_EachStepIsLongerThanPrevious()
    {
        var previous = TimeSpan.Zero;

        for (var attempt = 1; attempt < OutboxRetryPolicy.MaxAttempts; attempt++)
        {
            var delay = OutboxRetryPolicy.NextDelay(attempt);

            delay.Should().NotBeNull();
            delay!.Value.Should().BeGreaterThan(previous);

            previous = delay.Value;
        }
    }

    /// <summary>
    /// ★ ENG MUHIMI: urinishlar tugaganda <c>null</c> qaytadi — xabar
    /// <c>Failed</c> ga o'tadi. Bunsiz yaroqsiz chat_id li bitta xabar
    /// navbatni MANGU band qilib turardi (eski tizimdagi holat).
    /// </summary>
    [Fact]
    public void NextDelay_AfterLastAttempt_IsNull() =>
        OutboxRetryPolicy.NextDelay(OutboxRetryPolicy.MaxAttempts).Should().BeNull();

    [Fact]
    public void MaxAttempts_IsFive() =>
        OutboxRetryPolicy.MaxAttempts.Should().Be(5);

    /// <summary>
    /// Hisoblagich buzuq (0 yoki manfiy) bo'lsa ham manfiy kutish yoki
    /// indeks xatosi bo'lmasin — birinchi qadamga qaytadi.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void NextDelay_WithBrokenCounter_FallsBackToFirstStep(int failedAttempts) =>
        OutboxRetryPolicy.NextDelay(failedAttempts).Should().Be(TimeSpan.FromMinutes(1));
}
