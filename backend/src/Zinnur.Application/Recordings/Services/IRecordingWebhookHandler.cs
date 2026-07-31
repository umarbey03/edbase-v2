using Zinnur.Application.Recordings.Dtos;

namespace Zinnur.Application.Recordings.Services;

/// <summary>
/// LiveKit hodisasini QAYTA ISHLAYDI (imzo ALLAQACHON tekshirilgan).
///
/// ★ VAZIFALAR BO'LINISHI: controller — imzo va idempotentlik darvozasi;
/// bu servis — holat o'zgarishi. Shu tufayli "imzoni tekshirishni unutish"
/// mumkin emas: bu servis umuman tekshirmaydi va tekshirmasligi ham kerak,
/// chunki u faqat BITTA joydan chaqiriladi.
///
/// ⚠️ OG'IR ISH YO'Q: metod bazaga bir necha so'rov yuboradi, xolos.
/// Tashqi xizmatlarga murojaat (Egress'ni to'xtatish, ombordan tekshirish)
/// ATAYLAB WATCHDOG'ga qoldirilgan — webhook ichida sekin chaqiruv bo'lsa
/// LiveKit javobni kutolmay hodisani QAYTA yuborardi va bitta hodisa
/// bir necha marta ishlanardi.
/// </summary>
public interface IRecordingWebhookHandler
{
    /// <summary>
    /// Hodisani ishlaydi. HECH QACHON istisno tashlamaydi degan kafolat
    /// YO'Q — kutilmagan xatoni controller ushlaydi va LiveKit'ga baribir
    /// 200 qaytaradi (aks holda cheksiz qayta yuborish boshlanardi).
    /// </summary>
    /// <param name="body">So'rov tanasi — AYNAN kelgan baytlar.</param>
    Task<RecordingWebhookOutcome> HandleAsync(
        ReadOnlyMemory<byte> body, CancellationToken ct = default);
}
