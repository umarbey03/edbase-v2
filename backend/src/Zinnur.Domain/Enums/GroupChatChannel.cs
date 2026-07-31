namespace Zinnur.Domain.Enums;

/// <summary>
/// Guruh chatining KANALI — bitta guruhda ikkita mustaqil oqim bor.
///
/// ★ NIMA UCHUN IKKI KANAL (o'zimdan to'qilmadi — eski tizimdagi qoida):
/// eski ilovada <c>chat_messages.channel</c> ustuni bor va u
/// <c>"teacher"</c> / <c>"assistant"</c> qiymatlarini oladi
/// (<c>student_router._norm_channel</c>, <c>teacher_router._channel_for</c>).
/// O'quvchi ustozga va kuratorga ALOHIDA yozadi; ustoz kurator oqimini
/// ko'rmaydi va aksincha.
///
/// Kanalni tashlab yuborish MA'LUMOT KO'CHIRISHDA jimgina zarar berardi:
/// eski bazadagi ikki oqim bitta oqimga qo'shilib ketardi va ustoz
/// o'quvchining kuratorga atalgan savollarini o'qib qolardi.
///
/// ⚠️ Tartib MUHIM: qiymatlar bazaga <c>int</c> sifatida yoziladi.
/// Yangi kanal FAQAT oxiriga qo'shiladi.
/// </summary>
public enum GroupChatChannel
{
    /// <summary>Ustoz oqimi (eski tizimdagi <c>"teacher"</c>).</summary>
    Teacher = 0,

    /// <summary>
    /// Kurator (yordamchi) oqimi — eski tizimdagi <c>"assistant"</c>.
    ///
    /// Nom v2 lug'atiga moslandi: <c>Group.AssistantId</c> maydonidagi odam
    /// mahsulot tilida "kurator" deb ataladi (<c>GroupType.Curator</c> izohiga
    /// qarang). Ma'lumot ko'chirishda xaritalash: <c>"assistant" -> Curator</c>.
    /// </summary>
    Curator = 1,
}
