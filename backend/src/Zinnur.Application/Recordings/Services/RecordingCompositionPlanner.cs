using System.Globalization;
using System.Text;
using Zinnur.Domain.Entities;
using Zinnur.Domain.Enums;

namespace Zinnur.Application.Recordings.Services;

/// <summary>
/// ════════════════════════════════════════════════════════════════════════
/// TUNGI YIG'ISH REJASINI TUZADI — SOF FUNKSIYA
/// ════════════════════════════════════════════════════════════════════════
///
/// Kirishi: baza qatorlari. Chiqishi: <see cref="CompositionPlan"/>, ya'ni
/// ffmpeg kirishlari + TAYYOR <c>-filter_complex</c> satri.
///
/// 🔴 BU YERDA I/O YO'Q — VA BU BUTUN LOYIHANING ENG MUHIM QARORI:
/// protsess ham, tarmoq ham, fayl tizimi ham yo'q. Sabab oddiy — ana shu
/// filtr grafi butun quvurdagi ENG XATOGA MOYIL narsa:
///
///   • ekran ulashish dars o'rtasida yoqiladi va o'chiriladi;
///   • ustoz uzilib-ulanadi, ya'ni tasvirda TESHIK qoladi;
///   • ovoz esa BITTA uzluksiz fayl va u vaqt o'qining O'ZI.
///
/// Bularning har biri bitta <c>enable='between(t,…)'</c> ifodasiga
/// aylanadi. Agar graf Infrastructure'da qurilsa, uni tekshirishning
/// yagona yo'li 90 daqiqalik haqiqiy kodlashni yurgizish bo'lardi — ya'ni
/// amalda hech qachon tekshirilmasdi. Sof funksiya esa oltin satr
/// (golden string) bilan millisekundgacha solishtiriladi
/// (<c>RecordingCompositionPlannerTests</c>).
///
/// ════════════════════════════════════════════════════════════════════════
/// VAQT O'QI — OVOZ SPINA (§4.5-4, §9.1)
/// ════════════════════════════════════════════════════════════════════════
///
/// <c>T0 = MIN(StartedAt)</c> — HAMMA tayyor bo'lak bo'yicha, ovoz ham,
/// video ham. Shu tufayli yozib olingan biror narsa hech qachon
/// qirqilmaydi. Har bo'lak <c>[StartedAt - T0, EndedAt - T0]</c>
/// oralig'ini egallaydi, natijaning uzunligi esa <c>MAX(EndedAt) - T0</c>.
///
/// 🔴 OVOZ BITTA MARTA, BITTA SURISH BILAN QO'YILADI VA KEYIN HECH QACHON
/// KESILMAYDI, BO'LINMAYDI YOKI ARALASHTIRILMAYDI. Xona mikseri butun
/// dars davomida uzluksiz yozadi (ustoz uzilganda ham), ya'ni u tayyor
/// vaqt o'qi. Video bo'laklar AYNI <c>T0</c> ga nisbatan joylashtiriladi.
///
/// ★ NIMA UCHUN BU MUHIM: har ovoz bo'lagi uchun alohida moslashtirish
/// qilinsa, har chokda kichik xatolik paydo bo'lardi va ular 80 daqiqada
/// TO'PLANARDI ("accumulating drift"). Bu yerda esa xatolik ko'pi bilan
/// DOIMIY siljish bo'ladi, doimiy siljishni esa bitta raqam tuzatadi
/// (<c>recordings.compose_audio_offset_ms</c>).
///
/// ⚠️ UZUNLIK FAYLDAN EMAS, LIVEKIT VAQTIDAN OLINADI. <c>started_at</c> /
/// <c>ended_at</c> ni BITTA LiveKit jarayoni bosadi, ya'ni ular o'zaro
/// kelishilgan. Fayllarning ichki vaqt belgilari esa har birida noldan
/// boshlanadi va paket yo'qotilishi tufayli devor soatidan chetga chiqadi
/// — reja ularga UMUMAN qaramaydi (o'lchangan uzunlik faqat OGOHLANTIRISH
/// uchun, §9.1-1).
///
/// ════════════════════════════════════════════════════════════════════════
/// TASVIR QATLAMLARI (§4.6)
/// ════════════════════════════════════════════════════════════════════════
///
///   ekran ulashish bor      -> ekran butun kadrni to'ldiradi,
///                              kamera pastki-o'ngda 480×270 kichik oyna;
///   ekran yo'q, kamera bor  -> kamera butun kadrni to'ldiradi;
///   ikkalasi ham yo'q       -> QORA (ustoz uzilgan oraliq).
///
/// Bularning hammasi BITTA <c>filter_complex</c> ga aylanadi: har kirish
/// <c>-itsoffset</c> bilan o'z joyiga suriladi va
/// <c>enable='between(t,…)'</c> bilan o'z oralig'ida yoqiladi. Oraliqlar
/// .NET da hisoblanadi (qo'lda yozilmaydi).
/// </summary>
public static class RecordingCompositionPlanner
{
    // ---------------------------------------------------------------- kadr o'lchamlari

