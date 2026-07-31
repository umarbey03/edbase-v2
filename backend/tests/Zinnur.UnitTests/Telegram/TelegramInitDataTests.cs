using System.Security.Cryptography;
using System.Text;
using Zinnur.Application.Telegram;

namespace Zinnur.UnitTests.Telegram;

/// <summary>
/// Mini App <c>initData</c> imzosining tekshiruvi.
///
/// ★ BU FAYL — TIZIMNING ENG MUHIM HIMOYASINI QO'RIQLAYDI. Agar imzo
/// tekshiruvi buzilsa, istalgan odam o'zini istalgan o'quvchi qilib
/// ko'rsata olardi. Shuning uchun bu yerda "to'g'ri holat o'tadi" degan
/// bitta test YETARLI EMAS — RAD ETISH holatlari alohida-alohida
/// tekshiriladi.
///
/// Testlar imzoni MUSTAQIL (o'z kodi bilan) yasaydi: agar tekshiruvchidagi
/// algoritm o'zgarsa, test yasagan imzo mos kelmay qoladi va xato darhol
/// ko'rinadi.
/// </summary>
public class TelegramInitDataTests
{
    private const string BotToken = "123456789:AAH-test-bot-token-abcdefghijklmnop";

    private static readonly DateTimeOffset Now =
        new(2026, 7, 31, 12, 0, 0, TimeSpan.Zero);

    private static readonly TimeSpan MaxAge = TimeSpan.FromHours(24);

    private const string UserJson =
        """{"id":555000111,"first_name":"Abbos","last_name":"Karimov","username":"abbos","is_bot":false}""";

    // ------------------------------------------------------------------ to'g'ri holat

    /// <summary>
    /// ★ Bu test AYNI PAYTDA "hash `data_check_string` ga qo'shilmaydi"
    /// qoidasini ham qo'riqlaydi: imzo <see cref="Sign"/> da AYNAN
    /// hash'siz maydonlar ustidan hisoblanadi. Agar tekshiruvchi `hash` ni
    /// ham qo'shib yuborsa, bu test darhol yiqiladi.
    /// </summary>
    [Fact]
    public void Verify_WithValidSignature_Succeeds()
    {
        var initData = Build(BotToken, Fields(Now));

        var result = TelegramInitData.Verify(initData, BotToken, Now, MaxAge);

        result.IsValid.Should().BeTrue(result.Reason);
        result.TelegramUserId.Should().Be(555000111);
        result.User!.FirstName.Should().Be("Abbos");
        result.User.Username.Should().Be("abbos");
    }

    /// <summary>Maydonlar tartibi muhim emas — tekshiruvchi ularni o'zi saralaydi.</summary>
    [Fact]
    public void Verify_WithShuffledFieldOrder_Succeeds()
    {
        var fields = Fields(Now);
        var initData = Build(BotToken, fields, shuffle: true);

        TelegramInitData.Verify(initData, BotToken, Now, MaxAge).IsValid.Should().BeTrue();
    }

    // ------------------------------------------------------------------ imzo

    [Fact]
    public void Verify_WithTamperedHash_IsRejected()
    {
        var initData = Build(BotToken, Fields(Now));

        // Oxirgi belgini almashtiramiz (hex bo'lib qoladi, lekin imzo boshqa).
        var broken = initData[..^1] + (initData[^1] == 'a' ? 'b' : 'a');

        var result = TelegramInitData.Verify(broken, BotToken, Now, MaxAge);

        result.IsValid.Should().BeFalse("buzilgan imzo hech qachon qabul qilinmasligi kerak");
    }

