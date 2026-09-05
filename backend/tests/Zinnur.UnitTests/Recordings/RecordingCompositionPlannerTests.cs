using Zinnur.Application.Recordings.Services;
using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;

namespace Zinnur.UnitTests.Recordings;

/// <summary>
/// ════════════════════════════════════════════════════════════════════════
/// TUNGI YIG'ISH REJASI — SPEC DAGI ENG MUHIM TEST FAYLI (§8)
/// ════════════════════════════════════════════════════════════════════════
///
/// <c>RecordingCompositionPlanner</c> SOF funksiya bo'lgani uchun bu yerda
/// protsess ham, tarmoq ham, fayl ham yo'q: kirish — baza qatorlari,
/// chiqish — ffmpeg ning <c>-filter_complex</c> satri. Aynan shu satr
/// butun quvurdagi eng xatoga moyil narsa va u SATRMA-SATR
/// solishtiriladi ("golden string").
///
/// ── NIMA UCHUN "GRAF BO'SH EMAS" YETARLI EMAS ───────────────────────────
///
/// Bu yerdagi har bir xato JIMGINA buziladi: video ovozdan bir necha
/// soniya siljiydi, ekran ulashish kamera ostida qolib ketadi, ustoz
/// uzilgan oraliq qora emas, oldingi kadr bilan to'ladi. Ularning
/// hech biri ffmpeg'ni yiqitmaydi va hech biri logda ko'rinmaydi — ular
/// faqat faylni OCHGANDA ko'rinadi, ya'ni oradan bir hafta o'tib.
///
/// ── 🔴 DARSNING UZUNLIGI AHAMIYATSIZ BO'LISHI SHART ─────────────────────
///
/// Bu talab alohida yozilgan, chunki 2026-09-04 da AYNAN uzunlikka
/// bog'liq nosozlik tuzatilgan edi: watchdog 10 daqiqadan uzun HAR
/// QANDAY darsning yozuvini yo'q qilardi va qisqa darslar ishlagani
/// uchun nosozlik "tasodifiy" ko'rinardi. Shuning uchun bu yerda 20
/// daqiqalik va 2 soatlik dars AYNI tuzilishdagi grafni berishi
/// tekshiriladi.
/// </summary>
public sealed class RecordingCompositionPlannerTests
{
    /// <summary>Vaqt o'qining nol nuqtasi — barcha oraliqlar shundan sanaladi.</summary>
    private static readonly DateTimeOffset Origin =
        new(2026, 9, 5, 10, 0, 0, TimeSpan.Zero);

    private const string FinalKey = "recordings/2026-09/7/0123456789abcdef.mp4";

    /// <summary>Har kirish uchun takrorlanadigan masshtablash bo'g'ini.</summary>
    private const string Fit =
        "scale=1920:1080:force_original_aspect_ratio=decrease,"
        + "pad=1920:1080:(ow-iw)/2:(oh-ih)/2";

    private const string Tail = "aresample=async=1:first_pts=0";

    // ═══════════════════════════════════════════════════ 1) haqiqiy dars