    /// <summary>Chiqish kadrining kengligi (§4.6).</summary>
    public const int CanvasWidth = 1920;

    /// <summary>Chiqish kadrining balandligi.</summary>
    public const int CanvasHeight = 1080;

    /// <summary>
    /// Chiqish kadr chastotasi. 25 EMAS (D3): kamera 30 fps beradi va 25 ga
    /// tushirish odam DIQQAT BILAN qaraydigan yagona narsada — ustozning
    /// harakatida — sezilarli silkinish hosil qiladi.
    /// </summary>
    public const int CanvasFps = 30;

    /// <summary>Kichik oynaning (kamera) kengligi; balandligi nisbatdan.</summary>
    public const int InsetWidth = 480;

    /// <summary>Kichik oynaning kadr chetidan masofasi.</summary>
    public const int InsetMargin = 24;

    /// <summary>Chiqish ovozining namuna chastotasi.</summary>
    public const int AudioSampleRate = 48000;

    // ---------------------------------------------------------------- xodimga ko'rinadigan matnlar

    /// <summary>Bironta ham tayyor bo'lak yo'q — yig'ish mumkin emas (§4.5).</summary>
    public const string NoTracksReason = "Darsdan yozib olingan trek topilmadi.";

    /// <summary>Bo'laklar bor, lekin vaqt o'qi noldan katta emas.</summary>
    public const string EmptyTimelineReason = "Yozilgan bo'laklarning uzunligi aniqlanmadi.";

    /// <summary>Tasvir bor, ovoz yo'q — fayl JIM chiqadi (§4.6).</summary>
    public const string SilentWarning = "Dars ovozi yozib olinmadi.";

    // ---------------------------------------------------------------- filtr bo'laklari

    /// <summary>
    /// Kadrni to'ldirish: nisbatni saqlab kattalashtiriladi, qolgan joy
    /// qora chiziq bilan MARKAZLAB to'ldiriladi. Ekran ulashish 4:3 ham
    /// bo'lishi mumkin — cho'zish esa matnni o'qib bo'lmas qilardi.
    /// </summary>
    private static readonly string ScaleToCanvas = string.Create(
        CultureInfo.InvariantCulture,
        $"scale={CanvasWidth}:{CanvasHeight}:force_original_aspect_ratio=decrease,"
        + $"pad={CanvasWidth}:{CanvasHeight}:(ow-iw)/2:(oh-ih)/2");

    /// <summary>
    /// Kichik oyna: kenglik qat'iy, balandlik nisbatdan va JUFT
    /// (<c>-2</c>) — <c>yuv420p</c> toq balandlikni qabul qilmaydi.
    /// </summary>
    private static readonly string ScaleToInset =
        string.Create(CultureInfo.InvariantCulture, $"scale={InsetWidth}:-2");

    /// <summary>
    /// Ovoz oqimining oxirgi bo'g'ini.
    ///
    /// ★ <c>async=1</c> — Opus oqimi ichidagi kichik vaqt uzilishlarini
    /// namuna qo'shib/tashlab YUTADI. Busiz uzilishdan KEYINGI butun
    /// dars o'sha qadar siljib qolardi: 40 ms lik nuqson o'rniga 40 ms
    /// doimiy lab-ovoz nomuvofiqligi.
    ///
    /// ⚠️ SPEC-RECORDING-V2 §4.6 da bu parametr <c>first_pta=0</c> deb
    /// yozilgan — ffmpeg'da bunday nom YO'Q va graf "Option not found"
    /// bilan rad etilardi. To'g'ri nomi <c>first_pts</c>.
    /// </summary>
    private const string AudioTail = "aresample=async=1:first_pts=0";

