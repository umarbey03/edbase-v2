using Microsoft.AspNetCore.SignalR;
using Zinnur.Application.Common.Exceptions;
using Zinnur.Domain.Exceptions;

namespace Zinnur.WebApi.Hubs;

/// <summary>
/// ========================================================================
/// HUB XATOLARINI KLIENTGA TUSHUNARLI QILIB YETKAZISH
/// ========================================================================
///
/// Use-case istisnolarini <see cref="HubException"/> ga o'giradi.
///
/// ★ NIMA UCHUN KERAK: SignalR FAQAT <see cref="HubException"/> ning
/// matnini klientga uzatadi. Boshqa har qanday istisno o'rniga
/// "An unexpected error occurred invoking 'JoinThread'" degan UMUMIY satr
/// ketadi (prod'da <c>EnableDetailedErrors=false</c>). Ya'ni ruxsat yo'qligi,
/// tezlik chegarasi va haqiqiy server nosozligi klient uchun BIR XIL
/// ko'rinardi: UI "ruxsat yo'q" deb ayta olmasdi va foydalanuvchi qayta-qayta
/// urinaverardi.
///
/// REST tomonda bu ishni <c>ExceptionHandlingMiddleware</c> qiladi, lekin u
/// hub chaqiruvlariga UMUMAN tegmaydi — middleware quvuri SignalR
/// chaqiruvining ichida ishlamaydi. Shuning uchun tarjima shu yerda.
///
/// ★ NIMA UCHUN ALOHIDA SINF (hub ichidagi <c>private</c> metod EMAS):
/// AYNI muammo ikkala hub'da bor edi. <see cref="GroupChatHub"/> da tarjima
/// yozilgan, <see cref="LiveClassHub"/> da esa YO'Q edi — natijada dars
/// xonasiga kira olmagan o'quvchi "ruxsatingiz yo'q" o'rniga umumiy xato
/// ko'rardi. Qoida ikki joyda qo'lda yozilsa, uchinchi hub qo'shilganda
/// yana unutilardi. Endi u BITTA joyda va
/// <c>HubErrorTranslationTests</c> uni ikkala hub uchun ham qulflaydi.
///
/// ★ KUTILMAGAN ISTISNOLAR ATAYLAB USHLANMAYDI: ular SignalR'ga o'tib,
/// logga (va Sentry'ga) to'liq stack bilan tushishi kerak — va klientga
/// tafsilot ketmasin. Shuning uchun bu yerda <c>catch (Exception)</c> yo'q:
/// ro'yxat ATAYLAB yopiq.
///
/// ★ ASL ISTISNO <c>InnerException</c> da SAQLANADI: foydalanuvchi
/// tushunarli o'zbekcha xabar oladi, log esa haqiqiy sababni (turi va
/// steki bilan) ko'radi. Tashlab yuborilsa, "klientda 'ruxsat yo'q'
/// chiqdi — qaysi tekshiruvdan?" degan savolga javob topib bo'lmasdi.
/// </summary>
internal static class HubErrors
{
    /// <summary>Natija QAYTARADIGAN use-case chaqiruvi uchun.</summary>
    public static async Task<T> TranslateAsync<T>(Func<Task<T>> action)
    {
        ArgumentNullException.ThrowIfNull(action);

        try
        {
            return await action();
        }
        catch (ForbiddenException ex)
        {
            throw Wrap(ex);
        }
        catch (NotFoundException ex)
        {
            throw Wrap(ex);
        }
        catch (TooManyRequestsException ex)
        {
            throw Wrap(ex);
        }
        catch (ConflictException ex)
        {
            throw Wrap(ex);
        }
        catch (ValidationException ex)
        {
            throw Wrap(ex);
        }
        catch (DomainException ex)
        {
            // Bo'sh xabar shu yerdan keladi — foydalanuvchi sababni ko'rsin.
            throw Wrap(ex);
        }
    }

    /// <summary>
    /// Natija QAYTARMAYDIGAN use-case chaqiruvi uchun (masalan davomat yozish).
    ///
    /// Generic variantga o'raladi — catch ro'yxati IKKI nusxa bo'lsa, biriga
    /// yangi istisno turi qo'shilib ikkinchisiga qo'shilmay qolardi va
    /// nosozlik faqat bitta hub metodida ko'rinardi.
    /// </summary>
    public static async Task TranslateAsync(Func<Task> action)
    {
        ArgumentNullException.ThrowIfNull(action);

        await TranslateAsync(async () =>
        {
            await action();
            return true;
        });
    }

    /// <summary>
    /// SINXRON domain qoidasi uchun (masalan <c>ChatMessage.NormalizeBody</c>
    /// bo'sh matnda <see cref="DomainException"/> tashlaydi).
    ///
    /// Delegat ATAYLAB ichkarida chaqiriladi: <c>Translate(Normalize(body))</c>
    /// deb yozilsa istisno tarjimadan OLDIN ko'tarilardi va tarjima umuman
    /// ishlamasdi.
    /// </summary>
    public static T Translate<T>(Func<T> action)
    {
        ArgumentNullException.ThrowIfNull(action);

        // To'liq sinxron: `Task.FromResult` delegat ICHIDA chaqiriladi, ya'ni
        // istisno yuqoridagi catch ro'yxatiga tushadi. Kutish bo'lmagani
        // uchun `GetResult()` bloklanmaydi va istisnoni `AggregateException`
        // ga o'ramasdan asl holida qaytaradi.
        return TranslateAsync(() => Task.FromResult(action()))
            .GetAwaiter().GetResult();
    }

    private static HubException Wrap(Exception ex) => new(ex.Message, ex);
}