    /// <summary>
    /// 🔴 BU FAYLNING ASOSIY TESTI: PRODUKSIYADAGI ODATIY DARS.
    ///
    /// <code>
    ///   0 s     xona ovozi boshlandi (butun darsga BITTA fayl)
    ///   5 s     ustoz kamerasi yoqildi
    ///   900 s   EKRAN ULASHISH yoqildi -> kamera burchakka o'tadi
    ///   1800 s  ustoz UZILDI          -> kamera bo'lagi yopiladi
    ///   1860 s  ustoz QAYTDI          -> YANGI kamera bo'lagi
    ///   2700 s  ekran o'chirildi      -> kamera yana butun kadrda
    ///   5400 s  dars tugadi
    /// </code>
    ///
    /// Kutilgan natija: 1800–1860 oralig'ida QORA kadr (ustoz yo'q edi),
    /// 900–2700 oralig'ida ekran butun kadrda va kamera burchakda,
    /// ovoz esa BITTA <c>adelay</c> bilan bir marta qo'yiladi va boshqa
    /// tegilmaydi.
    /// </summary>
    [Fact]
    public void RealLesson_ScreenSwapAndReconnect_ProducesTheExpectedGraph()
    {
        var plan = Plan(
            Audio(1, 0, 5400),
            Camera(2, 5, 1800),
            Screen(3, 900, 2700),
            Camera(4, 1860, 5400));

        plan.FilterGraph.Should().Be(string.Join(';',
            // kirishlar: kamera ikki o'lchamda kerak (butun kadr + burchak)
            "[0:v]split=2[v0a][v0b]",
            $"[v0a]{Fit}[v0full]",
            "[v0b]scale=480:-2[v0pip]",
            $"[1:v]{Fit}[v1full]",
            "[2:v]split=2[v2a][v2b]",
            $"[v2a]{Fit}[v2full]",
            "[v2b]scale=480:-2[v2pip]",

            // ⚠️ `d=` SHART: fon manbai cheksiz, ffmpeg esa asosiy kirish
            //    tugaguncha kodlaydi.
            "color=c=black:s=1920x1080:r=30:d=5400[bg]",

            // 1-qatlam: ekransiz paytdagi kamera
            "[bg][v0full]overlay=0:0:enable='between(t,5,900)'[ov0]",
            "[ov0][v2full]overlay=0:0:enable='between(t,2700,5400)'[ov1]",

            // 2-qatlam: ekran — kamerani berkitadi
            "[ov1][v1full]overlay=0:0:enable='between(t,900,2700)'[ov2]",

            // 3-qatlam: kichik oyna — ekran ustida
            "[ov2][v0pip]overlay=W-w-24:H-h-24:enable='between(t,900,1800)'[ov3]",
            "[ov3][v2pip]overlay=W-w-24:H-h-24:enable='between(t,1860,2700)'[v]",

            // ovoz: BITTA surish, kesish yo'q, aralashtirish yo'q
            $"[3:a]adelay=0|0,{Tail}[a]"));

        plan.TimelineSeconds.Should().Be(5400);
        plan.Warning.Should().BeNull();
    }

    /// <summary>
    /// 🔴 <c>amix</c> XONA OVOZI REJIMIDA UMUMAN BO'LMASLIGI SHART va
    /// <c>adelay</c> AYNAN BITTA bo'lishi kerak (§8).
    ///
    /// Ovoz — vaqt o'qining O'ZI. Uni bo'lish yoki aralashtirish har
    /// chokda kichik xatolik tug'diradi va ular 80 daqiqada TO'PLANADI.
    /// </summary>
    [Fact]
    public void RoomAudio_IsPlacedOnce_WithoutAmix()
    {
        var plan = Plan(
            Audio(1, 0, 5400),
            Camera(2, 5, 1800),
            Screen(3, 900, 2700),
            Camera(4, 1860, 5400));

        plan.FilterGraph.Should().NotContain("amix", "xona aralashmasi allaqachon yagona manba");
        Occurrences(plan.FilterGraph, "adelay").Should().Be(1);
        Occurrences(plan.FilterGraph, "atrim").Should().Be(0);
    }

    // ═══════════════════════════════════════════════════ 2) uzunlikka bog'liqlik YO'Q

