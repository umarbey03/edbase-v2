using System.Globalization;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;
using Zinnur.Application.Common.Exceptions;
using Zinnur.Application.Courses.Services;

namespace Zinnur.WebApi.Media;

/// <summary>
/// ========================================================================
/// MEDIA OQIMI JAVOBINI YOZISH — `200` / `206` UCHUN YAGONA JOY
/// ========================================================================
///
/// ★ NIMA UCHUN `File(...)` ISHLATILMAYDI: MVC'ning
/// <c>enableRangeProcessing: true</c> mexanizmi IZLANADIGAN (seekable)
/// oqimni talab qiladi. Bizdagi oqim OMBORDAN kelayotgan TARMOQ oqimi —
/// u izlanmaydi, ya'ni MVC butun faylni avval buferlashga majbur bo'lardi.
/// 1 GB video uchun bu yo'l umuman yaramaydi. Shuning uchun oraliq
/// ALLAQACHON omborga uzatilgan va bu yerda faqat SARLAVHALAR to'g'ri
/// qo'yiladi.
///
/// ★ NIMA UCHUN ALOHIDA SINF: AYNI javob shakli ikki controller'da
/// (dars mediasi va vazifa sharti biriktirmasi) kerak. Nusxalansa, bir
/// kuni ulardan birida `Accept-Ranges` yoki `nosniff` tushib qolardi —
/// birinchisi seek'ni jimgina o'chiradi, ikkinchisi saqlangan XSS yo'lini
/// ochadi.
/// </summary>
internal static class MediaResponse
{
    /// <summary>
    /// Oqimni javobga yozadi va kerakli sarlavhalarni qo'yadi.
    ///
    /// ⚠️ OQIM EGALIGI: `RegisterForDisposeAsync` — `StoredMedia` BUTUNLAY
    /// (ostidagi HTTP javobi bilan) yopilishi uchun. Faqat oqimni yopish
    /// yetarli emas: javob obyekti yopilmasa ombor bilan ulanish hovuzga
    /// qaytmaydi va sekin, ko'rinmas soket oqishi paydo bo'ladi.
    /// </summary>
    internal static async Task<IActionResult> WriteAsync(
        ControllerBase controller, LessonAssetDownload download, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(download);

        var response = controller.Response;

        response.RegisterForDisposeAsync(download.Content);

        // 🔴 `Accept-Ranges` HAR JAVOBDA (200 da ham): brauzer aynan shu
        //    sarlavhadan "bu faylda seek qilsa bo'ladi" degan xulosaga
        //    keladi. U bo'lmasa pleyer oldinga o'tish imkonini UMUMAN
        //    ko'rsatmaydi — hatto server `Range` ni qo'llasa ham.
        response.Headers.AcceptRanges = "bytes";

        // nosniff: brauzer turni O'ZI taxmin qilib, faylni HTML deb
        // ko'rsatib yubormasin (saqlangan XSS'ning klassik yo'li).
        response.Headers.XContentTypeOptions = "nosniff";

        // ⚠️ `no-store` ATAYLAB EMAS (vazifa javobi fayllaridan FARQLI).
        //
        // Dars videosi SHAXSIY ma'lumot emas — u kursning kontenti. Uni
        // keshlashni butunlay taqiqlash har seek harakatida butun bo'lakni
        // qaytadan yuklashga majbur qilardi va 1 GB fayl uchun bu tarmoqni
        // bekordan yeb qo'yardi.
        //
        // `private` — faqat BRAUZER keshi (oraliq proksi saqlamaydi): fayl
        // ruxsat tekshiruvi ostida turadi, ya'ni umumiy kesh uni boshqa
        // foydalanuvchiga bermasligi kerak.
        response.Headers.CacheControl = "private, max-age=3600";

        response.ContentType = download.ContentType;

        // Fayl nomi — obyekt kalitini OSHKOR QILMAYDI (u servisda yasalgan).
        response.Headers.ContentDisposition = new ContentDispositionHeaderValue("inline")
        {
            FileNameStar = download.FileName,
        }.ToString();

        if (download.Range is { } range)
        {
            // ★ 206: `Content-Range` va `Content-Length` ANIQ qo'yiladi —
            //   aks holda ASP.NET chunked transfer'ga o'tadi va ba'zi
            //   pleyerlar qisman javobni tushunmaydi.
            response.StatusCode = StatusCodes.Status206PartialContent;

            response.Headers.ContentRange = string.Create(
                CultureInfo.InvariantCulture,
                $"bytes {range.From}-{range.To}/{download.TotalLength}");

            response.ContentLength = range.Length;
        }
        else
        {
            response.StatusCode = StatusCodes.Status200OK;
            response.ContentLength = download.Content.ContentLength ?? download.TotalLength;
        }

        await download.Content.Content.CopyToAsync(response.Body, ct).ConfigureAwait(false);

        // Javob QO'LDA yozilgan — MVC yana hech nima yozmasligi kerak.
        return new EmptyResult();
    }

    /// <summary>
    /// `file` bo'lagi umuman yuborilmagan yoki bo'sh.
    ///
    /// ★ NIMA UCHUN `ValidationProblem(...)` EMAS, ISTISNO: loyihada 400
    /// javobining shakli BITTA joyda yasaladi
    /// (`ExceptionHandlingMiddleware`) va u yerda `traceId` ham qo'shiladi.
    /// Controller o'zi `ValidationProblem` qaytarsa, javob shakli
    /// (`traceId` siz) qolganlaridan farq qilardi va frontend'ning
    /// `toUserMessage(error)` yordamchisi uni boshqacha o'qirdi.
    /// </summary>
    internal static ValidationException MissingFile() =>
        new(new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["file"] = ["Fayl yuborilmadi yoki bo'sh (`file` maydoni kutiladi)."],
        });

    /// <summary>
    /// `Range` sarlavhasining XOM qiymati (bo'lmasa <c>null</c>).
    ///
    /// `StringValues.ToString()` bir nechta sarlavha bo'lsa ularni vergul
    /// bilan qo'shib yuboradi — bu esa "ko'p oraliq" ko'rinishiga aylanadi
    /// va `RangeHeader` uni ataylab E'TIBORSIZ qoldiradi (to'liq javob).
    /// Bu xavfsiz yo'nalish.
    /// </summary>
    internal static string? RawRange(StringValues range) =>
        range.Count == 0 ? null : range.ToString();
}
