using Zinnur.Application.Recordings.Services;
using Zinnur.Application.Settings;

namespace Zinnur.UnitTests.Settings;

/// <summary>
/// ════════════════════════════════════════════════════════════════════════
/// YOZUV QUVURI v2 NING SOZLAMALARI (SPEC-RECORDING-V2 §2.7)
/// ════════════════════════════════════════════════════════════════════════
///
/// ★ NIMA UCHUN ALOHIDA FAYL, <c>SettingsRegistryTests</c> GA QO'SHIMCHA
///   EMAS: bu sakkiz kalitning standart qiymatlari — YOYISH REJASINING
///   O'ZI. Ularning har biri "yangi kod o'rnatildi, lekin hech narsa
///   o'zgarmadi" holatini kafolatlaydi va bittasi o'zgarsa, prod'da
///   o'zgaradigan narsa — 33 guruhning dars yozuvi.
///
/// 🔴 UCHTA KALITNI M5 va M6 ALLAQACHON O'QIYDI — REGISTRDA YO'Q PAYTIDA
///    YOZILGAN KOD BILAN. Ular <c>SettingsRegistry.TryGet</c> orqali
///    o'qiydi va topilmasa SPEC dagi standartga tushadi. Kalitlar
///    qo'shilgach o'sha joylar HECH QANDAY o'zgarishsiz ishlashi kerak,
///    ya'ni registrdagi standart SPEC dagi standart bilan AYNAN bir xil
///    bo'lishi shart. Shuning uchun bu yerda satrlar KO'CHIRILGAN, kod
///    ichidagi konstantalarga havola qilinmagan: ikkalasi ham xato
///    bo'lsa, bir-birini tasdiqlab turaverardi.
/// </summary>
public class RecordingPipelineSettingsTests
{
    /// <summary>SPEC §2.7 dagi sakkiz kalit — AYNAN shu nomlar bilan.</summary>
    public static TheoryData<string> AllKeys =>
    [
        "recordings.track_pipeline_enabled",
        "recordings.track_pipeline_shadow_groups",
        "recordings.compose_window_start",
        "recordings.compose_window_end",
        "recordings.compose_preset",
        "recordings.compose_crf",
        "recordings.audio_capture_mode",
        "recordings.compose_audio_offset_ms",
    ];

    /// <summary>
    /// Sakkiztasi ham registrda bor, PANELDAN boshqariladi va ish
    /// jarayonidagi keshga tushadi.
    ///
    /// ★ "Paneldan boshqariladi" — yoyish rejasining sharti: orqaga
    ///   qaytish yo'li (<c>track_pipeline_enabled = false</c>) deploysiz
    ///   bo'lishi kerak. Kalitlardan biri <c>Environment</c> ga o'tsa,
    ///   o'sha yo'l jimgina yopilardi.
    /// </summary>
    [Theory]
    [MemberData(nameof(AllKeys))]
    public void EveryPipelineKey_IsRegistered_AndRuntimeEditable(string key)
    {
        SettingsRegistry.TryGet(key, out var definition).Should().BeTrue();

        definition.Source.Should().Be(SettingSource.Database);
        definition.IsEditable.Should().BeTrue();
        definition.ReadOnlyReason.Should().BeNull();

        // Kesh AYNAN shu ro'yxatni o'qiydi (`IRuntimeSettings`).
        SettingsRegistry.Runtime.Should().Contain(definition);
    }

    /// <summary>
    /// Kalit nomlarining <c>SettingsRegistry.Keys</c> dagi konstantalari
    /// haqiqiy kalitlarga MOS.
    ///
    /// ★ NIMA UCHUN KERAK: M5/M6 hozircha SATR literallardan foydalanadi
    ///   (kalit ular yozilganda registrda yo'q edi) va ular bir kun bu
    ///   konstantalarga almashtiriladi. Nom bir harf bilan farq qilsa,
    ///   almashtirish "sozlama e'tiborsiz qoldi" ga aylanardi — hech
    ///   qanday xatosiz.
    /// </summary>
    [Fact]
    public void KeyConstants_MatchTheRegisteredKeys()
    {
        SettingsRegistry.Keys.RecordingsTrackPipelineEnabled
            .Should().Be("recordings.track_pipeline_enabled");

        SettingsRegistry.Keys.RecordingsTrackPipelineShadowGroups
            .Should().Be("recordings.track_pipeline_shadow_groups");

        SettingsRegistry.Keys.RecordingsComposeWindowStart
            .Should().Be("recordings.compose_window_start");

        SettingsRegistry.Keys.RecordingsComposeWindowEnd
            .Should().Be("recordings.compose_window_end");

        SettingsRegistry.Keys.RecordingsComposePreset
            .Should().Be("recordings.compose_preset");

        SettingsRegistry.Keys.RecordingsComposeCrf
            .Should().Be("recordings.compose_crf");

        SettingsRegistry.Keys.RecordingsAudioCaptureMode
            .Should().Be("recordings.audio_capture_mode");

        SettingsRegistry.Keys.RecordingsComposeAudioOffsetMs
            .Should().Be("recordings.compose_audio_offset_ms");
    }

