using System.Text;
using Zinnur.Infrastructure.Options;
using Zinnur.Infrastructure.Services;

namespace Zinnur.IntegrationTests.Recordings;

/// <summary>
/// ========================================================================
/// WEBHOOK: FAOL VA ZAXIRA KALIT JUFTLIGI (LiveKit Cloud, 2026-09-21)
/// ========================================================================
///
/// Serverni almashtirgandan keyin eski serverdan kechikkan hodisalar keladi
/// (birinchi navbatda <c>egress_ended</c>). Ular zaxira juftlik bilan
/// qabul qilinishi, lekin himoya zaiflashmasligi SHART: <c>iss</c> faqat
/// qaysi sirni sinashni tanlaydi, imzo esa o'sha sir bilan tekshiriladi.
/// </summary>
public class LiveKitWebhookStandbyKeyTests
{
    private const string Body = """{"event":"egress_ended","id":"EV_1"}""";

    private static readonly LiveKitOptions Active = new()
    {
        Url = "https://zinnur.livekit.cloud",
        ApiKey = "APIcloud",
        ApiSecret = "cloud-siri-kamida-32-belgi-0123456789",
    };

    private static readonly LiveKitOptions Standby = new()
    {
        Url = "http://livekit:7880",
        ApiKey = "zinnur-self",
        ApiSecret = "self-hosted-siri-kamida-32-belgi-012345",
    };

    private static readonly LiveKitOptions ActiveWithStandby = new()
    {
        Url = Active.Url,
        ApiKey = Active.ApiKey,
        ApiSecret = Active.ApiSecret,
        StandbyApiKey = Standby.ApiKey,
        StandbyApiSecret = Standby.ApiSecret,
    };

    [Fact]
    public void ActivePair_IsAccepted()
    {
        Verify(ActiveWithStandby, RecordingWorld.SignToken(Active, Body)).IsValid.Should().BeTrue();
    }

    /// <summary>Almashtirishdan keyin eski serverdan kelgan hodisa — qabul.</summary>
    [Fact]
    public void StandbyPair_IsAccepted()
    {
        var result = Verify(ActiveWithStandby, RecordingWorld.SignToken(Standby, Body));

        result.IsValid.Should().BeTrue(result.Reason);
    }

    /// <summary>Zaxira sozlanmagan — boshqa server imzosi avvalgidek rad.</summary>
    [Fact]
    public void StandbyPair_WithoutStandbyConfigured_IsRejected()
    {
        Verify(Active, RecordingWorld.SignToken(Standby, Body)).IsValid.Should().BeFalse();
    }

    /// <summary>🔴 `iss` zaxira kalit, lekin imzo faol sir bilan — rad.</summary>
    [Fact]
    public void StandbyIssuer_SignedWithActiveSecret_IsRejected()
    {
        var token = RecordingWorld.SignToken(Active, Body, issuer: Standby.ApiKey);

        Verify(ActiveWithStandby, token).IsValid.Should().BeFalse();
    }

    /// <summary>🔴 `iss` faol kalit, lekin imzo zaxira sir bilan — rad.</summary>
    [Fact]
    public void ActiveIssuer_SignedWithStandbySecret_IsRejected()
    {
        var token = RecordingWorld.SignToken(Standby, Body, issuer: Active.ApiKey);

        Verify(ActiveWithStandby, token).IsValid.Should().BeFalse();
    }

    /// <summary>Zaxira juftlikda ham tana xeshi majburiy.</summary>
    [Fact]
    public void StandbyPair_WithTamperedBody_IsRejected()
    {
        var token = RecordingWorld.SignToken(Standby, bodyForHash: """{"event":"boshqa"}""");

        Verify(ActiveWithStandby, token).IsValid.Should().BeFalse();
    }

    private static Zinnur.Application.Recordings.Services.WebhookVerification Verify(
        LiveKitOptions options, string token) =>
        new LiveKitWebhookVerifier(new FixedOptions(options), TimeProvider.System)
            .Verify("Bearer " + token, Encoding.UTF8.GetBytes(Body));

    private sealed class FixedOptions(LiveKitOptions current) : IRuntimeOptions<LiveKitOptions>
    {
        public LiveKitOptions Current { get; } = current;
    }
}
