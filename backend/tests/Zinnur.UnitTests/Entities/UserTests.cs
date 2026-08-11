using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;
using Zinnur.Domain.Exceptions;

namespace Zinnur.UnitTests.Entities;

/// <summary>
/// <see cref="User"/> — sessiyani bekor qilish (`TokenVersion`) qoidalari.
///
/// NIMA UCHUN MUHIM: eski tizimda "Chiqish" faqat cookie'ni o'chirardi va
/// JWT yana 14 kun yaroqli qolardi — o'g'irlangan tokenni bekor qilishning
/// iloji yo'q edi. Endi `ver` claim'i `TokenVersion` bilan solishtiriladi,
/// shuning uchun rol/parol o'zgarganda hisoblagich OSHISHI SHART.
/// </summary>
public class UserTests
{
    private static User NewUser(UserRole role = UserRole.Student) => new()
    {
        FullName = "Ali Valiyev",
        Email = "ali@zinnur.uz",
        PasswordHash = "$2a$11$eskiHash",
        Role = role,
    };

    // ------------------------------------------------------------------ InvalidateTokens

    [Fact]
    public void InvalidateTokens_IncrementsTokenVersion()
    {
        var user = NewUser();

        user.InvalidateTokens();

        user.TokenVersion.Should().Be(1);
    }

    [Fact]
    public void InvalidateTokens_CalledThreeTimes_IncrementsEachTime()
    {
        var user = NewUser();

        user.InvalidateTokens();
        user.InvalidateTokens();
        user.InvalidateTokens();

        user.TokenVersion.Should().Be(3);
    }

    // ------------------------------------------------------------------ ChangeRole

    [Fact]
    public void ChangeRole_ToDifferentRole_SetsTheNewRole()
    {
        var user = NewUser(UserRole.Student);

        user.ChangeRole(UserRole.Teacher);

        user.Role.Should().Be(UserRole.Teacher);
    }

    /// <summary>
    /// Rol o'zgargach eski token yangi huquqlarni (yoki eskisini) olib
    /// yurmasligi kerak — masalan rolidan mahrum qilingan admin.
    /// </summary>
    [Fact]
    public void ChangeRole_ToDifferentRole_BumpsTokenVersion()
    {
        var user = NewUser(UserRole.Admin);

        user.ChangeRole(UserRole.Student);

        user.TokenVersion.Should().Be(1);
    }

    [Fact]
    public void ChangeRole_ToDifferentRole_SetsUpdatedAt()
    {
        var user = NewUser(UserRole.Student);

        user.ChangeRole(UserRole.Assistant);

        user.UpdatedAt.Should().NotBeNull();
    }

    /// <summary>
    /// Rol o'zgarmagan bo'lsa hech kimni tizimdan chiqarib yuborish shart emas:
    /// CRM'da ro'yxatni ommaviy saqlash rolni "o'zgartirmasdan" qayta yozadi.
    /// </summary>
    [Fact]
    public void ChangeRole_ToSameRole_DoesNotBumpTokenVersion()
    {
        var user = NewUser(UserRole.Teacher);

        user.ChangeRole(UserRole.Teacher);

        user.TokenVersion.Should().Be(0);
    }

    [Fact]
    public void ChangeRole_ToSameRole_DoesNotTouchUpdatedAt()
    {
        var user = NewUser(UserRole.Teacher);

        user.ChangeRole(UserRole.Teacher);

        user.UpdatedAt.Should().BeNull();
    }

    [Theory]
    [InlineData(UserRole.Student, UserRole.Teacher)]
    [InlineData(UserRole.Teacher, UserRole.Assistant)]
    [InlineData(UserRole.Assistant, UserRole.Academic)]
    [InlineData(UserRole.Academic, UserRole.Admin)]
    [InlineData(UserRole.Admin, UserRole.Student)]
    public void ChangeRole_BetweenAnyTwoDifferentRoles_BumpsTokenVersion(UserRole from, UserRole to)
    {
        var user = NewUser(from);

        user.ChangeRole(to);

        user.TokenVersion.Should().Be(1);
    }

    // ------------------------------------------------------------------ SetPassword

    [Fact]
    public void SetPassword_StoresTheNewHash()
    {
        var user = NewUser();

        user.SetPassword("$2a$11$yangiHash");

        user.PasswordHash.Should().Be("$2a$11$yangiHash");
    }

    /// <summary>
    /// Parol almashtirilgach barcha eski qurilmalardagi sessiyalar o'lishi kerak —
    /// bu "parolim o'g'irlangan" holatidagi yagona himoya.
    /// </summary>
    [Fact]
    public void SetPassword_BumpsTokenVersion()
    {
        var user = NewUser();

        user.SetPassword("$2a$11$yangiHash");

        user.TokenVersion.Should().Be(1);
    }