    /// <summary>
    /// 🔴 ENG MUHIM TASDIQ SHU YERDA: yangi quvur O'CHIQ holda yetkaziladi.
    ///
    /// Standart <c>true</c> bo'lsa, migratsiya prod'ga chiqqan kuni
    /// <c>Group.RecordingPipeline = TrackComposition</c> qo'yilgan har
    /// qanday guruh (yoki keyinchalik qo'yilgani) darhol tajriba yo'liga
    /// o'tardi — hech kim yoqmasdan.
    /// </summary>
    [Fact]
    public void TrackPipeline_IsOffByDefault()
    {
        SettingsRegistry.TryGet("recordings.track_pipeline_enabled", out var toggle)
            .Should().BeTrue();

        toggle.Kind.Should().Be(SettingValueKind.Toggle);
        toggle.DefaultValue.Should().Be(
            "false", "quvur o'rnatiladi, lekin admin panelidan YOQILADI");
    }

    /// <summary>
    /// Solishtiruv ro'yxati standart holda BO'SH — ya'ni hech bir guruh
    /// ikkita yozuv olmaydi.
    /// </summary>
    [Fact]
    public void ShadowGroups_AreEmptyByDefault_AndBounded()
    {
        SettingsRegistry.TryGet("recordings.track_pipeline_shadow_groups", out var shadow)
            .Should().BeTrue();

        shadow.Kind.Should().Be(SettingValueKind.Text);
        shadow.DefaultValue.Should().BeEmpty("bo'sh ro'yxat = hech kim");

        // SPEC §2.7 dagi chegara. U shunchaki tartib emas: ro'yxat
        // uzayib ketsa, u guruh ustunining o'rnini egallab olardi.
        shadow.MaxLength.Should().Be(100);
    }

    /// <summary>
    /// Tungi oyna standartlari KOD dagi standartlar bilan AYNI.
    ///
    /// 🔴 ULAR IKKI JOYDA: registrda (panel ko'rsatadigan qiymat) va
    /// <c>RecordingCompositionWindow</c> da (kalit o'qilmaganda ishlaydigan
    /// zaxira). Ular ajralib ketsa, panelda "00:00–09:00" turgani holda
    /// tizim boshqa oynada ishlardi — jimgina yolg'on.
    /// </summary>
    [Fact]
    public void ComposeWindow_DefaultsMatchTheCodeDefaults()
    {
        SettingsRegistry.TryGet("recordings.compose_window_start", out var start)
            .Should().BeTrue();
        SettingsRegistry.TryGet("recordings.compose_window_end", out var end)
            .Should().BeTrue();

        start.DefaultValue.Should().Be("00:00");
        end.DefaultValue.Should().Be("09:00");

        // Va ular AYNAN o'qiladigan shaklda: `Parse` qat'iy `HH:mm` kutadi,
        // ya'ni "9:00" jimgina zaxiraga tushardi.
        RecordingCompositionWindow.Parse(start.DefaultValue, new TimeOnly(23, 59))
            .Should().Be(RecordingCompositionWindow.DefaultStart);

        RecordingCompositionWindow.Parse(end.DefaultValue, new TimeOnly(23, 59))
            .Should().Be(RecordingCompositionWindow.DefaultEnd);
    }