    /// <summary>
    /// Rejani tuzadi.
    /// </summary>
    /// <param name="recording">Yig'iladigan yozuv (kaliti va Id'si olinadi).</param>
    /// <param name="tracks">
    /// Shu yozuvning BARCHA bo'laklari. Faqat
    /// <see cref="RecordingStatus.Completed"/> bo'lganlari ishlatiladi:
    /// yiqilgan bo'lakning fayli omborda YO'Q va uni rejaga qo'shish butun
    /// kodlashni yiqitardi (bitta bo'lakni yo'qotish o'rniga).
    /// </param>
    /// <param name="settings">Sozlamalardan kelgan kodlash parametrlari.</param>
    /// <param name="excludeTrackIds">
    /// Rejadan CHIQARIB TASHLANADIGAN bo'laklar.
    ///
    /// 🔴 NIMA UCHUN BU PARAMETR BOR: xom fayl <c>Completed</c> qatorga
    /// ega bo'lsa-yu ombordan TOPILMASA (o'chirilgan, prefiks
    /// almashtirilgan, R2 da yo'qolgan), yig'uvchi uni yuklab ola olmaydi
    /// va butun kodlash to'xtaydi. Bunda chaqiruvchi rejani O'SHA
    /// bo'laksiz QAYTA quradi: dars 90 daqiqasidan besh daqiqasini
    /// yo'qotadi, HAMMASINI emas.
    ///
    /// ⚠️ QATORNING HOLATI O'ZGARMAYDI va bu ATAYLAB:
    /// <c>RecordingTrack.MarkFailed</c> tayyor bo'lakni ATAYLAB orqaga
    /// qaytarmaydi (kech kelgan hodisa tayyor faylni ro'yxatdan
    /// o'chirmasin). Ya'ni "fayl yo'qolgani" LOGDA va shu ro'yxatda
    /// qoladi, bazadagi tarixiy javob esa buzilmaydi.
    /// </param>
    public static CompositionPlanResult Create(
        SessionRecording recording,
        IEnumerable<RecordingTrack> tracks,
        CompositionPlanSettings settings,
        IReadOnlyCollection<long>? excludeTrackIds = null)
    {
        ArgumentNullException.ThrowIfNull(recording);
        ArgumentNullException.ThrowIfNull(tracks);
        ArgumentNullException.ThrowIfNull(settings);

        var completed = tracks
            .Where(t => t.Status == RecordingStatus.Completed
                     && t.StartedAt is not null
                     && t.EndedAt is not null
                     && !string.IsNullOrWhiteSpace(t.ObjectKey)
                     && excludeTrackIds?.Contains(t.Id) != true)
            .OrderBy(t => t.StartedAt!.Value)
            .ThenBy(t => t.Id)
            .ToList();

        if (completed.Count == 0)
            return CompositionPlanResult.Fail(NoTracksReason);

        var t0 = completed.Min(t => t.StartedAt!.Value);
        var timeline = Round((completed.Max(t => t.EndedAt!.Value) - t0).TotalSeconds);

        if (timeline <= 0)
            return CompositionPlanResult.Fail(EmptyTimelineReason);

        // ══════════════════════════════════════════════════════════════
        // OVOZ MANBASI — IKKALASI EMAS, BITTASI (§2.3)
        //
        // 🔴 Xona aralashmasi ALLAQACHON ustozning ovozini o'z ichiga
        //    oladi. Uning ustiga ustozning mikrofon treki qo'shilsa, bir
        //    ovoz ikki marta, biroz siljigan holda eshitiladi — bu
        //    "aks-sado" emas, mikrofon buzilganday tuyuladigan taroqli
        //    filtrlash.
        //
        // ★ QAROR SOZLAMADAN EMAS, QATORLARDAN OLINADI. Sozlama
        //   (`records.audio_capture_mode`) dars bilan tungi yig'ish
        //   ORASIDA o'zgargan bo'lishi mumkin; fayllar esa o'zgarmaydi.
        //   Ikkala tur ham bo'lsa (ya'ni sozlama dars o'rtasida
        //   almashtirilgan bo'lsa) xona aralashmasi YUTADI: u to'liqroq.
        // ══════════════════════════════════════════════════════════════
        var roomAudio = completed.Where(t => t.Kind == RecordingTrackKind.RoomAudio).ToList();

        var audio = roomAudio.Count > 0
            ? roomAudio
            : completed
                .Where(t => t.Kind is RecordingTrackKind.MicAudio or RecordingTrackKind.ScreenAudio)
                .ToList();

        var videos = BuildVideoLayers(completed, t0);

        // Kirishlar tartibi: avval video, keyin ovoz. Filtr grafidagi
        // `[0:v]`, `[1:v]`, `[N:a]` yorliqlari AYNAN shu tartibga
        // bog'langan, shuning uchun ikkalasi bitta joyda yasaladi.
        var inputs = new List<CompositionInput>(videos.Count + audio.Count);

        foreach (var layer in videos)
            inputs.Add(InputOf(layer.Track, inputs.Count, t0, video: true));

        foreach (var row in audio)
            inputs.Add(InputOf(row, inputs.Count, t0, video: false));

        var graph = new StringBuilder();

        BuildVideoGraph(graph, videos, timeline);

        var silent = audio.Count == 0;

        BuildAudioGraph(graph, audio, videos.Count, t0, timeline, settings.AudioOffsetMs);

        var plan = new CompositionPlan(
            RecordingId: recording.Id,
            TargetObjectKey: recording.ObjectKey,
            Inputs: inputs,
            FilterGraph: graph.ToString(),
            TimelineSeconds: timeline,
            Preset: settings.Preset,
            Crf: settings.Crf,
            Warning: silent ? SilentWarning : null);

        return CompositionPlanResult.Ok(plan);
    }

