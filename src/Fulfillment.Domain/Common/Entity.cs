namespace Fulfillment.Domain.Common;

/// <summary>
/// Base type for persisted entities. Audit timestamps and <see cref="Version"/> are maintained by the
/// persistence layer, so they are read-only from the domain's point of view.
/// </summary>
public abstract class Entity
{
    public Guid Id { get; protected set; } = Guid.NewGuid();

    public DateTime CreatedAt { get; private set; }

    public DateTime UpdatedAt { get; private set; }

    /// <summary>Optimistic concurrency token, incremented on every update.</summary>
    public long Version { get; private set; }
}
