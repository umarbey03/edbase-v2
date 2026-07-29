namespace Zinnur.Application.Common.Interfaces;

/// <summary>Parol hash'lash. BCrypt amalga oshirilishi Infrastructure'da.</summary>
public interface IPasswordHasher
{
    /// <summary>Parolni hash'laydi. CPU-og'ir — thread pool'da bajariladi.</summary>
    Task<string> HashAsync(string password, CancellationToken ct = default);

    /// <summary>
    /// Parolni tekshiradi.
    ///
    /// MUHIM: BCrypt ~250 ms sof CPU yeydi. Eski tizimda u to'g'ridan-to'g'ri
    /// async endpoint ichida chaqirilgani uchun HAR KIRISH butun serverni
    /// 250 ms muzlatardi. Shuning uchun bu metod async va ichida
    /// <c>Task.Run</c> orqali thread pool'ga chiqariladi.
    /// </summary>
    Task<bool> VerifyAsync(string password, string hash, CancellationToken ct = default);
}