    // ═════════════════════════════════════════════════════════ tasvir qatlamlari

    /// <summary>
    /// Har video bo'lak uchun ikki xil oraliq hisoblaydi: qachon u BUTUN
    /// kadrni egallaydi va qachon KICHIK OYNAGA tushadi.
    ///
    /// ★ QOIDA BITTA: ekran ulashish bor paytda ekran yutadi, kamera esa
    /// burchakka o'tadi. Ya'ni kameraning oralig'i ekranlar birlashmasi
    /// bo'yicha IKKIGA bo'linadi.
    ///
    /// ⚠️ BO'SH ORALIQLI BO'LAK UMUMAN KIRISH BO'LMAYDI: ulanmagan filtr
    /// chiqishi ffmpeg uchun XATO ("Output pad ... not connected"), ya'ni
    /// bitta nol uzunlikli bo'lak butun kodlashni yiqitardi.
    /// </summary>
    private static List<VideoLayer> BuildVideoLayers(
        IReadOnlyList<RecordingTrack> completed, DateTimeOffset t0)
    {
        var screens = new List<Interval>();
        var layers = new List<VideoLayer>();

        foreach (var track in completed)
        {
            if (track.Kind != RecordingTrackKind.ScreenVideo) continue;

            var span = SpanOf(track, t0);

            if (span.Length > 0) screens.Add(span);
        }

        var covered = Interval.Union(screens);

        foreach (var track in completed)
        {
            var span = SpanOf(track, t0);

            if (span.Length <= 0) continue;

            switch (track.Kind)
            {
                case RecordingTrackKind.ScreenVideo:
                    layers.Add(new VideoLayer(track, [span], []));
                    break;

                case RecordingTrackKind.CameraVideo:
                    var full = Interval.Subtract(span, covered);
                    var inset = Interval.Intersect(span, covered);

                    if (full.Count > 0 || inset.Count > 0)
                        layers.Add(new VideoLayer(track, full, inset));

                    break;

                default:
                    break;      // ovoz — bu yerda emas
            }
        }

        // `completed` allaqachon (StartedAt, Id) bo'yicha tartiblangan, ya'ni
        // qatlamlar ham vaqt bo'yicha tartibda. Bu MUHIM: ustma-ust tushgan
        // (uzilish paytidagi) ikki bo'lakdan KEYINGISI ustiga chiziladi.
        return layers;
    }

