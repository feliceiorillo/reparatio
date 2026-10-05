namespace Reparatio.Repairs.Domain;

// Snapshot supplied by the application layer, scoped and read consistently.
public sealed record TechnicianCandidate
{
    public Guid Id { get; }
    public Guid TenantId { get; }
    public Guid SiteId { get; }
    public bool IsAvailable { get; }
    public int ActiveRepairCount { get; }
    public DateTimeOffset? LastAssignedAt { get; }

    public TechnicianCandidate(Guid id, Guid tenantId, Guid siteId, bool isAvailable,
        int activeRepairCount, DateTimeOffset? lastAssignedAt)
    {
        if (id == Guid.Empty || tenantId == Guid.Empty || siteId == Guid.Empty)
            throw new ArgumentException("Identifiers must not be empty.");
        ArgumentOutOfRangeException.ThrowIfNegative(activeRepairCount);
        Id = id;
        TenantId = tenantId;
        SiteId = siteId;
        IsAvailable = isAvailable;
        ActiveRepairCount = activeRepairCount;
        LastAssignedAt = lastAssignedAt;
    }
}

public static class TechnicianAssignmentPolicy
{
    public static TechnicianCandidate? Select(Guid tenantId, Guid siteId,
        IEnumerable<TechnicianCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        if (tenantId == Guid.Empty || siteId == Guid.Empty)
            throw new ArgumentException("Tenant and site must not be empty.");
        return candidates
            .Where(t => t.TenantId == tenantId && t.SiteId == siteId && t.IsAvailable)
            .OrderBy(t => t.ActiveRepairCount)
            .ThenBy(t => t.LastAssignedAt.HasValue)
            .ThenBy(t => t.LastAssignedAt)
            .ThenBy(t => t.Id)
            .FirstOrDefault();
    }
}
