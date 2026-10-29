using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Zinnur.Application.Interactive.Content;

namespace Zinnur.UnitTests.Interactive;

/// <summary>Qat'iy kalitli imzo — testlar takrorlanuvchan bo'lsin.</summary>
internal sealed class FixedSigner : IInteractiveSigner
{
    private static readonly byte[] Key = Encoding.UTF8.GetBytes("unit-test-key-0123456789abcdef");

    public byte[] Sign(string purpose, string message) =>
        HMACSHA256.HashData(Key, Encoding.UTF8.GetBytes(purpose + "\n" + message));
}

internal static class InteractiveTestKit
{
    public const long VersionId = 42;

    public static readonly string[] Fixtures =
        ["atf1_birlik_01", "atf1_birlik_28", "atf1_imtihon", "atf2_birlik_05"];

    public static readonly FixedSigner Signer = new();

    public static AudioHandleCodec Audio { get; } = new(Signer);

    public static MatchTokenCodec Match { get; } = new(Signer);

    public static StudentUnitProjector Projector { get; } = new(Audio, Match);

    public static string Raw(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Interactive", "Fixtures", name + ".json"));

    public static JsonObject Load(string name) => UnitJson.Parse(Raw(name));
}