    private static void BuildVideoGraph(
        StringBuilder graph, IReadOnlyList<VideoLayer> layers, double timeline)
    {
        var index = new Dictionary<long, int>(layers.Count);

        for (var i = 0; i < layers.Count; i++)
        {
            var layer = layers[i];

            index[layer.Track.Id] = i;

            var hasFull = layer.FullIntervals.Count > 0;
            var hasInset = layer.InsetIntervals.Count > 0;

            if (hasFull && hasInset)
            {
                // Bitta manba IKKI o'lchamda kerak — `split` busiz filtr
                // chiqishini ikki marta ishlatib bo'lmaydi.
                Add(graph, $"[{i}:v]split=2[v{i}a][v{i}b]");
                Add(graph, $"[v{i}a]{ScaleToCanvas}[v{i}full]");
                Add(graph, $"[v{i}b]{ScaleToInset}[v{i}pip]");
            }
            else if (hasFull)
            {
                Add(graph, $"[{i}:v]{ScaleToCanvas}[v{i}full]");
            }
            else
            {
                Add(graph, $"[{i}:v]{ScaleToInset}[v{i}pip]");
            }
        }

        // ⚠️ `d=` SHART: fon manbai cheksiz, `overlay` esa ASOSIY kirish
        //    tugaguncha ishlaydi. Uzunliksiz fon bilan ffmpeg abadiy
        //    kodlab turardi (SPEC §4.6 dagi namunada bu tushib qolgan).
        var background =
            $"color=c=black:s={CanvasWidth}x{CanvasHeight}:r={CanvasFps}:d={Num(timeline)}";

        var steps = new List<(string Source, string Filter, string Enable)>();

        // 1-qatlam: ekransiz paytdagi kamera (butun kadr).
        foreach (var layer in layers.Where(l => l.Track.Kind == RecordingTrackKind.CameraVideo
                                             && l.FullIntervals.Count > 0))
        {
            steps.Add((
                $"[v{index[layer.Track.Id]}full]",
                "overlay=0:0",
                Interval.Enable(layer.FullIntervals)));
        }

        // 2-qatlam: ekran ulashish — kamerani BERKITADI.
        foreach (var layer in layers.Where(l => l.Track.Kind == RecordingTrackKind.ScreenVideo))
        {
            steps.Add((
                $"[v{index[layer.Track.Id]}full]",
                "overlay=0:0",
                Interval.Enable(layer.FullIntervals)));
        }

        // 3-qatlam: kichik oyna — ekranning USTIDA, pastki-o'ng burchakda.
        foreach (var layer in layers.Where(l => l.Track.Kind == RecordingTrackKind.CameraVideo
                                             && l.InsetIntervals.Count > 0))
        {
            steps.Add((
                $"[v{index[layer.Track.Id]}pip]",
                $"overlay=W-w-{InsetMargin}:H-h-{InsetMargin}",
                Interval.Enable(layer.InsetIntervals)));
        }

        if (steps.Count == 0)
        {
            // Tasvirsiz dars — QORA fon butun davomiylikka. Bu MUVAFFAQIYAT,
            // nosozlik emas: ustoz kamerani yoqmagan bo'lsa ham darsning
            // tushuntirishi ovozda va u to'liq saqlangan (§4.1-6).
            Add(graph, $"{background}{CompositionPlan.VideoLabel}");

            return;
        }

        Add(graph, $"{background}[bg]");

        var previous = "[bg]";

        for (var i = 0; i < steps.Count; i++)
        {
            var (source, filter, enable) = steps[i];
            var output = i == steps.Count - 1 ? CompositionPlan.VideoLabel : $"[ov{i}]";

            Add(graph, $"{previous}{source}{filter}:enable='{enable}'{output}");

            previous = output;
        }
    }

    // ═════════════════════════════════════════════════════════ ovoz