    /// <summary>
    /// 🔴 DARSNING UZUNLIGI TUZILISHGA TA'SIR QILMAYDI.
    ///
    /// 20 daqiqalik va 2 soatlik dars AYNI SHAKLDAGI grafni beradi —
    /// faqat raqamlar boshqa. Bu test 2026-09-04 dagi nosozlikning
    /// takrorlanmasligi uchun: o'sha yerda ham hamma narsa "ishlardi",
    /// faqat dars uzun bo'lsa yo'qolardi.
    /// </summary>
    [Theory]
    [InlineData(1200)]      // 20 daqiqa
    [InlineData(4800)]      // 80 daqiqa — odatiy dars
    [InlineData(7200)]      // 2 soat
    public void LessonLength_DoesNotChangeTheShapeOfTheGraph(int seconds)
    {
        var screenStart = seconds / 4d;
        var screenEnd = seconds / 2d;

        var plan = Plan(
            Audio(1, 0, seconds),
            Camera(2, 0, seconds),
            Screen(3, screenStart, screenEnd));

        plan.FilterGraph.Should().Be(string.Join(';',
            "[0:v]split=2[v0a][v0b]",
            $"[v0a]{Fit}[v0full]",
            "[v0b]scale=480:-2[v0pip]",
            $"[1:v]{Fit}[v1full]",
            $"color=c=black:s=1920x1080:r=30:d={N(seconds)}[bg]",
            $"[bg][v0full]overlay=0:0:enable='between(t,0,{N(screenStart)})"
            + $"+between(t,{N(screenEnd)},{N(seconds)})'[ov0]",
            $"[ov0][v1full]overlay=0:0:enable='between(t,{N(screenStart)},{N(screenEnd)})'[ov1]",
            "[ov1][v0pip]overlay=W-w-24:H-h-24:"
            + $"enable='between(t,{N(screenStart)},{N(screenEnd)})'[v]",
            $"[2:a]adelay=0|0,{Tail}[a]"));

        plan.TimelineSeconds.Should().Be(seconds);
    }

    /// <summary>
    /// Uzunlik faqat RAQAMLARGA tegadi: ikki xil uzunlikdagi darsning
    /// grafidan raqamlarni olib tashlasak, ular AYNAN teng bo'ladi.
    /// </summary>
    [Fact]
    public void LessonLength_ChangesOnlyNumbers_NotFilters()
    {
        var shortLesson = Plan(Audio(1, 0, 1200), Camera(2, 0, 1200), Screen(3, 300, 600));
        var longLesson = Plan(Audio(1, 0, 7200), Camera(2, 0, 7200), Screen(3, 1800, 3600));

        Skeleton(shortLesson.FilterGraph).Should().Be(Skeleton(longLesson.FilterGraph));
    }

    // ═══════════════════════════════════════════════════ 3) sodda holatlar

    /// <summary>Faqat kamera — ekran ham, bo'linish ham kerak emas.</summary>
    [Fact]
    public void SingleCamera_NeedsNoSplitAndNoInset()
    {
        var plan = Plan(Audio(1, 0, 600), Camera(2, 0, 600));

        plan.FilterGraph.Should().Be(string.Join(';',
            $"[0:v]{Fit}[v0full]",
            "color=c=black:s=1920x1080:r=30:d=600[bg]",
            "[bg][v0full]overlay=0:0:enable='between(t,0,600)'[v]",
            $"[1:a]adelay=0|0,{Tail}[a]"));

        plan.FilterGraph.Should().NotContain("split", "kichik oyna kerak bo'lmasa bo'linish ham kerak emas");
    }

    /// <summary>
    /// Ekran IKKI marta yoqildi: kamera oralig'i UCHGA bo'linadi va
    /// kichik oyna ikki oraliqda ko'rinadi.
    /// </summary>
    [Fact]
    public void TwoScreenIntervals_SplitTheCameraIntoThreeVisibleRanges()
    {
        var plan = Plan(
            Audio(1, 0, 1000),
            Camera(2, 0, 1000),
            Screen(3, 100, 300),
            Screen(4, 600, 800));

        plan.FilterGraph.Should().Be(string.Join(';',
            "[0:v]split=2[v0a][v0b]",
            $"[v0a]{Fit}[v0full]",
            "[v0b]scale=480:-2[v0pip]",
            $"[1:v]{Fit}[v1full]",
            $"[2:v]{Fit}[v2full]",
            "color=c=black:s=1920x1080:r=30:d=1000[bg]",
            "[bg][v0full]overlay=0:0:enable='between(t,0,100)+between(t,300,600)"
            + "+between(t,800,1000)'[ov0]",
            "[ov0][v1full]overlay=0:0:enable='between(t,100,300)'[ov1]",
            "[ov1][v2full]overlay=0:0:enable='between(t,600,800)'[ov2]",
            "[ov2][v0pip]overlay=W-w-24:H-h-24:enable='between(t,100,300)"
            + "+between(t,600,800)'[v]",
            $"[3:a]adelay=0|0,{Tail}[a]"));
    }

