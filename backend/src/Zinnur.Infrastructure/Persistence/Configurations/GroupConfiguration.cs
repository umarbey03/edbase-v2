using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zinnur.Domain.Entities;

namespace Zinnur.Infrastructure.Persistence.Configurations;

public sealed class GroupConfiguration : IEntityTypeConfiguration<Group>
{
    public void Configure(EntityTypeBuilder<Group> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("Groups");
        builder.HasKey(g => g.Id);

        builder.Property(g => g.Name).IsRequired().HasMaxLength(150);

        // DateOnly -> Postgres `date` (vaqt zonasi muammosi umuman yo'q).
        builder.Property(g => g.StartDate).HasColumnType("date");

        // ============================================================
        // JADVAL QOIDASI USTUNLARI
        // ============================================================

        builder.Property(g => g.Type).HasConversion<int>();

        // DARS KUNLARI -> Postgres `integer[]`, JSON EMAS.
        //
        // NIMA UCHUN OSHKOR YOZILGAN: EF Core 8 dan boshlab "primitive
        // collection" (masalan `List<int>`) relational provayderlarda
        // STANDART holda JSON ustunga tushadi. JSON bo'lsa:
        //   • `WHERE 1 = ANY("Weekdays")` kabi so'rov indekssiz qoladi,
        //   • qiymatlar matn bo'lgani uchun baza turini tekshirmaydi,
        //   • boshqa vositalar (psql, hisobot) uchun o'qish noqulay.
        // Npgsql native massivni qo'llab-quvvatlaydi, shuning uchun ustun
        // turi va element konvertatsiyasi ANIQ ko'rsatiladi.
        //
        // `ElementType(...HasConversion<int>())` — `DayOfWeek` enum'ini
        // massiv ELEMENTI darajasida int'ga o'giradi. Busiz enum massivi
        // provayder xohishiga qolib ketardi (matn yoki JSON).
        //
        // ⚠️ KONVENSIYA: .NET `DayOfWeek` — Yakshanba = 0. Eski Python
        // tizimida Dushanba = 0 edi; ma'lumot ko'chirishda
        // `dotnet = (python + 1) % 7` konvertatsiyasi MAJBURIY
        // (batafsil: `Group.Weekdays` izohi).
        builder.PrimitiveCollection(g => g.Weekdays)
            .ElementType(element => element.HasConversion<int>())
            .HasColumnType("integer[]")
            .IsRequired();

        // Mahalliy devor-vaqti (zonasiz) -> `time`. Zona jadval
        // generatsiyasida qo'llanadi (`IScheduleTimeZoneProvider`).
        builder.Property(g => g.StartTime).HasColumnType("time");

        // HISOBLANUVCHI property'lar — ustun EMAS (domain mantiqi).
        builder.Ignore(g => g.IsCuratorGroup);
        builder.Ignore(g => g.HostId);
        builder.Ignore(g => g.PlannedSessionType);
        builder.Ignore(g => g.EndDate);

        // Kurs o'chirilsa guruh tarixi saqlanib qoladi -> FK NULL bo'ladi.
        builder.HasOne(g => g.Course)
            .WithMany()
            .HasForeignKey(g => g.CourseId)
            .OnDelete(DeleteBehavior.SetNull);

        // ============================================================
        // VIDEO DARSLAR BOSHLANISH NUQTASI -> ModuleLessons(Id)
        // ============================================================
        //
        // NAVIGATSIYA PROPERTY'SI ATAYLAB YO'Q (`TeacherId` bilan bir xil
        // naqsh). Navigatsiya yuklangan holda FK'ni o'zgartirsak, EF
        // navigatsiya bilan FK orasidagi ziddiyatni o'zi "hal qilishga"
        // urinadi va bog'lanishni kutilmaganda tiklab yoki bo'shatib qo'yishi
        // mumkin (`GroupService.LoadForManageAsync` izohida ayni sabab).
        // DTO uchun kerak bo'lgan nomlar so'rovda ichki `SELECT` bilan
        // olinadi — navigatsiyaga ehtiyoj yo'q.
        //
        // ★ SetNull MAJBURIY: dars o'chirilsa GURUH o'chib ketmasligi kerak.
        //   • Cascade bo'lsa bitta kurs darsini o'chirish shu darsdan
        //     boshlanadigan BARCHA guruhlarni, ular bilan birga jadval,
        //     davomat, to'lov va chat tarixini olib ketardi;
        //   • Restrict ham yaramaydi — u kurs kontentini tahrirlashni
        //     bloklardi ("darsni o'chirib bo'lmaydi") va o'quv bo'limi
        //     sababini topa olmasdi.
        // SetNull esa aynan kerakli xatti-harakat: cheklov yo'qoladi,
        // guruh kursni boshidan boshlaydigan holatga qaytadi.
        builder.HasOne<ModuleLesson>()
            .WithMany()
            .HasForeignKey(g => g.VideoStartLessonId)
            .OnDelete(DeleteBehavior.SetNull);

        // ★ INDEKS — QARORI VA SABABI.
        //
        // SO'ROVLAR UCHUN INDEKS KERAK EMAS: gating DOIM guruhdan darsga
        // qaraydi ("shu guruh qaysi darsdan boshlaydi") va bu yo'l guruh
        // qatorini o'qish bilan tugaydi. TESKARI yo'nalishdagi ("qaysi
        // guruhlar shu darsdan boshlaydi") so'rov kod bazasida BIRORTA ham
        // yo'q. Bu bilan `CuratorGroupId` FARQ QILADI — u aynan teskari
        // yo'nalishda, a'zolar/davomat/jadval so'rovlarida HAR SAFAR o'qiladi.
        //
        // LEKIN INDEKS BARIBIR BOR va u ATAYLAB QOLDIRILGAN: EF Core FK
        // ustuniga indeksni KONVENSIYA bilan o'zi qo'shadi, va bu holda u
        // FOYDALI — `ON DELETE SET NULL` da Postgres o'chirilayotgan har
        // dars uchun ishora qiluvchi qatorlarni izlaydi. Konvensiyani
        // buzish uchun maxsus model konvensiyasi yozish kerak bo'lardi:
        // arzon indeksdan qutulish uchun bu juda qimmat narx.
        //
        // Nomi shu yerda OSHKOR yozilgan (fayl uslubi: `TeacherId`,
        // `CuratorGroupId` ham shunday) — aks holda o'quvchi indeks
        // borligini konfiguratsiyadan bilmasdi va migratsiyani ochib
        // ko'rishga majbur bo'lardi.
        builder.HasIndex(g => g.VideoStartLessonId)
            .HasDatabaseName("IX_Groups_VideoStartLessonId");

        // USTOZ / KURATOR — navigatsiya property'si yo'q, shuning uchun
        // munosabat QO'LDA e'lon qilinadi (aks holda EF hech qanday FK yaratmaydi
        // va bazada "yo'q ustoz" ga ishora qoladi).
        //
        // Restrict: foydalanuvchi hech qachon O'CHIRILMAYDI, `IsActive=false`
        // qilinadi. Shu sabab User'ga ishora qiluvchi BARCHA FK'lar Restrict —
        // izchil va kutilmagan kaskad o'chirish bo'lmaydi.
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(g => g.TeacherId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(g => g.AssistantId)
            .OnDelete(DeleteBehavior.Restrict);

        // KURATOR GURUHI HAVOLASI — o'ziga-o'zi ishora qiluvchi munosabat.
        //
        // SetNull: kurator guruhi o'chirilsa bog'langan ustoz guruhlari
        // O'CHIB KETMASLIGI kerak — ular mustaqil o'quv birligi. Cascade
        // bo'lsa bitta kurator guruhini o'chirish o'nlab guruhni va ular
        // bilan birga butun jadval, davomat va chat tarixini olib ketardi.
        builder.HasOne(g => g.CuratorGroup)
            .WithMany()
            .HasForeignKey(g => g.CuratorGroupId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(g => new { g.IsActive, g.CourseId })
            .HasDatabaseName("IX_Groups_IsActive_CourseId");

        builder.HasIndex(g => g.TeacherId).HasDatabaseName("IX_Groups_TeacherId");
        builder.HasIndex(g => g.AssistantId).HasDatabaseName("IX_Groups_AssistantId");

        // Kurator guruhi a'zolari HAR SAFAR shu ustun bo'yicha izlanadi:
        // "qaysi ustoz guruhlari shu kuratorga bog'langan" (a'zolar ro'yxati,
        // davomat, jadval). Indekssiz bu har so'rovda butun jadval skani edi.
        builder.HasIndex(g => g.CuratorGroupId).HasDatabaseName("IX_Groups_CuratorGroupId");
    }
}