    /// <summary>
    /// Ovoz zanjiri. Uch hol bor va ular ATAYLAB bir-biriga o'xshamaydi.
    ///
    /// ── 1) BITTA OVOZ FAYLI — ODATIY HOL ────────────────────────────────
    ///
    /// <c>[N:a]adelay=…,aresample=…[a]</c>. BITTA surish, kesish yo'q,
    /// aralashtirish yo'q. Butun quvur shu holat uchun qurilgan.
    ///
    /// ── 2) BIR NECHTA XONA OVOZI — MIKSER O'LIB QAYTA YOQILGAN ──────────
    ///
    /// Bo'laklar KETMA-KET ulanadi (<c>concat</c>), ustma-ust
    /// QO'YILMAYDI. Oradagi bo'shliq — haqiqiy jimlik va u
    /// <c>apad</c> bilan to'ldiriladi.
    ///
    /// 🔴 <c>amix</c> BU YERDA ISHLATILMAYDI. Bo'laklar kesishmaydi, ya'ni
    /// aralashtirishning ma'nosi yo'q; <c>amix</c> esa standart holatda
    /// kirishlar soniga BO'LADI va butun darsning ovozini ikki barobar
    /// pasaytirib qo'yardi.
    ///
    /// ── 3) ZAXIRA REJIM: USTOZ MIKROFONI + EKRAN OVOZI (§3.4b) ──────────
    ///
    /// Bu YAGONA holat, unda ikki manba HAQIQATAN ustma-ust tushadi va
    /// <c>amix</c> o'rinli. U <c>normalize=0</c> bilan chaqiriladi —
    /// aks holda ekran ovozi paydo bo'lgan lahzada ustozning ovozi ikki
    /// barobar pasayardi.
    /// </summary>
    private static void BuildAudioGraph(
        StringBuilder graph,
        List<RecordingTrack> audio,
        int firstAudioIndex,
        DateTimeOffset t0,
        double timeline,
        int offsetMs)
    {
        if (audio.Count == 0)
        {
            // Ovozsiz mp4 ni brauzerning `<video>` elementi va ba'zi
            // pleyerlar yomon ochadi, shuning uchun JIMLIK oqimi
            // qo'shiladi. Sabab xodimga `CompositionError` orqali
            // aytiladi (§4.6).
            Add(graph,
                $"anullsrc=r={AudioSampleRate}:cl=stereo:d={Num(timeline)}"
                + CompositionPlan.AudioLabel);

            return;
        }

        var delays = audio
            .Select(t => (long)Math.Round((t.StartedAt!.Value - t0).TotalMilliseconds) + offsetMs)
            .ToArray();

        if (audio.Count == 1)
        {
            Add(graph,
                $"[{firstAudioIndex}:a]{Placement(delays[0])},{AudioTail}"
                + CompositionPlan.AudioLabel);

            return;
        }

        // Zaxira rejim: mikrofon + ekran ovozi USTMA-UST tushadi.
        var overlapping = audio[0].Kind != RecordingTrackKind.RoomAudio;

        if (overlapping)
        {
            var mixed = new StringBuilder();

            for (var i = 0; i < audio.Count; i++)
            {
                Add(graph, $"[{firstAudioIndex + i}:a]{Placement(delays[i])}[a{i}]");
                mixed.Append(CultureInfo.InvariantCulture, $"[a{i}]");
            }

            Add(graph,
                $"{mixed}amix=inputs={audio.Count}:normalize=0,{AudioTail}"
                + CompositionPlan.AudioLabel);

            return;
        }

        // Bir nechta XONA OVOZI — ketma-ket ulanadi.
        //
        // ★ HAR BO'LAK O'Z UYASINI to'liq egallaydi: `atrim` uzunini
        //   kesadi, `apad` kaltasini jimlik bilan cho'zadi. Shu tufayli
        //   keyingi bo'lak AYNAN o'z vaqtida boshlanadi va xatolik
        //   to'planmaydi.
        //
        // ⚠️ SURISH FAQAT BIRINCHISIDA: `concat` dan keyin har bo'lak
        //    o'zidan oldingilarning UMUMIY uzunligidan boshlanadi, ya'ni
        //    ikkinchisiga ham surish berilsa u ikki marta suriladi.
        var pieces = new StringBuilder();

        for (var i = 0; i < audio.Count; i++)
        {
            var chain = new List<string>(2);

            if (i == 0) chain.Add(Placement(delays[0]));

            if (i < audio.Count - 1)
            {
                var slot = Round((delays[i + 1] - (i == 0 ? 0 : delays[i])) / 1000d);

                if (slot > 0)
                    chain.Add($"atrim=end={Num(slot)},apad=whole_dur={Num(slot)}");
            }

            if (chain.Count == 0)
            {
                // Filtri yo'q bo'lak to'g'ridan-to'g'ri `concat` ga beriladi
                // — bo'sh yorliq yasab o'tirishning hojati yo'q.
                pieces.Append(CultureInfo.InvariantCulture, $"[{firstAudioIndex + i}:a]");

                continue;
            }

            Add(graph, $"[{firstAudioIndex + i}:a]{string.Join(',', chain)}[a{i}]");
            pieces.Append(CultureInfo.InvariantCulture, $"[a{i}]");
        }

        Add(graph,
            $"{pieces}concat=n={audio.Count}:v=0:a=1,{AudioTail}"
            + CompositionPlan.AudioLabel);
    }