    /// <summary>
    /// Ustoz uzilgan oraliq HECH QANDAY qatlam bilan qoplanmaydi — u
    /// qora bo'lib chiqadi.
    ///
    /// ★ NIMA UCHUN BU MUHIM: `overlay` ning standart xatti-harakati
    /// kirish tugagach OXIRGI KADRNI takrorlashdir. `enable` bo'lmasa
    /// uzilish 60 soniyalik "muzlagan ustoz" bo'lib chiqardi va buni
    /// hech kim nosozlik deb tushunmasdi.
    /// </summary>
    [Fact]
    public void ReconnectGap_IsCoveredByNothing()
    {
        var plan = Plan(
            Audio(1, 0, 3000),
            Camera(2, 0, 1000),
            Camera(3, 1200, 3000));

        plan.FilterGraph.Should().Be(string.Join(';',
            $"[0:v]{Fit}[v0full]",
            $"[1:v]{Fit}[v1full]",
            "color=c=black:s=1920x1080:r=30:d=3000[bg]",
            "[bg][v0full]overlay=0:0:enable='between(t,0,1000)'[ov0]",
            "[ov0][v1full]overlay=0:0:enable='between(t,1200,3000)'[v]",
            $"[2:a]adelay=0|0,{Tail}[a]"));

        plan.FilterGraph.Should().NotContain("between(t,1000,1200)",
            "1000–1200 oralig'ida ustoz yo'q edi va u QORA bo'lishi kerak");
    }

    // ═══════════════════════════════════════════════════ 4) buzilgan holatlar

    /// <summary>
    /// Ustoz kamerani UMUMAN yoqmagan dars — bu MUVAFFAQIYAT, nosozlik
    /// emas (§4.1-6). O'quv bo'limi tushuntirish sifatini baholaydi va u
    /// OVOZDA.
    /// </summary>
    [Fact]
    public void AudioOnly_RendersABlackCanvasForTheWholeLesson()
    {
        var plan = Plan(Audio(1, 0, 4800));

        plan.FilterGraph.Should().Be(string.Join(';',
            "color=c=black:s=1920x1080:r=30:d=4800[v]",
            $"[0:a]adelay=0|0,{Tail}[a]"));

        plan.Warning.Should().BeNull("ovoz bor, ya'ni ogohlantiradigan narsa yo'q");
    }

    /// <summary>
    /// Mikser yiqilgan dars: tasvir bor, ovoz yo'q. Fayl JIMLIK oqimi
    /// bilan chiqadi va xodim buni OCHMASDAN bilishi uchun
    /// ogohlantirish beriladi (§4.6).
    /// </summary>
    [Fact]
    public void VideoWithoutAudio_GetsASilentTrackAndAWarning()
    {
        var plan = Plan(Camera(1, 0, 1800));

        plan.FilterGraph.Should().Be(string.Join(';',
            $"[0:v]{Fit}[v0full]",
            "color=c=black:s=1920x1080:r=30:d=1800[bg]",
            "[bg][v0full]overlay=0:0:enable='between(t,0,1800)'[v]",
            "anullsrc=r=48000:cl=stereo:d=1800[a]"));

        plan.Warning.Should().Be("Dars ovozi yozib olinmadi.");
    }

