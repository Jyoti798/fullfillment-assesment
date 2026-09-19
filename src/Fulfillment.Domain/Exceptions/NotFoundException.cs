namespace Fulfillment.Domain.Exceptions;

/// <summary>The requested resource does not exist. Mapped to HTTP 404.</summary>
public sealed class NotFoundException : Exception
{
    public NotFoundException(string resource, object key)
        : base($"{resource} '{key}' was not found.")
    {
    }
}