    /// <summary>
    /// Imzo TO'G'RI yasalgan, lekin keyin ma'lumot o'zgartirilgan —
    /// eng klassik hujum.
    /// </summary>
    [Fact]
    public void Verify_WhenPayloadChangedAfterSigning_IsRejected()
    {
        var fields = Fields(Now);
        var initData = Build(BotToken, fields);

        // Boshqa foydalanuvchining ID'siga almashtiramiz.
        var tampered = initData.Replace(
            Uri.EscapeDataString(UserJson),
            Uri.EscapeDataString(UserJson.Replace("555000111", "999000999", StringComparison.Ordinal)),
            StringComparison.Ordinal);

        TelegramInitData.Verify(tampered, BotToken, Now, MaxAge).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Verify_WithDifferentBotToken_IsRejected()
    {
        var initData = Build("987654321:BBB-boshqa-bot-tokeni-qqqqqqqqqqqq", Fields(Now));

        TelegramInitData.Verify(initData, BotToken, Now, MaxAge).IsValid.Should().BeFalse();
    }

    /// <summary>
    /// ★★ CHALKASHISHGA QARSHI TEST.
    ///
    /// Telegram Login Widget BOSHQA sxema ishlatadi:
    /// <c>secret_key = SHA256(bot_token)</c>. Agar tekshiruvchi shu
    /// sxemaga o'tib ketsa, Mini App imzosi umuman himoya qilmasdi.
    /// Shu sxema bilan yasalgan imzo RAD ETILISHI shart.
    /// </summary>
    [Fact]
    public void Verify_WithLoginWidgetScheme_IsRejected()
    {
        var fields = Fields(Now);
        var dataCheckString = DataCheckString(fields);

        // Login Widget sxemasi (BIZNIKI EMAS)
        var secret = SHA256.HashData(Encoding.UTF8.GetBytes(BotToken));
        var hash = Hex(HMACSHA256.HashData(secret, Encoding.UTF8.GetBytes(dataCheckString)));

        var initData = Query(fields) + "&hash=" + hash;

        TelegramInitData.Verify(initData, BotToken, Now, MaxAge).IsValid.Should().BeFalse(
            "Mini App sxemasi Login Widget sxemasidan FARQ QILADI");
    }

    /// <summary>
    /// Imzo <c>hash</c> maydonini HAM qo'shib hisoblangan bo'lsa — rad etilsin.
    /// (Ya'ni bizning `data_check_string` imizda `hash` YO'Q.)
    /// </summary>
    [Fact]
    public void Verify_WhenHashIncludedInDataCheckString_IsRejected()
    {
        var fields = Fields(Now);

        const string Placeholder = "deadbeef";
        var dataCheckString = DataCheckString(fields) + "\nhash=" + Placeholder;

        var secret = HMACSHA256.HashData(
            Encoding.UTF8.GetBytes("WebAppData"), Encoding.UTF8.GetBytes(BotToken));

        var hash = Hex(HMACSHA256.HashData(secret, Encoding.UTF8.GetBytes(dataCheckString)));

        TelegramInitData.Verify(Query(fields) + "&hash=" + hash, BotToken, Now, MaxAge)
            .IsValid.Should().BeFalse();
    }

    // ------------------------------------------------------------------ muddat

    /// <summary>
    /// ★ Bir marta o'g'irlangan `initData` imzosi bilan birga ABADIY
    /// yaroqli qolmasligi kerak — aks holda akkaunt umrbod egallab olinardi.
    /// </summary>
    [Fact]
    public void Verify_WithExpiredAuthDate_IsRejected()
    {
        var signedAt = Now.AddHours(-25);
        var initData = Build(BotToken, Fields(signedAt));

        var result = TelegramInitData.Verify(initData, BotToken, Now, MaxAge);

        result.IsValid.Should().BeFalse();
        result.Reason.Should().Contain("muddat");
    }

    [Fact]
    public void Verify_WithAuthDateJustInsideWindow_Succeeds()
    {
        var signedAt = Now.AddHours(-23);

        TelegramInitData.Verify(Build(BotToken, Fields(signedAt)), BotToken, Now, MaxAge)
            .IsValid.Should().BeTrue();
    }

    /// <summary>Soatlar farqi tabiiy — kichik oldinlik qabul qilinadi.</summary>
    [Fact]
    public void Verify_WithSlightlyFutureAuthDate_Succeeds()
    {
        var signedAt = Now.AddMinutes(2);

        TelegramInitData.Verify(Build(BotToken, Fields(signedAt)), BotToken, Now, MaxAge)
            .IsValid.Should().BeTrue();
    }

    [Fact]
    public void Verify_WithFarFutureAuthDate_IsRejected()
    {
        var signedAt = Now.AddHours(2);

        TelegramInitData.Verify(Build(BotToken, Fields(signedAt)), BotToken, Now, MaxAge)
            .IsValid.Should().BeFalse();
    }

    // ------------------------------------------------------------------ shakl

    [Fact]
    public void Verify_WithoutHash_IsRejected()
    {
        var fields = Fields(Now);

        TelegramInitData.Verify(Query(fields), BotToken, Now, MaxAge).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Verify_WithoutUser_IsRejected()
    {
        var fields = new List<KeyValuePair<string, string>>
        {
            new("auth_date", Unix(Now)),
            new("query_id", "AAF"),
        };

        var result = TelegramInitData.Verify(Build(BotToken, fields), BotToken, Now, MaxAge);

        result.IsValid.Should().BeFalse();
        result.Reason.Should().Contain("user");
    }

    [Fact]
    public void Verify_WithoutAuthDate_IsRejected()
    {
        var fields = new List<KeyValuePair<string, string>>
        {
            new("user", UserJson),
            new("query_id", "AAF"),
        };

        TelegramInitData.Verify(Build(BotToken, fields), BotToken, Now, MaxAge)
            .IsValid.Should().BeFalse();
    }

    /// <summary>Bot hisobi o'quvchi bo'la olmaydi.</summary>
    [Fact]
    public void Verify_WithBotUser_IsRejected()
    {
        var fields = new List<KeyValuePair<string, string>>
        {
            new("auth_date", Unix(Now)),
            new("user", """{"id":42,"first_name":"Robot","is_bot":true}"""),
        };

        TelegramInitData.Verify(Build(BotToken, fields), BotToken, Now, MaxAge)
            .IsValid.Should().BeFalse();
    }

    /// <summary>Takroriy maydon — qalbakilashtirishga urinish belgisi.</summary>
    [Fact]
    public void Verify_WithDuplicateField_IsRejected()
    {
        var initData = Build(BotToken, Fields(Now)) + "&auth_date=" + Unix(Now);

        TelegramInitData.Verify(initData, BotToken, Now, MaxAge).IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Verify_WithEmptyInitData_IsRejected(string? initData)
    {
        TelegramInitData.Verify(initData, BotToken, Now, MaxAge).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Verify_WithoutBotToken_IsRejected()
    {
        var initData = Build(BotToken, Fields(Now));

        TelegramInitData.Verify(initData, botToken: "", Now, MaxAge).IsValid.Should().BeFalse();
    }

    /// <summary>Uzun satr HMAC hisobiga umuman tushmasligi kerak (arzon DoS).</summary>
    [Fact]
    public void Verify_WithOversizedInitData_IsRejected()
    {
        var initData = "user=" + new string('x', TelegramInitData.MaxInitDataLength + 1);

        TelegramInitData.Verify(initData, BotToken, Now, MaxAge).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Verify_WithNonHexHash_IsRejected()
    {
        var fields = Fields(Now);

        TelegramInitData.Verify(Query(fields) + "&hash=zzzz", BotToken, Now, MaxAge)
            .IsValid.Should().BeFalse();
    }

    // ================================================================= yordamchi
    //
    // Imzo BU YERDA MUSTAQIL yasaladi — tekshiruvchining kodidan
    // foydalanilmaydi. Aks holda ikkalasi bir xil xatoga yo'l qo'ysa,
    // test baribir yashil bo'lardi.

    private static List<KeyValuePair<string, string>> Fields(DateTimeOffset signedAt) =>
    [
        new("query_id", "AAHdF6IQAAAAAN0XohDhrOrc"),
        new("user", UserJson),
        new("auth_date", Unix(signedAt)),
        new("chat_type", "sender"),
    ];

    private static string Unix(DateTimeOffset value) =>
        value.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static string DataCheckString(IEnumerable<KeyValuePair<string, string>> fields) =>
        string.Join('\n', fields
            .OrderBy(f => f.Key, StringComparer.Ordinal)
            .Select(f => $"{f.Key}={f.Value}"));

    private static string Query(IEnumerable<KeyValuePair<string, string>> fields) =>
        string.Join('&', fields.Select(f =>
            $"{Uri.EscapeDataString(f.Key)}={Uri.EscapeDataString(f.Value)}"));

    private static string Build(
        string botToken, List<KeyValuePair<string, string>> fields, bool shuffle = false)
    {
        // secret_key = HMAC_SHA256(key="WebAppData", data=bot_token)  ← Mini App sxemasi
        var secret = HMACSHA256.HashData(
            Encoding.UTF8.GetBytes("WebAppData"), Encoding.UTF8.GetBytes(botToken));

        var hash = Hex(HMACSHA256.HashData(
            secret, Encoding.UTF8.GetBytes(DataCheckString(fields))));

        var ordered = shuffle
            ? fields.OrderByDescending(f => f.Key, StringComparer.Ordinal).ToList()
            : fields;

        return Query(ordered) + "&hash=" + hash;
    }

    private static string Hex(byte[] value) => Convert.ToHexString(value).ToLowerInvariant();
}