    /// <summary>Bironta ham tayyor bo'lak yo'q — reja UMUMAN tuzilmaydi.</summary>
    [Fact]
    public void NoCompletedTracks_ProducesNoPlan()
    {
        var recording = Recording();

        var result = RecordingCompositionPlanner.Create(
            recording,
            [Track(1, RecordingTrackKind.CameraVideo, 0, 600, status: RecordingStatus.Failed)],
            CompositionPlanSettings.Default);

        result.Plan.Should().BeNull();
        result.Error.Should().Be("Darsdan yozib olingan trek topilmadi.");
    }

    /// <summary>
    /// 🔴 YIQILGAN BO'LAK REJAGA UMUMAN KIRMAYDI. Uning fayli omborda
    /// YO'Q; rejaga qo'shilsa BUTUN kodlash yiqilardi — bitta bo'lakni
    /// yo'qotish o'rniga.
    /// </summary>
    [Fact]
    public void FailedTracks_AreExcludedFromTheGraph()
    {
        var plan = Plan(
            Audio(1, 0, 1000),
            Camera(2, 0, 500),
            Track(3, RecordingTrackKind.CameraVideo, 500, 1000, status: RecordingStatus.Failed));

        plan.Inputs.Should().HaveCount(2);
        plan.FilterGraph.Should().NotContain("[2:v]");
    }

    /// <summary>Nol uzunlikdagi bo'lak KIRISH BO'LMAYDI — ulanmagan filtr chiqishi ffmpeg uchun XATO.</summary>
    [Fact]
    public void ZeroLengthVideo_IsNotAnInput()
    {
        var plan = Plan(
            Audio(1, 0, 600),
            Camera(2, 100, 100),
            Camera(3, 200, 500));

        plan.Inputs.Should().HaveCount(2);
        plan.Inputs.Select(i => i.TrackId).Should().Equal(3, 1);
    }

    // ═══════════════════════════════════════════════════ 5) vaqt o'qining boshi

    /// <summary>
    /// <c>T0</c> — HAMMA tayyor bo'lak bo'yicha eng erkin boshlanish,
    /// ovoz ham, video ham. Bu yerda VIDEO birinchi boshlangan, ya'ni
    /// ovozga surish tushadi.
    /// </summary>
    [Fact]
    public void AudioStartingAfterTheFirstVideo_IsDelayed()
    {
        var plan = Plan(
            Camera(1, 0, 1000),
            Audio(2, 12.5, 1000));

        plan.Inputs.Single(i => i.Kind == RecordingTrackKind.CameraVideo)
            .ItsOffsetSeconds.Should().Be(0);

        plan.FilterGraph.Should().Contain($"[1:a]adelay=12500|12500,{Tail}[a]");
    }

    /// <summary>
    /// Ovoz birinchi boshlangan — u vaqt o'qining ANKORI, video esa
    /// <c>-itsoffset</c> bilan suriladi.
    /// </summary>
    [Fact]
    public void AudioStartingBeforeTheFirstVideo_AnchorsTheTimeline()
    {
        var plan = Plan(
            Audio(1, 0, 1000),
            Camera(2, 30, 1000));

        plan.Inputs.Single(i => i.Kind == RecordingTrackKind.CameraVideo)
            .ItsOffsetSeconds.Should().Be(30);

        plan.FilterGraph.Should().Contain("adelay=0|0");
        plan.TimelineSeconds.Should().Be(1000);
    }

    /// <summary>
    /// 🔴 OVOZ KIRISHIGA <c>-itsoffset</c> BERILMAYDI. Uning o'rni filtr
    /// grafida (<c>adelay</c>) va u yerda kalibrlash konstantasi ham
    /// qo'shiladi. Ikkala joyda ham surish berilsa, ovoz IKKI BAROBAR
    /// siljirdi.
    /// </summary>
    [Fact]
    public void AudioInput_NeverCarriesItsOffset()
    {
        var plan = Plan(Camera(1, 0, 1000), Audio(2, 60, 1000));

        plan.Inputs.Single(i => i.Kind == RecordingTrackKind.RoomAudio)
            .ItsOffsetSeconds.Should().Be(0);
    }

