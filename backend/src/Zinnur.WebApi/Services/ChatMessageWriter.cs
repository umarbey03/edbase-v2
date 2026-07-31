using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Zinnur.Application.Common.Interfaces;
using Zinnur.Domain.Entities;

namespace Zinnur.WebApi.Services;

/// <summary>
/// Chat xabarlarini bazaga PAKETLAB yozadigan fon xizmati.
///
/// NIMA UCHUN KERAK (200 foydalanuvchi):
/// Naif yo'lda har xabar broadcast'dan oldin bazaga yoziladi va DB kechikishi
/// (5-20 ms) butun chat tezligini belgilaydi. Faol darsda bu sezilarli
/// sekinlashish beradi va DB ulanishlar havzasini band qiladi.
///
/// Bu yerda: hub xabarni kanalga tashlaydi (bloklamaydi), fon xizmati esa
/// ularni to'plab, bitta INSERT paketida yozadi. Natijada 100 ta xabar
/// 100 ta tranzaksiya emas, 1 ta tranzaksiya bo'ladi.
///
/// KELISHUV: server to'satdan o'chsa navbatdagi bir necha xabar yo'qolishi
/// mumkin. Chat uchun bu maqbul — pul yoki davomat emas. Muhim ma'lumot
/// (davomat, to'lov) hech qachon bu yo'l bilan yozilmaydi.
/// </summary>
public sealed class ChatMessageWriter : BackgroundService, IChatMessageWriter
{
    /// <summary>Bitta paketdagi maksimal xabar soni.</summary>
    private const int BatchSize = 100;

    /// <summary>Paket to'lmasa ham shu vaqtdan keyin yoziladi.</summary>
    private static readonly TimeSpan FlushInterval = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Chegaralangan kanal: xotira cheksiz o'smasin.
    /// To'lib qolsa eng eski xabar tashlanadi (<see cref="BoundedChannelFullMode.DropOldest"/>) —
    /// chat yozilishi hech qachon jonli darsni bloklamaydi.
    ///
    /// ★ TASHLANGAN XABAR ENDI JIM EMAS: <c>itemDropped</c> qayta chaqiruvi
    /// bilan har tashlash logga tushadi. Ilgari yuk ostida tarix jimgina
    /// teshilardi va buni hech kim sezmasdi — "xabarim yo'qoldi" shikoyatini
    /// tekshirib bo'lmasdi. Tashlash TARQATISHGA ta'sir qilmaydi (u allaqachon
    /// bo'lib bo'lgan), faqat tarix yozuvi yo'qoladi — shuning uchun bu
    /// yiqilish emas, OGOHLANTIRISH.
    /// </summary>
    private readonly Channel<ChatMessage> _channel;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ChatMessageWriter> _logger;

    public ChatMessageWriter(IServiceScopeFactory scopeFactory, ILogger<ChatMessageWriter> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;

        // Kanal konstruktorda quriladi: `itemDropped` ga `_logger` kerak,
        // maydon initsializatoridan esa boshqa maydonga murojaat qilib bo'lmaydi.
        _channel = Channel.CreateBounded<ChatMessage>(
            new BoundedChannelOptions(10_000)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false,
            },
            itemDropped: dropped => ChatWriterLog.MessageDropped(_logger, dropped.SessionId));
    }

    /// <inheritdoc />
    public ValueTask EnqueueAsync(ChatMessage message, CancellationToken ct = default) =>
        _channel.Writer.WriteAsync(message, ct);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var buffer = new List<ChatMessage>(BatchSize);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Birinchi xabarni kutamiz (bo'sh paytda CPU yemaydi)
                if (!await _channel.Reader.WaitToReadAsync(stoppingToken).ConfigureAwait(false))
                    break;

                buffer.Clear();

                // Paketni to'ldiramiz: kanalda bor bo'lganini darrov olamiz
                while (buffer.Count < BatchSize && _channel.Reader.TryRead(out var msg))
                    buffer.Add(msg);

                if (buffer.Count == 0)
                    continue;

                await FlushAsync(buffer, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;      // normal to'xtatish
            }
            catch (Exception ex)
            {
                // Bitta paket xatosi fon xizmatini o'ldirmasin
                ApiLog.ChatBatchFailed(_logger, ex, buffer.Count);
                await Task.Delay(FlushInterval, stoppingToken).ConfigureAwait(false);
            }
        }

        // To'xtashdan oldin qolganini yozib qo'yamiz
        buffer.Clear();
        while (_channel.Reader.TryRead(out var msg))
            buffer.Add(msg);

        if (buffer.Count > 0)
            await FlushAsync(buffer, CancellationToken.None).ConfigureAwait(false);
    }

    private async Task FlushAsync(List<ChatMessage> batch, CancellationToken ct)
    {
        // BackgroundService — singleton, DbContext esa scoped. Har paket uchun
        // yangi scope ochamiz (aks holda DbContext ulanishni abadiy ushlab turadi).
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        db.ChatMessages.AddRange(batch);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        ApiLog.ChatBatchWritten(_logger, batch.Count);
    }
}

/// <summary>
/// Chat yozuvchisining loglari.
///
/// ★ NIMA UCHUN ALOHIDA SINF (<c>ApiLog</c> ga qo'shilmadi): <c>GroupChatLog</c>
/// bilan AYNI sabab — har modul o'z EventId oralig'ini saqlaydi va umumiy
/// faylga bir vaqtda bir necha modul yozib, raqamlarni to'qnashtirmaydi.
///
/// ★ NIMA UCHUN <c>[LoggerMessage]</c>: manba generatori ajratmasiz kod
/// yozadi; oddiy <c>logger.LogWarning($"...")</c> log darajasi o'chiq
/// bo'lganda ham satr yig'ardi (CA1848).
/// </summary>
internal static partial class ChatWriterLog
{
    [LoggerMessage(
        EventId = 6100,
        Level = LogLevel.Warning,
        Message = "Chat navbati to'lgan — xabar tarixga yozilmadi: session={SessionId}")]
    public static partial void MessageDropped(ILogger logger, long sessionId);
}
