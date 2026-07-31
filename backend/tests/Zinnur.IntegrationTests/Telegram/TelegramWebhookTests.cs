using System.Net;
using Zinnur.Application.Telegram;
using Zinnur.Domain.Enums;

namespace Zinnur.IntegrationTests.Telegram;

/// <summary>
/// Telegram webhook: sir, bog'lash oqimi va idempotentlik — HAQIQIY baza bilan.
///
/// ★ BU FAYL ESKI TIZIMNING X-1 ZAIFLIGINI QO'RIQLAYDI: telefon faqat
/// Telegram tasdiqlagan kontaktdan olinadi va kontakt EGASI xabar
/// YUBORUVCHISI bilan mos kelishi shart.
/// </summary>
public sealed class TelegramWebhookTests(TelegramApiFactory factory)
    : IClassFixture<TelegramApiFactory>
{
    // ================================================================= sir

    [Fact]
    public async Task Webhook_WithoutSecretHeader_IsForbidden()
    {
        var response = await factory.PostUpdateAsync(
            TelegramApiFactory.TextUpdate(TelegramApiFactory.NextUpdateId(), 1, "/start"),
            secret: null);

        response.Status.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Webhook_WithWrongSecret_IsForbidden()
    {
        var response = await factory.PostUpdateAsync(
            TelegramApiFactory.TextUpdate(TelegramApiFactory.NextUpdateId(), 1, "/start"),
            secret: "boshqa-sir");

        response.Status.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// Sir uzunligi bir xil, mazmuni boshqa — doimiy vaqtdagi solishtiruv
    /// baribir rad etishi kerak.
    /// </summary>
    [Fact]
    public async Task Webhook_WithSameLengthWrongSecret_IsForbidden()
    {
        var wrong = new string('x', TelegramApiFactory.WebhookSecret.Length);

        var response = await factory.PostUpdateAsync(
            TelegramApiFactory.TextUpdate(TelegramApiFactory.NextUpdateId(), 1, "/start"),
            secret: wrong);

        response.Status.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>Sir bilan kelgan so'rov rad etilmaydigan bo'lsa ham hech narsa yozmasligi mumkin.</summary>
    [Fact]
    public async Task Webhook_WithCorrectSecret_ReturnsOk()
    {
        var response = await factory.PostUpdateAsync(
            TelegramApiFactory.UnknownUpdate(TelegramApiFactory.NextUpdateId()));

        response.Status.Should().Be(HttpStatusCode.OK);
        response.Outcome.Should().Be("Ignored");
    }

    // ================================================================= bog'lash

    [Fact]
    public async Task Webhook_WithOwnContact_LinksStudent()
    {
        const string Phone = "+998901110001";
        var studentId = await factory.CreateUserAsync(UserRole.Student, Phone);
        var telegramId = NewTelegramId();
        var updateId = TelegramApiFactory.NextUpdateId();

        var response = await factory.PostUpdateAsync(
            TelegramApiFactory.ContactUpdate(updateId, telegramId, Phone, contactUserId: telegramId));

        response.Status.Should().Be(HttpStatusCode.OK);
        response.Outcome.Should().Be("Linked");

        (await factory.TelegramIdOfAsync(studentId)).Should().Be(telegramId);

        (await factory.QueuedTemplateAsync(updateId))
            .Should().Be(TelegramTemplates.ContactLinked, "o'quvchiga javob navbatga yozilishi kerak");
    }

    /// <summary>
    /// ★★ ENG MUHIM XAVFSIZLIK TESTI (audit: X-1).
    ///
    /// Telegram'da BOSHQA odamning kontakt kartasini yuborish mumkin.
    /// `contact.user_id != from.id` bo'lsa so'rov RAD ETILISHI va profil
    /// TEGILMASLIGI shart — aks holda hujumchi jabrlanuvchining kontaktini
    /// yuborib, uning akkauntini o'ziga bog'lab olardi.
    /// </summary>
    [Fact]
    public async Task Webhook_WithSomeoneElsesContact_IsRejected()
    {
        const string VictimPhone = "+998901110002";
        var victimId = await factory.CreateUserAsync(UserRole.Student, VictimPhone);

        var attackerTelegramId = NewTelegramId();
        var victimTelegramId = NewTelegramId();
        var updateId = TelegramApiFactory.NextUpdateId();

        // Hujumchi O'Z nomidan, LEKIN jabrlanuvchining kontaktini yuboradi.
        var response = await factory.PostUpdateAsync(
            TelegramApiFactory.ContactUpdate(
                updateId, attackerTelegramId, VictimPhone, contactUserId: victimTelegramId));

        response.Status.Should().Be(HttpStatusCode.OK, "Telegram'ga baribir 200 qaytadi");
        response.Outcome.Should().Be("ContactMismatch");

        (await factory.TelegramIdOfAsync(victimId)).Should().BeNull(
            "begona kontakt orqali profil HECH QACHON bog'lanmasligi kerak");
    }

    /// <summary>
    /// <c>user_id</c> umuman yo'q — bu telefon kitobidan qo'lda kiritilgan
    /// yozuv, ya'ni raqam egaligi TASDIQLANMAGAN.
    /// </summary>
    [Fact]
    public async Task Webhook_WithContactWithoutUserId_IsRejected()
    {
        const string Phone = "+998901110003";
        var studentId = await factory.CreateUserAsync(UserRole.Student, Phone);

        var response = await factory.PostUpdateAsync(
            TelegramApiFactory.ContactUpdate(
                TelegramApiFactory.NextUpdateId(), NewTelegramId(), Phone, contactUserId: null));

        response.Outcome.Should().Be("ContactMismatch");
        (await factory.TelegramIdOfAsync(studentId)).Should().BeNull();
    }

    /// <summary>
    /// Telefon NORMALIZATSIYA qilingan ustun bo'yicha topiladi:
    /// bazada `+998 90 111 00 04`, botdan `998901110004` kelsa ham mos keladi.
    /// </summary>
    [Fact]
    public async Task Webhook_MatchesPhoneByNormalizedColumn()
    {
        var studentId = await factory.CreateUserAsync(UserRole.Student, "+998 90 111 00 04");
        var telegramId = NewTelegramId();

        var response = await factory.PostUpdateAsync(
            TelegramApiFactory.ContactUpdate(
                TelegramApiFactory.NextUpdateId(), telegramId, "998901110004", telegramId));

        response.Outcome.Should().Be("Linked");
        (await factory.TelegramIdOfAsync(studentId)).Should().Be(telegramId);
    }

    /// <summary>Telegram raqamni ba'zan `+` siz yuboradi — natija bir xil bo'lishi kerak.</summary>
    [Fact]
    public async Task Webhook_MatchesPhoneWithoutPlusPrefix()
    {
        var studentId = await factory.CreateUserAsync(UserRole.Student, "998901110005");
        var telegramId = NewTelegramId();

        var response = await factory.PostUpdateAsync(
            TelegramApiFactory.ContactUpdate(
                TelegramApiFactory.NextUpdateId(), telegramId, "+998901110005", telegramId));

        response.Outcome.Should().Be("Linked");
        (await factory.TelegramIdOfAsync(studentId)).Should().Be(telegramId);
    }

    /// <summary>Raqam ro'yxatda bo'lmasa AKKAUNT YARATILMAYDI.</summary>
    [Fact]
    public async Task Webhook_WithUnknownPhone_DoesNotCreateAccount()
    {
        var before = await factory.CountUsersAsync();
        var updateId = TelegramApiFactory.NextUpdateId();
        var telegramId = NewTelegramId();

        var response = await factory.PostUpdateAsync(
            TelegramApiFactory.ContactUpdate(updateId, telegramId, "+998995559999", telegramId));

        response.Outcome.Should().Be("PhoneNotFound");
        (await factory.CountUsersAsync()).Should().Be(before);

        (await factory.QueuedTemplateAsync(updateId))
            .Should().Be(TelegramTemplates.ContactUnknown,
                "foydalanuvchi nima qilishni bilishi uchun aniq javob olishi kerak");
    }

    /// <summary>
    /// ★ XODIM RAQAMI Telegram orqali BOG'LANMAYDI. Xodimlar email+parol
    /// bilan kiradi; aks holda Telegram kanali orqali xodim huquqiga yo'l
    /// ochilardi (eski tizimda aynan shunday edi).
    /// </summary>
    [Theory]
    [InlineData(UserRole.Admin, "+998901110011")]
    [InlineData(UserRole.Academic, "+998901110012")]
    [InlineData(UserRole.Teacher, "+998901110013")]
    [InlineData(UserRole.Assistant, "+998901110014")]
    public async Task Webhook_WithStaffPhone_DoesNotLink(UserRole role, string phone)
    {
        var staffId = await factory.CreateUserAsync(role, phone);
        var telegramId = NewTelegramId();

        var response = await factory.PostUpdateAsync(
            TelegramApiFactory.ContactUpdate(
                TelegramApiFactory.NextUpdateId(), telegramId, phone, telegramId));

        response.Outcome.Should().Be("StaffPhone");
        (await factory.TelegramIdOfAsync(staffId)).Should().BeNull();
    }

    [Fact]
    public async Task Webhook_WithInactiveStudent_DoesNotLink()
    {
        const string Phone = "+998901110006";
        var studentId = await factory.CreateUserAsync(UserRole.Student, Phone, isActive: false);
        var telegramId = NewTelegramId();

        var response = await factory.PostUpdateAsync(
            TelegramApiFactory.ContactUpdate(
                TelegramApiFactory.NextUpdateId(), telegramId, Phone, telegramId));

        response.Outcome.Should().Be("Inactive");
        (await factory.TelegramIdOfAsync(studentId)).Should().BeNull();
    }

    // ================================================================= unikallik

    /// <summary>
    /// BITTA TELEGRAM — BITTA O'QUVCHI. Bog'langan Telegram akkaunt boshqa
    /// raqam bilan kelsa RAD ETILADI (unikal indeks ham to'sardi, lekin
    /// foydalanuvchi tushunarli javob olishi kerak).
    /// </summary>
    [Fact]
    public async Task Webhook_WhenTelegramAlreadyLinked_RefusesSecondProfile()
    {
        var telegramId = NewTelegramId();

        await factory.CreateUserAsync(UserRole.Student, "+998901110007", telegramId: telegramId);
        var secondId = await factory.CreateUserAsync(UserRole.Student, "+998901110008");

        var response = await factory.PostUpdateAsync(
            TelegramApiFactory.ContactUpdate(
                TelegramApiFactory.NextUpdateId(), telegramId, "+998901110008", telegramId));

        response.Outcome.Should().Be("TelegramTaken");
        (await factory.TelegramIdOfAsync(secondId)).Should().BeNull();
    }

    /// <summary>
    /// BITTA O'QUVCHI — BITTA TELEGRAM. Qayta bog'lash AVTOMATIK
    /// BAJARILMAYDI: raqam boshqa odamga o'tib ketgan bo'lishi mumkin
    /// (operator ishlatilmagan raqamni qayta sotadi), shuning uchun eski
    /// bog'lanishni faqat o'quv bo'limi bekor qiladi.
    /// </summary>
    [Fact]
    public async Task Webhook_WhenProfileHasAnotherTelegram_RefusesRebind()
    {
        const string Phone = "+998901110009";
        var oldTelegramId = NewTelegramId();
        var newTelegramId = NewTelegramId();

        var studentId = await factory.CreateUserAsync(
            UserRole.Student, Phone, telegramId: oldTelegramId);

        var response = await factory.PostUpdateAsync(
            TelegramApiFactory.ContactUpdate(
                TelegramApiFactory.NextUpdateId(), newTelegramId, Phone, newTelegramId));

        response.Outcome.Should().Be("ProfileTaken");

        (await factory.TelegramIdOfAsync(studentId)).Should().Be(oldTelegramId,
            "eski bog'lanish JIMGINA almashtirilmasligi kerak");
    }

    /// <summary>Ayni odam ayni raqamni qayta yuborsa — xato emas, holat o'zgarmaydi.</summary>
    [Fact]
    public async Task Webhook_WithSameContactAgain_IsIdempotent()
    {
        const string Phone = "+998901110010";
        var studentId = await factory.CreateUserAsync(UserRole.Student, Phone);
        var telegramId = NewTelegramId();

        var first = await factory.PostUpdateAsync(
            TelegramApiFactory.ContactUpdate(
                TelegramApiFactory.NextUpdateId(), telegramId, Phone, telegramId));

        var second = await factory.PostUpdateAsync(
            TelegramApiFactory.ContactUpdate(
                TelegramApiFactory.NextUpdateId(), telegramId, Phone, telegramId));

        first.Outcome.Should().Be("Linked");
        second.Outcome.Should().Be("AlreadyLinked");

        (await factory.TelegramIdOfAsync(studentId)).Should().Be(telegramId);
    }

    // ================================================================= idempotentlik

    /// <summary>
    /// ★ Telegram javobni ololmasa AYNI yangilanishni QAYTA yuboradi.
    /// Ikkinchisi HECH NARSA qilmasligi va navbatga ikkinchi javob
    /// TUSHMASLIGI kerak.
    /// </summary>
    [Fact]
    public async Task Webhook_WithRepeatedUpdateId_IsProcessedOnce()
    {
        const string Phone = "+998901110020";
        var studentId = await factory.CreateUserAsync(UserRole.Student, Phone);
        var telegramId = NewTelegramId();
        var updateId = TelegramApiFactory.NextUpdateId();

        var json = TelegramApiFactory.ContactUpdate(updateId, telegramId, Phone, telegramId);

        var first = await factory.PostUpdateAsync(json);
        var second = await factory.PostUpdateAsync(json);

        first.Outcome.Should().Be("Linked");
        second.Outcome.Should().Be("Duplicate");

        (await factory.TelegramIdOfAsync(studentId)).Should().Be(telegramId);
        (await factory.QueuedCountAsync(updateId)).Should().Be(1,
            "bitta yangilanishga ko'pi bilan bitta javob");
    }

    // ================================================================= /start

    [Fact]
    public async Task Webhook_WithStartCommand_QueuesContactRequest()
    {
        var updateId = TelegramApiFactory.NextUpdateId();

        var response = await factory.PostUpdateAsync(
            TelegramApiFactory.TextUpdate(updateId, NewTelegramId(), "/start"));

        response.Outcome.Should().Be("Greeted");
        (await factory.QueuedTemplateAsync(updateId)).Should().Be(TelegramTemplates.StartUnlinked);
    }

    /// <summary>
    /// <c>/start &lt;payload&gt;</c> qabul qilinadi, lekin payload SHAXSNI
    /// ANIQLAMAYDI — javob bog'lanmagan foydalanuvchiniki bilan bir xil.
    /// </summary>
    [Fact]
    public async Task Webhook_WithStartPayload_DoesNotIdentifyUser()
    {
        var updateId = TelegramApiFactory.NextUpdateId();

        var response = await factory.PostUpdateAsync(
            TelegramApiFactory.TextUpdate(updateId, NewTelegramId(), "/start ref_kampaniya42"));

        response.Outcome.Should().Be("Greeted");
        (await factory.QueuedTemplateAsync(updateId)).Should().Be(TelegramTemplates.StartUnlinked);
    }

    [Fact]
    public async Task Webhook_WithStartFromLinkedStudent_OffersApp()
    {
        var telegramId = NewTelegramId();
        await factory.CreateUserAsync(UserRole.Student, "+998901110030", telegramId: telegramId);

        var updateId = TelegramApiFactory.NextUpdateId();

        var response = await factory.PostUpdateAsync(
            TelegramApiFactory.TextUpdate(updateId, telegramId, "/start"));

        response.Outcome.Should().Be("Greeted");
        (await factory.QueuedTemplateAsync(updateId)).Should().Be(TelegramTemplates.StartLinked);
    }

    /// <summary>Oddiy matn TELEFON DEB QABUL QILINMAYDI — faqat yordam javobi.</summary>
    [Fact]
    public async Task Webhook_WithPlainTextPhoneNumber_DoesNotLink()
    {
        const string Phone = "+998901110040";
        var studentId = await factory.CreateUserAsync(UserRole.Student, Phone);
        var updateId = TelegramApiFactory.NextUpdateId();

        var response = await factory.PostUpdateAsync(
            TelegramApiFactory.TextUpdate(updateId, NewTelegramId(), Phone));

        response.Outcome.Should().Be("Helped");

        (await factory.TelegramIdOfAsync(studentId)).Should().BeNull(
            "qo'lda yozilgan raqam HECH QACHON shaxsni tasdiqlamaydi (audit: X-1b)");
    }

    // ================================================================= guruh chati

    /// <summary>
    /// Guruh chatida bog'lash YO'Q: u yerda istalgan a'zo boshqasining
    /// kontaktini tashlab yuborishi mumkin.
    /// </summary>
    [Fact]
    public async Task Webhook_InGroupChat_IsIgnored()
    {
        const string Phone = "+998901110050";
        var studentId = await factory.CreateUserAsync(UserRole.Student, Phone);
        var telegramId = NewTelegramId();

        var response = await factory.PostUpdateAsync(
            TelegramApiFactory.ContactUpdate(
                TelegramApiFactory.NextUpdateId(),
                telegramId,
                Phone,
                telegramId,
                chatId: -100123456,
                chatType: "supergroup"));

        response.Outcome.Should().Be("Ignored");
        (await factory.TelegramIdOfAsync(studentId)).Should().BeNull();
    }

    /// <summary>Har test o'z Telegram ID'sini olsin (unikal indeks to'qnashmasin).</summary>
    private static long NewTelegramId() => 7_000_000_000L + TelegramApiFactory.NextUpdateId() % 900_000_000L;
}
