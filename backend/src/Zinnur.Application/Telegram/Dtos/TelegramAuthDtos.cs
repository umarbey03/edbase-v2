namespace Zinnur.Application.Telegram.Dtos;

/// <summary>
/// Mini App kirish so'rovi.
///
/// ★ FAQAT BITTA MAYDON — bu ATAYLAB. Telegram ID, telefon yoki ism
/// klientdan ALOHIDA qabul qilinmaydi: hammasi imzolangan
/// <c>initData</c> ning O'ZIDAN chiqariladi. Aks holda klient imzoni
/// bir foydalanuvchidan, "telegramId" ni esa boshqasidan yuborib,
/// tekshiruvni chetlab o'tardi.
/// </summary>
/// <param name="InitData">
/// <c>window.Telegram.WebApp.initData</c> — xom (URL-kodlangan) satr.
/// Klient uni HECH QANDAY o'zgartirmasdan, AYNAN shu ko'rinishda yuborishi
/// shart: bitta bo'shliq yoki tartib o'zgarsa imzo buziladi.
/// </param>
public sealed record MiniAppAuthRequest(string? InitData);
