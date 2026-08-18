using System.Linq.Expressions;
using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;

namespace Zinnur.Application.Common;

/// <summary>
/// ════════════════════════════════════════════════════════════════════════
/// "SHU GURUHNING O'QUVCHILARI" — YAGONA QOIDA (2026-08-18)
/// ════════════════════════════════════════════════════════════════════════
///
/// 🔴 NIMA UCHUN QO'SHILDI: bu savol loyihada 14 joyda so'ralardi va
/// IKKI XIL javob berardi:
///   • ba'zi joylar faqat `m.GroupId == groupId` (to'g'ridan-to'g'ri);
///   • ba'zilari kurator guruhini KENGAYTIRIB, bog'langan ustoz
///     guruhlarining a'zolarini ham qo'shardi.
///
/// Natijasi foydalanuvchiga ko'rinadigan nosozliklar edi:
///   • kurator darsi boshlanganda o'quvchilarga Telegram xabar ketardi
///     (kengaytirilgan), lekin darsga kirmoqchi bo'lganda "ruxsatingiz
///     yo'q" degan 403 olardi (to'g'ridan-to'g'ri);
///   • o'sha darsning DAVOMAT VARAG'I va BAHOLAR JURNALI bo'sh
///     chiqardi — kurator hech kimga baho qo'ya olmasdi;
///   • guruhlar ro'yxati "22 o'quvchi" der, global qidiruv esa AYNI
///     guruh uchun "0 o'quvchi" derdi.
///
/// ★ TO'G'RI QOIDA — KENGAYTIRILGAN, va bu domendan kelib chiqadi:
/// <see cref="GroupType.Curator"/> izohida yozilganidek, kurator
/// guruhida o'quvchilar TO'G'RIDAN-TO'G'RI a'zo BO'LMAYDI — ular
/// bog'langan ustoz guruhlaridan keladi. Ya'ni to'g'ridan-to'g'ri
/// sanoq kurator guruhida DOIM 0 beradi.
///
/// ★ `Type` TEKSHIRUVI SHART EMAS: <c>Group.CuratorGroupId</c> faqat
/// kurator guruhiga ishora qiladi, ya'ni ikkinchi shox oddiy guruhlarda
/// o'z-o'zidan yolg'on bo'ladi.
///
/// ⚠️ BU QOIDA HISOB-KITOBGA ISHLATILMAYDI. To'lov, oylik va umumiy
/// o'quvchi sanog'i kurator guruhini ATAYLAB chiqarib tashlaydi
/// (<c>PaymentService</c>, <c>LessonAccrualService</c>,
/// <c>StudentStatsService</c>): u yerda o'quvchi ustoz guruhida ham,
/// kurator guruhida ham uchraydi va kengaytirilsa IKKI MARTA
/// hisoblanardi — bir dars uchun ikki marta pul yechilardi.
/// </summary>
public static class GroupMembershipScope
{
    /// <summary>
    /// Shu guruhga tegishli FAOL a'zolar (kurator guruhida — bog'langan
    /// ustoz guruhlaridagilar).
    /// </summary>
    /// <remarks>
    /// ⚠️ KORRELYATSIYALANGAN ICHKI SO'ROVLARDA (guruh <c>Id</c> si
    /// USTUN sifatida keladigan <c>Count</c>/<c>Any</c> larda) bu ifodani
    /// ishlatib bo'lmaydi — u <c>groupId</c> ni QIYMAT sifatida oladi.
    /// U yerda shart QO'LDA yoziladi, lekin AYNI ikki shox bilan va shu
    /// sinfga havola qiluvchi izoh bilan.
    /// </remarks>
    public static Expression<Func<GroupMember, bool>> ActiveIn(long groupId) =>
        m => m.Status == MemberStatus.Active
          && (m.GroupId == groupId || m.Group!.CuratorGroupId == groupId);
}
