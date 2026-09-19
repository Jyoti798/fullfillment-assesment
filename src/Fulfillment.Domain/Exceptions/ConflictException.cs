namespace Fulfillment.Domain.Exceptions;

/// <summary>
/// The request is well-formed but conflicts with the current state (insufficient stock, invalid status
/// transition, duplicate SKU, concurrent update). Mapped to HTTP 409.
/// </summary>
public class ConflictException : Exception
{
    public ConflictException(string message)
        : base(message)
    {
    }
}