    [Fact]
    public void SetPassword_SetsUpdatedAt()
    {
        var user = NewUser();

        user.SetPassword("$2a$11$yangiHash");

        user.UpdatedAt.Should().NotBeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("   ")]
    [InlineData("\t")]
    [InlineData("\r\n")]
    public void SetPassword_WithEmptyOrWhitespaceHash_ThrowsDomainException(string newHash)
    {
        var user = NewUser();

        var act = () => user.SetPassword(newHash);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void SetPassword_WithEmptyHash_KeepsTheExistingHash()
    {
        var user = NewUser();
        var originalHash = user.PasswordHash;

        var act = () => user.SetPassword("   ");
        act.Should().Throw<DomainException>();

        user.PasswordHash.Should().Be(originalHash);
    }

    [Fact]
    public void SetPassword_WithEmptyHash_DoesNotBumpTokenVersion()
    {
        var user = NewUser();

        var act = () => user.SetPassword("");
        act.Should().Throw<DomainException>();

        user.TokenVersion.Should().Be(0);
    }

    [Fact]
    public void TokenVersion_OnNewUser_StartsAtZero()
    {
        var user = NewUser();

        user.TokenVersion.Should().Be(0);
    }

    // ------------------------------------------------------------------ Telegram

    private static readonly DateTimeOffset Now =
        new(2026, 8, 11, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void LinkTelegram_StoresIdUsernameAndTime()
    {
        var user = NewUser();

        user.LinkTelegram(123456789, "@ali_valiyev", Now);

        user.TelegramId.Should().Be(123456789);
        // `@` SAQLANMAYDI — havolani frontend o'zi yasaydi.
        user.TelegramUsername.Should().Be("ali_valiyev");
        user.TelegramLinkedAt.Should().Be(Now);
    }

    [Fact]
    public void LinkTelegram_WithoutUsername_StoresNull()
    {
        var user = NewUser();

        user.LinkTelegram(1, username: null, Now);

        user.TelegramUsername.Should().BeNull();
    }

    [Fact]
    public void LinkTelegram_WithBlankUsername_StoresNull()
    {
        var user = NewUser();

        user.LinkTelegram(1, "   ", Now);

        user.TelegramUsername.Should().BeNull();
    }

    /// <summary>
    /// Telegram 32 belgini kafolatlaydi, lekin qiymat TASHQI tizimdan keladi.
    /// Uzun qiymat istisno ko'tarsa butun webhook yiqilib, o'quvchi bog'lanish
    /// o'rniga jimgina xato olardi — shuning uchun QIRQILADI.
    /// </summary>
    [Fact]
    public void LinkTelegram_WithTooLongUsername_TruncatesInsteadOfThrowing()
    {
        var user = NewUser();

        user.LinkTelegram(1, new string('u', 50), Now);

        user.TelegramUsername.Should().HaveLength(User.MaxTelegramUsernameLength);
    }

    /// <summary>🔴 Uzish — kirish huquqini olib qo'yish, ya'ni sessiyalar o'lishi SHART.</summary>
    [Fact]
    public void UnlinkTelegram_BumpsTokenVersionAndClearsEverything()
    {
        var user = NewUser();
        user.LinkTelegram(999, "ali", Now);

        var (oldId, oldUsername) = user.UnlinkTelegram(Now);

        oldId.Should().Be(999);
        oldUsername.Should().Be("ali");

        user.TelegramId.Should().BeNull();
        user.TelegramUsername.Should().BeNull();
        user.TelegramLinkedAt.Should().BeNull();
        user.TokenVersion.Should().Be(1, "uzilgandan keyin eski kirish tokeni ishlamasligi kerak");
    }

    [Fact]
    public void UnlinkTelegram_WhenNotLinked_Throws()
    {
        var user = NewUser();

        var act = () => user.UnlinkTelegram(Now);

        act.Should().Throw<DomainException>();
        user.TokenVersion.Should().Be(0, "bo'lmagan amal sessiyalarni bekor qilmasligi kerak");
    }

    [Fact]
    public void RefreshTelegramUsername_WhenChanged_ReturnsTrue()
    {
        var user = NewUser();
        user.LinkTelegram(1, "eski_nom", Now);

        var changed = user.RefreshTelegramUsername("yangi_nom");

        changed.Should().BeTrue();
        user.TelegramUsername.Should().Be("yangi_nom");
    }

    /// <summary>Har <c>/start</c> bekorga <c>UPDATE</c> yozmasligi kerak.</summary>
    [Fact]
    public void RefreshTelegramUsername_WhenSame_ReturnsFalse()
    {
        var user = NewUser();
        user.LinkTelegram(1, "ayni_nom", Now);

        user.RefreshTelegramUsername("@ayni_nom").Should().BeFalse();
    }

    /// <summary>Bog'lanmagan profilga username yozilmasligi kerak.</summary>
    [Fact]
    public void RefreshTelegramUsername_WhenNotLinked_DoesNothing()
    {
        var user = NewUser();

        user.RefreshTelegramUsername("kimdir").Should().BeFalse();
        user.TelegramUsername.Should().BeNull();
    }
}
