namespace Fulfillment.Domain.Exceptions;

/// <summary>A business or invariant validation failure with per-field messages. Mapped to HTTP 400.</summary>
public sealed class DomainValidationException : Exception
{
    public DomainValidationException(string field, string message)
        : this(new Dictionary<string, string[]> { [field] = [message] })
    {
    }

    public DomainValidationException(IDictionary<string, string[]> errors)
        : base("One or more validation errors occurred.")
    {
        Errors = errors;
    }

    public IDictionary<string, string[]> Errors { get; }
}
