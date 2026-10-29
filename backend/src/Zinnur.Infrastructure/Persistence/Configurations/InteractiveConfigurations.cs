using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zinnur.Domain.Entities;

namespace Zinnur.Infrastructure.Persistence.Configurations;

/*
  ★ INTERAKTIV DARSLIK JADVALLARI (2026-10-29) — bitta faylda: sakkiz jadval
  bir-biriga qattiq bog'langan (birlik → versiya → natija) va har biri
  qisqa. Alohida fayllarga bo'linsa, bog'lanishlar qayerda e'lon qilinganini
  topish qiyinlashardi.

  ⚠️ `jsonb` ustunlar C# da `string`: tarkibni faqat Application qatlami
  (System.Text.Json) o'qiydi, EF esa uni shaffof tashiydi. Bu ataylab —
  domen modeli JSON kutubxonasiga bog'lanmaydi.
*/

public sealed class InteractiveBookConfiguration : IEntityTypeConfiguration<InteractiveBook>
{
    public void Configure(EntityTypeBuilder<InteractiveBook> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("InteractiveBooks");
        builder.HasKey(b => b.Id);

        builder.Property(b => b.Code).IsRequired().HasMaxLength(InteractiveBook.MaxCodeLength);
        builder.Property(b => b.Title).IsRequired().HasMaxLength(InteractiveBook.MaxTitleLength);
        builder.Property(b => b.Subtitle).HasMaxLength(InteractiveBook.MaxTitleLength);

        builder.HasIndex(b => b.Code).IsUnique().HasDatabaseName("UX_InteractiveBooks_Code");

        // Kurs o'chirilsa kitob qoladi (kontent qimmat) — faqat bog'lanish uziladi.
        builder.HasOne(b => b.Course)
            .WithMany()
            .HasForeignKey(b => b.CourseId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

public sealed class InteractiveUnitConfiguration : IEntityTypeConfiguration<InteractiveUnit>
{
    public void Configure(EntityTypeBuilder<InteractiveUnit> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("InteractiveUnits");
        builder.HasKey(u => u.Id);

        builder.Property(u => u.Code).IsRequired().HasMaxLength(InteractiveUnit.MaxCodeLength);
        builder.Property(u => u.TitleUz).IsRequired().HasMaxLength(InteractiveUnit.MaxTitleLength);
        builder.Property(u => u.TitleAr).HasMaxLength(InteractiveUnit.MaxTitleLength);

        builder.HasOne(u => u.Book)
            .WithMany(b => b.Units)
            .HasForeignKey(u => u.BookId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(u => new { u.BookId, u.Code })
            .IsUnique()
            .HasDatabaseName("UX_InteractiveUnits_Book_Code");

        // 🔴 Bitta darsga ko'pi bilan BITTA birlik: aks holda "dars sahifasidagi
        //    darslik" degan savolga ikki javob bo'lardi.
        builder.HasIndex(u => u.ModuleLessonId)
            .IsUnique()
            .HasFilter("\"ModuleLessonId\" IS NOT NULL")
            .HasDatabaseName("UX_InteractiveUnits_ModuleLessonId");

        // Dars o'chirilsa birlik "bog'lanmagan" bo'lib qoladi, kontent yo'qolmaydi.
        builder.HasOne(u => u.ModuleLesson)
            .WithMany()
            .HasForeignKey(u => u.ModuleLessonId)
            .OnDelete(DeleteBehavior.SetNull);

        // Versiyaga ishora — aylanma bog'lanish, shuning uchun Restrict va
        // versiya qatori birlikdan KEYIN yoziladi (import ikki bosqichda saqlaydi).
        builder.HasOne(u => u.CurrentVersion)
            .WithMany()
            .HasForeignKey(u => u.CurrentVersionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class InteractiveUnitVersionConfiguration : IEntityTypeConfiguration<InteractiveUnitVersion>
{
    public void Configure(EntityTypeBuilder<InteractiveUnitVersion> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("InteractiveUnitVersions");
        builder.HasKey(v => v.Id);

        builder.Property(v => v.ContentRaw).IsRequired().HasColumnType("text");
        builder.Property(v => v.ContentSha256).IsRequired()
            .HasMaxLength(InteractiveUnitVersion.Sha256Length).IsFixedLength();
        builder.Property(v => v.Content).IsRequired().HasColumnType("jsonb");
        builder.Property(v => v.TeacherGuideMd).HasColumnType("text");
        builder.Property(v => v.TeacherGuideSha256)
            .HasMaxLength(InteractiveUnitVersion.Sha256Length).IsFixedLength();
        builder.Property(v => v.AudioManifest).IsRequired().HasColumnType("jsonb");
        builder.Property(v => v.Stats).IsRequired().HasColumnType("jsonb");
        builder.Property(v => v.SourcePath).HasMaxLength(InteractiveUnitVersion.MaxSourcePathLength);

        builder.HasOne(v => v.Unit)
            .WithMany()
            .HasForeignKey(v => v.UnitId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(v => new { v.UnitId, v.Version })
            .IsUnique()
            .HasDatabaseName("UX_InteractiveUnitVersions_Unit_Version");

        builder.HasIndex(v => new { v.UnitId, v.ContentSha256 })
            .HasDatabaseName("IX_InteractiveUnitVersions_Unit_Sha");

        builder.HasOne<InteractiveImportRun>()
            .WithMany()
            .HasForeignKey(v => v.ImportRunId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

public sealed class InteractiveAudioConfiguration : IEntityTypeConfiguration<InteractiveAudio>
{
    public void Configure(EntityTypeBuilder<InteractiveAudio> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("InteractiveAudio");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.Sha256).IsRequired()
            .HasMaxLength(InteractiveUnitVersion.Sha256Length).IsFixedLength();
        builder.Property(a => a.ObjectKey).IsRequired().HasMaxLength(500);
        builder.Property(a => a.ContentType).IsRequired().HasMaxLength(100);

        builder.HasIndex(a => a.Sha256).IsUnique().HasDatabaseName("UX_InteractiveAudio_Sha256");
    }
}

public sealed class InteractiveImportRunConfiguration : IEntityTypeConfiguration<InteractiveImportRun>
{
    public void Configure(EntityTypeBuilder<InteractiveImportRun> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("InteractiveImportRuns");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.BookCode).IsRequired().HasMaxLength(InteractiveBook.MaxCodeLength);
        builder.Property(r => r.Result).IsRequired().HasMaxLength(16);
        builder.Property(r => r.Report).IsRequired().HasColumnType("jsonb");

        builder.HasIndex(r => r.StartedAt).HasDatabaseName("IX_InteractiveImportRuns_StartedAt");
    }
}

public sealed class InteractiveUnitProgressConfiguration : IEntityTypeConfiguration<InteractiveUnitProgress>
{
    public void Configure(EntityTypeBuilder<InteractiveUnitProgress> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("InteractiveUnitProgress");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.BlocksDone).IsRequired().HasColumnType("jsonb");

        builder.HasIndex(p => new { p.StudentId, p.UnitId })
            .IsUnique()
            .HasDatabaseName("UX_InteractiveUnitProgress_Student_Unit");

        // Ball sinxronizatsiyasi (`PointsSyncService`) shu ustun bo'yicha oynani o'qiydi.
        builder.HasIndex(p => p.CompletedAt).HasDatabaseName("IX_InteractiveUnitProgress_CompletedAt");

        builder.HasOne<User>().WithMany().HasForeignKey(p => p.StudentId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(p => p.Unit).WithMany().HasForeignKey(p => p.UnitId).OnDelete(DeleteBehavior.Cascade);

        // Optimistik qulf: ikki parallel "blok bajarildi" bir-birini yutib yubormasin.
        builder.Property<uint>("xmin").IsRowVersion();
    }
}

public sealed class InteractiveItemResultConfiguration : IEntityTypeConfiguration<InteractiveItemResult>
{
    public void Configure(EntityTypeBuilder<InteractiveItemResult> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("InteractiveItemResults");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.ItemKey).IsRequired().HasMaxLength(InteractiveItemResult.MaxItemKeyLength);
        builder.Property(r => r.State).IsRequired().HasColumnType("jsonb");
        builder.Ignore(r => r.IsFirstDone);

        builder.HasIndex(r => new { r.StudentId, r.UnitId, r.ItemKey })
            .IsUnique()
            .HasDatabaseName("UX_InteractiveItemResults_Student_Unit_Item");

        builder.HasOne<User>().WithMany().HasForeignKey(r => r.StudentId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<InteractiveUnit>().WithMany().HasForeignKey(r => r.UnitId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<InteractiveUnitVersion>().WithMany().HasForeignKey(r => r.VersionId)
            .OnDelete(DeleteBehavior.Restrict);

        // 🔴 Birinchi urinish parallel ikki so'rovda ikki marta yozilmasin.
        builder.Property<uint>("xmin").IsRowVersion();
    }
}

public sealed class InteractiveTestAttemptConfiguration : IEntityTypeConfiguration<InteractiveTestAttempt>
{
    public void Configure(EntityTypeBuilder<InteractiveTestAttempt> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("InteractiveTestAttempts");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.Answers).IsRequired().HasColumnType("jsonb");
        builder.Property(a => a.Score).HasPrecision(6, 2);
        builder.Property(a => a.MaxScore).HasPrecision(6, 2);

        // Birinchi topshiriq — o'quvchi × birlik uchun bitta.
        builder.HasIndex(a => new { a.StudentId, a.UnitId })
            .IsUnique()
            .HasFilter("\"IsFirst\"")
            .HasDatabaseName("UX_InteractiveTestAttempts_First");

        builder.HasIndex(a => a.SubmittedAt).HasDatabaseName("IX_InteractiveTestAttempts_SubmittedAt");

        builder.HasOne<User>().WithMany().HasForeignKey(a => a.StudentId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<InteractiveUnit>().WithMany().HasForeignKey(a => a.UnitId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<InteractiveUnitVersion>().WithMany().HasForeignKey(a => a.VersionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