    // ═══════════════════════════════════════════════════ 6) kalibrlash konstantasi

    /// <summary>
    /// Musbat kalibrlash — ovoz KECHIKTIRILADI.
    /// </summary>
    [Fact]
    public void PositiveAudioOffset_IsAddedToTheDelay()
    {
        var plan = Plan(
            new CompositionPlanSettings("slow", 19, 250),
            Audio(1, 0, 1000),
            Camera(2, 0, 1000));

        plan.FilterGraph.Should().Contain("adelay=250|250");
        Occurrences(plan.FilterGraph, "adelay").Should().Be(1);
        plan.Preset.Should().Be("slow");
        plan.Crf.Should().Be(19);
    }

    /// <summary>
    /// 🔴 MANFIY KALIBRLASH — HAQIQIY HOL: ovoz odatda vaqt o'qining
    /// BOSHIDA turadi (surish 0), ya'ni manfiy konstanta uni noldan
    /// oldinga surardi. <c>adelay</c> manfiy qiymatni qabul qilmaydi,
    /// shuning uchun ovozning BOSHIDAN kesiladi.
    ///
    /// ⚠️ Nolga yaxlitlash ham yechim EMAS: u kalibrlashni jimgina
    /// ishlamaydigan qilib qo'yardi va §9.1 dagi "doimiy siljishni bitta
    /// raqam bilan tuzatish" yo'li yopilardi.
    /// </summary>
    [Fact]
    public void NegativeAudioOffset_TrimsTheStartInsteadOfDelaying()
    {
        var plan = Plan(
            new CompositionPlanSettings("medium", 21, -300),
            Audio(1, 0, 1000),
            Camera(2, 0, 1000));

        plan.FilterGraph.Should().Contain($"[1:a]atrim=start=0.3,asetpts=PTS-STARTPTS,{Tail}[a]");
        plan.FilterGraph.Should().NotContain("adelay");
    }

    // ═══════════════════════════════════════════════════ 7) ikkinchi mikser

    /// <summary>
    /// Mikser dars o'rtasida o'lib, o'rniga yangisi qo'yilgan (§4.1-3).
    ///
    /// 🔴 BO'LAKLAR KETMA-KET ULANADI (<c>concat</c>), ARALASHTIRILMAYDI.
    /// Ular vaqt o'qining ketma-ket qismlari, bir-birining nusxasi emas;
    /// <c>amix</c> esa ikkalasini bir vaqtda o'ynatib, oradagi
    /// bo'shliqni ham noto'g'ri to'ldirardi.
    ///
    /// Birinchi bo'lak O'Z UYASINI to'liq egallaydi (<c>atrim</c> +
    /// <c>apad</c>), shuning uchun ikkinchisi AYNAN o'z vaqtida
    /// boshlanadi va oradagi 100 soniya HAQIQIY jimlik bo'ladi.
    ///
    /// ⚠️ SURISH FAQAT BIRINCHISIDA: <c>concat</c> dan keyin ikkinchi
    /// bo'lak birinchisining uzunligidan boshlanadi, ya'ni unga ham
    /// surish berilsa u IKKI marta suriladi.
    /// </summary>
    [Fact]
    public void TwoRoomAudioRows_AreConcatenatedWithSilenceInTheGap()
    {
        var plan = Plan(
            Audio(1, 0, 900, sid: RecordingTrack.RoomAudioSid),
            Camera(2, 0, 2000),
            Audio(3, 1000, 2000, sid: RecordingTrack.RoomAudioSid + "2"));

        plan.FilterGraph.Should().Be(string.Join(';',
            $"[0:v]{Fit}[v0full]",
            "color=c=black:s=1920x1080:r=30:d=2000[bg]",
            "[bg][v0full]overlay=0:0:enable='between(t,0,2000)'[v]",
            "[1:a]adelay=0|0,atrim=end=1000,apad=whole_dur=1000[a0]",
            $"[a0][2:a]concat=n=2:v=0:a=1,{Tail}[a]"));

        plan.FilterGraph.Should().NotContain("amix", "bo'laklar kesishmaydi, aralashtirish kerak emas");
    }

