namespace Zinnur.Domain.Exceptions;

/// <summary>
/// Biznes qoidasi buzilganda ko'tariladi (masalan: tugagan darsni boshlash).
/// WebApi qatlamidagi global middleware buni HTTP 409/400 ga aylantiradi —
/// domain HTTP haqida hech narsa bilmaydi (Clean Architecture qoidasi).
/// </summary>
public class DomainException : Exception
{
    public DomainException(string message) : base(message) { }

    public DomainException(string message, Exception inner) : base(message, inner) { }
}