    /// <summary>
    /// Ovozni vaqt o'qidagi joyiga qo'yadi.
    ///
    /// ⚠️ MANFIY SURISH — HAQIQIY HOL: kalibrlash konstantasi
    /// (<c>recordings.compose_audio_offset_ms</c>) manfiy bo'lishi mumkin,
    /// ovoz esa ko'pincha vaqt o'qining O'ZI boshi (surish 0). Bunda
    /// ovozning boshidan shuncha KESILADI — <c>adelay</c> manfiy qiymatni
    /// qabul qilmaydi va uni nolga yaxlitlash kalibrlashni jimgina
    /// ishlamaydigan qilib qo'yardi.
    /// </summary>
    private static string Placement(long delayMs) =>
        delayMs >= 0
            ? $"adelay={delayMs.ToString(CultureInfo.InvariantCulture)}|"
              + delayMs.ToString(CultureInfo.InvariantCulture)
            : $"atrim=start={Num(Round(-delayMs / 1000d))},asetpts=PTS-STARTPTS";

    // ═════════════════════════════════════════════════════════ yordamchilar

    private static CompositionInput InputOf(
        RecordingTrack track, int index, DateTimeOffset t0, bool video)
    {
        var span = SpanOf(track, t0);

        return new CompositionInput(
            Index: index,
            TrackId: track.Id,
            Kind: track.Kind,
            ObjectKey: track.ObjectKey,
            FileName: FileNameOf(track, index),

            // 🔴 OVOZDA SURISH YO'Q — u filtr grafida (`adelay`) beriladi.
            //    Sabab `CompositionInput.ItsOffsetSeconds` izohida.
            ItsOffsetSeconds: video ? span.Start : 0,
            ExpectedDurationMs: (int)Math.Round(span.Length * 1000));
    }

    /// <summary>
    /// Ishchi papkadagi fayl nomi: <c>00-TR_abc.webm</c>.
    ///
    /// ★ TARTIB RAQAMI OLDINDA — ikki xil bo'lakning kaliti bir xil
    /// nomga tushib qolmasin (ombor kaliti uzun, fayl nomi esa faqat
    /// oxirgi bo'lagidan olinadi).
    /// </summary>
    private static string FileNameOf(RecordingTrack track, int index)
    {
        var tail = track.ObjectKey.Split('/')[^1];
        var safe = new StringBuilder(tail.Length);

        foreach (var ch in tail)
        {
            safe.Append(char.IsAsciiLetterOrDigit(ch) || ch is '.' or '_' or '-' ? ch : '_');
        }

        return $"{index.ToString("00", CultureInfo.InvariantCulture)}-{safe}";
    }

    private static Interval SpanOf(RecordingTrack track, DateTimeOffset t0) =>
        new(
            Round((track.StartedAt!.Value - t0).TotalSeconds),
            Round((track.EndedAt!.Value - t0).TotalSeconds));

    private static void Add(StringBuilder graph, string part)
    {
        if (graph.Length > 0) graph.Append(';');

        graph.Append(part);
    }

    /// <summary>
    /// Millisekundgacha yaxlitlaydi. Filtr grafidagi raqam bilan
    /// <c>-itsoffset</c> dagi raqam AYNAN bir xil bo'lishi SHART — aks
    /// holda kadr o'z <c>enable</c> oralig'ining chetida qolib ketishi
    /// mumkin.
    /// </summary>
    private static double Round(double seconds) => Math.Round(seconds, 3);

    private static string Num(double value) =>
        value.ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>Bitta video bo'lak va uning ikki xil ko'rinishdagi oraliqlari.</summary>
    private sealed record VideoLayer(
        RecordingTrack Track,
        IReadOnlyList<Interval> FullIntervals,
        IReadOnlyList<Interval> InsetIntervals);
}

/// <summary>
/// Reja yoki uni tuzib bo'lmaganining SABABI.
///
/// ★ NIMA UCHUN <c>null</c> EMAS: chaqiruvchi sababni yozuvning
/// <c>CompositionError</c> ustuniga qo'yadi va xodim uni ro'yxatda
/// ko'radi. <c>null</c> qaytarilsa, sabab chaqiruv joyida QAYTA o'ylab
/// topilardi va ikkita boshqa-boshqa matn paydo bo'lardi.
/// </summary>
public sealed record CompositionPlanResult(CompositionPlan? Plan, string? Error)
{
    public static CompositionPlanResult Ok(CompositionPlan plan) => new(plan, null);

    public static CompositionPlanResult Fail(string error) => new(null, error);
}

