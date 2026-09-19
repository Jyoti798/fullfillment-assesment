namespace Fulfillment.Domain.Exceptions;

/// <summary>
/// A concurrent writer changed the same data first. Unlike every other <see cref="ConflictException"/> this one
/// is transient: re-running the operation on fresh data may well succeed, so the unit of work retries it. If the
/// retries run out it still surfaces as HTTP 409.
/// </summary>
public sealed class ConcurrencyConflictException()
    : ConflictException("The resource was modified by another request. Reload it and try again.");
