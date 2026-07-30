using Zinnur.Domain.Enums;

namespace Zinnur.Infrastructure.Options;

/// <summary>
/// <c>Payments</c> bo'limi — moliya modulining MUHITGA oid sozlamalari.
///
/// ★ NIMA UCHUN CHEGARA VA QAMROV BU YERDA EMAS (faqat STANDART qiymat
/// sifatida): ular biznes qarori va o'quv bo'limi ularni ish jarayonida
/// o'zgartiradi — shuning uchun asosiy manba BAZA
/// (<c>AppSettings</c> jadvali, <see cref="Zinnur.Infrastructure.Services.FinanceSettingsStore"/>).
/// Bu yerdagi qiymatlar faqat baza hali bo'sh bo'lganda (birinchi ishga
/// tushirish yoki eski tizimdan ko'chirishdan oldin) ishlatiladi.
///
/// <see cref="EnforceBlock"/> esa AKSINCHA — faqat konfiguratsiyada.
/// Sababi <c>IFinanceSettingsStore</c> izohida.
/// </summary>
public sealed class PaymentsOptions
{
    /// <summary>appsettings / env dagi bo'lim nomi: <c>Payments__...</c>.</summary>
    public const string SectionName = "Payments";

    /// <summary>
    /// "Qattiq rejim" kaliti.
    ///
    /// <c>false</c> — YUMSHOQ rejim: qarz hisoblanadi va interfeysda
    /// ko'rinadi, lekin HECH KIM bloklanmaydi. Sinov/staging muhitida shu
    /// qo'yiladi: u yerdagi baza odatda prod nusxasidan tiklanadi va
    /// sinovchilar tasodifan bloklanib qolmasligi kerak.
    /// </summary>
    public bool EnforceBlock { get; set; } = true;

    /// <summary>Baza bo'sh bo'lgandagi standart chegara (eski tizim qiymati).</summary>
    public decimal DefaultBlockThreshold { get; set; } = 540_000m;

    /// <summary>Baza bo'sh bo'lgandagi standart qamrov (eski tizimda <c>video</c>).</summary>
    public PaymentBlockScope DefaultBlockScope { get; set; } = PaymentBlockScope.Video;
}