    // ═══════════════════════════════════════════════════ 8) zaxira ovoz rejimi

    /// <summary>
    /// Zaxira rejim (§3.4b): ustoz mikrofoni + ekran ovozi. Bu YAGONA
    /// holat, unda ikki manba HAQIQATAN ustma-ust tushadi.
    ///
    /// 🔴 <c>normalize=0</c> SHART: standart holatda <c>amix</c>
    /// kirishlar soniga BO'LADI, ya'ni ekran ovozi paydo bo'lgan lahzada
    /// ustozning ovozi ikki barobar pasayardi.
    /// </summary>
    [Fact]
    public void TeacherTrackMode_MixesMicAndScreenAudioWithoutNormalising()
    {
        var plan = Plan(
            Track(1, RecordingTrackKind.MicAudio, 0, 1200),
            Camera(2, 0, 1200),
            Track(3, RecordingTrackKind.ScreenAudio, 300, 900));

        plan.FilterGraph.Should().Be(string.Join(';',
            $"[0:v]{Fit}[v0full]",
            "color=c=black:s=1920x1080:r=30:d=1200[bg]",
            "[bg][v0full]overlay=0:0:enable='between(t,0,1200)'[v]",
            "[1:a]adelay=0|0[a0]",
            "[2:a]adelay=300000|300000[a1]",
            $"[a0][a1]amix=inputs=2:normalize=0,{Tail}[a]"));
    }

    /// <summary>
    /// 🔴 IKKALA OVOZ MANBASI BIR VAQTDA BO'LSA — XONA ARALASHMASI
    /// YUTADI (§2.3).
    ///
    /// Bunday holat sozlama dars O'RTASIDA almashtirilganda paydo
    /// bo'ladi. Ikkalasini birga ishlatish ustozning ovozini IKKI MARTA,
    /// biroz siljigan holda eshittirardi — bu "aks-sado" emas, mikrofon
    /// buzilganday tuyuladigan taroqli filtrlash.
    ///
    /// ★ QAROR SOZLAMADAN EMAS, QATORLARDAN OLINADI: fayllar dars
    /// paytida qanday yozilgan bo'lsa shundayligicha qoladi, sozlama esa
    /// tungi yig'ishgacha o'zgargan bo'lishi mumkin.
    /// </summary>
    [Fact]
    public void BothAudioSources_RoomMixWins()
    {
        var plan = Plan(
            Audio(1, 0, 1200),
            Track(2, RecordingTrackKind.MicAudio, 0, 1200),
            Camera(3, 0, 1200));

        plan.FilterGraph.Should().NotContain("amix");
        Occurrences(plan.FilterGraph, "adelay").Should().Be(1);

        plan.Inputs.Should().NotContain(i => i.Kind == RecordingTrackKind.MicAudio);
    }

    // ═══════════════════════════════════════════════════ 9) kirishlar

    /// <summary>
    /// Kirishlar tartibi: avval VIDEO (vaqt bo'yicha), keyin OVOZ. Filtr
    /// grafidagi <c>[N:v]</c> / <c>[N:a]</c> AYNAN shu tartibga bog'langan.
    /// </summary>
    [Fact]
    public void Inputs_AreVideoFirstThenAudio_InTimeOrder()
    {
        var plan = Plan(
            Audio(1, 0, 3000),
            Camera(2, 100, 1000),
            Screen(3, 50, 900));

        plan.Inputs.Select(i => i.TrackId).Should().Equal(3, 2, 1);
        plan.Inputs.Select(i => i.Index).Should().Equal(0, 1, 2);
    }

