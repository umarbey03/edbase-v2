namespace Zinnur.Migration.Pipeline;

/// <summary>
/// ========================================================================
/// KO'CHIRISH DAVOMIDAGI XOTIRA — FK BUTUNLIGINING ASOSI
/// ========================================================================
///
/// ★ MUAMMO: qatorlar turli sabablarga ko'ra tushib qolishi mumkin
/// (tanilmagan enum, CHECK cheklovi, dublikat kalit). Agar foydalanuvchi
/// tushib qolsa-yu, uning guruh a'zoligi ko'chsa — FK xatosi bilan BUTUN
/// paket yiqiladi va tunda, bosim ostida sababini topish kerak bo'ladi.
///
/// ★ YECHIM: har jadval ko'chgach uning MUVAFFAQIYATLI id'lari shu yerda
/// saqlanadi. Bola jadval ota id'sini shu to'plamdan tekshiradi va yo'q
/// bo'lsa O'ZI ham o'tkazib yuboriladi — sabab bilan. Natijada ko'chirish
/// yiqilmaydi, yo'qotish esa hisobotda ANIQ ko'rinadi.
///
/// Xotira sarfi: <c>long</c> to'plami. 100 000 foydalanuvchi ~ 3 MB.
/// Prod hajmida (bir necha ming qator) bu umuman sezilmaydi.
/// </summary>
internal sealed class MigrationState
{
    private readonly Dictionary<string, HashSet<long>> _ids = new(StringComparer.Ordinal);

    /// <summary>
    /// Telefoni boshqa foydalanuvchi bilan URIShayotgan id'lar.
    /// Ular uchun <c>PhoneNormalized</c> NULL yoziladi (batafsil
    /// <c>MigrationPlan.Users</c> izohida).
    /// </summary>
    public HashSet<long> PhoneDuplicateLosers { get; } = [];

    /// <summary>
    /// Kichik harfga o'tkazilgandan KEYIN elektron pochtasi boshqa
    /// foydalanuvchi bilan urishayotgan id'lar. v2 da <c>Email</c>
    /// kichik harflarda va UNIKAL — bunday qatorlar ko'cha olmaydi.
    /// </summary>
    public HashSet<long> EmailDuplicateLosers { get; } = [];

    /// <summary>
    /// Kvitansiya raqami takrorlangan moliya jurnali yozuvlari.
    /// v2 da <c>UX_PaymentTransactions_ReceiptNo</c> (filtrlangan unikal)
    /// bor; eski tizimda esa cheklov yo'q edi.
    /// </summary>
    public HashSet<long> ReceiptDuplicateLosers { get; } = [];

    /// <summary>
    /// Ishlatilgan LiveKit xona nomlari — v2 da ular UNIKAL
    /// (<c>UX_LiveSessions_RoomName</c>), eski tizimda esa emas edi.
    /// </summary>
    public HashSet<string> UsedRoomNames { get; } = new(StringComparer.Ordinal);

    public HashSet<long> Set(string table)
    {
        if (_ids.TryGetValue(table, out var set)) return set;

        set = [];
        _ids[table] = set;
        return set;
    }

    public void Add(string table, long id) => Set(table).Add(id);

    public bool Has(string table, long id) => Set(table).Contains(id);

    /// <summary>Ixtiyoriy havola: <c>null</c> bo'lsa to'g'ri, bo'lmasa to'plamda bo'lishi shart.</summary>
    public bool HasOptional(string table, long? id) => id is null || Set(table).Contains(id.Value);
}