    /// <summary>
    /// Kodlash standartlari — loyiha egasining qarori (SPEC §10, D2):
    /// <c>medium</c> / CRF 21, <c>slow</c> esa faqat tanlov sifatida
    /// mavjud.
    /// </summary>
    [Fact]
    public void EncodingDefaults_FollowTheOwnersDecision()
    {
        SettingsRegistry.TryGet("recordings.compose_preset", out var preset).Should().BeTrue();
        SettingsRegistry.TryGet("recordings.compose_crf", out var crf).Should().BeTrue();

        preset.Kind.Should().Be(SettingValueKind.Choice);
        preset.DefaultValue.Should().Be(
            "medium", "`slow` tungi oynaga sig'maydi (SPEC §10, Qaror 2)");

        preset.Choices.Should().ContainInOrder("veryfast", "faster", "fast", "medium", "slow");

        crf.Kind.Should().Be(SettingValueKind.Number);
        crf.DefaultValue.Should().Be("21");

        // 🔴 `crf = 0` yo'qotishsiz kodlash — bitta darsdan o'nlab
        //    gigabayt. Chegara halokatga qarshi to'siq.
        crf.Minimum.Should().Be(16m);
        crf.Maximum.Should().Be(28m);
    }

    /// <summary>
    /// 🔴 OVOZ MANBASI: standart — XONA MIKSERI, ya'ni o'quvchilar ham
    /// yoziladi (loyiha egasining 2026-09-05 qarori, SPEC §10 D1).
    ///
    /// ⚠️ TANLOVLAR AYNAN IKKITA. Uchinchisi paydo bo'lsa "hech qachon
    /// ikkala manba birga emas" qoidasi (§2.3) buzilishi mumkin bo'lardi:
    /// ustozning ovozi ikki faylda bo'lib, montajda o'zi bilan
    /// aralashardi.
    /// </summary>
    [Fact]
    public void AudioCaptureMode_DefaultsToRoomComposite()
    {
        SettingsRegistry.TryGet("recordings.audio_capture_mode", out var mode).Should().BeTrue();

        mode.Kind.Should().Be(SettingValueKind.Choice);
        mode.DefaultValue.Should().Be("RoomComposite", "o'quvchilarning ovozi ham yoziladi");

        mode.Choices.Should().BeEquivalentTo(["RoomComposite", "TeacherTrack"]);
    }

    /// <summary>
    /// 🔴 M5 NING ZAXIRA YO'LI BUZILMAGANMI. <c>TrackRecordingWebhookHandler</c>
    /// "qiymat <c>TeacherTrack</c> EMASMI" deb so'raydi va registrda kalit
    /// bo'lmagan paytda <c>RoomComposite</c> ga tushardi. Kalit qo'shilgach
    /// AMALDAGI qiymat ham AYNAN o'sha bo'lishi shart — aks holda M5
    /// hech qanday kod o'zgarishisiz boshqacha ishlab ketardi.
    /// </summary>
    [Fact]
    public void AudioCaptureMode_ResolvedDefault_KeepsTheRoomMixerOn()
    {
        SettingsRegistry.TryGet("recordings.audio_capture_mode", out var mode).Should().BeTrue();

        // M5 dagi tekshiruvning AYNI shakli.
        var isRoomMode = !string.Equals(
            mode.DefaultValue.Trim(), "TeacherTrack", StringComparison.OrdinalIgnoreCase);

        isRoomMode.Should().BeTrue();
    }

    /// <summary>
    /// Kalibrlash konstantasi standart holda NOL — ya'ni hech narsa
    /// siljitilmaydi, va chegaralar ikki soniya bilan cheklangan.
    /// </summary>
    [Fact]
    public void AudioOffset_IsZeroByDefault_AndBounded()
    {
        SettingsRegistry.TryGet("recordings.compose_audio_offset_ms", out var offset)
            .Should().BeTrue();

        offset.Kind.Should().Be(SettingValueKind.Number);
        offset.DefaultValue.Should().Be("0", "o'lchov qilinmaguncha hech narsa siljitilmaydi");
        offset.Minimum.Should().Be(-2000m);
        offset.Maximum.Should().Be(2000m);
    }

    /// <summary>
    /// Hammasi <c>Content</c> ("O'quv kontenti") bo'limida — panelda ular
    /// ombor sirlari yonida emas, o'quv sozlamalari orasida turadi.
    /// </summary>
    [Theory]
    [MemberData(nameof(AllKeys))]
    public void EveryPipelineKey_LivesInTheContentGroup(string key)
    {
        SettingsRegistry.TryGet(key, out var definition).Should().BeTrue();

        definition.Group.Should().Be(SettingGroup.Content);
    }
}