    /// <summary>
    /// Ishchi papkadagi fayl nomi TARTIB RAQAMI bilan boshlanadi: ikki
    /// bo'lakning ombor kaliti oxirgi qismi bilan to'qnashib qolmasin.
    /// </summary>
    [Fact]
    public void InputFileNames_ArePrefixedWithTheirIndex()
    {
        var plan = Plan(Audio(1, 0, 600), Camera(2, 0, 600));

        plan.Inputs[0].FileName.Should().Be("00-TR_2.webm");
        plan.Inputs[1].FileName.Should().Be("01-ROOM.ogg");
    }

    /// <summary>
    /// Kutilgan uzunlik LIVEKIT vaqtidan hisoblanadi — keyinchalik
    /// <c>ffprobe</c> o'lchagani bilan solishtiriladi (§9.1-1).
    /// </summary>
    [Fact]
    public void InputExpectedDuration_ComesFromLiveKitTimestamps()
    {
        var plan = Plan(Audio(1, 0, 4800), Camera(2, 30, 1830));

        plan.Inputs[0].ExpectedDurationMs.Should().Be(1_800_000);
        plan.Inputs[1].ExpectedDurationMs.Should().Be(4_800_000);
    }

    /// <summary>Yakuniy kalit REJADA — yig'ish MAVJUD kalitga yozadi, yangisini o'ylab topmaydi.</summary>
    [Fact]
    public void Plan_TargetsTheExistingObjectKey()
    {
        Plan(Audio(1, 0, 600)).TargetObjectKey.Should().Be(FinalKey);
    }

    // ═══════════════════════════════════════════════════ yordamchilar

    private static CompositionPlan Plan(params RecordingTrack[] tracks) =>
        Plan(CompositionPlanSettings.Default, tracks);

    private static CompositionPlan Plan(
        CompositionPlanSettings settings, params RecordingTrack[] tracks)
    {
        var result = RecordingCompositionPlanner.Create(Recording(), tracks, settings);

        result.Error.Should().BeNull();

        return result.Plan!;
    }

    private static SessionRecording Recording() => new()
    {
        Id = 42,
        SessionId = 7,
        ObjectKey = FinalKey,
        Pipeline = RecordingPipeline.TrackComposition,
    };

    private static RecordingTrack Audio(long id, double start, double end, string? sid = null) =>
        Track(id, RecordingTrackKind.RoomAudio, start, end, sid ?? RecordingTrack.RoomAudioSid);

    private static RecordingTrack Camera(long id, double start, double end) =>
        Track(id, RecordingTrackKind.CameraVideo, start, end);

    private static RecordingTrack Screen(long id, double start, double end) =>
        Track(id, RecordingTrackKind.ScreenVideo, start, end);

    private static RecordingTrack Track(
        long id,
        RecordingTrackKind kind,
        double start,
        double end,
        string? sid = null,
        RecordingStatus status = RecordingStatus.Completed)
    {
        var trackSid = sid ?? $"TR_{id}";

        var extension = kind is RecordingTrackKind.CameraVideo or RecordingTrackKind.ScreenVideo
            ? "webm"
            : "ogg";

        return new RecordingTrack
        {
            Id = id,
            RecordingId = 42,
            TrackSid = trackSid,
            Kind = kind,
            Status = status,
            ObjectKey = $"raw/7/42/{trackSid}.{extension}",
            StartedAt = Origin.AddSeconds(start),
            EndedAt = Origin.AddSeconds(end),
        };
    }

    private static int Occurrences(string text, string needle)
    {
        var count = 0;
        var index = 0;

        while ((index = text.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }

        return count;
    }

    /// <summary>Grafdan barcha raqamlarni olib tashlaydi — faqat TUZILISH qoladi.</summary>
    private static string Skeleton(string graph) =>
        string.Concat(graph.Where(c => !char.IsAsciiDigit(c) && c != '.'));

    private static string N(double value) =>
        value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
}
