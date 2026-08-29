using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Zinnur.Application.Telegram;
using Zinnur.Domain.Enums;
using Zinnur.IntegrationTests.Infrastructure;

namespace Zinnur.IntegrationTests.Telegram;

/// <summary>
/// ========================================================================
/// TELEFON + BIR MARTALIK KOD BILAN KIRISH — TO'LIQ OQIM
/// ========================================================================
///
/// 2026-08-13 dan bu platformaga kirishning ASOSIY yo'li (email va parol
/// olib tashlandi). Shuning uchun bu sinf yorliq ISHLATMAYDI: kod
/// HAQIQIY navbatdan (`MessageOutbox`) o'qiladi va HAQIQIY endpointlar
/// orqali tasdiqlanadi.
///
/// ★ NIMA UCHUN KODNI NAVBATDAN O'QIYMIZ: u boshqa hech qayerda yo'q.
/// Redis'da faqat HASH turadi (bu ataylab), javobda esa kod umuman
/// qaytmaydi. Navbatdagi xabar tanasi — kod ko'rinadigan yagona joy, va
/// bu to'g'ri: aynan o'sha matn foydalanuvchiga boradi. Ya'ni test
/// foydalanuvchi ko'radigan narsani o'qiydi.
///
/// ⚠️ Fon worker'i O'CHIQ (`Notifications:Enabled=false`) — xabar
/// yuborilmaydi, faqat navbatga yoziladi. Bizga aynan shu kerak:
/// yuborishning O'ZI `OutboxDispatchTests` da sinalgan.
/// </summary>
public sealed partial class PhoneLoginEndpointsTests(TelegramApiFactory factory)
    : IClassFixture<TelegramApiFactory>
{
    // ================================================================ muvaffaqiyat

    /// <summary>
    /// ★ ASOSIY OQIM: raqam -> kod -> token.
    ///
    /// Bu test butun zanjirni bosib o'tadi: normalizatsiya, profil
    /// qidiruvi, kod yasash, navbatga yozish, kodni tekshirish va token
    /// berish. Zanjirning istalgan bo'g'ini uzilsa u qizaradi.
    /// </summary>
    [Fact]
    public async Task RequestAndVerify_ReturnsWorkingSession()
    {
        var phone = TestPhones.Next();

        var userId = await factory.CreateUserAsync(
            UserRole.Student, rawPhone: phone, telegramId: NextTelegramId());

        var code = await RequestCodeAsync(phone, userId);

        var tokens = await VerifyAsync(phone, code);

        tokens.AccessToken.Should().NotBeNullOrWhiteSpace();
        tokens.RefreshToken.Should().NotBeNullOrWhiteSpace();
        tokens.User.Id.Should().Be(userId);

        // Token HAQIQATAN ishlaydimi — javobning o'zi yetarli dalil emas.
        using var client = factory.CreateAuthorizedClient(tokens.AccessToken);
        var me = await client.GetAsync(new Uri("/api/v1/auth/me", UriKind.Relative));

        me.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// 🔴 XODIM HAM KIRA OLADI — 2026-08-13 dagi eng muhim xulq o'zgarishi.
    ///
    /// Ilgari Telegram kanali `Student` bilan cheklangan edi (audit X-1
    /// mitigatsiyasi), chunki xodimlar email va parol bilan kirardi. Endi
    /// bunday eshik yo'q, ya'ni bu test ish stolida ishlaydigan butun
    /// xodimlar guruhining kira olishini qulflaydi.
    /// </summary>
    [Theory]
    [InlineData(UserRole.Teacher)]
    [InlineData(UserRole.Assistant)]
    [InlineData(UserRole.Academic)]
    [InlineData(UserRole.Admin)]
    public async Task Verify_AllowsStaffRoles(UserRole role)
    {
        var phone = TestPhones.Next();

        var userId = await factory.CreateUserAsync(
            role, rawPhone: phone, telegramId: NextTelegramId());

        var code = await RequestCodeAsync(phone, userId);
        var tokens = await VerifyAsync(phone, code);

        tokens.User.Role.Should().Be(role.ToString());
    }

    /// <summary>
    /// Raqam XOM ko'rinishda kiritilsa ham ishlaydi.
    ///
    /// ★ NIMA UCHUN MUHIM: foydalanuvchi `90 123 45 67` deb yozadi, bazada
    /// esa `+998901234567` turadi. Ikki tomon AYNI `User.NormalizePhone`
    /// dan o'tadi — ikkinchi normalizator yozilsa bu test qizaradi.
    /// </summary>
    [Fact]
    public async Task RequestCode_AcceptsUnnormalizedPhone()
    {
        var phone = TestPhones.Next();                       // +998905000123
        var local = phone[4..];                              // 905000123

        var userId = await factory.CreateUserAsync(
            UserRole.Student, rawPhone: phone, telegramId: NextTelegramId());

        // Bo'shliq va defis bilan — foydalanuvchi qanday yozsa shunday.
        var messy = $"{local[..2]} {local[2..5]}-{local[5..]}";

        var code = await RequestCodeAsync(messy, userId);
        var tokens = await VerifyAsync(messy, code);

        tokens.User.Id.Should().Be(userId);
    }

    // ================================================================ hisob sanash

    /// <summary>
    /// 🔴 NOMA'LUM RAQAM — SABAB OCHIQ AYTILADI (2026-08-29 dan).
    ///
    /// ⚠️ BU TEST AVVAL TESKARISINI TEKSHIRARDI
    /// (`RequestCode_ForUnknownPhone_LooksIdentical`): javob noma'lum va
    /// haqiqiy raqam uchun AYNAN bir xil bo'lishi talab qilinardi.
    ///
    /// Loyiha egasi 2026-08-29 da ayirboshlash tushuntirilgandan keyin
    /// ochiq xabarni talab qildi: o'quvchi "kod kelmadi" deb kutib
    /// o'tirardi va sababni bilmasdi, bot esa AYNI holatda allaqachon
    /// ochiq aytardi — sayt bilan bot bir-biriga zid gapirardi.
    ///
    /// ★ HISOB SANASHGA QARSHI HIMOYA `RequestCode_QuotaAppliesToUnknownPhonesToo`
    ///   testiga KO'CHDI: raqam bo'yicha kvota profil qidiruvidan OLDIN
    ///   ishlaydi, ya'ni ro'yxatni skanerlash uchun har raqamga bir daqiqa
    ///   kerak. O'sha test buzilsa — himoya yo'qolgan bo'ladi.
    /// </summary>
    [Fact]
    public async Task RequestCode_ForUnknownPhone_SaysSo()
    {
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/api/v1/auth/phone/request-code", new { phone = TestPhones.Next() });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("ro'yxatda yo'q",
            "foydalanuvchi kod kutib o'tirmasligi uchun sabab AYNIQSA aytilishi kerak");
    }

    /// <summary>Noma'lum raqamga xabar NAVBATGA HAM tushmaydi.</summary>
    [Fact]
    public async Task RequestCode_ForUnknownPhone_QueuesNothing()
    {
        var before = await OutboxCountAsync();

        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync(
            "/api/v1/auth/phone/request-code", new { phone = TestPhones.Next() });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        (await OutboxCountAsync()).Should().Be(before);
    }

    /// <summary>
    /// 🔴 TELEGRAM BOG'LANMAGAN PROFIL — kod yuborilmaydi va sabab
    /// aytiladi (2026-08-29 dan; ilgari javob AYNI bo'lishi talab
    /// qilinardi).
    ///
    /// Bu shox uchun ochiq xabar ayniqsa foydali: foydalanuvchi nima
    /// qilishini biladi — «Telegram orqali kirish» tugmasi bosilsa, bot
    /// raqamni o'zi bog'laydi.
    /// </summary>
    [Fact]
    public async Task RequestCode_WhenTelegramNotLinked_QueuesNothing()
    {
        var phone = TestPhones.Next();
        await factory.CreateUserAsync(UserRole.Teacher, rawPhone: phone, telegramId: null);

        var before = await OutboxCountAsync();

        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync(
            "/api/v1/auth/phone/request-code", new { phone });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await OutboxCountAsync()).Should().Be(before,
            "bog'lanmagan profilga kod yuboradigan manzil yo'q");
    }

    /// <summary>
    /// O'chirilgan profilga ham kod yuborilmaydi — va sabab aytiladi
    /// (2026-08-29 dan; ilgari javob AYNI bo'lishi talab qilinardi).
    /// </summary>
    [Fact]
    public async Task RequestCode_WhenProfileInactive_QueuesNothing()
    {
        var phone = TestPhones.Next();

        await factory.CreateUserAsync(
            UserRole.Student, rawPhone: phone, isActive: false, telegramId: NextTelegramId());

        var before = await OutboxCountAsync();

        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync(
            "/api/v1/auth/phone/request-code", new { phone });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await OutboxCountAsync()).Should().Be(before);
    }

    // ================================================================ kod qoidalari

    /// <summary>Xato kod — 401 va sessiya YO'Q.</summary>
    [Fact]
    public async Task Verify_WithWrongCode_ReturnsUnauthorized()
    {
        var phone = TestPhones.Next();

        var userId = await factory.CreateUserAsync(
            UserRole.Student, rawPhone: phone, telegramId: NextTelegramId());

        var code = await RequestCodeAsync(phone, userId);

        // Boshqa kod: birinchi raqamini o'zgartiramiz (kod baribir 6 xonali).
        var wrong = (code[0] == '9' ? '0' : (char)(code[0] + 1)) + code[1..];

        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync(
            "/api/v1/auth/phone/verify", new { phone, code = wrong });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// 🔴 KOD BIR MARTALIK: ikkinchi marta ishlatilmaydi.
    ///
    /// Bunsiz Telegram tarixida qolgan eski xabar (yoki elkadan qaragan
    /// odam ko'rgan kod) muddati tugagunicha qayta-qayta kirish uchun
    /// yaraydi. Bir martalik kodning butun ma'nosi shu shartda.
    /// </summary>
    [Fact]
    public async Task Verify_CodeCannotBeReused()
    {
        var phone = TestPhones.Next();

        var userId = await factory.CreateUserAsync(
            UserRole.Student, rawPhone: phone, telegramId: NextTelegramId());

        var code = await RequestCodeAsync(phone, userId);

        using var client = factory.CreateClient();

        using var first = await client.PostAsJsonAsync(
            "/api/v1/auth/phone/verify", new { phone, code });
        first.StatusCode.Should().Be(HttpStatusCode.OK);

        using var second = await client.PostAsJsonAsync(
            "/api/v1/auth/phone/verify", new { phone, code });

        second.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "ishlatilgan kod darhol o'chiriladi");
    }

    /// <summary>
    /// 🔴 URINISHLAR CHEGARASI: 5 tadan keyin kod BEKOR bo'ladi.
    ///
    /// ★ ENG MUHIM QISMI — OXIRGI TEKSHIRUV: chegaradan keyin TO'G'RI
    ///   kod ham ishlamaydi. Aks holda hujumchi 5 talik paketlar bilan
    ///   davom etaverardi va chegara faqat sekinlatuvchi bezovtalik
    ///   bo'lib qolardi.
    /// </summary>
    [Fact]
    public async Task Verify_AfterTooManyAttempts_InvalidatesTheCode()
    {
        var phone = TestPhones.Next();

        var userId = await factory.CreateUserAsync(
            UserRole.Student, rawPhone: phone, telegramId: NextTelegramId());

        var code = await RequestCodeAsync(phone, userId);

        using var client = factory.CreateClient();

        // 5 ta noto'g'ri urinish (`PhoneLoginCodeStore.MaxAttempts`).
        for (var attempt = 0; attempt < 5; attempt++)
        {
            using var wrong = await client.PostAsJsonAsync(
                "/api/v1/auth/phone/verify", new { phone, code = "000000" });

            wrong.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        using var blocked = await client.PostAsJsonAsync(
            "/api/v1/auth/phone/verify", new { phone, code });

        blocked.StatusCode.Should().Be(HttpStatusCode.TooManyRequests,
            "chegaradan keyin TO'G'RI kod ham qabul qilinmaydi — kod bekor qilingan");
    }

    // ================================================================ kvota

    /// <summary>
    /// 🔴 QAYTA YUBORISH OYNASI — RAQAM bo'yicha, IP bo'yicha EMAS.
    ///
    /// Bu chegara HTTP siyosatidan MUSTAQIL va aynan shuning uchun kerak:
    /// IP cheklovi reverse-proxy ortida ishlamaydi (hamma bitta bo'limga
    /// tushadi), IP almashtirish esa arzon. Raqam bo'yicha oyna esa
    /// hujumchi qayerdan kelishidan qat'i nazar ishlaydi va bitta odamning
    /// telefoniga xabar yog'dirishning oldini oladi.
    /// </summary>
    [Fact]
    public async Task RequestCode_SecondRequestWithinCooldown_IsRejected()
    {
        var phone = TestPhones.Next();

        await factory.CreateUserAsync(
            UserRole.Student, rawPhone: phone, telegramId: NextTelegramId());

        using var client = factory.CreateClient();

        using var first = await client.PostAsJsonAsync(
            "/api/v1/auth/phone/request-code", new { phone });
        first.StatusCode.Should().Be(HttpStatusCode.OK);

        using var second = await client.PostAsJsonAsync(
            "/api/v1/auth/phone/request-code", new { phone });

        second.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);

        second.Headers.RetryAfter.Should().NotBeNull(
            "klient qancha kutishni bilishi kerak — \"biroz kuting\" foydasiz matn");
    }

    /// <summary>
    /// 🔴 KVOTA MAVJUD BO'LMAGAN RAQAMGA HAM QO'LLANADI — ENDI BU
    /// HISOB SANASHGA QARSHI YAGONA HIMOYA (2026-08-29 dan).
    ///
    /// Ilgari himoya ikki qatlamli edi: javoblar bir xil bo'lardi VA
    /// kvota ishlardi. Javoblar ochiq qilingandan keyin (loyiha egasining
    /// talabi) faqat kvota qoldi.
    ///
    /// ⚠️ BU TEST BUZILSA — REJANI EMAS, KODNI TUZATING. U buzilishi
    /// `TryReserveAsync` profil qidiruvidan KEYINGA surilganini bildiradi;
    /// o'sha tartibda raqamlar ro'yxatini cheklovsiz skanerlash mumkin
    /// bo'lib qoladi. Tartib `PhoneLoginService.RequestCodeAsync` da
    /// izohlangan.
    /// </summary>
    [Fact]
    public async Task RequestCode_QuotaAppliesToUnknownPhonesToo()
    {
        var phone = TestPhones.Next();          // hech kimga tegishli emas

        using var client = factory.CreateClient();

        using var first = await client.PostAsJsonAsync(
            "/api/v1/auth/phone/request-code", new { phone });
        first.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "noma'lum raqam — sabab ochiq aytiladi");

        using var second = await client.PostAsJsonAsync(
            "/api/v1/auth/phone/request-code", new { phone });

        second.StatusCode.Should().Be(HttpStatusCode.TooManyRequests,
            "kvota raqam bo'yicha va PROFIL QIDIRUVIDAN OLDIN ishlashi kerak — "
            + "aks holda ro'yxatni cheklovsiz skanerlash mumkin bo'lardi");
    }

    // ================================================================ bog'lanish uzilishi

    /// <summary>
    /// 🔴 KOD BERILGANDAN KEYIN TELEGRAM UZILSA — KOD ISHLAMAYDI.
    ///
    /// "Bog'lanishni uzish" amalining butun ma'nosi kirish huquqini olib
    /// qo'yish (`User.UnlinkTelegram` izohi). Bu tekshiruvsiz uzilgan
    /// hisob qo'lidagi kod bilan yana bir marta kirib olardi — ya'ni
    /// o'quv bo'limi "uzdim" desa ham, keyingi 5 daqiqa eshik ochiq
    /// qolardi.
    /// </summary>
    [Fact]
    public async Task Verify_AfterTelegramUnlinked_IsRejected()
    {
        var phone = TestPhones.Next();

        var userId = await factory.CreateUserAsync(
            UserRole.Student, rawPhone: phone, telegramId: NextTelegramId());

        var code = await RequestCodeAsync(phone, userId);

        // Bog'lanishni to'g'ridan-to'g'ri uzamiz (API yo'li
        // `UserTelegramUnlinkTests` da alohida sinalgan).
        await factory.WithDbAsync(async db =>
        {
            var user = await db.Users.FirstAsync(u => u.Id == userId);
            user.UnlinkTelegram(DateTimeOffset.UtcNow);
            return await db.SaveChangesAsync();
        });

        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync(
            "/api/v1/auth/phone/verify", new { phone, code });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ================================================================ yordamchi

    /// <summary>
    /// Kod so'raydi va uni NAVBATDAGI xabar matnidan ajratib oladi.
    /// </summary>
    private async Task<string> RequestCodeAsync(string phone, long userId)
    {
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/api/v1/auth/phone/request-code", new { phone });

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            await response.Content.ReadAsStringAsync());

        var body = await factory.WithDbAsync(db => db.MessageOutbox
            .AsNoTracking()
            .Where(m => m.RecipientUserId == userId
                     && m.TemplateKey == TelegramTemplates.LoginCode)
            .OrderByDescending(m => m.Id)
            .Select(m => m.Body)
            .FirstOrDefaultAsync());

        body.Should().NotBeNull("kod xabari navbatga tushishi kerak");

        var match = CodePattern().Match(body!);

        match.Success.Should().BeTrue(
            "xabar matnida 6 xonali kod `<code>` tegi ichida bo'lishi kerak");

        return match.Groups[1].Value;
    }

    private async Task<AuthTokens> VerifyAsync(string phone, string code)
    {
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/api/v1/auth/phone/verify", new { phone, code });

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            await response.Content.ReadAsStringAsync());

        return (await response.Content.ReadFromJsonAsync<AuthTokens>())!;
    }

    private Task<int> OutboxCountAsync() =>
        factory.WithDbAsync(db => db.MessageOutbox
            .CountAsync(m => m.TemplateKey == TelegramTemplates.LoginCode));

    /// <summary>
    /// Har testga betakror Telegram ID (`IX_Users_TelegramId` — unikal).
    /// `TelegramWorld.NextUpdateId` bilan AYNI naqsh.
    /// </summary>
    private static long NextTelegramId() => Interlocked.Increment(ref _telegramId);

    private static long _telegramId =
        DateTimeOffset.UtcNow.ToUnixTimeSeconds() * 100 % 900_000_000 + 700_000_000;

    /// <summary>
    /// Xabar matnidagi kod: <c>&lt;code&gt;123456&lt;/code&gt;</c>.
    /// Manba-generatsiyali regex (CA1859/SYSLIB1045) — har chaqiruvda
    /// qaytadan kompilyatsiya qilinmaydi.
    /// </summary>
    [GeneratedRegex(@"<code>(\d{6})</code>", RegexOptions.CultureInvariant)]
    private static partial Regex CodePattern();
}