/// <summary>
/// Rejaga tashqaridan keladigan sozlamalar (<c>SettingsRegistry</c> dan).
/// </summary>
/// <param name="Preset">x264 preseti (<c>recordings.compose_preset</c>).</param>
/// <param name="Crf">x264 CRF (<c>recordings.compose_crf</c>).</param>
/// <param name="AudioOffsetMs">
/// Kalibrlash konstantasi (<c>recordings.compose_audio_offset_ms</c>).
///
/// ★ NIMA UCHUN BU SOZLAMA UMUMAN BOR: DOIMIY siljish — quvurning
/// o'zgarmas kechikishi va uni BITTA raqam tuzatadi. To'planib
/// boradigan siljish esa loyihaning nuqsoni va uni bu raqam bilan
/// tuzatib BO'LMAYDI (§9.1). Ikkalasini ajratishning yagona yo'li —
/// darsning uch nuqtasida o'lchash.
/// </param>
public sealed record CompositionPlanSettings(string Preset, int Crf, int AudioOffsetMs)
{
    /// <summary>SPEC §2.7 dagi standart qiymatlar (sozlama registrda yo'q bo'lsa).</summary>
    public static CompositionPlanSettings Default { get; } = new("medium", 21, 0);
}

/// <summary>
/// Vaqt o'qidagi oraliq (sekund, <c>T0</c> dan).
///
/// ★ NIMA UCHUN ALOHIDA TUR: kamera oralig'i ekranlar bo'yicha ikkiga
/// bo'linadi va bu amal (birlashma, ayirma, kesishma) uchta joyda kerak.
/// Kortejlar bilan yozilsa, "qaysi biri boshi?" degan savol har chaqiruv
/// joyida qaytadan tug'ilardi.
/// </summary>
internal readonly record struct Interval(double Start, double End)
{
    public double Length => End - Start;

    /// <summary>Ustma-ust tushgan oraliqlarni BIRLASHTIRADI (tartiblab).</summary>
    public static IReadOnlyList<Interval> Union(IReadOnlyList<Interval> intervals)
    {
        if (intervals.Count == 0) return [];

        var sorted = intervals.OrderBy(i => i.Start).ThenBy(i => i.End).ToList();
        var merged = new List<Interval> { sorted[0] };

        foreach (var next in sorted.Skip(1))
        {
            var last = merged[^1];

            if (next.Start <= last.End)
                merged[^1] = new Interval(last.Start, Math.Max(last.End, next.End));
            else
                merged.Add(next);
        }

        return merged;
    }

    /// <summary><paramref name="span"/> dan <paramref name="covers"/> ni ayiradi.</summary>
    public static IReadOnlyList<Interval> Subtract(Interval span, IReadOnlyList<Interval> covers)
    {
        var result = new List<Interval>();
        var cursor = span.Start;

        foreach (var cover in covers)
        {
            if (cover.End <= cursor) continue;
            if (cover.Start >= span.End) break;

            if (cover.Start > cursor) result.Add(new Interval(cursor, cover.Start));

            cursor = Math.Max(cursor, cover.End);
        }

        if (cursor < span.End) result.Add(new Interval(cursor, span.End));

        return result.Where(i => i.Length > 0).ToList();
    }

    /// <summary><paramref name="span"/> va <paramref name="covers"/> kesishmasi.</summary>
    public static IReadOnlyList<Interval> Intersect(Interval span, IReadOnlyList<Interval> covers)
    {
        var result = new List<Interval>();

        foreach (var cover in covers)
        {
            var start = Math.Max(span.Start, cover.Start);
            var end = Math.Min(span.End, cover.End);

            if (end > start) result.Add(new Interval(start, end));
        }

        return result;
    }

    /// <summary>
    /// ffmpeg <c>enable</c> ifodasi: <c>between(t,0,900)+between(t,1800,2700)</c>.
    ///
    /// ★ <c>+</c> — ffmpeg ifodalarida MANTIQIY YOKI ning idiomatik shakli:
    /// <c>between</c> 0 yoki 1 qaytaradi va <c>enable</c> noldan farqli
    /// har qanday qiymatni "yoqilgan" deb biladi.
    /// </summary>
    public static string Enable(IReadOnlyList<Interval> intervals) =>
        string.Join('+', intervals.Select(i =>
            "between(t,"
            + i.Start.ToString("0.###", CultureInfo.InvariantCulture)
            + ","
            + i.End.ToString("0.###", CultureInfo.InvariantCulture)
            + ")"));
}
