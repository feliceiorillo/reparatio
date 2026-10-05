namespace Reparatio.Repairs.Domain;

// Sequence allocated monotonically and uniquely by the store within the site transaction.
public sealed record WaitingRepairCandidate(Guid RepairId, Guid TenantId, Guid SiteId, long ArrivalSequence);
public sealed record WaitingAssignment(Guid RepairId, Guid TechnicianId);

public static class WaitingAssignmentPolicy
{
    public static IReadOnlyList<WaitingAssignment> Plan(Guid tenantId, Guid siteId,
        IEnumerable<WaitingRepairCandidate> waiting, IEnumerable<TechnicianCandidate> technicians,
        DateTimeOffset assignedAt) => throw new NotImplementedException();
}
